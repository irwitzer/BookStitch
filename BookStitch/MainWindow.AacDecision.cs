using BookStitch.Dialog;
using BookStitch.Models;
using BookStitch.Services;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace BookStitch;

public partial class MainWindow
{
    private AacDecisionPlan? _acceptedAacPlan;
    private bool _aacPreflightRunning;
    private bool _aacPlanningEnabled;
    private AudioEvidenceCache _folderAudioEvidence = new();
    private CancellationTokenSource? _aacPreviewCancellation;
    private Task? _aacPreviewTask;
    private string? _aacPreviewKey;
    private AacDecisionOptions? _aacPreviewOptions;
    private bool _aacApplyingPreview;

    private void ResetAacEvidence()
    {
        ResetPreparedAudioColumns();
        _aacPreviewCancellation?.Cancel();
        _aacPreviewCancellation = null;
        _aacPreviewTask = null;
        _aacPreviewOptions = null;
        _aacPreviewKey = null;
        _aacPlanningEnabled = false;
        _folderAudioEvidence = new AudioEvidenceCache();
    }

    private string AacRequestKey(IReadOnlyList<TrackInfo> tracks, string root, ExportPreset user, string toolchain,
        int? fixedRate, bool forceAll) => AacDecisionPlanner.Signature(tracks, root, user.DisplayName,
            _settings.DefaultSampleRateHz, toolchain) + $"/{fixedRate}/{forceAll}/" +
        string.Join(";", tracks.Where(track => !track.IsExcluded).Select(track =>
            AudioEvidenceFileIdentity.Read(TrackPathService.GetTrackPath(root, track)).ToString()));

    private void QueueAacPreview()
    {
        try { QueueAacPreviewCore(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _aacPreviewKey = null;
            _aacPreviewOptions = null;
        }
    }

    private void QueueAacPreviewCore()
    {
        if (!_aacPlanningEnabled || _aacPreflightRunning || _aacApplyingPreview || IsBusy || _isResumeLoading ||
            _isWorkspaceTransitioning || ProjectReviewState.IsReviewBeforeMerge || !_ffmpegStatus.IsComplete ||
            Tracks.Count == 0 || Tracks.All(track => track.IsExcluded)) return;
        var user = ExportPreset.Parse(SelectedExportPreset);
        if (user.IsPassThrough) return;
        var tracks = Tracks.ToArray();
        var toolchain = AacDecisionPreflightService.ToolchainIdentity(_ffmpegStatus.FfmpegPath!, _ffmpegStatus.FfprobePath!);
        var key = AacRequestKey(tracks, _currentFolderPath, user, toolchain, null, _acceptedAacPlan?.ForceEncodeAll == true);
        if (_aacPreviewKey == key)
        {
            if (_aacPreviewOptions is not null) ApplyAacPreview(_aacPreviewOptions);
            return;
        }
        _aacPreviewCancellation?.Cancel();
        _aacPreviewCancellation = new CancellationTokenSource();
        _aacPreviewKey = key;
        _aacPreviewOptions = null;
        var accepted = _acceptedAacPlan;
        if (accepted is not null && accepted.UserSampleRateHz == _settings.DefaultSampleRateHz &&
            accepted.Signature == AacDecisionPlanner.Signature(tracks, _currentFolderPath, SelectedExportPreset,
                accepted.SampleRateHz, toolchain) &&
            tracks.Where(track => !track.IsExcluded).All(track => _folderAudioEvidence.TryGet(
                TrackPathService.GetTrackPath(_currentFolderPath, track), AudioEvidenceCache.ProbeSchemaIdentity +
                AacToolchainIdentityCache.Identity(_ffmpegStatus.FfprobePath!), out _)))
        {
            _aacPreviewCancellation.Dispose();
            _aacPreviewCancellation = null;
            return;
        }
        _aacPreviewTask = RecalculateAacPreviewAsync(key, user, _aacPreviewCancellation);
    }

    private async Task RecalculateAacPreviewAsync(string key, ExportPreset user, CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(150, cancellation.Token);
            var root = _currentFolderPath;
            var tracks = Tracks.Select(track => new TrackInfo { SourceId = track.SourceId,
                FilePath = TrackPathService.GetTrackPath(root, track), FileName = track.FileName,
                Extension = track.Extension, IsExcluded = track.IsExcluded }).ToArray();
            var options = await new AacDecisionPreflightService().AnalyzeAsync(tracks, root, user,
                _settings.DefaultSampleRateHz, _ffmpegStatus.FfmpegPath!, _ffmpegStatus.FfprobePath!, cancellation.Token,
                forceEncodeAll: _acceptedAacPlan?.ForceEncodeAll == true, evidenceCache: _folderAudioEvidence,
                configuredParallelJobs: ParallelJobsInput);
            cancellation.Token.ThrowIfCancellationRequested();
            if (_aacPreviewKey != key || IsBusy) return;
            _aacPreviewOptions = options;
            ApplyAacPreview(options);
            _ = Dispatcher.BeginInvoke(new Action(async () =>
            {
                if (_aacPreviewKey != key || IsBusy || _aacPreflightRunning || ProjectReviewState.IsReviewBeforeMerge) return;
                if (AacDecisionSelectionService.Select(options, forceEncodeAll: _acceptedAacPlan?.ForceEncodeAll == true).NeedsDialog)
                    await EnsureAacDecisionAsync();
                else ConfirmLoadedPresetBitrateIfNeeded();
            }));
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            // A preview never creates consent or an error modal. Start validates and reports the source error.
            if (_aacPreviewKey == key) StatusText = LocalizationText.Get("Aac.Plan.SourceError");
        }
        finally
        {
            if (ReferenceEquals(_aacPreviewCancellation, cancellation)) _aacPreviewCancellation = null;
            cancellation.Dispose();
        }
    }
    private void ApplyAacPreview(AacDecisionOptions options)
    {
        _aacApplyingPreview = true;
        try
        {
            AacDecisionPlanner.Apply(AacDecisionSelectionService.Select(options,
                forceEncodeAll: _acceptedAacPlan?.ForceEncodeAll == true).Plan, Tracks.ToArray());
            RefreshTrackPresentationViews();
            UpdateFinalStatus(Tracks.Count);
            OnExportPreviewChanged();
        }
        finally { _aacApplyingPreview = false; }
    }

