using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using TaskBaton.Core;

namespace TaskBaton.Desktop;

public partial class MainWindow
{
    private bool closeChoiceOpen;

    public void RequestClose()
    {
        if (closeChoiceOpen) return;
        try
        {
            if (preferences.CloseToTray is null)
            {
                closeChoiceOpen = true;
                var choice = new CloseBehaviorDialog(this, preferences.Language);
                if (choice.ShowDialog() != true) return;
                SaveCloseBehavior(choice.CloseToTray);
            }
            if (preferences.CloseToTray == true) Hide();
            else RequestExit();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or RelayException)
        {
            ErrorText.Text = error.Message; ErrorBanner.Visibility = Visibility.Visible;
        }
        finally { closeChoiceOpen = false; }
    }

    public void RequestExit() => Close();

    private bool ReceiveCloseMessage(int message, nint wParam)
    {
        // SC_CLOSE is the user's close button or Alt+F4; programmatic shutdown remains an exit.
        if (message != 0x112 || ((long)wParam & 0xFFF0) != 0xF060) return false;
        Dispatcher.BeginInvoke(new Action(RequestClose));
        return true;
    }

    private void SaveCloseBehavior(bool closeToTray)
    {
        var next = preferences with { CloseToTray = closeToTray };
        Appearance.Save(service.Home, next);
        preferences = next;
    }

    private void AddCloseBehaviorSetting(Panel group)
    {
        string Text(string key) => CloseBehaviorText.Get(key, preferences.Language);
        var setting = new CheckBox { Name = "CloseToTraySetting", IsChecked = preferences.CloseToTray == true };
        setting.SetResourceReference(StyleProperty, "SwitchStyle");
        AutomationProperties.SetAutomationId(setting, setting.Name);
        AutomationProperties.SetName(setting, Text("setting"));
        SettingRow(group, Text("setting"), Text("description"), setting);
        var restoring = false;
        async void Changed(object sender, RoutedEventArgs args)
        {
            if (restoring) return;
            await Run(() => { SaveCloseBehavior(setting.IsChecked == true); return Task.CompletedTask; }, Text("saved"), refresh: false);
            restoring = true;
            try { setting.IsChecked = preferences.CloseToTray == true; }
            finally { restoring = false; }
        }
        setting.Checked += Changed; setting.Unchecked += Changed;
    }
}
