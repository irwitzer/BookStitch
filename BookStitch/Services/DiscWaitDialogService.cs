using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BookStitch.Services;

public sealed record DiscWaitDialogRequest(
    int DiscNumber,
    int TotalDiscs,
    string MediaName,
    string InitialInstruction,
    string HintText,
    string DriveDisplayName = "",
    Action<DiscPollingDisplayState>? NotifyDisplayState = null);

/// <summary>
/// Bewährter produktiver Legacy-Fallback für den CD-Wartedialog.
///
/// WICHTIG FÜR ENTWICKLER UND LLMs:
/// Diese Implementierung ist absichtlich weiterhin kompilierbar und auswählbar.
/// Sie ist kein Dead Code und darf nicht ohne ausdrückliche Projektentscheidung
/// entfernt, vereinfacht oder mit der Boxed-Variante zusammengeführt werden.
/// Ihr Zweck ist der sofortige Rückfall bei Problemen mit der neuen Darstellung
/// sowie der direkte Vergleich beider Dialogvarianten mit identischer Fachlogik.
/// Änderungen nur bei einem nachgewiesenen Fehler in der Legacy-Variante.
/// </summary>
public sealed class DiscWaitDialogService : IDiscWaitDialogService
{
    public async Task<DiscWaitDialogOutcome> WaitForDiscAsync(
        Window owner,
        DiscWaitDialogRequest request,
        Func<CancellationToken, Task<DiscPollingResult>> checkDiscAsync,
        Action<string> setStatusText,
        Action<string> setProgressText,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(checkDiscAsync);

        var mediaName = string.IsNullOrWhiteSpace(request.MediaName) ? "Disc" : request.MediaName.Trim();
        var dialogTitle = $"{mediaName} {request.DiscNumber} von {request.TotalDiscs} einlegen";
        var headingText = string.IsNullOrWhiteSpace(request.DriveDisplayName)
            ? dialogTitle
            : $"{dialogTitle}  ·  {request.DriveDisplayName.Trim()}";
        var primaryButtonText = "Fortfahren";

        var userDeferred = false;
        var resultSource = new TaskCompletionSource<DiscWaitDialogOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var isChecking = false;
        var closeByCode = false;
        var displayedState = DiscPollingDisplayState.Waiting;
        var lastNotifiedState = DiscPollingDisplayState.Waiting;

        var statusBlock = new TextBlock
        {
            Text = request.InitialInstruction,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0),
            Foreground = GetDialogBrush(owner, "MainTextBrush", Colors.White)
        };

