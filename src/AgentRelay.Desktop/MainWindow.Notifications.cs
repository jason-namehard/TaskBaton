using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AgentRelay.Core;

namespace AgentRelay.Desktop;

public partial class MainWindow
{
    private string? notificationSetupHint;
    private Window? activeNotificationWindow;
    private string CurrentNotifierPath()=>Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","desktop","AgentRelay.Notify.exe"));

    private void InitializeNotificationDelivery()
    {
        try
        {
            service.RebindDesktopExecutable(CurrentNotifierPath());
            if(preferences.LastNotificationId is null)
                SaveNotificationCursor(service.Inbox().Select(n=>n.NotificationId).DefaultIfEmpty().Max());
        }
        catch(Exception error) when(error is RelayException or IOException or UnauthorizedAccessException)
        {notificationSetupHint=T("Desktop notification unavailable")+": "+error.Message;}
    }

    private void SaveNotificationCursor(long id)
    {
        var next=preferences with {LastNotificationId=id};Appearance.Save(service.Home,next);preferences=next;
    }

    private async Task<string?> DeliverPendingDesktopNotificationsAsync()
    {
        try
        {
            if(preferences.LastNotificationId is null)InitializeNotificationDelivery();
            var previous=preferences.LastNotificationId??0;
            var pending=service.Inbox().Where(n=>n.NotificationId>previous).OrderBy(n=>n.NotificationId).ToArray();
            if(pending.Length==0)return notificationSetupHint;
            // Claim this batch before external delivery. Unknown process results must not cause duplicate pop-ups after refresh.
            SaveNotificationCursor(pending[^1].NotificationId);
            if(service.DesktopExecutable() is null)return null;
            var tasks=service.Tasks().ToDictionary(v=>v.Task.TaskId,v=>v.Task,StringComparer.Ordinal);
            string? hint=notificationSetupHint;
            foreach(var item in pending.GroupBy(n=>n.TaskId).Select(g=>g.Last()))
            {
                if(!tasks.TryGetValue(item.TaskId,out var task))continue; // A user may have deleted a task before its desktop event was handled.
                var result=await new DesktopNotifier(service.DesktopExecutable(),muteAudio:true,suppressDisplay:true).NotifyAsync(service.Home,task,CancellationToken.None);
                ShowApplicationNotification(service.NotificationContent(task),task.TaskId);
                PlayNotificationSound(preferences.NotificationSound);
                if(result.State!="submitted")
                    hint=T("Desktop notification unavailable")+": "+result.Detail+
                        " · "+T("An in-app notification was shown instead.");
            }
            return hint;
        }
        catch(Exception error) when(error is RelayException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {return T("Desktop notification unavailable")+": "+error.Message;}
    }

    private void ShowApplicationNotification(DesktopNotificationContent content,string? targetTask=null)
    {
        activeNotificationWindow?.Close();
        var popup=new ToastNotificationWindow(this,content,T("In-app notification"),T("Close"),
            targetTask is null?null:T("Open task"),targetTask is null?null:()=>{taskId=targetTask;Go("Home");OpenFromTray();});
        var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(9)};
        timer.Tick+=(_,_)=>popup.Close();
        popup.MouseEnter+=(_,_)=>timer.Stop();popup.MouseLeave+=(_,_)=>timer.Start();
        void OwnerClosed(object? sender,EventArgs args)=>popup.Close();
        Closed+=OwnerClosed;
        popup.Closed+=(_,_)=>{timer.Stop();Closed-=OwnerClosed;if(activeNotificationWindow==popup)activeNotificationWindow=null;};
        activeNotificationWindow=popup;popup.Show();timer.Start();
    }

    private static void PlayNotificationSound(string sound)
        =>NotificationSounds.Play(sound);

