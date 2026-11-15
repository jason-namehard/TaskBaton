using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AgentRelay.Core;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Microsoft.Windows.AppLifecycle;

namespace AgentRelay.Notify;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        string? report = Option(args, "--report-path");
        void Write(ResumeResult result)
        {
            if (report is not null) File.WriteAllText(report, RelayJson.Write(result));
        }
        try
        {
            if (args.Contains("--probe"))
            {
                if (!AppNotificationManager.IsSupported()) return 3;
                Write(new ResumeResult("supported", Detail: "agent-relay-desktop:1"));
                return 0;
            }
            if (args.Contains("--copy"))
            {
                Clipboard.SetText(ReadTask(args).Prompt);
                Write(new ResumeResult("completed", Detail: "继续提示词已复制。"));
                return 0;
            }
            if (args.Contains("--open"))
            {
                var view = ReadTask(args);
                var window = DetailWindow(view);
                window.Closed += (_, _) => app.Shutdown();
                return app.Run(window);
            }
            if (!AppNotificationManager.IsSupported())
            {
                Write(new ResumeResult("unavailable", "notifications_unsupported", "Windows 当前不支持应用通知。"));
                return 3;
            }
            var manager = AppNotificationManager.Default;
            void Handle(AppNotificationActivatedEventArgs activation)
            {
                app.Dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        var values = activation.Arguments;
                        if(values.TryGetValue("action",out var testAction)&&testAction=="test")
                        {app.Shutdown();return;}
                        if (!values.TryGetValue("home", out var home) || !values.TryGetValue("task", out var task) ||
                            !values.TryGetValue("attempt", out var attempt) || !values.TryGetValue("action", out var action) || action is not ("copy" or "view"))
                            throw new RelayException("notification_arguments_invalid", "通知参数无效，请在 Inbox 查看任务。", 3);
                        var view = new RelayService(home).Status(task);
                        RequireAttempt(view, attempt);
                        var copied = action == "copy";
                        if (copied) Clipboard.SetText(view.Prompt);
                        var window = DetailWindow(view, copied);
                        window.Closed += (_, _) => app.Shutdown();
                        window.Show(); window.Activate();
                    }
                    catch (Exception error) when (error is RelayException or IOException or UnauthorizedAccessException or ExternalException)
                    {
                        MessageBox.Show(error.Message, "TaskBaton", MessageBoxButton.OK, MessageBoxImage.Error);
                        app.Shutdown(3);
                    }
                });
            }
            manager.NotificationInvoked += (_, activation) => Handle(activation);
            manager.Register();
            app.Exit += (_, _) => manager.Unregister();
            var activated = AppInstance.GetCurrent().GetActivatedEventArgs();
            if (activated.Kind == ExtendedActivationKind.AppNotification)
                Handle((AppNotificationActivatedEventArgs)activated.Data);
            if (args.Contains("--unregister"))
            {
                manager.UnregisterAll();
                return 0;
            }
            if (args.Contains("--notify")||args.Contains("--test"))
            {
                if (manager.Setting != AppNotificationSetting.Enabled)
                {
                    Write(new ResumeResult("unavailable", "notifications_disabled", "Windows 通知设置：" + manager.Setting));
                    manager.Unregister();
                    return 3;
                }
                var builder=new AppNotificationBuilder();
                if(args.Contains("--test"))
                    builder.AddArgument("action","test").AddText(Required(args,"--title")).AddText(Required(args,"--message"));
                else
                {
                    var home=Required(args,"--home");var task=Required(args,"--task");
                    var service=new RelayService(home);var view=service.Status(task);
                    RequireAttempt(view,Required(args,"--attempt"));
                    var content=service.NotificationContent(view.Task);
                    builder.AddArgument("action","view").AddArgument("home",home).AddArgument("task",task).AddArgument("attempt",view.Task.AttemptId)
                        .AddText(content.Title).AddText(content.Message)
                        .AddButton(new AppNotificationButton("打开任务").AddArgument("action","view").AddArgument("home",home).AddArgument("task",task).AddArgument("attempt",view.Task.AttemptId));
                }
                if(args.Contains("--silent"))builder.MuteAudio();
                var notification=builder.BuildNotification();
                if(args.Contains("--history-only"))notification.SuppressDisplay=true;
                manager.Show(notification);
                if (notification.Id == 0) throw new InvalidOperationException("Windows did not assign a notification ID.");
                Write(new ResumeResult("submitted", Detail: args.Contains("--history-only")
                    ? $"Windows 已记录通知 {notification.Id}；屏幕提醒由 TaskBaton 主程序显示。"
                    : $"Windows 已接受通知 {notification.Id}；可见性取决于系统通知设置。"));
                manager.Unregister();
                return 0;
            }
            // Windows launches this process through the app-owned COM activation registration.
            if (!args.Any(a => a.Contains("AppNotificationActivated", StringComparison.Ordinal)))
                throw new ArgumentException("Use --notify, --test, --open, --copy, --probe or --unregister.");
            return app.Run();
        }
        catch (Exception error) when (error is RelayException or IOException or UnauthorizedAccessException or ExternalException or ArgumentException)
        {
            Write(new ResumeResult("failed", "desktop_error", error.Message));
            Console.Error.WriteLine(error.Message);
            return 3;
        }
    }

    private static TaskView ReadTask(string[] args) => new RelayService(Required(args, "--home")).Status(Required(args, "--task"));
    private static void RequireAttempt(TaskView view, string attempt)
    {
        if (view.Task.AttemptId != attempt)
            throw new RelayException("notification_stale", "此通知属于旧尝试，请在 Inbox 查看当前任务。", 3);
    }
    private static string Required(string[] args, string name) => Option(args, name) ?? throw new ArgumentException(name + " is required.");
    private static string? Option(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        if (index < 0) return null;
        if (index + 1 == args.Length) throw new ArgumentException(name + " requires a value.");
        return args[index + 1];
    }

    private static Window DetailWindow(TaskView view, bool copied = false)
    {
        var panel = new DockPanel { Margin = new Thickness(24) };
        var title = new TextBlock { Text = "TaskBaton · 继续任务", FontSize = 24, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 14) };
        DockPanel.SetDock(title, Dock.Top); panel.Children.Add(title);
        var state = new TextBlock { Text = $"{view.Task.RequesterId} ← {view.Task.RecipientId}    {view.Task.State}\n任务 {view.Task.TaskId}\n会话恢复：{view.Task.ResumeStatus}",
            Margin = new Thickness(0, 0, 0, 18), TextWrapping = TextWrapping.Wrap };
        DockPanel.SetDock(state, Dock.Top); panel.Children.Add(state);
        var copy = new Button { Content = copied ? "已复制 · 再次复制" : "复制继续提示词", Padding = new Thickness(18, 10, 18, 10),
            HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(view.Prompt); copy.Content = "已复制 · 粘贴到原会话继续"; }
            catch (ExternalException error) { MessageBox.Show(error.Message, "剪贴板暂不可用"); }
        };
        DockPanel.SetDock(copy, Dock.Bottom); panel.Children.Add(copy);
        panel.Children.Add(new TextBox { Text = view.Prompt, IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 15, Padding = new Thickness(12),
            Background = new SolidColorBrush(Color.FromRgb(245, 247, 250)) });
        return new Window { Title = "TaskBaton", Width = 720, Height = 440, MinWidth = 520, MinHeight = 340,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, Content = panel };
    }
}

