using BookStitch.Models;
using BookStitch.Services;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
namespace BookStitch.Dialog;
public partial class ProjectIntegrityDialog : Window
{
    private readonly ProjectFormatSweepService _sweep;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly string _root;
    private bool _settled;
    private readonly Func<Task> _settleMaintenance;
    private int _processed, _total;
    private readonly string? _rebuildProject;
    private readonly ProjectRebuildService _rebuilder;
    private readonly DiagnosticLogService _log;
    private readonly ProjectIntegrityOptions _options;
    private readonly DispatcherTimer _activityTimer;
    private IntegrityProgress? _phaseProgress;
    private int _activityDots;
    public ProjectOperationOutcome Outcome {get;private set;}=ProjectOperationOutcome.Failed;
    public ProjectIntegrityDialog(string root, AudioProbeBatchService probes, ProjectIntegrityOptions options, DiagnosticLogService log, Func<Task>? settleMaintenance = null,string? rebuildProject=null)
    {
        options=options with {Performance=new ProjectIntegrityPerformance((Application.Current as App)?.PerformanceTraceService)};
        _rebuildProject=rebuildProject;_rebuilder=new(probes);_options=options;_log=log;
        InitializeComponent(); _root = root; _settleMaintenance = settleMaintenance ?? (() => Task.CompletedTask);
        _sweep = new(Path.Combine(SettingsService.GetSettingsFolder(root), ProjectFormatSweepService.StateFileName),
            ProjectIndexService.DiscoverProjectFolders, new ProjectIntegrityService(probes, message => log.WriteApplicationEvent("PROJECT INTEGRITY", message), log.WriteError), options,
            new Progress<IntegrityProgress>(ShowProgress));
        if(rebuildProject is not null)
        {
            Title=HeadingText.Text=LocalizationText.Get("Rebuild.Title");
            ExplanationText.Text=LocalizationText.Get("Rebuild.Explanation");
        }
        _activityTimer=new DispatcherTimer(DispatcherPriority.Background,Dispatcher) {Interval=TimeSpan.FromMilliseconds(500)};
        _activityTimer.Tick+=ActivityTick;
        Loaded += Run;
    }
    private void ShowProgress(IntegrityProgress progress)
    {
        if(!Dispatcher.CheckAccess())
        {
            if(!Dispatcher.HasShutdownStarted)Dispatcher.BeginInvoke(new Action(()=>ShowProgress(progress)));
            return;
        }
        if (_settled || _cancellation.IsCancellationRequested) return;
        StopActivity();
        _phaseProgress=progress;
        if(_rebuildProject is not null)
        {
            RenderPhase();
            StartActivity();
            Progress.IsIndeterminate=progress.TotalFiles==0;
            if(progress.TotalFiles>0){Progress.Maximum=progress.TotalFiles;Progress.Value=progress.CompletedFiles;}
            return;
        }
        if (progress.TotalProjects > 0) { _processed = progress.ProcessedProjects; _total = progress.TotalProjects; }
        RenderPhase();
        if(progress.TotalFiles==0)StartActivity();
        Progress.IsIndeterminate = progress.TotalFiles == 0 && _total == 0;
        if (progress.TotalFiles > 0) { Progress.Maximum = progress.TotalFiles; Progress.Value = progress.CompletedFiles; }
        else if (_total > 0) { Progress.Maximum = _total; Progress.Value = _processed; }
    }
    private void RenderPhase()
    {
        if(_phaseProgress is not { } phase)return;
        var text=LocalizationText.Get(phase.PhaseKey);
        if(_activityDots>0)text=text.TrimEnd(' ','.','…')+new string('.',_activityDots);
        PhaseText.Text=_rebuildProject is not null?text:LocalizationText.Format("Integrity.Progress",
            Math.Min(_processed+1,_total),_total,Path.GetFileName(phase.ProjectFolder),text,phase.CompletedFiles,phase.TotalFiles);
    }
    private void StartActivity(){_activityDots=1;RenderPhase();_activityTimer.Start();}
    private void StopActivity(){_activityTimer.Stop();_activityDots=0;RenderPhase();}
    private void ActivityTick(object? sender,EventArgs e)
    {
        if(_settled||_cancellation.IsCancellationRequested){StopActivity();return;}
        _activityDots=_activityDots%3+1;RenderPhase();
    }

    private async void Run(object sender, RoutedEventArgs e)
    {
        Loaded -= Run;
        var succeeded=false;
        try
        {
            await _settleMaintenance();
            _cancellation.Token.ThrowIfCancellationRequested();
            if(_rebuildProject is null)
            {
                var summary=await _sweep.RunAsync(_root, _cancellation.Token);
                succeeded=summary.RunStatus==ProjectFormatSweepRunStatus.Completed;
                Outcome=summary.RunStatus==ProjectFormatSweepRunStatus.Canceled?ProjectOperationOutcome.Canceled:ProjectOperationOutcome.Failed;
                ResultText.Text=ProjectIntegrityPresentation.FormatCompletion(summary);
            }
            else
            {
                ProjectIntegrityResult result;_options.Performance?.Begin();
                var progress=new Progress<IntegrityProgress>(ShowProgress);
                try {result=await Task.Run(()=>_rebuilder.RebuildAsync(_rebuildProject,_options,_cancellation.Token,progress));_options.Performance?.Add("Projects");_options.Performance?.End();}
                catch(OperationCanceledException){_options.Performance?.End(canceled:true);throw;}
                catch(Exception ex){_options.Performance?.End(error:ex);throw;}
                succeeded=result.WasRepaired && !result.SafetyBlocked && result.Health is ProjectHealthStatus.OK or ProjectHealthStatus.Warning;
                ResultText.Text=ProjectIntegrityPresentation.FormatRebuild(result,succeeded);
                if(result.Findings.Count>0)_log.WriteApplicationEvent("PROJECT REBUILD",string.Join(Environment.NewLine,result.Findings.Select(f=>$"{f.IssueCode}; {f.RepairAction}; {f.TechnicalDetail}; {f.Path}; SourceId={f.SourceId}; repaired={f.WasRepaired}")));
            }
        }
        catch (OperationCanceledException) { Outcome=ProjectOperationOutcome.Canceled;ResultText.Text = LocalizationText.Get("Integrity.Run.Canceled"); }
        catch (Exception ex) { ResultText.Text = LocalizationText.Get("Integrity.Failed") + "\n" + ex.Message; }
        finally
        {
            _settled = true;
            StopActivity();
            if(succeeded)Outcome=ProjectOperationOutcome.Completed;
            PhaseText.Text=string.Empty;
            Progress.IsIndeterminate = false;
            if(succeeded){Progress.Maximum=100;Progress.Value=100;Progress.Foreground=Brushes.ForestGreen;}
            CancelButton.IsEnabled = true;
            CancelButton.Content = LocalizationText.Get("UiSchliessen");
        }
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Cancel_Click(object sender, RoutedEventArgs e)
    { if (_settled) Close(); else { StopActivity();_cancellation.Cancel(); CancelButton.IsEnabled = false; } }
    protected override void OnClosing(CancelEventArgs e)
    { if (!_settled) { StopActivity();_cancellation.Cancel(); e.Cancel = true; } base.OnClosing(e); }
    protected override void OnClosed(EventArgs e) { _activityTimer.Stop();_activityTimer.Tick-=ActivityTick;_cancellation.Dispose(); base.OnClosed(e); }
}
