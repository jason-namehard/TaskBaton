using System.Globalization;
using System.Windows.Controls;
using AgentRelay.Core;

namespace AgentRelay.Desktop;

public partial class MainWindow
{
    private void AddUnattendedHandoff(Panel panel, TaskRecord task, HandoffProgress progress)
    {
        if (demo || task.ManualContinuous) return;
        var status = service.UnattendedStatus(task);
        if (task.UnattendedRun is { } run)
        {
            panel.Children.Add(Muted(string.Format(CultureInfo.CurrentUICulture,
                T("Unattended sends: {0}/{1} · cutoff {2}"), run.SendsStarted, run.SendLimit,
                run.EndsAt.LocalDateTime.ToString("g", CultureInfo.CurrentCulture)), 12.5, new(0, 8, 0, 0)));
            var message = status switch
            {
                UnattendedHandoffStatus.Stopped => "Unattended delivery stopped",
                UnattendedHandoffStatus.TimeLimit => "Unattended delivery stopped at the time limit",
                UnattendedHandoffStatus.SendLimit => "Unattended delivery stopped at the send limit",
                UnattendedHandoffStatus.SessionUnverified => "Unattended delivery stopped because the conversation could not be verified",
                UnattendedHandoffStatus.HostUnconfirmed => "Unattended delivery is on hold until the host outcome can be confirmed. Check the conversation before sending manually.",
                UnattendedHandoffStatus.Active when task.CliFileStatus == "submitting" =>
                    "Waiting for the host turn to be confirmed before sending again.",
                UnattendedHandoffStatus.Active when progress.Delivery == "cli_ready" ||
                    progress.Delivery == "copy_prompt" && task.State is (TaskState.AwaitingManual or TaskState.AwaitingReply) =>
                    "Unattended delivery needs a verified bridge and a known conversation or a verified new-conversation route.",
                _ => null
            };
            if (message is not null) panel.Children.Add(Note(message));
            if (status == UnattendedHandoffStatus.SessionUnverified) panel.Children.Add(Note(run.SessionIssue!));
        }
        if (progress.Finished || task.UnattendedRun is null &&
            service.CliExtension(task.RequesterId) is null && service.CliExtension(task.RecipientId) is null) return;

        var actions = Row(panel);
        if (status is UnattendedHandoffStatus.Active or UnattendedHandoffStatus.HostUnconfirmed)
            Add(actions, "Stop unattended run", () =>
            {
                service.StopUnattendedHandoff(task.TaskId);
                return Task.CompletedTask;
            });
        else
            Add(actions, "Start unattended run", () =>
            {
                var dialog = new UnattendedHandoffDialog(this, preferences.Language, task.UnattendedRun?.SendLimit ?? 6);
                if (dialog.ShowDialog() == true)
                    service.StartUnattendedHandoff(task.TaskId, dialog.Limits.Sends, dialog.Limits.Minutes);
                else reviewCancelled = true;
                return Task.CompletedTask;
            });
    }
}
