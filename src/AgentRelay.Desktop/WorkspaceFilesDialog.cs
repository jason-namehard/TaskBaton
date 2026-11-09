using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using AgentRelay.Core;

namespace AgentRelay.Desktop;

public sealed class WorkspaceFilesDialog : Window
{
    private sealed record FileRow(WorkspaceFileEntry File, string Name, string Ownership, string Cleanup, int TaskCount);

    public WorkspaceFilesDialog(Window owner, RelayService service, string language)
    {
        var ui = new EntryDialogLayout(this, owner, UiText.Translate("Relay files", language), language);
        ui.Body.Children.Add(ui.Note("Review Relay copies, user files, and the tasks that reference them. Only expired Relay files with no active reference qualify for cleanup."));
        var table = new DataGrid
        {
            Name = "WorkspaceFilesTable", AutoGenerateColumns = false, IsReadOnly = true,
            Height = 300, SelectionMode = DataGridSelectionMode.Single, SelectionUnit = DataGridSelectionUnit.FullRow
        };
        foreach (var (header, property, width) in new[]
        {
            ("File", nameof(FileRow.Name), 2d), ("File ownership", nameof(FileRow.Ownership), 1.2d),
            ("Cleanup status", nameof(FileRow.Cleanup), 1.2d), ("Task references", nameof(FileRow.TaskCount), 1d)
        })
            table.Columns.Add(new DataGridTextColumn
            {
                Header = ui.T(header), Binding = new Binding(property), Width = new DataGridLength(width, DataGridLengthUnitType.Star)
            });
        ui.Body.Children.Add(table);
        var detail = ui.Field(ui.Body, "File and task references", "WorkspaceFileDetails", multiline: true);
        detail.IsReadOnly = true;
        detail.Height = 138;
        var culture = CultureInfo.CurrentUICulture;
        var files = Array.Empty<WorkspaceFileEntry>();

        string Owner(WorkspaceFileOwner value) => ui.T(value switch
        {
            WorkspaceFileOwner.Relay => "Relay managed file",
            WorkspaceFileOwner.User => "User or project file",
            WorkspaceFileOwner.TaskControl => "Task control file",
            WorkspaceFileOwner.Unknown => "Ownership unverified",
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        });
        string Cleanup(WorkspaceFileStatus value) => ui.T(value switch
        {
            WorkspaceFileStatus.Eligible => "Eligible for cleanup",
            WorkspaceFileStatus.InUse => "In use by a task",
            WorkspaceFileStatus.Retained => "Within retention period",
            WorkspaceFileStatus.Protected => "Protected file",
            WorkspaceFileStatus.Unverified => "Ownership unverified",
            WorkspaceFileStatus.Missing => "File missing",
            WorkspaceFileStatus.Removed => "Cleaned up",
            WorkspaceFileStatus.Unsafe => "Path unavailable for cleanup",
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        });
        void Refresh()
        {
            var selection = (table.SelectedItem as FileRow)?.File.Path;
            files = service.WorkspaceFiles();
            var rows = files.Select(file => new FileRow(file, Path.GetFileName(Path.TrimEndingDirectorySeparator(file.Path)), Owner(file.Owner),
                Cleanup(file.Status), file.References.Select(reference => reference.TaskId).Distinct().Count())).ToArray();
            table.ItemsSource = rows;
            table.SelectedItem = rows.FirstOrDefault(row => row.File.Path == selection) ?? rows.FirstOrDefault();
        }

        table.SelectionChanged += (_, _) =>
        {
            if (table.SelectedItem is not FileRow row) { detail.Clear(); return; }
            var file = row.File;
            var lines = new List<string> { file.Path, row.Ownership + " · " + row.Cleanup };
            if (file.ExpiresAt is { } expiry)
                lines.Add(string.Format(culture, ui.T("Retention ends {0}"), expiry.ToLocalTime().ToString("f", culture)));
            if (file.OwnerTaskId is { } ownerId)
                lines.Add(string.Format(culture, ui.T("Registered task {0}"), ownerId));
            foreach (var group in file.References.GroupBy(reference => (reference.TaskId, reference.AttemptId, reference.State, reference.Active)))
            {
                var uses = string.Join(", ", group.Select(reference => ui.T("file_reference_" + reference.Use)).Distinct());
                lines.Add($"{group.Key.TaskId} / {group.Key.AttemptId} · {ui.T(group.Key.State.ToString())} · {uses}" +
                    (group.Key.Active ? " · " + ui.T("In use by a task") : ""));
            }
            detail.Text = string.Join(Environment.NewLine, lines);
        };

        ui.Button(ui.Actions, "Refresh", "RefreshWorkspaceFiles", Refresh);
        ui.Button(ui.Actions, "Preview cleanup", "PreviewWorkspaceCleanup", () =>
        {
            var preview = files.Where(file => file.Status == WorkspaceFileStatus.Eligible).ToArray();
            if (preview.Length == 0) { ui.Status.Text = ui.T("No files qualify for cleanup."); return; }
            var message = string.Format(culture, ui.T("Delete only these {0} reviewed files?"), preview.Length) +
                "\n\n" + string.Join("\n", preview.Select(file => file.Path));
            if (ThemedMessageBox.Show(this, message, ui.T("Preview cleanup"), MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning) != MessageBoxResult.OK) return;

            var result = service.ApplyWorkspaceCleanup(preview);
            Refresh();
            ui.Status.Text = string.Format(culture, ui.T("Removed {0} files. Kept {1}."), result.Deleted, result.Eligible - result.Deleted);
            if (result.Kept is { Length: > 0 } kept)
                ui.Status.Text += "\n" + string.Join("\n", kept.Select(item =>
                    item.Path + " · " + (item.Error ?? ui.T("File changed or is now protected."))));
        }, primary: true);
        var close = ui.Button(ui.Actions, "Close", "CloseWorkspaceFiles", Close);
        close.IsCancel = true;
        Refresh();
    }
}
