using System.IO;
using System.Reflection;
using System.Windows.Controls;
using AgentRelay.Core;
using AgentRelay.Desktop;

namespace AgentRelay.Desktop.Smoke;

internal static partial class Program
{
    private static async Task CheckUnattendedHandoff(MainWindow owner, string root)
    {
        var fixture = Path.Combine(root, "unattended");
        var station = new RelayService(Path.Combine(fixture, "station"));
        station.Initialize();
        var log = Path.Combine(fixture, "calls.log");
        var script = Path.Combine(fixture, "bridge.ps1");
        File.WriteAllText(script, """
            param([string]$Log, [string]$Agent, [string]$Session, [string]$Prompt)
            [IO.File]::AppendAllLines($Log, [string[]]@($Agent))
            $id = if ($Session -eq 'create') { "created-$Agent" } else { $Session }
            @{session_id=$id; reply='Fixture turn ended'; accepted=$true}|ConvertTo-Json -Compress
            """);
        foreach (var id in new[] { "unattended-a", "unattended-b" })
        {
            station.RegisterManual(id, "fixture", id);
            CliCommand Command(string session) => new("pwsh",
                ["-NoLogo", "-NoProfile", "-NonInteractive", "-File", script, log, id, session, "{prompt}"], null, fixture);
            var recipe = new CliRecipe(1, "Tray fixture", "fixture", Command("create"), Command("{session_id}"), [],
                "fixture", "local tests", "MIT", "Local simulation");
            var proof = new CliProbe("create-" + id, "create", "fixture", "verified-" + id,
                true, true, null, DateTimeOffset.UtcNow, FileWritten: true);
            SaveExtension(station, new(id, "fixture-" + id, recipe, proof, proof with { ProbeId = "resume-" + id, Operation = "resume" }));
        }
        station.SetPermission("unattended-b", "unattended-a", "allow");
        var task = station.CreateCliHandoff(new("unattended-a", "unattended-b", "Build, test, and revise the fixture.",
            EnableDirection: true, RelayPlan: new([
                new("unattended-b", RelayStepRole.Work), new("unattended-a", RelayStepRole.Test),
                new("unattended-b", RelayStepRole.Modify)]))).Task;
        station.UseNewTaskSession(task.TaskId, "unattended-a");
        Appearance.Save(station.Home, new DesktopPreferences("en", CloseToTray: true, NotificationSound: "none"));

        TaskRecord Current() => station.Status(task.TaskId).Task;
        void FinishTurn(TaskRecord turn, string content)
        {
            var reply = station.TurnReplyPath(turn);
            File.WriteAllText(reply, content);
            File.WriteAllText(station.ManualReceiptPath(turn), RelayJson.Write(new ManualReceipt(turn.TaskId,
                turn.AttemptId, turn.AwaitingAgentId!, turn.ManualTurn, "done", true, reply)));
        }

        var window = new MainWindow(station) { Owner = owner };
        window.Show();
        try
        {
            await Click(window, "Refresh");
            SelectTask(window, task.TaskId);
            await Modal<UnattendedHandoffDialog>(window, "Start unattended run", async dialog =>
            {
                Check(FindAll<TextBlock>(dialog).Any(text => text.Text.Contains("Balance, charges and quota are unknown.", StringComparison.Ordinal)),
                    "Unattended run explains that model billing information is unknown");
                var sends = Find<TextBox>(dialog, "UnattendedSendLimit");
                sends.Text = "0";
                Press(dialog, "ConfirmUnattendedRun");
                Check(dialog.IsVisible && Current().UnattendedRun is null,
                    "Unattended run rejects an invalid send limit without activating");
                sends.Text = "2";
                Find<TextBox>(dialog, "UnattendedMinutes").Text = "60";
                await SaveImage(dialog, Path.Combine(fixture, "unattended-setup.png"));
                Press(dialog, "ConfirmUnattendedRun");
            });
            await Wait(() => Current() is { ManualTurn: 0, CliFileStatus: "submitted", UnattendedRun.SendsStarted: 1 });
            window.RequestClose();
            Check(!window.IsVisible, "Unattended run continues with the window hidden in the tray");
            FinishTurn(Current(), "Implementation ready.");
            await Wait(() => Current() is { ManualTurn: 1, CliFileStatus: "submitted", UnattendedRun.SendsStarted: 2 });
            Check(!window.IsVisible && File.ReadAllLines(log).SequenceEqual(new[] { "unattended-b", "unattended-a" }),
                "Unattended run imports a receipt and sends the next step from the tray");
            FinishTurn(Current(), "Test report ready.");
            await Wait(() => Current().ManualTurn == 2);
            typeof(MainWindow).GetMethod("OpenFromTray", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
            await Click(window, "Refresh");
            Check(Current() is { ManualTurn: 2, State: TaskState.AwaitingReply, UnattendedRun.SendsStarted: 2 } &&
                station.CanSendFileHandoff(Current()) && File.ReadAllLines(log).Length == 2 &&
                FindAll<TextBlock>(window).Any(text => text.Text == "Unattended delivery stopped at the send limit"),
                "Unattended run visibly stops at the send limit while leaving the pending step available for manual delivery");
            await SaveImage(window, Path.Combine(fixture, "unattended-limit.png"));
        }
        finally { window.RequestExit(); }
    }
}
