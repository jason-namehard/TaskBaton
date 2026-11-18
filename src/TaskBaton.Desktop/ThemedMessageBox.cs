using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TaskBaton.Desktop;

/// <summary>Confirmations and notices in the app's own theme, with the same contract as <see cref="MessageBox"/>.</summary>
public sealed class ThemedMessageBox : Window
{
    /// <summary>The interface language for button labels; the main window keeps it current.</summary>
    internal static string UiLanguage { get; set; } = "en";

    public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;

    public static MessageBoxResult Show(Window owner, string message, string caption,
        MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.None)
    {
        var box = new ThemedMessageBox(owner, message, caption, buttons, image);
        box.ShowDialog();
        return box.Result;
    }

    private ThemedMessageBox(Window owner, string message, string caption, MessageBoxButton buttons, MessageBoxImage image)
    {
        string T(string text) => UiText.Translate(text, UiLanguage);
        Name = "ThemedMessage"; Owner = owner; Title = caption; Width = 470; SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = owner.FontFamily; FontSize = 13.5; Resources = owner.Resources; UseLayoutRounding = true;
        SetResourceReference(BackgroundProperty, "PageBrush"); SetResourceReference(ForegroundProperty, "TextBrush");
        var tone = image switch { MessageBoxImage.Warning => "warn", MessageBoxImage.Error => "bad", _ => "info" };
        var (soft, strong) = tone switch { "warn" => ("WarnSoftBrush", "WarnBrush"), "bad" => ("BadSoftBrush", "BadBrush"), _ => ("InfoSoftBrush", "InfoBrush") };

        var layout = new DockPanel();
        var footer = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 0, -8, -8) };
        var footerFrame = new Border { Child = footer, Padding = new(24, 14, 24, 16), BorderThickness = new(0, 1, 0, 0) };
        footerFrame.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        footerFrame.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        DockPanel.SetDock(footerFrame, Dock.Bottom); layout.Children.Add(footerFrame);

        var body = new Grid { Margin = new(24, 26, 26, 20) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new(1, GridUnitType.Star) });
        var badge = new Border { Width = 36, Height = 36, CornerRadius = new(18), VerticalAlignment = VerticalAlignment.Top, Margin = new(0, 0, 14, 0),
            Child = Icons.Create(image switch { MessageBoxImage.Warning or MessageBoxImage.Error => "Alert", MessageBoxImage.Question => "Help", _ => "Info" }, size: 19, brushKey: strong) };
        badge.SetResourceReference(Border.BackgroundProperty, soft);
        body.Children.Add(badge);
        var text = new StackPanel();
        Grid.SetColumn(text, 1); body.Children.Add(text);
        text.Children.Add(new TextBlock { Text = caption, FontSize = 15.5, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new(0, 6, 30, 6) });
        var detail = new TextBlock { Name = "MessageText", Text = message, TextWrapping = TextWrapping.Wrap, LineHeight = 20 };
        detail.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        text.Children.Add(new ScrollViewer { Content = detail, MaxHeight = 380, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        layout.Children.Add(body);
        Content = layout;

        void Add(string label, string name, MessageBoxResult result, bool primary, bool cancel)
        {
            var button = new Button { Content = T(label), Name = name, IsDefault = primary, IsCancel = cancel, MinWidth = 84 };
            AutomationProperties.SetAutomationId(button, name);
            if (primary) button.SetResourceReference(StyleProperty, image == MessageBoxImage.Warning && buttons != MessageBoxButton.OK ? "DangerPrimaryButtonStyle" : "PrimaryButtonStyle");
            button.Click += (_, _) => { Result = result; Close(); };
            footer.Children.Add(button);
        }
        switch (buttons)
        {
            case MessageBoxButton.OKCancel: Add("OK", "MessageOk", MessageBoxResult.OK, true, false); Add("Cancel", "MessageCancel", MessageBoxResult.Cancel, false, true); break;
            case MessageBoxButton.YesNo: Add("Yes", "MessageYes", MessageBoxResult.Yes, true, false); Add("No", "MessageNo", MessageBoxResult.No, false, true); break;
            case MessageBoxButton.YesNoCancel: Add("Yes", "MessageYes", MessageBoxResult.Yes, true, false); Add("No", "MessageNo", MessageBoxResult.No, false, false); Add("Cancel", "MessageCancel", MessageBoxResult.Cancel, false, true); break;
            default: Add("OK", "MessageOk", MessageBoxResult.OK, true, true); break;
        }
        // Closing from the caption counts as the safe answer, like the system dialog.
        Closed += (_, _) => { if (Result == MessageBoxResult.None) Result = buttons is MessageBoxButton.OK ? MessageBoxResult.OK : buttons is MessageBoxButton.YesNo ? MessageBoxResult.No : MessageBoxResult.Cancel; };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        ThemedChrome.ApplyToDialog(this, resizable: false);
    }
}
