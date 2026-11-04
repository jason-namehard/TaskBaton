using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;

namespace AgentRelay.Desktop;

/// <summary>Displays an image with pointer-anchored zoom and persistent drag panning.</summary>
public sealed class ImagePreviewWindow : Window
{
    private readonly Grid viewport;
    private readonly MatrixTransform transform = new(Matrix.Identity);
    private readonly TextBlock zoomLabel;
    private bool panning;
    private Point panStart;
    private Point panOffset;

    public Matrix ImageTransform => transform.Matrix;

    public ImagePreviewWindow(Window owner, BitmapSource image, string language)
    {
        string T(string text) => UiText.Translate(text, language);
        Name = "ImagePreviewWindow";
        AutomationProperties.SetAutomationId(this, Name);
        Owner = owner;
        Title = T("Image preview");
        Width = Math.Min(1000, SystemParameters.WorkArea.Width - 100);
        Height = Math.Min(760, SystemParameters.WorkArea.Height - 100);
        MinWidth = 360;
        MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = owner.FontFamily;
        FontSize = owner.FontSize;
        Resources = owner.Resources;
        UseLayoutRounding = true;
        SetResourceReference(BackgroundProperty, "PageBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        WindowChrome.SetWindowChrome(this, ThemedChrome.Create(resizable: true));
        SourceInitialized += (_, _) => Appearance.ApplyTitleBar(this);

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(ThemedChrome.CaptionHeight) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var title = new TextBlock { Text = Title, Margin = new(16, 0, 55, 0), VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        layout.Children.Add(title);
        var close = ThemedChrome.CaptionButton("Close", "Close", Close, close: true);
        close.Name = "ImagePreviewClose";
        close.Focusable = true;
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.VerticalAlignment = VerticalAlignment.Top;
        AutomationProperties.SetAutomationId(close, close.Name);
        AutomationProperties.SetName(close, T("Close image preview"));
        close.ToolTip = T("Close image preview");
        WindowChrome.SetIsHitTestVisibleInChrome(close, true);
        layout.Children.Add(close);

        viewport = new Grid { Name = "ImagePreviewViewport", ClipToBounds = true, Margin = new(12, 0, 12, 0), Cursor = Cursors.Hand };
        AutomationProperties.SetAutomationId(viewport, viewport.Name);
        viewport.SetResourceReference(Panel.BackgroundProperty, "SurfaceAltBrush");
        var picture = new Image { Source = image, Stretch = Stretch.Uniform, RenderTransform = transform };
        RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
        viewport.Children.Add(picture);
        Grid.SetRow(viewport, 1);
        layout.Children.Add(viewport);

        var footer = new Grid { Margin = new(12, 8, 12, 8) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var hint = new TextBlock { Text = T("Scroll to zoom · Drag to pan"), VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap, Margin = new(4, 0, 8, 0) };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        footer.Children.Add(hint);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(controls, 1);
        footer.Children.Add(controls);
        Button ZoomButton(string name, string label, string accessibleName, double factor)
        {
            var button = new Button { Name = name, Content = label, Width = 32, Height = 32, MinHeight = 0, Padding = new(0), Margin = new(0) };
            AutomationProperties.SetAutomationId(button, name);
            AutomationProperties.SetName(button, T(accessibleName));
            button.ToolTip = T(accessibleName);
            button.Click += (_, _) => ZoomAt(new Point(viewport.ActualWidth / 2, viewport.ActualHeight / 2), factor);
            return button;
        }
        controls.Children.Add(ZoomButton("ImagePreviewZoomOut", "−", "Zoom out", 1 / 1.2));
        zoomLabel = new TextBlock { Text = "100%", MinWidth = 52, TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center };
        controls.Children.Add(zoomLabel);
        controls.Children.Add(ZoomButton("ImagePreviewZoomIn", "+", "Zoom in", 1.2));
        Grid.SetRow(footer, 2);
        layout.Children.Add(footer);
        Content = layout;

        viewport.PreviewMouseWheel += (_, e) =>
        {
            ZoomAt(e.GetPosition(viewport), Math.Pow(1.2, e.Delta / 120.0));
            e.Handled = true;
        };
        viewport.MouseLeftButtonDown += (_, e) =>
        {
            BeginPan(e.GetPosition(viewport));
            viewport.CaptureMouse();
            e.Handled = true;
        };
        viewport.MouseMove += (_, e) =>
        {
            if (panning && e.LeftButton == MouseButtonState.Pressed) PanTo(e.GetPosition(viewport));
        };
        viewport.MouseLeftButtonUp += (_, e) =>
        {
            if (!panning) return;
            EndPan();
            viewport.ReleaseMouseCapture();
            e.Handled = true;
        };
        viewport.LostMouseCapture += (_, _) => EndPan();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            Close();
        };
    }

    public void ZoomAt(Point anchor, double factor)
    {
        RequireFinite(anchor);
        if (!double.IsFinite(factor) || factor <= 0) throw new ArgumentOutOfRangeException(nameof(factor));
        var old = transform.Matrix;
        var scale = Math.Clamp(old.M11 * factor, 0.1, 20);
        var ratio = scale / old.M11;
        var offset = new Point(anchor.X - (anchor.X - old.OffsetX) * ratio,
            anchor.Y - (anchor.Y - old.OffsetY) * ratio);
        RequireFinite(offset);
        transform.Matrix = new Matrix(scale, 0, 0, scale, offset.X, offset.Y);
        zoomLabel.Text = $"{scale * 100:0}%";
    }

    public void BeginPan(Point point)
    {
        RequireFinite(point);
        panning = true;
        panStart = point;
        panOffset = new Point(transform.Matrix.OffsetX, transform.Matrix.OffsetY);
        viewport.Cursor = Cursors.SizeAll;
    }

    public void PanTo(Point point)
    {
        if (!panning) return;
        RequireFinite(point);
        var offset = new Point(panOffset.X + point.X - panStart.X, panOffset.Y + point.Y - panStart.Y);
        RequireFinite(offset);
        var matrix = transform.Matrix;
        matrix.OffsetX = offset.X;
        matrix.OffsetY = offset.Y;
        transform.Matrix = matrix;
    }

    public void EndPan()
    {
        panning = false;
        viewport.Cursor = Cursors.Hand;
    }

    private static void RequireFinite(Point point)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y)) throw new ArgumentOutOfRangeException(nameof(point));
    }
}
