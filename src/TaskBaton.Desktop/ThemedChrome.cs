using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Shell;

namespace TaskBaton.Desktop;

/// <summary>
/// Replaces the system title bar with the theme's own: the window keeps its native resizing, snapping and system menu,
/// and the app draws the caption buttons.
/// </summary>
internal static class ThemedChrome
{
    public const double CaptionHeight = 38;

    public static WindowChrome Create(bool resizable) => new()
    {
        CaptionHeight = CaptionHeight, ResizeBorderThickness = resizable ? new Thickness(6) : new Thickness(0),
        // One glass pixel keeps the system shadow behind a frame the app draws itself.
        GlassFrameThickness = new Thickness(0, 0, 0, 1), CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false
    };

    /// <summary>For dialogs: the content fills the window and a close button floats in the caption corner.</summary>
    public static void ApplyToDialog(Window dialog, bool resizable)
    {
        WindowChrome.SetWindowChrome(dialog, Create(resizable));
        var content = (UIElement)dialog.Content;
        dialog.Content = null;
        var root = new Grid();
        root.SetResourceReference(Panel.BackgroundProperty, "PageBrush");
        root.Children.Add(content);
        var close = CaptionButton("Close", "Close", () => dialog.Close(), close: true);
        close.HorizontalAlignment = HorizontalAlignment.Right; close.VerticalAlignment = VerticalAlignment.Top;
        root.Children.Add(close);
        dialog.Content = root;
        dialog.SourceInitialized += (_, _) => Appearance.ApplyTitleBar(dialog);
    }

    public static Button CaptionButton(string glyph, string name, Action click, bool close = false)
    {
        var path = new Path
        {
            Data = Geometry.Parse(glyph switch
            {
                "Minimize" => "M0,5 L10,5",
                "Maximize" => "M0.5,0.5 L9.5,0.5 L9.5,9.5 L0.5,9.5 Z",
                "Restore" => "M2.5,0.5 L9.5,0.5 L9.5,7.5 M0.5,2.5 L7.5,2.5 L7.5,9.5 L0.5,9.5 Z",
                _ => "M0.5,0.5 L9.5,9.5 M9.5,0.5 L0.5,9.5"
            }),
            Width = 10, Height = 10, StrokeThickness = 1, SnapsToDevicePixels = true
        };
        path.SetBinding(Shape.StrokeProperty, new System.Windows.Data.Binding(nameof(Control.Foreground))
            { RelativeSource = new(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Button), 1) });
        var button = new Button { Content = path, Name = "Caption" + name, Focusable = false };
        button.SetResourceReference(FrameworkElement.StyleProperty, close ? "CaptionCloseButtonStyle" : "CaptionButtonStyle");
        AutomationProperties.SetName(button, name);
        button.Click += (_, _) => click();
        return button;
    }
}
