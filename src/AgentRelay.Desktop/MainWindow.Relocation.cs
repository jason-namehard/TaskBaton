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
            await Run(() => Task.CompletedTask, "Ready", refresh: Navigation.SelectedItem is ListBoxItem { Tag: "Settings" },
                clearError: false, reportStatus: false);
            RestoreInputs(drafts);
        };
        Closed += (_, _) => relocationMaintenanceTimer.Stop();
        relocationMaintenanceTimer.Start();
    }
}