    private bool IsOriginalModeAvailable()
    {
        if (string.IsNullOrWhiteSpace(_currentProjectWorkFolder)) return _acceptedAacPlan?.IsOriginal == true;
        var main = new FolderProjectManifestService().Read(_currentProjectWorkFolder);
        if (main?.AacPlan?.ForceEncodeAll == true) return false;
        var original = main?.OriginalAacPlan;
        if (original is null || original.SourceSignature != AacDecisionPlanner.Signature(Tracks.ToArray(), _currentFolderPath,
            "", 0, original.ToolchainIdentity, sourceOnly: true)) return false;
        var evidence = Tracks.Select((t, i) =>
        {
            var saved = original.Tracks.FirstOrDefault(item => item.SourceId == t.SourceId);
            return new TrackInfo { SourceId = t.SourceId, IsExcluded = t.IsExcluded, DurationTicks = saved?.DurationTicks,
                AudioValidationPassed = saved is not null, AacOptimizationEligible = saved?.Eligible == true,
                AudioBitrateBitsPerSecond = saved?.AudioBitrateBitsPerSecond, SampleRateHz = saved?.SourceSampleRateHz,
                Channels = saved?.SourceChannels, AacConfigurationId = saved?.SourceConfigurationId };
        }).ToArray();
        if (evidence.All(t => t.IsExcluded)) return false;
        try
        {
            var candidate = AacDecisionPlanner.Build(evidence, ExportPreset.Parse(original.UserPreset), original.UserSampleRateHz,
                new Dictionary<string, string?>());
            return candidate.IsOriginal && candidate.SampleRateHz == main!.TargetSampleRateHz;
        }
        catch (InvalidDataException) { return false; }
    }

    private void AacPresetDropDownOpened(object sender, EventArgs e)
    {
        if (sender is not ComboBox combo) return;
        var available = IsOriginalModeAvailable();
        foreach (var value in combo.Items.OfType<string>())
            if (combo.ItemContainerGenerator.ContainerFromItem(value) is ComboBoxItem item)
                item.IsEnabled = !ExportPreset.Parse(value).IsPassThrough || available;
    }

