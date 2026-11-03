using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using AgentRelay.Core;
using Microsoft.Win32;

namespace AgentRelay.Desktop;

public partial class MainWindow : Window
{
    private RelayService service;
    private bool demo;
    private readonly HashSet<string> demoSampleIds;
    private DesktopPreferences preferences;
    private readonly Dictionary<string, TextBox> fields = new(StringComparer.Ordinal);
    private bool busy;
    private bool reviewCancelled;
    private bool refreshPending;
    private string? errorTaskId;
    private int errorTaskTurn;
    private uint ledgerMessage;
    private HwndSource? source;
    private string? taskId;
    private string? agentId;
    private string? connectionFrom;
    private string? connectionTo;
    public MainWindow(RelayService service, bool demo = false)
    {
        this.service = service;
        this.demo = demo;
        demoSampleIds = demo ? service.Tasks().Select(view => view.Task.TaskId).ToHashSet(StringComparer.Ordinal) : [];
        preferences = Appearance.Load(service.Home);
        InitializeComponent();
        Appearance.Apply(this, preferences);
        RefreshButton.Content = Icons.Create("Refresh", size: 17);
        DismissErrorButton.Content = Icons.Create("Close", size: 15);
        ErrorIcon.Child = Icons.Create("Alert", size: 17, brushKey: "BadBrush");
        PageHelp.Content = Icons.Create("Help", size: 16);
        VersionLabel.Text = "v" + RelayVersion.Current;
        // The theme draws the title area; these are the window's own caption buttons.
        CaptionButtons.Children.Add(ThemedChrome.CaptionButton("Minimize", "Minimize", () => SystemCommands.MinimizeWindow(this)));
        var maximize = ThemedChrome.CaptionButton("Maximize", "Maximize", () => { if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this); else SystemCommands.MaximizeWindow(this); });
        CaptionButtons.Children.Add(maximize);
        CaptionButtons.Children.Add(ThemedChrome.CaptionButton("Close", "Close", () => SystemCommands.CloseWindow(this), close: true));
        StateChanged += (_, _) =>
        {
            // A maximized window extends past the screen by its resize border; keep the content on screen.
            Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
            maximize.Content = ((Button)ThemedChrome.CaptionButton(WindowState == WindowState.Maximized ? "Restore" : "Maximize", "Maximize", () => { })).Content;
        };
        if (demo)
        {
            Title = "Agent Relay — Demo";
            DemoBadge.Visibility = Visibility.Visible;
        }
        UpdateChrome();
        SourceInitialized += (_, _) =>
        {
            ledgerMessage = WindowsLedgerSignal.Register();
            if (ledgerMessage == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            source.AddHook(ReceiveLedgerMessage);
            Appearance.ApplyTitleBar(this);
            InstallTray();
        };
        SystemEvents.UserPreferenceChanged += FollowSystemTheme;
        Closed += (_, _) => {SystemEvents.UserPreferenceChanged -= FollowSystemTheme;RemoveTray();receiptWatcher?.Dispose();source?.RemoveHook(ReceiveLedgerMessage);};
        Loaded += async (_, _) =>
        {
            Navigation.SelectedIndex = 0;
            WatchReceipts();
            InitializeNotificationDelivery();
            StartRelocationMaintenance();
            await Run(() => Task.CompletedTask, "Ready");
        };
    }

