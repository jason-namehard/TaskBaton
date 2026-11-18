using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TaskBaton.Desktop;

public sealed class ReviewDialog : Window
{
    public string Reason { get; private set; } = "";

    public ReviewDialog(Window owner, string title, string preview, string language = "en")
    {
        string T(string value)=>UiText.Translate(value,language);
        Owner = owner; Title = title; Width = 750; Height = 590; MinWidth = 550; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = owner.FontFamily; FontSize = 13; Resources = owner.Resources;
        Background=(Brush)owner.FindResource("PageBrush");Foreground=(Brush)owner.FindResource("TextBrush");
        SourceInitialized += (_, _) => Appearance.ApplyTitleBar(this);
        var panel = new DockPanel { Margin = new Thickness(26, 40, 26, 20) };
        Content = panel;
        var bottom = new StackPanel(); DockPanel.SetDock(bottom, Dock.Bottom); panel.Children.Add(bottom);
        bottom.Children.Add(new TextBlock { Text = T("Approval reason"), Margin = new Thickness(0,12,0,0) });
        var reason = new TextBox { Name = "ApprovalReason" }; bottom.Children.Add(reason);
        var check = new CheckBox { Name = "ApprovalCheck", Content = T("我已审阅目标、具体变更和影响，批准本次操作。"), Margin = new Thickness(0,0,0,12) };
        bottom.Children.Add(check);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; bottom.Children.Add(buttons);
        var approve = new Button { Name = "ApproveButton", Content = T("Approve"), IsEnabled = false, IsDefault = true,Style=(Style)owner.FindResource("PrimaryButtonStyle") };
        var cancel = new Button { Content = T("Cancel"), IsCancel = true }; buttons.Children.Add(approve); buttons.Children.Add(cancel);
        reason.TextChanged += (_, _) => approve.IsEnabled = check.IsChecked == true && !string.IsNullOrWhiteSpace(reason.Text);
        check.Checked += (_, _) => approve.IsEnabled = !string.IsNullOrWhiteSpace(reason.Text);
        check.Unchecked += (_, _) => approve.IsEnabled = false;
        approve.Click += (_, _) => { Reason = reason.Text.Trim(); DialogResult = true; };
        panel.Children.Add(new TextBox { Text = preview, IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new("Consolas") });
        ThemedChrome.ApplyToDialog(this, resizable: true);
    }
}