    private void RemoveAacOriginalPresets()
    {
        if (ExportPreset.Parse(SelectedExportPreset).IsPassThrough)
            SetSelectedExportPresetSilently(_settings.SelectedExportPreset);
        foreach (var value in ExportPresets.Where(p => ExportPreset.Parse(p).IsPassThrough).ToArray()) ExportPresets.Remove(value);
    }

    private void RestoreAacOriginalPreset(string folder)
    {
        _acceptedAacPlan = new FolderProjectManifestService().Read(folder)?.AacPlan;
        _aacPlanningEnabled = _acceptedAacPlan is not null;
        _folderAudioEvidence = _aacPlanningEnabled ? AudioEvidenceCache.Load(folder) : new AudioEvidenceCache();
        RemoveAacOriginalPresets();
        var original = new FolderProjectManifestService().Read(folder)?.OriginalAacPlan;
        if (original?.IsOriginal == true)
        {
            _ = original.Preset;
            ExportPresets.Add(original.EffectivePreset);
        }
    }

    private async Task<bool> EnsureAacDecisionAsync(IReadOnlyList<TrackInfo>? supplied = null, bool extension = false,
        CancellationToken cancellationToken = default, bool newFolderImport = false)
    {
        if (_aacPreflightRunning) return false;
        var store = new FolderProjectManifestService();
        var main = string.IsNullOrWhiteSpace(_currentProjectWorkFolder) ? null : store.Read(_currentProjectWorkFolder);
        if (!_aacPlanningEnabled && main?.AacPlan is null) return true;
        // Absence of the new plan is the explicit legacy boundary; never migrate loaded projects.
        if (!extension && !string.IsNullOrWhiteSpace(_currentProjectWorkFolder) && main?.AacPlan is null) return true;
        if (extension && main?.AacPlan is null) return true;
        var tracks = supplied ?? Tracks.ToArray();
        foreach (var track in tracks.Where(t => t.SourceId == Guid.Empty)) track.SourceId = Guid.NewGuid();
        var root = _currentFolderPath;
        var ffmpeg = _ffmpegStatus.FfmpegPath ?? "";
        var ffprobe = _ffmpegStatus.FfprobePath ?? "";
        _aacPreflightRunning = true;
        var wasBusy = IsBusy;
        var wasExporting = IsExporting;
        var previousStatus = StatusText;
        var ownsCancellation = _exportCancellation is null;
        if (ownsCancellation) _exportCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _exportCancellation!.Token;
        try
        {
            if (_aacPreviewTask is { IsCompleted: false })
            {
                IsBusy = true;
                IsExporting = true;
                StatusText = LocalizationText.Get("Aac.Plan.Analyzing");
                await _aacPreviewTask.WaitAsync(token);
            }
            var toolchain = await Task.Run(() => AacDecisionPreflightService.ToolchainIdentity(ffmpeg, ffprobe));
            token.ThrowIfCancellationRequested();
            var current = main?.AacPlan ?? _acceptedAacPlan;
            var selected = ExportPreset.Parse(SelectedExportPreset);
            var signature = AacDecisionPlanner.Signature(tracks, root, SelectedExportPreset,
                current?.SampleRateHz ?? _settings.DefaultSampleRateHz, toolchain);
            var sourceSignature = AacDecisionPlanner.Signature(tracks, root, "", 0, toolchain, sourceOnly: true);
            if (!newFolderImport && !extension && current is not null && current.Version == 1 && current.Signature == signature &&
                (ProjectReviewState.IsReviewBeforeMerge || current.UserSampleRateHz == _settings.DefaultSampleRateHz) &&
                (!_folderAudioEvidence.HasEntries || tracks.Where(track => !track.IsExcluded).All(track =>
                    _folderAudioEvidence.TryGet(TrackPathService.GetTrackPath(root, track),
                        AudioEvidenceCache.ProbeSchemaIdentity + AacToolchainIdentityCache.Identity(ffprobe), out _))) &&
                (main is null || current.PlanIntegrity == AacDecisionPlanner.Integrity(current)))
            {
                _acceptedAacPlan = current;
                AacDecisionPlanner.Apply(current, tracks);
                return true;
            }
            IsBusy = true;
            IsExporting = true;
            StatusText = LocalizationText.Get("Aac.Plan.Analyzing");
            var evidence = tracks.Select(t => new TrackInfo { SourceId = t.SourceId, FilePath = TrackPathService.GetTrackPath(root, t),
                FileName = t.FileName, Extension = t.Extension, IsExcluded = t.IsExcluded }).ToArray();
            var user = selected.IsPassThrough ? ExportPreset.Parse(current?.UserPreset ?? _settings.SelectedExportPreset) : selected;

            // Review changes are an explicit target selection and retain the confirmed project rate.
            var review = !extension && current is not null && ProjectReviewState.IsReviewBeforeMerge &&
                current.SourceSignature == sourceSignature;
            var requestKey = AacRequestKey(evidence, root, user, toolchain, review ? current!.SampleRateHz : null,
                current?.ForceEncodeAll == true);
            var options = _aacPreviewKey == requestKey && _aacPreviewOptions is not null ? _aacPreviewOptions :
                await new AacDecisionPreflightService().AnalyzeAsync(evidence, root, user,
                _settings.DefaultSampleRateHz, ffmpeg, ffprobe, token, review ? current!.SampleRateHz : null,
                forceEncodeAll: current?.ForceEncodeAll == true, evidenceCache: _folderAudioEvidence,
                configuredParallelJobs: ParallelJobsInput);
            // Cached preview options are immutable decisions, but their source projection still belongs to this invocation.
            if (_aacPreviewKey == requestKey && _aacPreviewOptions is not null)
                AacDecisionPlanner.Apply(options.Optimized, evidence);
            if (requestKey != AacRequestKey(evidence, root, user, toolchain, review ? current!.SampleRateHz : null,
                    current?.ForceEncodeAll == true) ||
                toolchain != AacDecisionPreflightService.ToolchainIdentity(ffmpeg, ffprobe) ||
                sourceSignature != AacDecisionPlanner.Signature(tracks, root, "", 0, toolchain, sourceOnly: true))
                throw new IOException(LocalizationText.Get("Aac.Plan.SourceError"));
            var selection = AacDecisionSelectionService.Select(options, review, current?.ForceEncodeAll == true,
                requireReuseConfirmation: newFolderImport);
            var accepted = selection.Plan;
            if (selected.IsPassThrough)
            {
                if (!options.Optimized.IsOriginal || current is null || options.Optimized.Channels != selected.Channels ||
                    options.Optimized.SampleRateHz != current.SampleRateHz)
                {
                    AppDialogService.Info(this, LocalizationText.Get("Aac.Plan.Title"), LocalizationText.Get("Aac.Plan.InvalidOriginal"));
                    return false;
                }
                accepted = options.Optimized;
            }
            if (selection.NeedsDialog)
            {
                var dialog = new AacDecisionDialog(options);
                ModalDialogService.ShowDialog(dialog, this);
                if (dialog.Result is not (AppDialogResult.Yes or AppDialogResult.No)) return false;
                accepted = AacDecisionSelectionService.Confirm(options,
                    dialog.Result == AppDialogResult.No ? AacDecisionChoice.EncodeAll : AacDecisionChoice.PassThrough)!;
            }
            else if (review && !accepted.ForceEncodeAll) accepted.Decision = "ReviewPreset";
            token.ThrowIfCancellationRequested();
            if (requestKey != AacRequestKey(evidence, root, user, toolchain, review ? current!.SampleRateHz : null,
                    current?.ForceEncodeAll == true) ||
                toolchain != AacDecisionPreflightService.ToolchainIdentity(ffmpeg, ffprobe) ||
                sourceSignature != AacDecisionPlanner.Signature(tracks, root, "", 0, toolchain, sourceOnly: true))
                throw new IOException(LocalizationText.Get("Aac.Plan.SourceError"));
            for (var i = 0; i < tracks.Count; i++)
            {
                if (tracks[i].IsExcluded) continue;
                tracks[i].SampleRateHz = evidence[i].SampleRateHz;
                tracks[i].AudioBitrateBitsPerSecond = evidence[i].AudioBitrateBitsPerSecond;
                tracks[i].AacConfigurationId = evidence[i].AacConfigurationId;
                tracks[i].AacOptimizationEligible = evidence[i].AacOptimizationEligible;
                tracks[i].AudioValidationPassed = evidence[i].AudioValidationPassed;
                tracks[i].DurationTicks = evidence[i].DurationTicks;
                tracks[i].Channels = evidence[i].Channels;
                tracks[i].AudioProfile = evidence[i].AudioProfile ?? tracks[i].AudioProfile;
            }
            accepted.ToolchainIdentity = toolchain;
            accepted.SourceSignature = sourceSignature;
            accepted.Signature = AacDecisionPlanner.Signature(tracks, root, accepted.EffectivePreset, accepted.SampleRateHz, toolchain);
            _acceptedAacPlan = accepted;
            AacDecisionPlanner.Apply(accepted, tracks);
            if (accepted.IsOriginal && !ExportPresets.Contains(accepted.EffectivePreset)) ExportPresets.Add(accepted.EffectivePreset);
            if (!extension && main is not null)
            {
                store.SavePlan(_currentProjectWorkFolder, accepted);
                _folderAudioEvidence.Save(_currentProjectWorkFolder);
            }
            SetSelectedExportPresetSilently(accepted.EffectivePreset);
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception error)
        {
            AppDialogService.Error(this, LocalizationText.Get("Aac.Plan.Title"), error.Message);
            return false;
        }
        finally
        {
            if (ownsCancellation) { _exportCancellation?.Dispose(); _exportCancellation = null; }
            IsBusy = wasBusy;
            IsExporting = wasExporting;
            StatusText = previousStatus;
            _aacPreflightRunning = false;
        }
    }

