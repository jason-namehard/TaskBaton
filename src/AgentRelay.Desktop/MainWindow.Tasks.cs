using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using AgentRelay.Core;
using Microsoft.Win32;

namespace AgentRelay.Desktop;

public partial class MainWindow
{
    private string taskFilter = "all";
    private string taskQuery = "";
    private readonly Dictionary<string, string> taskTitles = new(StringComparer.Ordinal);

    /// <summary>List on the left, the selected task's detail card on the right.</summary>
    private FrameworkElement HomePage()
    {
        avatarSlots = null;
        var rows = service.Tasks().OrderByDescending(v => v.Task.CreatedAt).Select(v => TaskRowFor(v.Task)).ToArray();
        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new(392) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new(16) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new(1, GridUnitType.Star) });

        var list = new DockPanel();
        layout.Children.Add(list);
        var tools = new DockPanel { Margin = new(0, 0, 0, 10) };
        DockPanel.SetDock(tools, Dock.Top); list.Children.Add(tools);
        var search = SearchBox("TaskSearch", "Search Agents, tasks or conversation IDs");
        tools.Children.Add(search.Host);

        var chips = new WrapPanel { Margin = new(0, 0, 0, 10) };
        DockPanel.SetDock(chips, Dock.Top); list.Children.Add(chips);
        var batch = new WrapPanel { Margin = new(0, 8, 0, 0) };
        DockPanel.SetDock(batch, Dock.Bottom); list.Children.Add(batch);

        var table = new DataGrid { Name = "TasksTable", ItemsSource = rows };
        table.SetResourceReference(StyleProperty, "TaskListStyle");
        table.SetResourceReference(DataGrid.RowStyleProperty, "TaskRowStyle");
        AutomationProperties.SetAutomationId(table, table.Name);
        table.Columns.Add(new DataGridTemplateColumn { Header = T("Route"), CellTemplate = (DataTemplate)FindResource("TaskRowTemplate"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        AddTaskBatchControls(batch, table, rows);
        list.Children.Add(table);

        var sessionText = new Dictionary<string, string>(StringComparer.Ordinal);
        var view = CollectionViewSource.GetDefaultView(table.ItemsSource);
        view.Filter = item => item is TaskRow row && (taskFilter == "all" || row.Group == taskFilter) &&
            (taskQuery.Length == 0 || (row.Route + " " + row.Title + " " + row.TaskId + " " + Sessions(row.TaskId, sessionText)).Contains(taskQuery, StringComparison.OrdinalIgnoreCase));
        foreach (var (key, label) in new[] { ("all", "All"), ("attention", "Needs attention"), ("active", "In progress"), ("done", "Completed") })
        {
            var count = key == "all" ? rows.Length : rows.Count(r => r.Group == key);
            var chip = new RadioButton { GroupName = "TaskFilter", IsChecked = taskFilter == key, Tag = key, Margin = new(0, 0, 5, 6) };
            chip.SetResourceReference(StyleProperty, "ChipStyle");
            chip.Content = new StackPanel { Orientation = Orientation.Horizontal, Children =
            {
                new TextBlock { Text = T(label), FontSize = 12.5 },
                Brushed(new TextBlock { Text = count.ToString(CultureInfo.InvariantCulture), FontSize = 12, Margin = new(6, 0, 0, 0), FontWeight = FontWeights.SemiBold }, "FaintBrush")
            } };
            chip.Checked += (_, _) => { taskFilter = key; view.Refresh(); };
            chips.Children.Add(chip);
        }
        search.Box.Text = taskQuery;
        search.Box.TextChanged += (_, _) => { taskQuery = search.Box.Text.Trim(); view.Refresh(); };

        var detail = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new(0, 0, 4, 0) };
        Grid.SetColumn(detail, 2); layout.Children.Add(detail);
        var pane = new StackPanel();
        var card = Card(pane);
        detail.Content = card;
        table.SelectionChanged += (_, _) =>
        {
            pane.Children.Clear();
            if (table.SelectedItem is not TaskRow row) { card.Visibility = Visibility.Collapsed; return; }
            card.Visibility = Visibility.Visible;
            taskId = row.TaskId;
            TaskDetail(pane, service.Status(row.TaskId), row);
        };
        table.SelectedItem = rows.SingleOrDefault(r => r.TaskId == taskId) ?? table.Items.Cast<TaskRow>().FirstOrDefault();
        if (table.SelectedItem is null) card.Visibility = Visibility.Collapsed;
        if (rows.Length == 0)
        {
            var empty = new StackPanel { Margin = new(4, 40, 4, 0), HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 420 };
            empty.Children.Add(new TextBlock { Text = T("No handoffs yet"), FontSize = 16, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
            empty.Children.Add(Muted("No handoffs yet. Add two Agents, then create your first handoff.", 13, new(0, 6, 0, 0)));
            ((TextBlock)empty.Children[1]).TextAlignment = TextAlignment.Center;
            detail.Content = empty;
        }
        UpdateSidebar(rows);
        return layout;
    }

    /// <summary>Conversation IDs bound to a task, read once per page so searching does not query the ledger on every keystroke.</summary>
    private string Sessions(string id, Dictionary<string, string> cache)
    {
        if (cache.TryGetValue(id, out var text)) return text;
        var task = service.Status(id).Task;
        return cache[id] = string.Join(" ", RelayService.Participants(task).Select(agent =>
            service.TaskSession(id, agent, RelayService.SessionPurpose(task, agent))?.SessionId));
    }

    private (Grid Host, TextBox Box) SearchBox(string name, string placeholder)
    {
        var host = new Grid();
        var box = new TextBox { Name = name, Margin = new(0), Padding = new(32, 5, 9, 5) };
        AutomationProperties.SetAutomationId(box, name); AutomationProperties.SetName(box, T(placeholder));
        var icon = Icons.Create("Search", size: 16, brushKey: "FaintBrush");
        icon.HorizontalAlignment = HorizontalAlignment.Left; icon.Margin = new(10, 0, 0, 0); icon.IsHitTestVisible = false;
        var hint = Brushed(new TextBlock { Text = T(placeholder), FontSize = 12.5, Margin = new(33, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false, TextTrimming = TextTrimming.CharacterEllipsis }, "FaintBrush");
        box.TextChanged += (_, _) => hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        host.Children.Add(box); host.Children.Add(icon); host.Children.Add(hint);
        return (host, box);
    }

    private TaskRow TaskRowFor(TaskRecord task)
    {
        var state = task.IsFileHandoff || task.Mode == "manual" ? FileState(task) : T(task.State.ToString());
        var (tone, group) = Classify(task);
        var (back, fore) = ToneKeys(tone);
        var route = AgentName(task.RequesterId) + " → " + AgentName(task.RecipientId);
        if (task.Collaboration is { } collaboration && collaboration.ReviewerId != task.RequesterId && collaboration.ReviewerId != task.RecipientId)
            route += " · " + T("Review") + " " + AgentName(collaboration.ReviewerId);
        return new TaskRow(task.TaskId, route,
            T(task.Mode == "manual" ? "Manual transfer" : "Automatic transfer"), state, task.CreatedAt, task.State.ToString(),
            AgentName(task.RequesterId), AgentName(task.RecipientId), Theme(AvatarKey(task.RequesterId)), Theme(AvatarKey(task.RecipientId)),
            TaskTitle(task), tone, group, Theme(back), Theme(fore));
    }

    /// <summary>The state's color and whether the user needs to act, is waiting, or is done.</summary>
    private (string Tone, string Group) Classify(TaskRecord task)
    {
        if (receiptImportErrors.ContainsKey(task.TaskId)) return ("warn", "attention");
        var delivery = task.IsFileHandoff ? service.FileHandoffProgress(task).Delivery : null;
        if (delivery == "cli_blocked") return ("bad", "attention");
        return task.State switch
        {
            TaskState.Succeeded => ("ok", "done"),
            TaskState.Cancelled => ("off", "done"),
            TaskState.Failed or TaskState.TimedOut or TaskState.DeliveryUnknown => ("bad", "attention"),
            TaskState.Paused or TaskState.AwaitingConfirmation => ("warn", "attention"),
            _ when delivery == "cli_unconfirmed" => ("warn", "attention"),
            TaskState.Dispatched or TaskState.Acknowledged or TaskState.Running => ("run", "active"),
            _ when sendingTasks.Contains(task.TaskId) || delivery is "cli_in_flight" or "agent_working" => ("run", "active"),
            _ => ("info", "active")
        };
    }

    /// <summary>The first meaningful line of the request, or the file name for files that are not text.</summary>
    private string TaskTitle(TaskRecord task)
    {
        var sample = demo && demoSampleIds.Contains(task.TaskId);
        if (!sample && taskTitles.TryGetValue(task.TaskId, out var cached)) return cached;
        // The managed copy outlives the original source file, which may be a deleted draft.
        var path = File.Exists(task.RequestPath) || task.ManualSourceFile is null ? task.RequestPath : task.ManualSourceFile;
        var title = Path.GetFileName(path);
        try
        {
            if (Path.GetExtension(path).ToLowerInvariant() is ".md" or ".txt" && File.Exists(path))
            {
                using var reader = new StreamReader(path);
                var buffer = new char[4096];
                var text = DemoSampleText.Translate(sample, new string(buffer, 0, reader.ReadBlock(buffer, 0, buffer.Length)), preferences.Language);
                var raw = text.Split('\n').Select(l => l.Trim()).ToList();
                // Skip a front-matter header such as Relay's own "relay_version: 1".
                if (raw.FirstOrDefault() == "---" && raw.IndexOf("---", 1) is > 0 and var close) raw = raw.Skip(close + 1).ToList();
                else raw = raw.SkipWhile(l => l.Length == 0 || System.Text.RegularExpressions.Regex.IsMatch(l, @"^[a-z_]+:\s")).ToList();
                var lines = raw.Select(l => l.TrimStart('-', '*', '>').Trim()).Where(l => l.Length > 0).ToArray();
                var line = lines.FirstOrDefault(l => !l.StartsWith('#')) ?? lines.FirstOrDefault()?.TrimStart('#').Trim();
                if (!string.IsNullOrWhiteSpace(line)) title = line.Length > 120 ? line[..120] + "…" : line;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        if (!sample) taskTitles[task.TaskId] = title;
        return title;
    }

    private void TaskDetail(StackPanel card, TaskView view, TaskRow row)
    {
        var task = view.Task;
        if (task.IsFileHandoff) { FileTaskPane(card, view, row); return; }
        var head = DetailSection(card);
        DetailHeader(head, task, row);
        if (task.ErrorMessage is not null) Alert(head, "bad", null, DemoSampleText.Translate(demo && demoSampleIds.Contains(task.TaskId), task.ErrorMessage, preferences.Language));
        if (task.ResumeStatus == "operator_completed") Alert(head, "info", null, T("Marked complete by the user. Agent completion was not verified."));
        var actionsSection = DetailSection(card);
        var actions = new WrapPanel { Margin = new(0, 0, 0, -8) };
        actionsSection.Children.Add(actions);
        var recipient = task.State is TaskState.AwaitingManual or TaskState.Dispatched or TaskState.Acknowledged or TaskState.Running ? task.RecipientId : task.RequesterId;
        Styled(Add(actions, string.Format(CultureInfo.CurrentUICulture, T("Copy prompt for {0}"), AgentName(recipient)), () => { Clipboard.SetText(HandoffPrompt(view)); return Task.CompletedTask; }, false), "PrimaryButtonStyle");
        if (task.State == TaskState.AwaitingConfirmation) Add(actions, "Approve Dispatch", async () => { await service.ApproveAsync(task.TaskId); });
        if (task.State is TaskState.AwaitingManual or TaskState.Dispatched or TaskState.Acknowledged or TaskState.Running)
            Add(actions, "Import completed reply", async () =>
            {
                var picker = new OpenFileDialog { Title = T("Import completed reply"), InitialDirectory = Path.GetDirectoryName(task.ResultPath), Filter = "Markdown / Text|*.md;*.txt|All files|*.*" };
                StatusText.Text = T("Waiting for reply file selection…");
                if (picker.ShowDialog(this) != true) { reviewCancelled = true; return; }
                StatusText.Text = T("Waiting for confirmation…");
                if (ThemedMessageBox.Show(this, T("Confirm that the recipient finished this handoff and this file is its reply."), T("Import completed reply"), MessageBoxButton.OKCancel) != MessageBoxResult.OK) { reviewCancelled = true; return; }
                StatusText.Text = T("Importing reply…");
                var artifact = service.RegisterArtifact(task.TaskId, task.AttemptId, "result", picker.FileName);
                service.Complete(task.TaskId, task.AttemptId, task.RecipientId, Guid.NewGuid().ToString(), artifact.Path);
                await service.ProcessContinuationsAsync(task.TaskId);
            });
        if (task.State is TaskState.Failed or TaskState.TimedOut or TaskState.DeliveryUnknown)
            Add(actions, "Retry", async () => { if (ThemedMessageBox.Show(this, T("Retry may repeat a delivery or model call. Continue?"), T("Retry"), MessageBoxButton.OKCancel) == MessageBoxResult.OK) await service.RetryAsync(task.TaskId, "Retry requested by user", true); else reviewCancelled = true; });
        Add(actions, "Copy diagnostics", () => { Clipboard.SetText(RelayJson.Write(new { view, events = service.Audit(task.TaskId), delivery = service.DeliveryEvidence(task.TaskId) })); return Task.CompletedTask; }, false);

        var files = DetailSection(card);
        var request = Output("TaskRequest", 130); request.Text = File.Exists(task.RequestPath) ? DemoSampleText.Translate(demo && demoSampleIds.Contains(task.TaskId), File.ReadAllText(task.RequestPath), preferences.Language) : T("File expired or missing.");
        files.Children.Add(new Expander { Header = T("Request"), Content = request, IsExpanded = true });
        if (task.State is TaskState.Succeeded or TaskState.Failed)
        {
            var reply = Output("TaskResult", 130); reply.Text = File.Exists(task.ResultPath) ? DemoSampleText.Translate(demo && demoSampleIds.Contains(task.TaskId), File.ReadAllText(task.ResultPath), preferences.Language) : T("File expired or missing.");
            files.Children.Add(new Expander { Header = T("Reply"), Content = reply, IsExpanded = true });
        }
        files.Children.Add(new Expander { Header = T("Processing history"), Content = Timeline(service.Audit(task.TaskId).Select(e => (e.RecordedAt, DesktopLocalizedContent.NotificationEvent(e.Event, preferences.Language), "")).ToArray()) });
        var footer = DetailFooter(card, task);
        if (task.State is not (TaskState.Succeeded or TaskState.Failed or TaskState.Cancelled or TaskState.TimedOut))
            Styled(Add(footer, "Cancel", () => ConfirmTaskCancellation(task.TaskId)), "GhostButtonStyle");
        AddTaskOperatorActions(footer, task);
    }

    /// <summary>Route, request title and state pill at the top of the detail card.</summary>
    private void DetailHeader(Panel section, TaskRecord task, TaskRow row)
    {
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel();
        left.Children.Add(RouteLine(task.RequesterId, task.RecipientId, 24, 16));
        left.Children.Add(Muted(row.Title, 12.5, new(0, 6, 0, 0)));
        head.Children.Add(left);
        var pill = Pill(row.State, row.Tone); pill.Margin = new(12, 2, 0, 0); pill.VerticalAlignment = VerticalAlignment.Top; pill.MaxWidth = 140;
        Grid.SetColumn(pill, 1); head.Children.Add(pill);
        section.Children.Add(head);
    }

    /// <summary>Task identity on the left; folder, cancellation and operator actions on the right.</summary>
    private WrapPanel DetailFooter(StackPanel card, TaskRecord task)
    {
        var section = DetailSection(card);
        var identity = Brushed(new TextBlock { Text = task.TaskId[..8] + " · " + task.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            Name = "TaskFooterIdentity", FontFamily = new("Cascadia Mono, Consolas"), FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, ToolTip = task.TaskId }, "FaintBrush");
        section.Children.Add(identity);
        var actions = new WrapPanel { Name = "TaskFooterActions", HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 8, -8, -8) };
        section.Children.Add(actions);
        Styled(Add(actions, "Open task folder", () => { Process.Start(new ProcessStartInfo(Path.GetDirectoryName(task.RequestPath)!) { UseShellExecute = true }); return Task.CompletedTask; }, false), "GhostButtonStyle", "Folder");
        return actions;
    }

    /// <summary>A vertical event list: time, a colored node and the event.</summary>
    private StackPanel Timeline(IReadOnlyList<(DateTimeOffset At, string Text, string Tone)> entries)
    {
        var list = new StackPanel { Margin = new(0, 2, 0, 0) };
        for (var i = 0; i < entries.Count; i++)
        {
            var (at, text, tone) = entries[i];
            var row = new Grid { Margin = new(0, 0, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new(92) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new(22) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new(1, GridUnitType.Star) });
            var local = at.ToLocalTime();
            row.Children.Add(Brushed(new TextBlock { Text = local.ToString(local.Date == DateTimeOffset.Now.Date ? "HH:mm" : "MM-dd HH:mm", CultureInfo.InvariantCulture),
                FontSize = 12, TextAlignment = TextAlignment.Right, Margin = new(0, 0, 0, 10) }, "FaintBrush"));
            if (i < entries.Count - 1)
            {
                var rail = new Border { Width = 1, HorizontalAlignment = HorizontalAlignment.Center, Margin = new(0, 12, 0, 0) };
                rail.SetResourceReference(Border.BackgroundProperty, "BorderBrush");
                Grid.SetColumn(rail, 1); row.Children.Add(rail);
            }
            var node = new System.Windows.Shapes.Ellipse { Width = 9, Height = 9, VerticalAlignment = VerticalAlignment.Top, Margin = new(0, 4, 0, 0) };
            node.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, tone switch { "ok" => "OkBrush", "warn" => "WarnBrush", "bad" => "BadBrush", _ => "BorderStrongBrush" });
            Grid.SetColumn(node, 1); row.Children.Add(node);
            var label = new TextBlock { Text = text, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 10) };
            Grid.SetColumn(label, 2); row.Children.Add(label);
            list.Children.Add(row);
        }
        return list;
    }

    /// <summary>Attention count in the sidebar and the overall health line.</summary>
    private void UpdateSidebar(IReadOnlyCollection<TaskRow>? rows = null)
    {
        rows ??= service.Tasks().Select(v => TaskRowFor(v.Task)).ToArray();
        attentionCount = rows.Count(r => r.Group == "attention");
        UpdateChrome();
    }

    private int attentionCount;
}