    private void AddNotificationSettings(Panel content)
    {
        var group=Group(content,"Notifications","SettingsNotifications");
        var notify=new CheckBox{Name="EnableDesktopNotifications",IsChecked=service.DesktopExecutable() is not null};
        notify.SetResourceReference(StyleProperty,"SwitchStyle");
        AutomationProperties.SetAutomationId(notify,notify.Name);
        AutomationProperties.SetName(notify,T("Enable Notifications"));
        var enable=SettingRow(group,"Notify me when a handoff finishes","Notify when Relay processes a completed handoff. Closing this app does not start a background monitor.",notify);
        if(notificationSetupHint is not null)
        {
            var warning=new StackPanel{Orientation=Orientation.Horizontal,Margin=new(0,6,0,0)};
            warning.Children.Add(Icons.Create("Alert",size:15,brushKey:"WarnBrush"));
            warning.Children.Add(Brushed(new TextBlock{Text=notificationSetupHint,FontSize=12.5,TextWrapping=TextWrapping.Wrap,Margin=new(7,0,0,0),MaxWidth=520},"WarnBrush"));
            ((Panel)enable.Children[0]).Children.Add(warning);
        }
        var restoringNotification=false;
        async void NotificationChanged(object sender,RoutedEventArgs args)
        {
            if(restoringNotification)return;
            var enabled=notify.IsChecked==true;
            await Run(async()=>
            {
                if(enabled)
                {
                    try
                    {
                        await service.ConfigureDesktopAsync(CurrentNotifierPath());
                        notificationSetupHint=null;
                    }
                    catch(Exception error) when(error is RelayException or System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
                    {
                        service.EnableDesktopFallback(CurrentNotifierPath());
                        notificationSetupHint=T("Native notifications are unavailable. In-app notifications remain enabled.")+" "+error.Message;
                    }
                    SaveNotificationCursor(service.Inbox().Select(n=>n.NotificationId).DefaultIfEmpty().Max());
                }
                else service.DisableDesktop();
            },enabled?"Desktop notifications enabled":"Desktop notifications disabled",refresh:false);
            restoringNotification=true;
            try{notify.IsChecked=service.DesktopExecutable() is not null;}
            finally{restoringNotification=false;}
        }
        notify.Checked+=NotificationChanged;
        notify.Unchecked+=NotificationChanged;

        var choice=new ComboBox{Name="NotificationSoundChoice",Width=160,Margin=new(0),DisplayMemberPath="Label",SelectedValuePath="Id",
            ItemsSource=new[]{new AppearanceOption("none",T("Silent")),new AppearanceOption("waterdrop",T("Water drop")),new AppearanceOption("soft-piano",T("Soft piano")),new AppearanceOption("warm-chord",T("Warm chord")),new AppearanceOption("error",T("Error sound"))},SelectedValue=preferences.NotificationSound};
        AutomationProperties.SetAutomationId(choice,choice.Name);
        choice.SelectionChanged+=async(_,_)=>
        {
            if(choice.SelectedValue is not string sound||sound==preferences.NotificationSound)return;
            await Run(()=>{var next=preferences with {NotificationSound=sound};Appearance.Save(service.Home,next);preferences=next;return Task.CompletedTask;},"Notification sound saved",refresh:false);
        };
        var sound=SettingRow(group,"Notification sound","Sounds follow Windows volume settings. Notification banners also depend on Windows notification and Do not disturb settings.",choice);
        Styled(RowAction(sound,"Preview sound",()=>{PlayNotificationSound((string)choice.SelectedValue);return Task.CompletedTask;},false),"GhostButtonStyle");
        RowAction(SettingRow(group,"Test notification","Shows a sample in the lower-right corner. No task or model session is created."),"Send test notification",async()=>
        {
            var content=new DesktopNotificationContent("Agent Relay · "+T("Test notification"),T("This tests desktop notifications only. No task or model session was created."));
            var result=await new DesktopNotifier(File.Exists(CurrentNotifierPath())?CurrentNotifierPath():service.DesktopExecutable(),muteAudio:true,suppressDisplay:true)
                .TestAsync(service.Home,content.Title,content.Message);
            ShowApplicationNotification(content);PlayNotificationSound(preferences.NotificationSound);
            notificationSetupHint=result.State=="submitted"?null:T("Desktop notification unavailable")+": "+result.Detail+" · "+T("An in-app notification was shown instead.");
            UpdateChrome();
            LedgerHint.Text=result.State=="submitted"?T("The in-app reminder was shown and Windows accepted a silent notification-center entry."):"";
        },false);
    }
}
