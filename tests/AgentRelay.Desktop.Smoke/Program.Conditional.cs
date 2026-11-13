using System.IO;
using System.Windows.Controls;
using AgentRelay.Core;
using AgentRelay.Desktop;

namespace AgentRelay.Desktop.Smoke;

internal static partial class Program
{
    private static async Task CheckConditionalHandoff(MainWindow owner, string root)
    {
        var fixture = Path.Combine(root, "conditional");
        var station = new RelayService(Path.Combine(fixture, "station"));
        station.Initialize();
        station.RegisterManual("conditional-a", "fixture", "Tester");
        station.RegisterManual("conditional-b", "fixture", "Implementer");
        Appearance.Save(station.Home, new DesktopPreferences("en", NotificationSound: "none"));
        var window = new MainWindow(station) { Owner = owner };
        window.Show();
        try
        {
            await Click(window, "Refresh");
            foreach (var outcome in new[] { "passed", "failed" })
            {
                await Modal<CliHandoffDialog>(window, "New automatic transfer task", dialog =>
                {
                    Find<RadioButton>(dialog, "CliTaskRelay").IsChecked = true;
                    var templates = Find<ComboBox>(dialog, "CliTaskRelayTemplate");
                    templates.SelectedItem = "Stop if tests pass";
                    Find<ComboBox>(dialog, "CliTaskRelayStart").SelectedIndex = 1;
                    var summary = Find<TextBlock>(dialog, "CliTaskRelaySummary").Text;
                    Check(summary.Contains("Implementer → Tester") &&
                        summary.Contains("If tests pass, stop. If tests fail, continue with Implementer."),
                        "The conditional template previews both routes with the chosen Agent names");
                    Find<TextBox>(dialog, "CliTaskPrompt").Text = $"Conditional fixture: {outcome}";
                    Press(dialog, "SendCliTask");
                    return Task.CompletedTask;
                });
                var task = station.Tasks().Select(view => view.Task).Single(task =>
                    task.State == TaskState.AwaitingManual && task.StopIfTestPassesAtStep == 2);
                SelectTask(window, task.TaskId);
                Receipt(task, true, "Build result");
                await Click(window, "Refresh");
                var test = station.Status(task.TaskId).Task;
                Check(test is { ManualTurn: 1, AwaitingAgentId: "conditional-a" },
                    "The template starts work, then hands the current result to the designated tester");
                Receipt(test, false, "Tests passed in prose only");
                await Click(window, "Refresh");
                Check(station.Status(task.TaskId).Task is { ManualTurn: 1, RelayTestOutcome: null } &&
                    FindAll<TextBlock>(window).Any(text => text.Text == "Receipt needs attention"),
                    "The desktop shows a receipt error and waits on the test turn when structured outcome is missing");
                Receipt(test, null, $"Structured test report: {outcome}", outcome);
                await Click(window, "Refresh");
                var after = station.Status(task.TaskId).Task;
                var route = Find<TextBlock>(window, "RelayTestCondition").Text;
                if (outcome == "passed")
                {
                    Check(after is { State: TaskState.Succeeded, ManualTurn: 1 } && route.Contains("ended at the test step"),
                        "The desktop shows an early finish on the tester's structured pass");
                    await SaveImage(window, Path.Combine(fixture, "passed.png"));
                }
                else
                {
                    Check(after is { State: TaskState.AwaitingReply, ManualTurn: 2, AwaitingAgentId: "conditional-b" } &&
                        route.Contains("did not pass") && File.ReadAllText(after.ManualCurrentFile!) == "Structured test report: failed",
                        "The desktop shows the failed test and hands the report to the planned modifier");
                    Receipt(after, false, "Applied the test report.");
                    await Click(window, "Refresh");
                    Check(station.Status(task.TaskId).Task is { State: TaskState.Succeeded, CompletionNote: "modified_not_retested" },
                        "The desktop keeps the failed test result and marks modifications that were never retested");
                    await SaveImage(window, Path.Combine(fixture, "failed-then-modified.png"));
                }
            }
        }
        finally { window.RequestExit(); }

        void Receipt(TaskRecord task, bool? needsReply, string report, string? outcome = null)
        {
            var file = station.TurnReplyPath(task);
            File.WriteAllText(file, report);
            File.WriteAllText(station.ManualReceiptPath(task), RelayJson.Write(new ManualReceipt(task.TaskId, task.AttemptId,
                task.AwaitingAgentId!, task.ManualTurn, "done", needsReply, file, Outcome: outcome)));
        }
    }
}
