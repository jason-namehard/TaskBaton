using System.Windows.Controls;
using System.Windows.Threading;
using AgentRelay.Core;

namespace AgentRelay.Desktop;

public partial class MainWindow
{
    private readonly DispatcherTimer relocationMaintenanceTimer = new() { Interval = TimeSpan.FromMinutes(1) };

    private bool CompletePendingRelayMove()
    {
        if (sendingTasks.Count != 0 || service.TryCompletePendingRelocation() is not { } next) return false;
        var previousHome = service.Home;
        CompleteRelocation(next, previousHome);
        return true;
    }

    private void StartRelocationMaintenance()
    {
        relocationMaintenanceTimer.Tick += async (_, _) =>
        {
            if (busy) return;
            var drafts = CaptureInputs();
            // The periodic pass also catches missed receipt events and elapsed unattended limits after sleep.
            var refresh = Navigation.SelectedItem is ListBoxItem { Tag: "Settings" } ||
                Navigation.SelectedItem is ListBoxItem { Tag: "Home" } && taskId is not null &&
                service.Tasks().Any(view => view.Task.TaskId == taskId && view.Task.UnattendedRun is not null);
            await Run(() => Task.CompletedTask, "Ready", refresh: refresh,
                clearError: false, reportStatus: false);
            RestoreInputs(drafts);
        };
        Closed += (_, _) => relocationMaintenanceTimer.Stop();
        relocationMaintenanceTimer.Start();
    }
}