    private nint ReceiveLedgerMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (ReceiveCloseMessage(message, wParam)) { handled = true; return 0; }
        if (ReceiveTrayMessage(message, lParam)) { handled = true; return 0; }
        // Local actions already render their committed changes and keep their own success message.
        if (message == ledgerMessage && wParam != Environment.ProcessId) QueueRefresh();
        return 0;
    }

    private void QueueRefresh()
    {
        if (refreshPending) return;
        refreshPending = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(async () =>
        {
            if (busy) return; // Run drains a queued hint after its operation completes.
            refreshPending = false;
            if (Navigation.SelectedItem is ListBoxItem item && (string)item.Tag is not ("Home" or "Inbox" or "Agents" or "Connections"))
            {
                LedgerHint.Text = T("Ledger changed · Refresh");
                return; // Preserve edits and migration previews until the user explicitly refreshes.
            }
            await Run(() => Task.CompletedTask, "已读取事件更新", clearError: false, reportStatus: false);
            LedgerHint.Text = T("已读取事件更新");
        }));
    }

    private async Task Run(Func<Task> action, string success, bool refresh = true, bool clearError = true, bool reportStatus = true)
    {
        if (busy) return;
        reviewCancelled = false;
        busy = true; PageContent.IsEnabled = false; Navigation.IsEnabled = false; RefreshButton.IsEnabled = false; PageActions.IsEnabled = false;
        if (clearError) { ErrorBanner.Visibility = Visibility.Collapsed; errorTaskId = null; }
        if (reportStatus) StatusText.Text = T("Working…");
        try
        {
            // Resolve a queued move before actions generate prompts containing the current relay paths.
            if (CompletePendingRelayMove()) refresh = true;
            await action();
            var receipts=service.ImportManualReceipts();
            if (CompletePendingRelayMove()) refresh = true;
            service.CleanupRelocatedHomes();
            receiptImportErrors.Clear();
            foreach(var error in receipts.Errors)receiptImportErrors[error.TaskId]=error.Message;
            if(errorTaskId is { } failedTaskId && !IsCurrentDelivery(failedTaskId,errorTaskTurn))
            { ErrorBanner.Visibility=Visibility.Collapsed; errorTaskId=null; }
            StartAutoDeliveries();
            var notificationHint=await DeliverPendingDesktopNotificationsAsync();
            if (refresh && !reviewCancelled) { RenderPage(); LedgerHint.Text = ""; }
            if(receipts.Errors.Length>0)LedgerHint.Text=T("Receipt needs attention")+": "+receipts.Errors[0].Message;
            else if(receipts.UpdatedTaskIds.Length>0)LedgerHint.Text=T("Handoff receipts updated");
            // Notification problems stay visible in the sidebar instead of crowding the status line.
            if(notificationHint is not null)notificationSetupHint=notificationHint;UpdateChrome();
            if (reportStatus) StatusText.Text = ErrorBanner.Visibility == Visibility.Visible ? T("Action failed") :
                reviewCancelled ? T("Action cancelled · No changes") : T(success);
        }
        catch (RelayException error) when (error.Code == "relocation_snapshot_failed")
        {
            var previousHome = service.Home;
            var movedHome = RelayService.ResolveRelocatedHome(previousHome);
            CompleteRelocation(new RelayService(movedHome), previousHome);
            ErrorText.Text = error.Message; ErrorBanner.Visibility = Visibility.Visible; StatusText.Text = T("Action failed");
            errorTaskId = null;
            RenderPage();
        }
        // Expected external input, filesystem, host-command and clipboard failures stay visible at the UI boundary.
        catch (Exception error) when (error is RelayException or IOException or UnauthorizedAccessException or
            JsonException or Microsoft.Data.Sqlite.SqliteException or ArgumentException or Win32Exception or ExternalException)
        {
            ErrorText.Text = error is RelayException relay ? DesktopLocalizedContent.SetupError(relay, preferences.Language) : error.Message;
            if (error is RelayException setup && setup.Code.StartsWith("setup_", StringComparison.Ordinal))
                ErrorText.Text = T("The Agent reply format is invalid. Check version, context and requested fields.") + "\n" + ErrorText.Text;
            ErrorBanner.Visibility = Visibility.Visible; StatusText.Text = T("Action failed");
            errorTaskId=null;
        }
        finally
        {
            busy = false; PageContent.IsEnabled = true; Navigation.IsEnabled = true; RefreshButton.IsEnabled = true; PageActions.IsEnabled = true;
            if (refreshPending) { refreshPending = false; QueueRefresh(); }
            if(receiptRefreshQueued)_=Dispatcher.BeginInvoke(new Action(async()=>await ReadReceipts()));
        }
    }

    private async void Navigate(object sender, SelectionChangedEventArgs e) => await Run(() => Task.CompletedTask, "Ready");
    private async void RefreshClick(object sender, RoutedEventArgs e) => await Run(() => Task.CompletedTask, "已刷新");
    private void DismissErrorClick(object sender,RoutedEventArgs e) { ErrorBanner.Visibility=Visibility.Collapsed; errorTaskId=null; }
    private async Task ChangeLanguage(string language)
        => await ChangeAppearance(preferences with { Language = language });

    private async Task ChangeAppearance(DesktopPreferences next)
    {
        var drafts=CaptureInputs();
        await Run(() =>
        {
            Appearance.Save(service.Home, next); preferences = next; Appearance.Apply(this,next); UpdateChrome();
            return Task.CompletedTask;
        }, "Language and appearance saved");
        RestoreInputs(drafts);
    }

    /// <summary>"Follow system" redraws when Windows switches between light and dark apps.</summary>
    private void FollowSystemTheme(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General || preferences.Palette != "system") return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var dark = Appearance.IsDark(this);
            Appearance.Apply(this, preferences);
            if (dark != Appearance.IsDark(this) && !busy) RenderPage();
        }));
    }

    private void NotificationHealthClick(object sender, RoutedEventArgs e) { settingsAnchor = "SettingsNotifications"; Go("Settings"); }

    private static readonly Dictionary<string, (string Subtitle, string? Help)> PageText = new(StringComparer.Ordinal)
    {
        ["Home"] = ("Handoffs between your Agents and their progress", "Relay stores collaboration messages, requests and temporary replies. Keep final deliverables and long-term references in your project folder; relay files expire under the retention policy."),
        ["Agents"] = ("Registered Agents and how Relay reaches them", "One-click setup is for a new node. To change an existing node, select it and use Edit Agent. An Agent bridge connects Relay to the Agent through ACP or its CLI; ACP is used when the host supports it. Without a bridge you copy prompts by hand."),
        ["Connections"] = ("One line per pair of Agents; each direction is enabled separately", "A → B means A can deliver files through Relay and notify B by CLI. A double arrow means both calling directions have a recipe. Green ends are enabled; red ends are paused or not enabled. A direction without an arrow has no recipe. Manual copy remains available; switches only control Relay routes."),
        ["Settings"] = ("Appearance, notifications, relay storage and advanced options", null)
    };

    private void RenderPage()
    {
        if (Navigation.SelectedItem is not ListBoxItem item) return;
        var page = (string)item.Tag;
        fields.Clear();
        PageActions.Children.Clear();
        PageTitle.Text = T(page == "Home" ? "Tasks" : page);
        var (subtitle, help) = PageText[page];
        PageSubtitle.Text = T(subtitle);
        PageHelp.Visibility = help is null ? Visibility.Collapsed : Visibility.Visible;
        PageHelp.ToolTip = help is null ? null : new TextBlock { Text = T(help), TextWrapping = TextWrapping.Wrap, MaxWidth = 380 };
        AutomationProperties.SetName(PageHelp, help is null ? "" : T(help));
        if (page == "Home") NewTaskMenu();
        PageContent.Content = page switch
        {
            "Home" => HomePage(), "Agents" => AgentsPage(), "Connections" => ConnectionsPage(),
            "Settings" => SettingsPage(),
            _ => throw new InvalidOperationException(page)
        };
        Localize(PageContent.Content as DependencyObject);
    }

    /// <summary>One "New task" button; automatic or manual delivery is chosen from its menu.</summary>
    private void NewTaskMenu()
    {
        var trigger = new Button { Tag = "New task", Margin = new(0) };
        AutomationProperties.SetName(trigger, T("New task"));
        trigger.SetResourceReference(StyleProperty, "PrimaryButtonStyle");
        var label = Icons.WithLabel("Plus", T("New task"));
        var chevron = Icons.Create("Down", size: 14); chevron.Margin = new(8, 1, 0, 0);
        label.Children.Add(chevron);
        trigger.Content = label;
        PageActions.Children.Add(trigger);
        var menu = Flyout(trigger, PageActions);
        MenuItem(menu, "New automatic transfer task", "Bolt", NewCliHandoff, "Relay delivers each turn through the Agent bridge");
        MenuItem(menu, "New manual transfer task", "Copy", async () =>
        {
            var agents = service.Agents().Select(a => new HandoffAgentOption(a.AgentId, a.DisplayName ?? a.AgentId)).ToArray();
            if (agents.Length < 2) throw new RelayException("agents_required", T("Add at least two Agents first."));
            var dialog = new FileHandoffDialog(this, service, preferences.Language);
            StatusText.Text = T("Waiting for handoff details…");
            if (dialog.ShowDialog() != true) { reviewCancelled = true; return; }
            var draft = dialog.Result!;
            var existingIds = service.Tasks().Select(v => v.Task.TaskId).ToHashSet(StringComparer.Ordinal);
            var result = await Task.Run(() => service.CreateFileHandoff(draft,reuseExisting:dialog.ReuseExisting));
            taskId = result.Task.TaskId;
            if (!existingIds.Contains(taskId)) service.ConfigureFileHandoff(taskId, dialog.Options(draft));
        }, "You copy the prompt into the Agent yourself");
    }

    private string AgentName(string id) => service.Agents().SingleOrDefault(a=>a.AgentId==id)?.DisplayName ?? id;
    private string HandoffPrompt(TaskView view)
    {
        var task=view.Task;
        var text=DesktopLocalizedContent.TaskPrompt(view,service.Artifacts(task.TaskId),preferences.Language,AgentName);
        text+="\n"+T("Sender")+": "+AgentName(task.RequesterId)+" ("+task.RequesterId+")"+
            "\n"+T("Recipient")+": "+AgentName(task.RecipientId)+" ("+task.RecipientId+")";
        text+="\n\nRelay home: "+service.Home+"\nRequest: "+task.RequestPath+"\nResult: "+task.ResultPath;
        if(task.State!=TaskState.AwaitingManual)return text;
        var executable=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","relay.exe"));
        var command=File.Exists(executable)?"& '"+executable.Replace("'","''")+"'":"relay";
        var common=" --home '"+service.Home.Replace("'","''")+"' task ";
        var identity=$" --task {task.TaskId} --attempt {task.AttemptId} --sender {task.RecipientId}";
        return text+"\n\nPowerShell callback commands (use the installed Relay CLI or equivalent MCP tools):\n"+
            command+common+"ack"+identity+" --idempotency-key received\n"+
            command+common+"start"+identity+" --idempotency-key started\n"+
            command+common+"complete"+identity+" --idempotency-key completed --status succeeded --result '"+task.ResultPath.Replace("'","''")+"'\n"+
            "On failure, use task fail with --status failed --error-code and --message plus the failure report path. Report only actual outcomes. If CLI/MCP is unavailable, return your reply file to the user; they can import it in the task details. File changes alone do not mark a task completed.";
    }
    private TextBlock Note(string text) => Brushed(new TextBlock{Text=T(text),TextWrapping=TextWrapping.Wrap,Margin=new(0,2,0,8),FontSize=12.5},"MutedBrush");
    private void Go(string page)
    {
        Navigation.SelectedItem=Navigation.Items.Cast<ListBoxItem>().Single(i=>(string)i.Tag==page);
    }

    private void ShowCliProbe(string id)
    {
        StatusText.Text=T("CLI test window open…");
        new CliProbeDialog(this,service,id,preferences.Language,demo,message=>StatusText.Text=T(message)).ShowDialog();
    }

    private string? Review(string title,string preview)
    {
        var dialog=new ReviewDialog(this,T(title),preview,preferences.Language);
        reviewCancelled=dialog.ShowDialog()!=true;
        return reviewCancelled ? null : dialog.Reason;
    }
    private Button Add(Panel row,string label,Func<Task> action,bool refresh=true)
    {
        var name=System.Text.RegularExpressions.Regex.Replace(label,"[^a-zA-Z0-9_]","");
        if(name.Length==0 || !char.IsLetter(name[0]))name="Action"+Guid.NewGuid().ToString("N");
        var button=new Button{Content=T(label),Tag=label,Name=name};
        AutomationProperties.SetAutomationId(button,button.Name);AutomationProperties.SetName(button,T(label));row.Children.Add(button);
        button.Click+=async(_,_)=>
        {
            var drafts=label=="Save Accent" ? CaptureInputs() : null;
            await Run(action,T(label)+": "+T("Ready"),refresh);
            if(drafts is not null)RestoreInputs(drafts);
        };
        return button;
    }
    private static StackPanel Page()=>new(){Margin=new Thickness(0,0,0,8)};
    private void Heading(Panel page,string title,string subtitle)
    {page.Children.Add(new TextBlock{Text=T(title),FontSize=15,FontWeight=FontWeights.SemiBold,Margin=new(0,6,0,4)});page.Children.Add(Muted(subtitle,12.5,new(0,0,0,12)));}
    private static WrapPanel Row(Panel page){var row=new WrapPanel{Margin=new(0,10,0,6)};page.Children.Add(row);return row;}
    private TextBox Field(Panel parent,string label,string name,double width)
    {
        var p=new StackPanel{Width=width,Margin=new(0,10,12,0)};
        var header=new DockPanel{MinHeight=36};p.Children.Add(header);
        var box=new TextBox{Name=name}; AutomationProperties.SetAutomationId(box,name); fields.Add(name,box);
        header.Children.Add(new TextBlock{Text=T(label),Foreground=Theme("MutedBrush"),VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.Wrap});
        p.Children.Add(box);parent.Children.Add(p);return box;
    }
    private static TextBox Output(string name,double height)=>new(){Name=name,IsReadOnly=true,Height=height,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,FontFamily=new("Consolas"),FontSize=12};
    private DataGrid Table(string name,params(string Label,string Path)[] columns)
    {var table=new DataGrid{Name=name,Height=200};AutomationProperties.SetAutomationId(table,name);foreach(var c in columns){var binding=new Binding(c.Path);
        if(c.Path is "State" or "Display" or "Permission" or "Capability" or "ValidationStatus" or "VerificationStatus" or "NextAction" or "Request.Purpose")binding.Converter=new UiValueConverter(preferences.Language);
        table.Columns.Add(new DataGridTextColumn{Header=T(c.Label),Binding=binding,ElementStyle=(Style)FindResource("TableTextStyle"),Width=new DataGridLength(1,DataGridLengthUnitType.Star)});}return table;}
    private string Required(TextBox box,string label)=>!string.IsNullOrWhiteSpace(box.Text) ? box.Text.Trim() : throw new RelayException("input_required",string.Format(CultureInfo.CurrentUICulture,T("Please fill in {0}."),T(label)));
    private static string? Optional(TextBox box)=>string.IsNullOrWhiteSpace(box.Text) ? null : box.Text.Trim();
    private int Number(TextBox box)=>int.TryParse(box.Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out var value) ? value : throw new RelayException("invalid_number",T("Settings must be integers."));
    private static Brush Brush(string color)=>(Brush)new BrushConverter().ConvertFromString(color)!;
    private static void OpenFile(string path)
    {if(!File.Exists(path))throw new RelayException("artifact_missing","文件已过期或缺失。");Process.Start(new ProcessStartInfo(path){UseShellExecute=true});}

    private string T(string source)=>UiText.Translate(source,preferences.Language);
    private Dictionary<string,string> CaptureInputs()=>fields.Values.Concat(SetupReplies(PageContent)).ToDictionary(b=>b.Name,b=>b.Text);
    private void RestoreInputs(IReadOnlyDictionary<string,string> drafts)
    {foreach(var box in fields.Values.Concat(SetupReplies(PageContent)))if(drafts.TryGetValue(box.Name,out var value))box.Text=value;}
    private static IEnumerable<TextBox> SetupReplies(DependencyObject root)
    {
        if(root is TextBox box && box.Name.EndsWith("SetupReply",StringComparison.Ordinal))yield return box;
        foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())foreach(var reply in SetupReplies(child))yield return reply;
    }
    private Brush Theme(string key)=>(Brush)FindResource(key);
    private void UpdateChrome()
    {
        Language=System.Windows.Markup.XmlLanguage.GetLanguage(preferences.Language);
        ThemedMessageBox.UiLanguage=preferences.Language;
        RefreshButton.ToolTip=T("Refresh");AutomationProperties.SetName(RefreshButton,T("Refresh"));
        DismissErrorButton.ToolTip=T("Dismiss error");
        AutomationProperties.SetName(DismissErrorButton,T("Dismiss error"));
        DemoBadgeText.Text=T("Demo");DemoBadge.ToolTip=T("DEMO · 虚构 Agent 与预设结果 · 未调用模型，未读取现用配置");
        foreach(ListBoxItem item in Navigation.Items)
        {
            var page=(string)item.Tag;
            var label=T(page=="Home"?"Tasks":page);
            var row=new DockPanel();
            var icon=Icons.Create(page,size:18);icon.Margin=new(0,0,11,0);
            row.Children.Add(icon);
            if(page=="Home"&&attentionCount>0)
            {
                var badge=new Border{CornerRadius=new(10),MinWidth=20,Height=20,Padding=new(6,0,6,0),Margin=new(6,0,0,0),
                    ToolTip=string.Format(CultureInfo.CurrentUICulture,T("{0} tasks need attention"),attentionCount),
                    Child=Brushed(new TextBlock{Text=attentionCount.ToString(CultureInfo.InvariantCulture),FontSize=11.5,FontWeight=FontWeights.SemiBold,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center},"WarnBrush")};
                badge.SetResourceReference(Border.BackgroundProperty,"WarnSoftBrush");
                DockPanel.SetDock(badge,Dock.Right);row.Children.Add(badge);
            }
            row.Children.Add(new TextBlock{Text=label,VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis});
            item.Content=row;item.ToolTip=label;
            AutomationProperties.SetName(item,label);AutomationProperties.SetAutomationId(item,page);
        }
        HealthText.Text=string.Format(CultureInfo.CurrentUICulture,T("Watching · {0} Agents"),service.Agents().Count);
        NotificationHealth.Visibility=notificationSetupHint is null?Visibility.Collapsed:Visibility.Visible;
        NotificationHealthText.Text=T("Desktop notification unavailable");
        NotificationHealth.ToolTip=notificationSetupHint;
    }
    private void Localize(DependencyObject? root)
    {
        if(root is null)return;
        if(root is TextBlock text){text.Tag??=text.Text;text.Text=T((string)text.Tag);}
        if(root is TabItem tab && tab.Header is string header){tab.Tag??=header;tab.Header=T((string)tab.Tag);}
        // Controls whose Tag already names an action keep the label their page chose.
        if(root is ContentControl control && control.Content is string content && control.Tag is null){control.Tag=content;control.Content=T(content);}
        foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())Localize(child);
    }
}

internal sealed class UiValueConverter(string language):IValueConverter
{
    public object Convert(object value,Type targetType,object parameter,CultureInfo culture)=>UiText.Translate(value.ToString()!,language);
    public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)=>throw new NotSupportedException();
}