        var hintBlock = new TextBlock
        {
            Text = request.HintText,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0),
            Foreground = GetDialogBrush(owner, "MutedTextBrush", Color.FromRgb(158, 171, 184))
        };

        var primaryButtonStyle = owner.TryFindResource("PrimaryButtonStyle") as Style;
        var secondaryButtonStyle = owner.TryFindResource("SecondaryButtonStyle") as Style;

        var checkButton = new Button
        {
            Content = primaryButtonText,
            Width = 120,
            Height = 34,
            Margin = new Thickness(0, 18, 8, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            Style = primaryButtonStyle
        };

        var cancelButton = new Button
        {
            Content = "Unterbrechen",
            Width = 150,
            Height = 34,
            Margin = new Thickness(0, 18, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            Style = secondaryButtonStyle
        };

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        buttonPanel.Children.Add(checkButton);
        buttonPanel.Children.Add(cancelButton);

        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(new TextBlock
        {
            Text = headingText,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = GetDialogBrush(owner, "MainTextBrush", Colors.White)
        });
        panel.Children.Add(statusBlock);
        panel.Children.Add(hintBlock);
        panel.Children.Add(buttonPanel);

        var dialog = new Window
        {
            Title = dialogTitle,
            Icon = Application.GetResourceStream(new Uri("/Assets/Icons/BookStitchAppIcon-Simplified-multisize.ico", UriKind.Relative))?.Stream is { } iconStream
                ? BitmapFrame.Create(iconStream)
                : null,
            Owner = owner,
            Width = 560,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = GetDialogBrush(owner, "PanelBackgroundBrush", Color.FromRgb(24, 30, 36)),
            Content = panel,
            Topmost = false
        };

        var pollTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };

        async Task CheckAsync(bool manualCheck)
        {
            if (isChecking || token.IsCancellationRequested || userDeferred || !dialog.IsVisible)
                return;

            isChecking = true;
            if (manualCheck)
            {
                checkButton.IsEnabled = false;
                SetTextIfChanged(statusBlock, $"{mediaName} {request.DiscNumber} von {request.TotalDiscs} wird geprüft …");
                setProgressText($"{mediaName} {request.DiscNumber} wird geprüft …");
            }

            setStatusText($"Warte auf {mediaName} {request.DiscNumber} von {request.TotalDiscs} …");

            try
            {
                var result = await checkDiscAsync(token);
                if (userDeferred || !dialog.IsVisible)
                    return;

                var keepActionableNoticeVisible =
                    DiscPollingDisplayStateRules.ShouldKeepNoticeVisible(displayedState, result.DisplayState);

                if (!keepActionableNoticeVisible)
                {
                    SetTextIfChanged(statusBlock, result.DialogText);
                    displayedState = result.DisplayState;
                }

                if (result.DisplayState != lastNotifiedState)
                {
                    lastNotifiedState = result.DisplayState;
                    request.NotifyDisplayState?.Invoke(result.DisplayState);
                }

                setStatusText(result.StatusText);
                setProgressText(result.ProgressText);

                if (result.CanImport)
                {
                    closeByCode = true;
                    resultSource.TrySetResult(DiscWaitDialogOutcome.Ready);
                    pollTimer.Stop();
                    dialog.Close();
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                closeByCode = true;
                resultSource.TrySetCanceled(token);
                pollTimer.Stop();
                if (dialog.IsVisible)
                    dialog.Close();
            }
            catch (Exception ex)
            {
                SetTextIfChanged(
                    statusBlock,
                    $"{mediaName} {request.DiscNumber} von {request.TotalDiscs} konnte gerade nicht gelesen werden.\n\n" +
                    $"BookStitch prüft gleich automatisch weiter. Du kannst auch auf „{primaryButtonText}“ klicken.\n\n" +
                    ex.Message);
                setStatusText($"Warte auf {mediaName} {request.DiscNumber}: Laufwerk noch nicht bereit.");
                setProgressText("Automatische Erkennung läuft …");
            }
            finally
            {
                isChecking = false;
                if (manualCheck && dialog.IsVisible && !token.IsCancellationRequested && !userDeferred)
                    checkButton.IsEnabled = true;
            }
        }

        pollTimer.Tick += async (_, _) => await CheckAsync(false);
        checkButton.Click += async (_, _) => await CheckAsync(true);
        cancelButton.Click += (_, _) =>
        {
            userDeferred = true;
            resultSource.TrySetResult(DiscWaitDialogOutcome.Deferred);
            pollTimer.Stop();
            dialog.Close();
        };
        dialog.Closed += (_, _) =>
        {
            pollTimer.Stop();
            if (!closeByCode && !userDeferred)
                resultSource.TrySetResult(DiscWaitDialogOutcome.Deferred);
        };
        dialog.Loaded += async (_, _) =>
        {
            setStatusText($"Warte auf {mediaName} {request.DiscNumber} von {request.TotalDiscs} …");
            setProgressText("Automatische Erkennung läuft …");
            pollTimer.Start();
            try
            {
                await Task.Delay(1200, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            await CheckAsync(false);
        };

        using var cancellationRegistration = token.Register(() =>
            owner.Dispatcher.BeginInvoke(() =>
            {
                closeByCode = true;
                resultSource.TrySetCanceled(token);
                pollTimer.Stop();
                if (dialog.IsVisible)
                    dialog.Close();
            }));

        dialog.ShowDialog();
        return await resultSource.Task;
    }

    private static void SetTextIfChanged(TextBlock textBlock, string value)
    {
        if (!string.Equals(textBlock.Text, value, StringComparison.Ordinal))
            textBlock.Text = value;
    }

    private static SolidColorBrush GetDialogBrush(FrameworkElement owner, string resourceKey, Color fallbackColor)
    {
        return owner.TryFindResource(resourceKey) is SolidColorBrush brush
            ? brush
            : new SolidColorBrush(fallbackColor);
    }
}
