using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using AgentRelay.Core;
using Microsoft.Win32;

namespace AgentRelay.Desktop;

public partial class MainWindow
{
    private string? settingsAnchor;

    /// <summary>Grouped settings with a table of contents. Every change applies on its own; nothing waits for a save button.</summary>
    private FrameworkElement SettingsPage()
    {
        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new(150) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new(22) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new(1, GridUnitType.Star) });
        var content = new StackPanel { Margin = new(0, -18, 0, 24), MaxWidth = 860, HorizontalAlignment = HorizontalAlignment.Left };
        var scroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new(0, 0, 8, 0) };
        Grid.SetColumn(scroll, 2); layout.Children.Add(scroll);
        var toc = new StackPanel();
        layout.Children.Add(toc);

        var look = Group(content, "Appearance", "SettingsAppearance");
        var theme = SettingRow(look, "Theme", "Changes apply immediately.");
        var tiles = new WrapPanel { Margin = new(0, 12, 0, 0) };
        ((Panel)theme.Children[0]).Children.Add(tiles);
        foreach (var option in Appearance.Palettes)
        {
            var tile = new Button { Name = "Palette_" + option.Id, Tag = "Palette:" + option.Id, Margin = new(0, 0, 12, 4), Padding = new(0), Cursor = Cursors.Hand };
            tile.SetResourceReference(StyleProperty, "GhostButtonStyle");
            AutomationProperties.SetAutomationId(tile, tile.Name); AutomationProperties.SetName(tile, T(option.Label));
            var stack = new StackPanel { Margin = new(4) };
            var preview = new Border { Width = 104, Height = 64, CornerRadius = new(9), BorderThickness = new(preferences.Palette == option.Id ? 2 : 1), ClipToBounds = true };
            preview.SetResourceReference(Border.BorderBrushProperty, preferences.Palette == option.Id ? "FocusBrush" : "BorderStrongBrush");
            preview.Child = option.Id == "system" ? SplitPreview() : PalettePreview(option.Id);
            stack.Children.Add(preview);
            stack.Children.Add(new TextBlock { Text = T(option.Label), Margin = new(0, 7, 0, 0), HorizontalAlignment = HorizontalAlignment.Center,
                FontWeight = preferences.Palette == option.Id ? FontWeights.SemiBold : FontWeights.Normal });
            tile.Content = stack;
            var id = option.Id;
            tile.Click += async (_, _) => { if (id != preferences.Palette) await ChangeAppearance(preferences with { Palette = id }); };
            tiles.Children.Add(tile);
        }

        var swatches = new StackPanel { Orientation = Orientation.Horizontal };
        void Swatch(string? color, string label)
        {
            var dot = new Border { Width = 22, Height = 22, CornerRadius = new(11), BorderThickness = new(preferences.Accent == color ? 2 : 1) };
            if (color is null) dot.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            else dot.Background = new SolidColorBrush(Appearance.ParseAccent(color));
            dot.SetResourceReference(Border.BorderBrushProperty, preferences.Accent == color ? "TextBrush" : "BorderStrongBrush");
            var button = new Button { Content = dot, ToolTip = T(label), Width = 30, Height = 30, MinHeight = 0, Padding = new(0), Margin = new(0, 0, 2, 0), Tag = "Accent:" + (color ?? "theme") };
            button.SetResourceReference(StyleProperty, "GhostButtonStyle");
            AutomationProperties.SetName(button, T(label));
            button.Click += async (_, _) => { if (color != preferences.Accent) await ChangeAppearance(preferences with { Accent = color }); };
            swatches.Children.Add(button);
        }
        Swatch(null, "Follow the theme");
        foreach (var (color, label) in new[] { ("#F38181", "Coral"), ("#359FA0", "Teal"), ("#8CC0EB", "Sky"), ("#E3B04B", "Amber") }) Swatch(color, label);
        var accent = new TextBox { Name = "CustomAccent", Width = 96, Margin = new(10, 0, 0, 0), Text = preferences.Accent ?? "", ToolTip = T("Custom accent (#RRGGBB)") };
        AutomationProperties.SetAutomationId(accent, accent.Name); AutomationProperties.SetName(accent, T("Custom accent (#RRGGBB)"));
        fields.Add(accent.Name, accent);
        async Task CommitAccent()
        {
            var next = Optional(accent);
            if (next != preferences.Accent) await ChangeAppearance(preferences with { Accent = next });
        }
        accent.LostFocus += async (_, _) => await CommitAccent();
        accent.KeyDown += async (_, e) => { if (e.Key == Key.Enter) await CommitAccent(); };
        swatches.Children.Add(accent);
        SettingRow(look, "Accent color", "Follows the theme unless you choose one. Type #RRGGBB for any color.", swatches);

        var language = new ComboBox { Name = "LanguageChoice", Width = 160, DisplayMemberPath = "Label", SelectedValuePath = "Id",
            ItemsSource = Appearance.Languages.Select(o => new AppearanceOption(o.Id, T(o.Label))).ToArray(), SelectedValue = preferences.Language };
        AutomationProperties.SetAutomationId(language, language.Name);
        BindingOperations.SetBinding(language, AutomationProperties.NameProperty, new Binding("SelectedItem.Label") { Source = language, StringFormat = T("Language") + ": {0}" });
        language.SelectionChanged += async (_, _) => { if (language.SelectedValue is string selected && selected != preferences.Language) await ChangeLanguage(selected); };
        SettingRow(look, "Language", null, language);
        AddCloseBehaviorSetting(look);
        if (demo)
            RowAction(SettingRow(look, "Demo workspace", "Demo mode blocks real CLI calls. Keep this workspace and switch to normal use to test your registered Agents."), "Use this workspace normally", () =>
            {
                demo = false; RelayLocation.Save(service.Home); Title = "Agent Relay"; DemoBadge.Visibility = Visibility.Collapsed;
                return Task.CompletedTask;
            });

        AddNotificationSettings(content);

        var storage = Group(content, "Relay storage", "SettingsStorage");
        var location = SettingRow(storage, "Location", "Temporary handoff files and the local ledger live here. Keep final results in your project folder.");
        ((Panel)location.Children[0]).Children.Add(Brushed(new TextBlock { Text = service.Home, FontFamily = new("Cascadia Mono, Consolas"), FontSize = 12, Margin = new(0, 6, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = service.Home }, "MutedBrush"));
        RowAction(location, "Open relay folder", () => { Process.Start(new ProcessStartInfo(service.Home) { UseShellExecute = true }); return Task.CompletedTask; }, false);
        Styled(RowAction(location, "Change relay folder", () =>
        {
            var picker = new OpenFolderDialog { Title = T("Choose relay folder") };
            if (picker.ShowDialog(this) != true) { reviewCancelled = true; return Task.CompletedTask; }
            var previousHome = service.Home;
            service.QueueRelocation(picker.FolderName);
            if (sendingTasks.Count == 0 && service.TryCompletePendingRelocation() is { } next) CompleteRelocation(next, previousHome);
            return Task.CompletedTask;
        }), "GhostButtonStyle");
        if (migrationSummary is not null) { var note = new StackPanel { Margin = new(18, 0, 18, 12) }; storage.Children.Add(note); Alert(note, "info", null, migrationSummary); }
        if (service.PendingRelocation() is { } pendingMove)
        {
            var pendingRow = SettingRow(storage, "Pending relay move", "Relay will move when active handoffs and notifications finish. Check migration retries a move interrupted during copying.");
            ((Panel)pendingRow.Children[0]).Children.Add(Muted(string.Format(CultureInfo.CurrentUICulture,T("Destination: {0}"),pendingMove.Target),12.5,new(0,6,0,0)));
            RowAction(pendingRow,"Check migration",() => { var previousHome=service.Home; CompleteRelocation(service.VerifyRelocation(),previousHome); return Task.CompletedTask; }).Content=T("Check");
            RowAction(pendingRow,"Cancel pending move",() => { service.CancelPendingRelocation(); return Task.CompletedTask; });
        }
        foreach (var plan in service.RelocationCleanupPlans())
        {
            var detail=string.Format(CultureInfo.CurrentUICulture,T("Previous folder: {0}\nScheduled cleanup: {1}"),plan.Source,plan.DeleteAfter.ToLocalTime().ToString("f",CultureInfo.CurrentUICulture));
            if(plan.Attempted && (plan.ModifiedFiles.Length>0 || plan.AddedEntries.Length>0))
                detail+="\n"+T("Changed or new files kept:")+"\n"+string.Join("\n",plan.ModifiedFiles.Concat(plan.AddedEntries));
            var cleanupRow=SettingRow(storage,"Previous relay folder", "Unchanged migrated files are deleted after 24 hours while Relay is running or the next time it starts. Changed or new files are kept.");
            ((Panel)cleanupRow.Children[0]).Children.Add(Muted(detail,12.5,new(0,6,0,0)));
            if(plan.Attempted)RowAction(cleanupRow,"Retry cleanup",() => { service.CleanupRelocatedHomes(); return Task.CompletedTask; });
        }
        var settings = service.Settings();
        var ttl = NumberBox("TtlDays", settings.ArtifactTtlDays);
        var retention = SettingRow(storage, "Keep handoff files", "Expired temporary files can be cleaned. Registered results are kept.", ttl, Muted("days", 12.5, new(8, 0, 0, 0)));
        Styled(RowAction(retention, "Clean expired files", () => { var proposed = service.Cleanup(false); if (ThemedMessageBox.Show(this, string.Format(CultureInfo.CurrentUICulture, T("Delete {0} expired relay files?"), proposed.Eligible), T("Clean expired files"), MessageBoxButton.OKCancel) == MessageBoxResult.OK) service.Cleanup(true); else reviewCancelled = true; return Task.CompletedTask; }), "GhostButtonStyle").Margin = new(16, 0, 0, 0);

        var advanced = Group(content, "Advanced", "SettingsAdvanced");
        var timeout = NumberBox("TaskTimeout", settings.TaskTimeoutMinutes);
        SettingRow(advanced, "Task timeout", "How long Relay waits for an Agent to finish one turn.", timeout, Muted("minutes", 12.5, new(8, 0, 0, 0)));
        var delivery = NumberBox("DeliveryTimeout", settings.DeliveryTimeoutSeconds);
        SettingRow(advanced, "Delivery timeout", "How long one delivery attempt may take.", delivery, Muted("seconds", 12.5, new(8, 0, 0, 0)));
        var retries = NumberBox("DeliveryAttempts", settings.MaxDeliveryAttempts);
        SettingRow(advanced, "Delivery attempts", "At most 3.", retries);
        Styled(RowAction(SettingRow(advanced, "Agent bridges", "CLI and ACP bridges are set up and tested for each Agent on the Agents page."), "Go to Agents", () => { Go("Agents"); return Task.CompletedTask; }), "GhostButtonStyle");
        async Task SaveNumbers()
        {
            var current = service.Settings();
            if (ttl.Text == current.ArtifactTtlDays.ToString(CultureInfo.InvariantCulture) && timeout.Text == current.TaskTimeoutMinutes.ToString(CultureInfo.InvariantCulture) &&
                delivery.Text == current.DeliveryTimeoutSeconds.ToString(CultureInfo.InvariantCulture) && retries.Text == current.MaxDeliveryAttempts.ToString(CultureInfo.InvariantCulture)) return;
            await Run(() => { service.Configure(new(Number(ttl), Number(timeout), Number(delivery), Number(retries))); return Task.CompletedTask; }, "Settings saved", refresh: false);
        }
        foreach (var box in new[] { ttl, timeout, delivery, retries })
        {
            box.LostFocus += async (_, _) => await SaveNumbers();
            box.KeyDown += async (_, e) => { if (e.Key == Key.Enter) await SaveNumbers(); };
        }

        var danger = Group(content, "Danger zone", "SettingsDanger", danger: true);
        Styled(RowAction(SettingRow(danger, "Remove all handoff rules", "Removes Relay's marked rules from every registered instruction file. Your other instructions are kept. Do this before deleting the portable software folder."),
            "Remove all handoff rules", () =>
            {
                if (ThemedMessageBox.Show(this, T("Remove Relay's marked rules from registered instruction files? Your other instructions will be kept. Do this before deleting the portable software folder."), T("Remove all handoff rules"), MessageBoxButton.OKCancel) == MessageBoxResult.OK) service.RemoveAllGuidance();
                else reviewCancelled = true;
                return Task.CompletedTask;
            }), "DangerButtonStyle");

        void Jump(string anchor)
        {
            if (LogicalTreeHelper.FindLogicalNode(content, anchor) is not FrameworkElement element) return;
            scroll.UpdateLayout();
            scroll.ScrollToVerticalOffset(Math.Max(0, element.TransformToAncestor(content).Transform(new Point()).Y - 4));
        }
        foreach (var (label, anchor, danger2) in new[] { ("Appearance", "SettingsAppearance", false), ("Notifications", "SettingsNotifications", false), ("Relay storage", "SettingsStorage", false), ("Advanced", "SettingsAdvanced", false), ("Danger zone", "SettingsDanger", true) })
        {
            var link = new Button { Content = T(label), Tag = "Jump:" + anchor, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new(0, 0, 0, 2), MinHeight = 32 };
            link.SetResourceReference(StyleProperty, "GhostButtonStyle");
            if (danger2) link.SetResourceReference(ForegroundProperty, "BadBrush");
            link.Click += (_, _) => Jump(anchor);
            toc.Children.Add(link);
        }
        if (settingsAnchor is { } pending) { settingsAnchor = null; layout.Loaded += (_, _) => Jump(pending); }
        UpdateSidebar();
        return layout;
    }

    private TextBox NumberBox(string name, int value)
    {
        var box = new TextBox { Name = name, Width = 72, Margin = new(0), Text = value.ToString(CultureInfo.InvariantCulture), HorizontalContentAlignment = HorizontalAlignment.Right };
        AutomationProperties.SetAutomationId(box, name);
        fields.Add(name, box);
        return box;
    }

    private static Grid PalettePreview(string palette)
    {
        var (page, nav, line, accent) = Appearance.Preview(palette);
        var grid = new Grid { Background = new SolidColorBrush(page) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new(24) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new(1, GridUnitType.Star) });
        grid.Children.Add(new Border { Background = new SolidColorBrush(nav) });
        var lines = new StackPanel { Margin = new(9, 10, 9, 0) };
        lines.Children.Add(new Border { Height = 5, CornerRadius = new(2.5), Background = new SolidColorBrush(line) });
        lines.Children.Add(new Border { Height = 5, CornerRadius = new(2.5), Background = new SolidColorBrush(line), Margin = new(0, 5, 22, 0) });
        lines.Children.Add(new Border { Height = 12, Width = 34, CornerRadius = new(4), Background = new SolidColorBrush(accent), Margin = new(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left });
        Grid.SetColumn(lines, 1); grid.Children.Add(lines);
        return grid;
    }

    private static Grid SplitPreview()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition());
        var light = PalettePreview("minimal"); var dark = PalettePreview("night");
        light.ColumnDefinitions[0].Width = new(12); dark.ColumnDefinitions[0].Width = new(12);
        Grid.SetColumn(dark, 1); grid.Children.Add(light); grid.Children.Add(dark);
        return grid;
    }
}
