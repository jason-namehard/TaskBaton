using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using AgentRelay.Core;

namespace AgentRelay.Desktop;

public partial class MainWindow
{
    private sealed record AgentRow(AgentRecord Source, string Name, string Bridge, Brush BridgeBrush, string Activity, string Host, string Initial, Brush AvatarBrush);

    private HashSet<string> ActiveAgentIds() => service.Tasks()
        .Where(view => view.Task.State is not (TaskState.Succeeded or TaskState.Failed or TaskState.TimedOut or TaskState.Cancelled))
        .SelectMany(view => new[] { view.Task.RequesterId, view.Task.RecipientId }).ToHashSet(StringComparer.Ordinal);

    private FrameworkElement AgentsPage()
    {
        avatarSlots = null;
        var page = Page();
        var scroll = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new(0, 0, 6, 0) };
        var table = Table("AgentsTable", ("Agent", "Name"), ("Agent bridge", "Bridge"), ("Handoffs", "Activity"));
        table.Columns[0] = AgentColumn();
        table.Columns[1] = BridgeColumn();
        table.Columns[2].Width = new DataGridLength(0.7, DataGridLengthUnitType.Star);
        table.Columns.Add(MoreColumn());
        table.RowHeight = 58; table.Height = double.NaN; table.MaxHeight = 420;
        var details = new StackPanel();

        Task Edit(AgentRecord? existing)
        {
            var dialog = new AgentEntryDialog(this, preferences.Language, existing, service);
            StatusText.Text = T("Waiting for Agent details…");
            if (dialog.ShowDialog() != true) { reviewCancelled = true; return Task.CompletedTask; }
            var draft = dialog.Result!;
            var id = existing?.AgentId ?? "agent-" + Guid.NewGuid().ToString("N")[..12];
            var priorRecipe = existing is null ? null : service.CliExtension(id)?.Recipe;
            if (string.IsNullOrWhiteSpace(draft.ConfigPath))
                service.RegisterManual(id, draft.Host, draft.Name, draft.CliPath, draft.HostVersion, draft.InstructionPaths);
            else service.Register(id, draft.Host, draft.ConfigPath, draft.CliPath, draft.HostVersion, draft.InstructionPaths, draft.Name);
            if (dialog.CliResult is { } cli)
            {
                var command = priorRecipe is null
                    ? new CliCommand(cli.Executable, cli.Arguments, cli.Input, cli.WorkingDirectory, cli.Environment)
                    : priorRecipe.Create with { Executable = cli.Executable, Arguments = cli.Arguments, Input = cli.Input, WorkingDirectory = cli.WorkingDirectory, Environment = cli.Environment };
                var recipe = priorRecipe is null
                    ? new CliRecipe(1, draft.Name + " CLI", draft.Host, command, null, [], "", "manual", "", "", cli.OutputFormat)
                    : priorRecipe with { Create = command, Resume = priorRecipe.Resume is { } resume ? resume with { Environment = cli.Environment } : null, OutputFormat = cli.OutputFormat };
                if (priorRecipe is null || RelayJson.Write(recipe) != RelayJson.Write(priorRecipe))
                    service.ImportCliRecipe(id, RelayJson.Write(recipe));
            }
            service.RefreshGuidance(); agentId = id;
            return Task.CompletedTask;
        }

        AgentRecord Selected() => table.SelectedItem is AgentRow row ? row.Source
            : throw new RelayException("selection_required", T("Select an Agent."));

        // One button to add an Agent; the dialog behind it asks first whether the Agent should set itself up.
        var create = Styled(Add(PageActions, "New Agent", () =>
        {
            var choice = new AgentSetupChoiceDialog(this, preferences.Language);
            StatusText.Text = T("Waiting for Agent details…");
            if (choice.ShowDialog() != true) { reviewCancelled = true; return Task.CompletedTask; }
            if (choice.Choice == "manual") return Edit(null);
            Clipboard.SetText(service.AgentSetupPrompt(relayCommand: AgentEntryDialog.ResolveRelayCommand()));
            ThemedMessageBox.Show(this, T("Setup prompt copied. Paste it into a new conversation with the Agent you want to register. It will inspect its calling method and fill Relay directly. This creates a new node and does not overwrite the selected Agent.") + "\n\n" + T("Optional handoff rule: after adding an Agent, select it and choose Install handoff rule. Relay will show the one-line rule and ask before changing its instruction file. The detailed guidance is read only when the Agent follows the rule."),
                T("One-click setup"), MessageBoxButton.OK, MessageBoxImage.Information);
            return Task.CompletedTask;
        }), "PrimaryButtonStyle", "Plus");
        create.Margin = new(0);

