using System.IO;
using System.Windows;
using System.Windows.Controls;
using AgentRelay.Core;
using AgentRelay.Desktop;

namespace AgentRelay.Desktop.Smoke;

internal static partial class Program
{
    private static async Task CheckStageOneWorkflow(MainWindow owner, string root)
    {
        var fixture = Path.Combine(root, "stage-one");
        var home = Path.Combine(fixture, "station");
        var clock = new AdjustableClock();
        var station = new RelayService(home, clock);
        station.Initialize();
        const string author = "stage-a", reviewer = "stage-b";
        const string workSession = "fixture-work-session", reviewSession = "fixture-review-session";
        station.RegisterManual(author, "fixture", "Author");
        station.RegisterManual(reviewer, "fixture", "Tester");
        var log = Path.Combine(fixture, "calls.log");
        var script = Path.Combine(fixture, "bridge.ps1");
        File.WriteAllText(script, """
            param([string]$Log, [string]$Session, [string]$Prompt)
            $interrupted = !(Test-Path -LiteralPath $Log)
            [IO.File]::AppendAllLines($Log, [string[]]@($Session))
            if ($Session -eq 'create') {
                $Session = if ($interrupted) { 'fixture-work-session' } else { 'fixture-review-session' }
            }
            @{type='session';sessionId=$Session}|ConvertTo-Json -Compress
            [Console]::Out.Flush()
            if ($interrupted) { exit 1 }
            @{type='status';phase='turn_end';reason=@{kind='completed'}}|ConvertTo-Json -Compress
            @{type='final';text='Fixture turn ended'}|ConvertTo-Json -Compress
            """);
        CliCommand Command(string session) => new("pwsh",
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-File", script, log, session, "{prompt}"], null, fixture);
        station.ImportCliRecipe(reviewer, RelayJson.Write(new CliRecipe(1, "Stage one fixture", "fixture",
            Command("create"), Command("{session_id}"), [], "fixture", "local tests", "MIT", "Local simulation", "dsh-jsonl")));
        Appearance.Save(home, new DesktopPreferences("en", NotificationSound: "none"));

        var entry = new MainWindow(station) { Owner = owner };
        entry.Show();
        TaskRecord relay;
        try
        {
            await Wait(() => Find<TextBlock>(entry, "StatusText").Text != "Working…");
            await Modal<CliHandoffDialog>(entry, "New automatic transfer task", dialog =>
            {
                Find<RadioButton>(dialog, "CliTaskRelay").IsChecked = true;
                Find<ComboBox>(dialog, "CliTaskRelayTemplate").SelectedIndex = 3;
                Find<ComboBox>(dialog, "CliTaskRelayStart").SelectedIndex = 1;
                Find<TextBox>(dialog, "CliTaskPrompt").Text = "Build, test, and revise the fixture.";
                Find<CheckBox>(dialog, "CliTaskEnableDirections").IsChecked = true;
                Press(dialog, "SendCliTask");
                return Task.CompletedTask;
            });
            await Wait(() => station.Tasks().Single().Task.CliFileStatus == "unknown");
            relay = station.Tasks().Single().Task;
            Check(relay.RelaySteps!.Select(step => (step.AgentId, step.Role)).SequenceEqual(
                new[] { (reviewer, RelayStepRole.Work), (author, RelayStepRole.Test), (reviewer, RelayStepRole.Modify) }),
                "Stage one starts a Work → Test → Modify task from the desktop relay template");
        }
        finally { entry.RequestExit(); }

        station = new RelayService(home, clock);
        var window = new MainWindow(station) { Owner = owner };
        window.Show();
        TaskRecord Read(TaskRecord task) => station.Status(task.TaskId).Task;
        void Receipt(TaskRecord task, bool? needsReply = null, string? content = null, string? outcome = null, string[]? open = null)
        {
            var result = content is null ? null : station.TurnReplyPath(task);
            if (result is not null) File.WriteAllText(result, content!);
            File.WriteAllText(station.ManualReceiptPath(task), RelayJson.Write(new ManualReceipt(
                task.TaskId, task.AttemptId, task.AwaitingAgentId!, task.ManualTurn, "done", needsReply, result,
                Outcome: outcome, OpenItems: open)));
        }
        async Task Send(TaskRecord task, string session)
        {
            await Click(window, "Send via bridge");
            await Wait(() => Read(task) is { State: TaskState.Dispatched, CliFileSessionId: var id } && id == session);
            await Click(window, "Refresh");
        }

        try
        {
            await Click(window, "Refresh");
            SelectTask(window, relay.TaskId);
            Find<Expander>(window, "RecoveryConversation").IsExpanded = true;
            window.UpdateLayout();
            Check(Find<TextBox>(window, "RecoverySessionId") is { Text: workSession, IsReadOnly: true } &&
                Read(relay) is { ManualTurn: 0, CliFileStatus: "unknown" } && File.ReadAllLines(log).Length == 1,
                "Stage one restores the exact interrupted conversation after reopening and leaves delivery unconfirmed");
            await SaveImage(window, Path.Combine(fixture, "01-recovery.png"));
            await ConfirmAction(window, "Not received · Allow resending", true);
            Check(Read(relay).ManualResendTurn == 0, "Stage one authorizes a resend for the interrupted turn only");
            Receipt(relay, true, "First version of the fixture.");
            await Click(window, "Refresh");
            relay = Read(relay);
            Check(relay is { ManualTurn: 1, AwaitingAgentId: author, ManualResendTurn: null } &&
                File.ReadAllLines(log).Length == 1, "Stage one accepts a late receipt, clears resend permission and advances to testing");

            Receipt(relay, true, "Test report: add the build log.");
            await Click(window, "Refresh");
            relay = Read(relay);
            Check(relay is { ManualTurn: 2, AwaitingAgentId: reviewer }, "Stage one routes the test report to the planned revision");
            await Send(relay, workSession);
            Receipt(relay, false, "Revised fixture; build log missing.");
            await Click(window, "Refresh");
            relay = Read(relay);
            Check(relay is { State: TaskState.Succeeded, CompletionNote: "modified_not_retested" },
                "Stage one completes the template and records that its final revision still needs a retest");

            var review = station.CreateFileHandoff(new(author, reviewer, relay.ResultPath, "Review the exact relay output.")).Task;
            review = station.ConfigureFileHandoff(review.TaskId, new(new CollaborationOptions(author, "Include the build log.",
                1, ReviewerFinal: true, IndependentAcceptance: true))).Task;
            await Click(window, "Refresh");
            SelectTask(window, review.TaskId);
            Check(review.ManualSourceFile == relay.ResultPath && File.ReadAllText(review.RequestPath) == File.ReadAllText(relay.ResultPath),
                "Stage one feeds the completed relay output into acceptance as a tracked source and an exact copy");
            await Send(review, reviewSession);
            Receipt(review, outcome: "changes_requested", open: ["Build log is missing"]);
            await Click(window, "Refresh");
            review = Read(review);
            Check(review is { State: TaskState.Paused, PauseReason: "round_limit", Collaboration.OpenItems: ["Build log is missing"] } &&
                FindAll<TextBlock>(window).Any(text => text.Text == "Review limit reached · Acceptance pending"),
                "Stage one stops at the review limit and displays that acceptance is pending");
            await SaveImage(window, Path.Combine(fixture, "02-review-limit.png"));

            clock.Advance(TimeSpan.FromDays(4));
            var navigation = Find<ListBox>(window, "Navigation");
            var tasksPage = navigation.SelectedItem;
            navigation.SelectedItem = navigation.Items.Cast<ListBoxItem>().Single(item => Equals(item.Tag, "Settings"));
            await Modal<WorkspaceFilesDialog>(window, "Review relay files", async dialog =>
            {
                var inventory = station.WorkspaceFiles();
                var output = inventory.Single(file => file.Path == relay.ResultPath);
                Check(output is { Owner: WorkspaceFileOwner.User, Status: WorkspaceFileStatus.InUse } &&
                    output.References.Any(reference => reference.TaskId == relay.TaskId && !reference.Active) &&
                    output.References.Any(reference => reference.TaskId == review.TaskId && reference.Active && reference.Use == "source"),
                    "Stage one inventory links the relay output to both its completed relay and its paused review");
                var confirmation = HandleNextDialog<ThemedMessageBox>(dialog, async box =>
                {
                    var preview = Find<TextBlock>(box, "MessageText").Text;
                    Check(preview.Contains(relay.RequestPath) && !preview.Contains(relay.ResultPath) &&
                        !preview.Contains(review.RequestPath) && !preview.Contains(station.ManualReceiptPath(review)),
                        "Stage one cleanup preview includes expired relay files and excludes review inputs and task controls");
                    await SaveImage(box, Path.Combine(fixture, "03-cleanup-preview.png"));
                    Press(box, "MessageOk");
                });
                Press(dialog, "PreviewWorkspaceCleanup");
                await confirmation;
                Check(!File.Exists(relay.RequestPath) && File.Exists(relay.ResultPath) && File.Exists(review.RequestPath) &&
                    station.WorkspaceFiles().Single(file => file.Path == relay.RequestPath).Status == WorkspaceFileStatus.Removed,
                    "Stage one confirms cleanup and keeps the files needed to continue the paused review");
                Press(dialog, "CloseWorkspaceFiles");
            });

            navigation.SelectedItem = tasksPage;
            await Click(window, "Refresh");
            SelectTask(window, review.TaskId);
            await Click(window, "Continue with 2 more rounds");
            review = Read(review);
            Receipt(review, content: "Revised fixture with the build log.", outcome: "submitted");
            await Click(window, "Refresh");
            review = Read(review);
            await Send(review, reviewSession);
            Receipt(review, outcome: "approved");
            await Click(window, "Refresh");
            review = Read(review);
            Check(review is { State: TaskState.Succeeded, Collaboration: { ReviewRound: 2, MaxReviewRounds: 3, OpenItems: [] } } &&
                File.ReadAllText(review.Collaboration.Deliverable!) == "Revised fixture with the build log." &&
                Find<TextBlock>(window, "HandoffRoundSummary").Text == "Completed steps: 3 · Completed reviews: 2 (limit: 3)",
                "Stage one resumes the paused review and finishes immediately when the revised output passes");
            Check(File.ReadAllLines(log).SequenceEqual(new[] { "create", workSession, "create", reviewSession }) &&
                station.TaskSession(review.TaskId, reviewer) is { IndependentReview: true, SessionId: reviewSession },
                "Stage one creates a dedicated review conversation and resumes it for the second review");
            await SaveImage(window, Path.Combine(fixture, "04-accepted.png"));

            Check(!station.WorkspaceFiles().Any(file => Path.GetDirectoryName(file.Path) == Path.Combine(home, ".cli-drafts")),
                "Stage one inventory omits internal CLI drafts after the actual request has been published");
        }
        finally { window.RequestExit(); }
    }
}
