using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using AgentRelay.Core;
using Microsoft.Win32;

namespace AgentRelay.Desktop;

public sealed class NewParallelHandoffDialog : Window
{
    public ParallelHandoffDraft? Result { get; private set; }

    public NewParallelHandoffDialog(Window owner, RelayService service, string language)
    {
        var ui = new EntryDialogLayout(this, owner, UiText.Translate("New parallel handoff", language), language);
        var agents = service.Agents().Select(agent => new HandoffAgentOption(agent.AgentId, agent.DisplayName ?? agent.AgentId)).ToArray();
        var title = ui.Field(ui.Body, "Task title", "ParallelTitle");
        ComboBox AgentChoice(string name, int selected) => new()
        {
            Name = name, ItemsSource = agents, SelectedIndex = selected, DisplayMemberPath = "Name", SelectedValuePath = "Id"
        };
        void Label(Panel panel, string caption, Control target)
        {
            panel.Children.Add(new Label { Content = ui.T(caption), Target = target, Padding = new(0, 0, 0, 2) });
            AutomationProperties.SetAutomationId(target, target.Name);
        }
        var from = AgentChoice("ParallelFrom", 0);
        Label(ui.Body, "Sender", from);
        ui.Body.Children.Add(from);
        var merger = AgentChoice("ParallelMerger", agents.Length - 1);
        Label(ui.Body, "Merge Agent", merger);
        ui.Body.Children.Add(merger);
        var project = ui.Field(ui.Body, "Project folder", "ParallelProject");
        ui.Button(ui.Body, "Choose project folder", "ChooseParallelProject", () =>
        {
            var picker = new OpenFolderDialog { Title = ui.T("Choose project folder") };
            if (picker.ShowDialog(this) == true) project.Text = picker.FolderName;
        });
        var files = ui.Field(ui.Body, "Shared files (one project-relative path per line)", "ParallelInputs", multiline: true);
        files.Height = 76;
        ui.Body.Children.Add(ui.Note("Each assignment gets its own copies of these files. Make proposed changes only in its working folder."));
        var work = new StackPanel();
        ui.Body.Children.Add(work);
        var rows = new List<(Panel Panel, ComboBox Agent, TextBox Instruction, Button Remove)>();
        var next = 0;
        void UpdateRemove()
        {
            foreach (var row in rows) row.Remove.IsEnabled = rows.Count > 2;
        }
        void AddAssignment()
        {
            var index = ++next;
            var row = new DockPanel { Margin = new(0, 0, 0, 8) };
            var agent = AgentChoice("ParallelWorker" + index, Math.Min(index, agents.Length - 1));
            agent.Width = 155;
            AutomationProperties.SetAutomationId(agent, agent.Name);
            DockPanel.SetDock(agent, Dock.Left);
            row.Children.Add(agent);
            var remove = ui.Button(row, "Remove", "RemoveParallelWorker" + index, () =>
            {
                rows.RemoveAll(item => item.Panel == row);
                work.Children.Remove(row);
                UpdateRemove();
            });
            DockPanel.SetDock(remove, Dock.Right);
            var instruction = EntryDialogLayout.Input("ParallelWork" + index);
            AutomationProperties.SetName(instruction, ui.T("Work assignment"));
            instruction.Margin = new(8, 0, 4, 0);
            row.Children.Add(instruction);
            rows.Add((row, agent, instruction, remove));
            work.Children.Add(row);
            UpdateRemove();
        }
        work.Children.Add(ui.Note("Work assignment"));
        AddAssignment();
        AddAssignment();
        ui.Button(ui.Body, "Add assignment", "AddParallelWorker", AddAssignment);
        var merge = ui.Field(ui.Body, "Merge instructions", "ParallelMergeInstructions", multiline: true);
        merge.Height = 76;
        ui.Button(ui.Actions, "Create assignments", "CreateParallelHandoff", () =>
        {
            if (string.IsNullOrWhiteSpace(title.Text) || string.IsNullOrWhiteSpace(merge.Text) ||
                rows.Any(row => string.IsNullOrWhiteSpace(row.Instruction.Text)))
                throw new RelayException("parallel_work_required", "Fill in the title, assignments and merge instructions.");
            if (string.IsNullOrWhiteSpace(project.Text))
                throw new RelayException("parallel_project_required", "Choose project folder");
            Result = new(title.Text, (string)from.SelectedValue, (string)merger.SelectedValue, project.Text.Trim(),
                files.Text.Split(['\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
                rows.Select(row => new ParallelWork((string)row.Agent.SelectedValue, row.Instruction.Text)).ToArray(), merge.Text);
            DialogResult = true;
        }, primary: true);
        ui.Button(ui.Actions, "Cancel", "CancelParallelHandoff", () => DialogResult = false).IsCancel = true;
    }
}

public sealed class ParallelResultsDialog : Window
{
    public string? MergeTaskId { get; private set; }

    public ParallelResultsDialog(Window owner, RelayService service, string groupId, string language)
    {
        var ui = new EntryDialogLayout(this, owner, UiText.Translate("Parallel results", language), language);
        var agents = service.Agents().ToDictionary(agent => agent.AgentId, agent => agent.DisplayName ?? agent.AgentId);
        var preview = ui.Field(ui.Body, "Assignments, file ownership and conflicts", "ParallelReview", multiline: true);
        preview.IsReadOnly = true;
        preview.Height = 410;
        var partial = new CheckBox { Name = "ParallelAllowPartial", Content = ui.T("Merge only the available results") };
        var conflicts = new CheckBox { Name = "ParallelReviewConflicts", Content = ui.T("Send conflicting versions to the merge Agent for review") };
        ui.Body.Children.Add(partial);
        ui.Body.Children.Add(conflicts);
        var review = service.ReviewParallelHandoff(groupId);
        var create = ui.Button(ui.Actions, "Create merge handoff", "CreateParallelMerge", () =>
        {
            MergeTaskId = service.CreateParallelMerge(review, partial.IsChecked == true, conflicts.IsChecked == true).Task.TaskId;
            DialogResult = true;
        }, primary: true);
        ui.Button(ui.Actions, "Refresh", "RefreshParallelReview", () =>
        {
            var receipts = service.ImportManualReceipts();
            review = service.ReviewParallelHandoff(groupId);
            Render();
            var taskIds = review.Workers.Select(worker => worker.TaskId).Append(review.Merge?.TaskId).ToHashSet();
            ui.Status.Text = string.Join("\n", receipts.Errors.Where(error => taskIds.Contains(error.TaskId)).Select(error => error.Message));
        });
        ui.Button(ui.Actions, "Close", "CloseParallelReview", () => DialogResult = false).IsCancel = true;
        partial.Checked += (_, _) => UpdateAction();
        partial.Unchecked += (_, _) => UpdateAction();
        conflicts.Checked += (_, _) => UpdateAction();
        conflicts.Unchecked += (_, _) => UpdateAction();
        Render();

        void Render()
        {
            var lines = new StringBuilder().AppendLine(review.Title)
                .AppendLine(ui.T("Merge Agent") + " · " + agents.GetValueOrDefault(review.MergeAgentId, review.MergeAgentId));
            if (review.SharedConflicts.Length > 0)
                lines.AppendLine().AppendLine(ui.T("Conflicting branch changes")).AppendLine(string.Join("\n", review.SharedConflicts));
            if (review.ProjectConflicts.Length > 0)
                lines.AppendLine().AppendLine(ui.T("Changed in the original project")).AppendLine(string.Join("\n", review.ProjectConflicts));
            foreach (var worker in review.Workers.OrderByDescending(worker => worker.Problem is not null)) AddMember(lines, worker);
            if (review.Merge is { } merge)
            {
                lines.AppendLine().AppendLine(ui.T("Merge handoff"));
                AddMember(lines, merge);
            }
            preview.Text = lines.ToString();
            partial.Visibility = review.CanMerge && review.Partial ? Visibility.Visible : Visibility.Collapsed;
            conflicts.Visibility = review.CanMerge && (review.SharedConflicts.Length > 0 || review.ProjectConflicts.Length > 0)
                ? Visibility.Visible : Visibility.Collapsed;
            partial.IsChecked = false;
            conflicts.IsChecked = false;
            UpdateAction();
        }

        void AddMember(StringBuilder lines, ParallelMember member)
        {
            lines.AppendLine().AppendLine(agents.GetValueOrDefault(member.AgentId, member.AgentId) + " · " +
                ui.T(member.State?.ToString() ?? "Task removed") + " · " + member.TaskId);
            lines.AppendLine(member.Instructions);
            if (member.Problem is not null)
                lines.AppendLine(ui.T(member.Problem == "missing_output" ? "Saved output is missing" : "No accepted result"));
            if (member.Detail is not null) lines.AppendLine(member.Detail);
            if (member.Report is not null) lines.AppendLine(member.Report);
            foreach (var change in member.Changes)
                lines.AppendLine(ui.T(change.Kind switch
                {
                    "added" => "Added", "modified" => "Modified", "deleted" => "Deleted",
                    _ => throw new ArgumentOutOfRangeException(nameof(change), change.Kind, "Unknown parallel change kind.")
                }) +
                    " · " + change.Path + (change.Snapshot is null ? "" : "\n  " + change.Snapshot));
        }

        void UpdateAction() => create.IsEnabled = review.CanMerge &&
            (partial.Visibility != Visibility.Visible || partial.IsChecked == true) &&
            (conflicts.Visibility != Visibility.Visible || conflicts.IsChecked == true);
    }
}
