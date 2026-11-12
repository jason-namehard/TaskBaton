using System.IO;
using System.Windows.Controls;
using AgentRelay.Core;
using AgentRelay.Desktop;

namespace AgentRelay.Desktop.Smoke;

internal static partial class Program
{
    private static async Task CheckParallelHandoff(MainWindow owner, string root)
    {
        var fixture = Path.Combine(root, "parallel");
        var project = Path.Combine(fixture, "project");
        Directory.CreateDirectory(project);
        var original = Path.Combine(project, "shared.txt");
        File.WriteAllText(original, "Base version");
        var station = new RelayService(Path.Combine(fixture, "station"));
        station.Initialize();
        foreach (var id in new[] { "parallel-sender", "parallel-worker", "parallel-other", "parallel-merge" })
            station.RegisterManual(id, "fixture", id);
        var script = Path.Combine(fixture, "worker.ps1");
        File.WriteAllText(script, """
            param([string]$Fixture, [string]$Prompt)
            $context = [regex]::Match($Prompt, 'Read (.+context\.json), perform').Groups[1].Value
            $c = Get-Content -LiteralPath $context -Raw | ConvertFrom-Json
            if ((Get-Location).Path -ne $c.parallel.working_directory) { exit 2 }
            $r = $c.receipt_template
            [IO.File]::WriteAllText((Join-Path $Fixture ($r.task_id + '.started')), (Get-Location).Path)
            $gate = Join-Path $Fixture 'release'
            $deadline = [DateTimeOffset]::UtcNow.AddSeconds(25)
            while (!(Test-Path -LiteralPath $gate)) {
                if ([DateTimeOffset]::UtcNow -ge $deadline) { exit 3 }
                Start-Sleep -Milliseconds 50
            }
            [IO.File]::WriteAllText((Join-Path $c.parallel.working_directory 'shared.txt'), $c.request)
            [IO.File]::WriteAllText($c.reply_file, 'Draft complete')
            $r.needs_reply = $false
            $r.result_file = $c.reply_file
            [IO.File]::WriteAllText($c.receipt_file, ($r | ConvertTo-Json -Depth 5))
            @{session_id='fixture-'+$r.task_id;reply='Done';accepted=$true}|ConvertTo-Json -Compress
            """);
        var command = new CliCommand("pwsh",
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-File", script, fixture, "{prompt}"], null, fixture);
        station.ImportCliRecipe("parallel-worker", RelayJson.Write(new CliRecipe(1, "Parallel fixture", "fixture", command,
            null, [], "fixture", "local tests", "MIT", "Local simulation")));
        station.SetPermission("parallel-sender", "parallel-worker", "allow");
        Appearance.Save(station.Home, new DesktopPreferences("en", NotificationSound: "none"));
        var window = new MainWindow(station) { Owner = owner };
        window.Show();
        try
        {
            await Click(window, "Refresh");
            await Modal<NewParallelHandoffDialog>(window, "New parallel handoff", dialog =>
            {
                Find<TextBox>(dialog, "ParallelTitle").Text = "Shared file fixture";
                Find<ComboBox>(dialog, "ParallelFrom").SelectedValue = "parallel-sender";
                Find<ComboBox>(dialog, "ParallelMerger").SelectedValue = "parallel-merge";
                Find<TextBox>(dialog, "ParallelProject").Text = project;
                Find<TextBox>(dialog, "ParallelInputs").Text = "shared.txt";
                Press(dialog, "AddParallelWorker");
                Find<ComboBox>(dialog, "ParallelWorker1").SelectedValue = "parallel-worker";
                Find<ComboBox>(dialog, "ParallelWorker2").SelectedValue = "parallel-worker";
                Find<ComboBox>(dialog, "ParallelWorker3").SelectedValue = "parallel-other";
                Find<TextBox>(dialog, "ParallelWork1").Text = "Branch A";
                Find<TextBox>(dialog, "ParallelWork2").Text = "Branch B";
                Find<TextBox>(dialog, "ParallelWork3").Text = "Branch C";
                Find<TextBox>(dialog, "ParallelMergeInstructions").Text = "Resolve the shared file.";
                Press(dialog, "CreateParallelHandoff");
                return Task.CompletedTask;
            });
            var groupId = station.Tasks().Select(view => view.Task.ParallelGroupId).Distinct().Single()!;
            var group = station.ReviewParallelHandoff(groupId);
            var a = group.Workers[0];
            var b = group.Workers[1];
            var failed = group.Workers[2];
            Check(group.Workers.Length == 3 && a.WorkingDirectory != b.WorkingDirectory &&
                File.ReadAllText(Path.Combine(a.WorkingDirectory, "shared.txt")) == "Base version" &&
                File.ReadAllText(Path.Combine(b.WorkingDirectory, "shared.txt")) == "Base version",
                "Parallel handoff creates separate working copies even for two assignments to the same Agent");
            SelectTask(window, a.TaskId);
            await Click(window, "Send via bridge");
            SelectTask(window, b.TaskId);
            await Click(window, "Send via bridge");
            await Wait(() => File.Exists(Path.Combine(fixture, a.TaskId + ".started")) &&
                File.Exists(Path.Combine(fixture, b.TaskId + ".started")));
            Check(station.Status(a.TaskId).Task.CliFileStatus == "submitting" &&
                station.Status(b.TaskId).Task.CliFileStatus == "submitting",
                "Parallel worker calls overlap in their separate working directories");
            File.WriteAllText(Path.Combine(fixture, "release"), "go");
            var rejected = station.Status(failed.TaskId).Task;
            File.WriteAllText(station.ManualReceiptPath(rejected), RelayJson.Write(new ManualReceipt(rejected.TaskId,
                rejected.AttemptId, failed.AgentId, 0, "failed", false, NextAction: "Fixture failed")));
            await Wait(() => station.ReviewParallelHandoff(groupId).CanMerge, attempts: 200);
            await Wait(() => station.TaskSession(a.TaskId, a.AgentId)?.SessionId is not null &&
                station.TaskSession(b.TaskId, b.AgentId)?.SessionId is not null);
            Check(station.TaskSession(a.TaskId, a.AgentId)?.SessionId == "fixture-" + a.TaskId &&
                station.TaskSession(b.TaskId, b.AgentId)?.SessionId == "fixture-" + b.TaskId,
                "Parallel worker calls create a distinct exact conversation per assignment");
            File.WriteAllText(original, "Edited in the project");
            await Click(window, "Refresh");
            SelectTask(window, a.TaskId);
            await Modal<ParallelResultsDialog>(window, "Parallel results", async dialog =>
            {
                var review = Find<TextBox>(dialog, "ParallelReview").Text;
                var create = Find<Button>(dialog, "CreateParallelMerge");
                Check(review.Contains(a.TaskId) && review.Contains(b.TaskId) && review.Contains("Fixture failed") &&
                    review.Contains("Conflicting branch changes") && review.Contains("Changed in the original project") &&
                    review.Contains("shared.txt"), "Parallel results show both ownership records, the failed branch and the project conflict");
                Check(!create.IsEnabled, "Parallel merge is gated by explicit partial and conflict confirmation");
                Find<CheckBox>(dialog, "ParallelAllowPartial").IsChecked = true;
                Check(!create.IsEnabled, "Partial approval alone does not authorize merging conflicting versions");
                Find<CheckBox>(dialog, "ParallelReviewConflicts").IsChecked = true;
                await SaveImage(dialog, Path.Combine(fixture, "parallel-review.png"));
                Press(dialog, "CreateParallelMerge");
            });
            var merged = station.ReviewParallelHandoff(groupId).Merge!;
            Check(merged.AgentId == "parallel-merge" && File.ReadAllText(original) == "Edited in the project",
                "Confirmed parallel merge creates one handoff for the chosen Agent and preserves the original project");
            File.WriteAllText(Path.Combine(merged.WorkingDirectory, "shared.txt"), "Reviewed result");
            var task = station.Status(merged.TaskId).Task;
            var reply = station.TurnReplyPath(task);
            File.WriteAllText(reply, "Resolved after reviewing both versions and the project copy.");
            File.WriteAllText(station.ManualReceiptPath(task), RelayJson.Write(new ManualReceipt(task.TaskId,
                task.AttemptId, merged.AgentId, 0, "done", false, reply)));
            await Click(window, "Refresh");
            var output = station.ReviewParallelHandoff(groupId).Merge!;
            Check(output.State == TaskState.Succeeded && File.ReadAllText(output.Changes.Single().Snapshot!) == "Reviewed result" &&
                File.ReadAllText(original) == "Edited in the project", "Parallel merge stores the resulting version with the merge Agent as its owner");
        }
        finally { window.RequestExit(); }
    }
}