    private void PersistRebasedAacPlan(IReadOnlyList<TrackInfo> planned, IReadOnlyList<TrackInfo> actual, string root)
    {
        if (_acceptedAacPlan is not { } plan) return;
        var decisions = plan.Tracks.Select(item => (Track: planned[item.Occurrence], Decision: item)).ToArray();
        plan.Tracks = actual.Select((track, i) =>
        {
            var match = decisions.FirstOrDefault(x => x.Track.SourceId == track.SourceId);
            return match.Decision is null || track.IsExcluded ? null : match.Decision with { SourceId = track.SourceId, Occurrence = i };
        }).Where(item => item is not null).Cast<AacTrackDecision>().ToList();
        plan.Signature = AacDecisionPlanner.Signature(actual, root, plan.EffectivePreset, plan.SampleRateHz, plan.ToolchainIdentity);
        plan.SourceSignature = AacDecisionPlanner.Signature(actual, root, "", 0, plan.ToolchainIdentity, sourceOnly: true);
        AacDecisionPlanner.Apply(plan, actual);
        new FolderProjectManifestService().SavePlan(_currentProjectWorkFolder, plan);
        _folderAudioEvidence.RebindVerifiedCopies(actual, root);
        _folderAudioEvidence.Save(_currentProjectWorkFolder);
    }

    private void PreserveAacPlanAfterAcquisitionPause(IReadOnlyList<TrackInfo> tracks, string sourceRoot, string originals)
    {
        if (_acceptedAacPlan is null) return;
        var identities = new SourceCopyIdentityStore().Load(_currentProjectWorkFolder);
        foreach (var track in tracks)
        {
            var relative = Path.GetRelativePath(sourceRoot, TrackPathService.GetTrackPath(sourceRoot, track));
            var target = Path.Combine(originals, relative);
            var identity = identities.SingleOrDefault(item => TrackPathService.PathEquals(
                Path.Combine(_currentProjectWorkFolder, item.TargetRelativePath), target));
            if (identity is null || !File.Exists(target)) throw new IOException(LocalizationText.Get("Aac.Plan.SourceError"));
            track.SourceId = identity.SourceId;
            track.FilePath = target;
            track.RelativeFolder = Path.GetDirectoryName(relative) ?? "";
        }
        _currentFolderPath = originals;
        PersistRebasedAacPlan(tracks, tracks, originals);
    }
}