        // One menu serves every row; the ⋯ button selects its row and opens it there.
        var rowMenuHost = new StackPanel();
        page.Children.Add(rowMenuHost);
        var anchor = new Button { Visibility = Visibility.Collapsed };
        rowMenuHost.Children.Add(anchor);
        var rowMenu = Flyout(anchor, rowMenuHost);
        var popup = (Popup)rowMenuHost.Children.OfType<Popup>().Single();
        MenuItem(rowMenu, "Edit Agent", "Edit", () => Edit(Selected()));
        MenuSeparator(rowMenu);
        MenuItem(rowMenu, "Delete Agent", "Trash", () =>
        {
            var agent = Selected();
            if (ActiveAgentIds().Contains(agent.AgentId))
                throw new RelayException("agent_busy", T("This Agent has unfinished handoffs. Complete or cancel them before removing it."));
            StatusText.Text = T("Waiting for confirmation…");
            if (ThemedMessageBox.Show(this, T("Remove this Agent from Relay? Existing task history is kept."), agent.DisplayName ?? agent.AgentId,
                    MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            { reviewCancelled = true; return Task.CompletedTask; }
            service.RemoveAgent(agent.AgentId); service.RemoveGuidance(agent.AgentId); agentId = null;
            return Task.CompletedTask;
        }, danger: true);
        table.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((_, args) =>
        {
            if (args.OriginalSource is not Button { Tag: "AgentMore" } button || button.DataContext is not AgentRow row) return;
            table.SelectedItem = row;
            popup.PlacementTarget = button; popup.IsOpen = true;
            args.Handled = true;
        }));

        var active = ActiveAgentIds();
        var rows = service.Agents().Select(agent =>
        {
            var bridge = service.Bridge(agent.AgentId);
            var name = agent.DisplayName ?? agent.AgentId;
            return new AgentRow(agent, name, BridgePresentation.Label(bridge, T), BridgePresentation.Accent(this, bridge),
                T(active.Contains(agent.AgentId) ? "Unfinished handoffs" : "No unfinished handoffs"),
                agent.Host is "manual" or "" ? T("Added by hand") : agent.Host + (agent.HostVersion is { } version ? " " + version : ""),
                Initial(name), Theme(AvatarKey(agent.AgentId)));
        }).ToArray();
        table.ItemsSource = rows; page.Children.Add(table);
        if (rows.Length == 0) page.Children.Add(Muted("No Agents yet. Add one with New Agent, or let an Agent fill in its own details with One-click setup.", 13, new(2, 14, 0, 0)));
        page.Children.Add(details);
        void ShowDetails()
        {
            details.Children.Clear();
            if (table.SelectedItem is not AgentRow row) return;
            agentId = row.Source.AgentId;
            var id = row.Source.AgentId;
            var header = new DockPanel { Margin = new(0, 22, 0, 0) };
            var headerAvatar=Avatar(id,30); DockPanel.SetDock(headerAvatar,Dock.Left); header.Children.Add(headerAvatar);
            var names = new StackPanel { Margin = new(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            names.Children.Add(new TextBlock { Text = row.Name, FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            names.Children.Add(Brushed(new TextBlock { Text = T("Agent ID") + ": " + id, FontSize = 12, FontFamily = new("Cascadia Mono, Consolas") }, "FaintBrush"));
            header.Children.Add(names);
            details.Children.Add(header);

            var rules = Group(details, "Handoff rule");
            var registration = service.GuidanceRegistrations().SingleOrDefault(r => r.AgentId == id);
            var state = registration is null ? T("Not installed") : string.Format(System.Globalization.CultureInfo.CurrentUICulture, T("Recorded installation: {0}"), registration.InstructionFile);
            var ruleRow = SettingRow(rules, "Instruction file", state + "\n" + T("Install the rule in the instruction file your host actually loads. The one-line rule uses a little context; details are read on demand. Existing sessions may need to reread the file or restart."));
            RowAction(ruleRow, "Install handoff rule", () => { InstallRules(Selected().AgentId); return Task.CompletedTask; });
            Styled(RowAction(ruleRow, "Remove handoff rule", () =>
            {
                if (ThemedMessageBox.Show(this, T("Remove this Agent's Relay handoff rule? Other instructions will be kept."), T("Remove handoff rule"), MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK)
                    service.RemoveGuidance(Selected().AgentId);
                else reviewCancelled = true;
                return Task.CompletedTask;
            }), "GhostButtonStyle");

            var bridge = Group(details, "Agent bridge");
            var bridgeState = new StackPanel { Orientation = Orientation.Horizontal };
            bridgeState.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = row.BridgeBrush, Margin = new(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
            bridgeState.Children.Add(new TextBlock { Text = row.Bridge, VerticalAlignment = VerticalAlignment.Center });
            var extension = service.CliExtension(id);
            SettingRow(bridge, "Connection", extension is null ? "Without a bridge you copy prompts by hand." :
                T(extension.CanCreate ? "New conversations verified" : "New conversations not verified") + " · " + T(extension.CanResume ? "Resume verified" : "Resume not verified") +
                (extension.CreateEvidence is not null || extension.ResumeEvidence is not null ? " · " + T("Proven by a real handoff") : ""), bridgeState);
            if (extension?.Block is { } block)
            {
                var failure = new StackPanel { Margin = new(18, 0, 18, 14) };
                bridge.Children.Add(failure);
                var (_, blockActions) = Alert(failure, "bad", string.Format(System.Globalization.CultureInfo.CurrentUICulture, T("Saved failure at {0}. Automatic sends remain paused until you resolve it; use the connection check to see the current status."), block.At.ToLocalTime().ToString("g")), block.Message);
                if (!CanRecheckDesktopBridge(id))
                    Add(blockActions, "I have resolved this · Allow automatic sends", () => { service.ClearCliBlock(id); return Task.CompletedTask; });
            }
            if (service.Bridge(id).HostNote is { } hostNote) { var note = new StackPanel { Margin = new(18, 0, 18, 14) }; bridge.Children.Add(note); Alert(note, "info", null, hostNote); }
            var testRow = SettingRow(bridge, "Test bridge", "Sends one short real call");
            RowAction(testRow, "Test bridge", () => { ShowCliProbe(id); return Task.CompletedTask; });
            var recoveryRow = SettingRow(bridge, "Recheck and restore bridge", "Read-only check, no model call");
            RowAction(recoveryRow, "Recheck and restore bridge", () => RecheckBridge(id));
            var health = new StackPanel { Margin = new(18, 0, 18, 6) };
            var checkRow = SettingRow(bridge, "Check bridge setup", "Reads the local configuration. No model is called.");
            Button copy = null!;
            var check = RowAction(checkRow, "Check bridge setup (no model call)", async () =>
            {
                health.Children.Clear();
                var results = await Task.Run(() => service.CliHealthAsync(id));
                copy.Visibility = extension is not null && (BridgePresentation.HasFailure(extension) || results.Any(result => result.Status == "fail"))
                    ? Visibility.Visible : Visibility.Collapsed;
                foreach (var result in results)
                {
                    var line = new DockPanel { Margin = new(0, 0, 0, 6) };
                    var mark = Icons.Create(result.Status == "pass" ? "Check" : "Alert", size: 15, brushKey: result.Status == "pass" ? "OkBrush" : result.Status == "warn" ? "WarnBrush" : "BadBrush");
                    mark.Margin = new(0, 1, 8, 0); mark.VerticalAlignment = VerticalAlignment.Top; DockPanel.SetDock(mark, Dock.Left);
                    line.Children.Add(mark); line.Children.Add(new TextBlock { Text = result.Message, TextWrapping = TextWrapping.Wrap, FontSize = 12.5 });
                    health.Children.Add(line);
                }
            }, false);
            check.Content = T("Check");
            copy = RowAction(checkRow, "Copy bridge diagnostic prompt", () => CopyBridgeDiagnostic(id), false);
            Styled(copy, "GhostButtonStyle"); copy.Content = T("Copy diagnostic prompt");
            copy.Visibility = BridgePresentation.HasFailure(extension) ? Visibility.Visible : Visibility.Collapsed;
            bridge.Children.Add(health);
            AddModelDefaults(details, id);

            var discovery = Group(details, "Session discovery");
            var current = service.DiscoverySource(id);
            var provider = new ComboBox { Name = "SessionDiscoveryProvider", ItemsSource = new[] { "zcode", "dsh", "hermes", "workbuddy" }, SelectedItem = current?.Provider, Width = 160, Margin = new(0) };
            SettingRow(discovery, "Conversation store", "Session discovery reads this Agent's local conversation store. It never selects the latest conversation.", provider);
            var storage = EntryDialogLayout.Input("SessionStorageRoot", current?.StorageRoot ?? ""); storage.Margin = new(0); storage.Width = 300;
            var storageRow = SettingRow(discovery, "Storage folder", null, storage);
            RowAction(storageRow, "Save session discovery", () =>
            {
                if (provider.SelectedItem is not string selected) throw new RelayException("session_provider_required", T("Select a session provider."));
                service.ConfigureSessionDiscovery(id, selected, storage.Text.Trim()); return Task.CompletedTask;
            }).Content = T("Save");
        }
        table.SelectionChanged += (_, _) => ShowDetails();
        table.SelectedItem = rows.SingleOrDefault(row => row.Source.AgentId == agentId) ?? rows.FirstOrDefault();
        UpdateSidebar();
        return scroll;
    }

    private DataGridTemplateColumn AgentColumn()
    {
        var row = new FrameworkElementFactory(typeof(DockPanel));
        row.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        var avatar = new FrameworkElementFactory(typeof(Border));
        avatar.SetValue(WidthProperty, 32.0); avatar.SetValue(HeightProperty, 32.0); avatar.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
        avatar.SetBinding(Border.BackgroundProperty, new Binding(nameof(AgentRow.AvatarBrush)));
        avatar.SetValue(DockPanel.DockProperty, Dock.Left);
        var letter = new FrameworkElementFactory(typeof(TextBlock));
        letter.SetBinding(TextBlock.TextProperty, new Binding(nameof(AgentRow.Initial)));
        letter.SetValue(TextBlock.FontSizeProperty, 14.0); letter.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
        letter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center); letter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        letter.SetResourceReference(TextBlock.ForegroundProperty, "AvatarTextBrush");
        avatar.AppendChild(letter);
        var names = new FrameworkElementFactory(typeof(StackPanel));
        names.SetValue(MarginProperty, new Thickness(11, 0, 0, 0)); names.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        var name = new FrameworkElementFactory(typeof(TextBlock));
        name.SetBinding(TextBlock.TextProperty, new Binding(nameof(AgentRow.Name)));
        name.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        name.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        name.SetBinding(ToolTipProperty, new Binding(nameof(AgentRow.Name)));
        var host = new FrameworkElementFactory(typeof(TextBlock));
        host.SetBinding(TextBlock.TextProperty, new Binding(nameof(AgentRow.Host)));
        host.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        host.SetValue(TextBlock.FontSizeProperty, 12.0); host.SetResourceReference(TextBlock.ForegroundProperty, "FaintBrush");
        names.AppendChild(name); names.AppendChild(host);
        row.AppendChild(avatar); row.AppendChild(names);
        return new DataGridTemplateColumn { Header = T("Agent"), CellTemplate = new DataTemplate { VisualTree = row }, Width = new DataGridLength(1.2, DataGridLengthUnitType.Star) };
    }

    private DataGridTemplateColumn BridgeColumn()
    {
        var row = new FrameworkElementFactory(typeof(StackPanel));
        row.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        row.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        var dot = new FrameworkElementFactory(typeof(Ellipse));
        dot.SetValue(WidthProperty, 8.0); dot.SetValue(HeightProperty, 8.0);
        dot.SetValue(MarginProperty, new Thickness(0, 0, 8, 0)); dot.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        dot.SetBinding(Shape.FillProperty, new Binding(nameof(AgentRow.BridgeBrush)));
        var label = new FrameworkElementFactory(typeof(TextBlock));
        label.SetBinding(TextBlock.TextProperty, new Binding(nameof(AgentRow.Bridge)));
        label.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        row.AppendChild(dot); row.AppendChild(label);
        return new DataGridTemplateColumn { Header = T("Agent bridge"), CellTemplate = new DataTemplate { VisualTree = row },
            Width = new DataGridLength(1, DataGridLengthUnitType.Star) };
    }

    private DataGridTemplateColumn MoreColumn()
    {
        var button = new FrameworkElementFactory(typeof(Button));
        button.SetValue(TagProperty, "AgentMore");
        button.SetValue(AutomationProperties.AutomationIdProperty, "AgentMore");
        button.SetResourceReference(StyleProperty, "IconButtonStyle");
        button.SetValue(ToolTipProperty, T("More actions"));
        button.SetValue(MarginProperty, new Thickness(0));
        button.SetBinding(AutomationProperties.NameProperty, new Binding(nameof(AgentRow.Name)) { StringFormat = T("More actions") + ": {0}" });
        var dots = new FrameworkElementFactory(typeof(Path));
        dots.SetValue(Path.DataProperty, Geometry.Parse("M0,3 L0.01,3 M6,3 L6.01,3 M12,3 L12.01,3"));
        dots.SetValue(Shape.StrokeThicknessProperty, 2.6); dots.SetValue(Shape.StrokeStartLineCapProperty, PenLineCap.Round); dots.SetValue(Shape.StrokeEndLineCapProperty, PenLineCap.Round);
        dots.SetResourceReference(Shape.StrokeProperty, "MutedBrush");
        button.AppendChild(dots);
        return new DataGridTemplateColumn { Header = "", CellTemplate = new DataTemplate { VisualTree = button }, Width = new DataGridLength(56) };
    }
}
