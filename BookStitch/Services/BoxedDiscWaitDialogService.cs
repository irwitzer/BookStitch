using BookStitch.Dialog;
using System.Windows;

namespace BookStitch.Services;

/// <summary>
/// Alternative Boxed-Darstellung des CD-Wartedialogs. Die Disc-Fachlogik bleibt
/// in den vorhandenen Pollingservices; diese Klasse übernimmt ausschließlich die
/// bewährte Timer-, Fortsetzungs- und Dialogablaufsteuerung für das neue Fenster.
/// Der Legacy-Fallback muss parallel erhalten bleiben.
/// </summary>
public sealed class BoxedDiscWaitDialogService : IDiscWaitDialogService
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
        var headingText = dialogTitle;
        var driveDisplayName = request.DriveDisplayName?.Trim() ?? string.Empty;
        var userDeferred = false;
        var resultSource = new TaskCompletionSource<DiscWaitDialogOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var isChecking = false;
        var closeByCode = false;
        var displayedState = DiscPollingDisplayState.Waiting;
        var lastNotifiedState = DiscPollingDisplayState.Waiting;

        var dialog = new BoxedDiscWaitDialog(
            dialogTitle,
            driveDisplayName,
            headingText,
            request.InitialInstruction,
            request.HintText)
        {
            Owner = owner
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
                dialog.IsManualCheckEnabled = false;
                dialog.SetStatusText($"{mediaName} {request.DiscNumber} von {request.TotalDiscs} wird geprüft …");
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
                    dialog.SetStatusText(result.DialogText);
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
                dialog.SetStatusText(
                    $"{mediaName} {request.DiscNumber} von {request.TotalDiscs} konnte gerade nicht gelesen werden.\n\n" +
                    "BookStitch prüft gleich automatisch weiter. Du kannst auch auf „Fortfahren“ klicken.\n\n" +
                    ex.Message);
                setStatusText($"Warte auf {mediaName} {request.DiscNumber}: Laufwerk noch nicht bereit.");
                setProgressText("Automatische Erkennung läuft …");
            }
            finally
            {
                isChecking = false;
                if (manualCheck && dialog.IsVisible && !token.IsCancellationRequested && !userDeferred)
                    dialog.IsManualCheckEnabled = true;
            }
        }

        pollTimer.Tick += async (_, _) => await CheckAsync(false);
        dialog.ManualCheckRequested += async (_, _) => await CheckAsync(true);
        dialog.DeferRequested += (_, _) =>
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
}
