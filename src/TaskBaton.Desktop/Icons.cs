using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace TaskBaton.Desktop;

/// <summary>Line icons drawn on a 20-unit grid with round 1.5-unit strokes, so they read the same on Windows 10 and 11.</summary>
internal static class Icons
{
    private static readonly Dictionary<string, string> Shapes = new(StringComparer.Ordinal)
    {
        ["Home"] = "M5.5,3.5 L14.5,3.5 Q17,3.5 17,6 L17,14 Q17,16.5 14.5,16.5 L5.5,16.5 Q3,16.5 3,14 L3,6 Q3,3.5 5.5,3.5 Z M7,7.5 L13,7.5 M7,10.5 L13,10.5 M7,13.5 L10.5,13.5",
        ["Agents"] = "M10.75,7 A2.75,2.75 0 1 1 5.25,7 A2.75,2.75 0 1 1 10.75,7 M3.5,16 C4.1,13.4 5.9,12 8,12 C10.1,12 11.9,13.4 12.5,16 M13,4.6 A2.5,2.5 0 0 1 13,9.4 M14.5,12.3 C15.6,12.9 16.3,14.1 16.5,16",
        ["Connections"] = "M7,10 A2,2 0 1 1 3,10 A2,2 0 1 1 7,10 M17,5 A2,2 0 1 1 13,5 A2,2 0 1 1 17,5 M17,15 A2,2 0 1 1 13,15 A2,2 0 1 1 17,15 M6.8,9.1 L13.2,5.9 M6.8,10.9 L13.2,14.1",
        ["Settings"] = "M4,6 L11,6 M15,6 L16,6 M4,14 L5,14 M9,14 L16,14 M15,6 A2,2 0 1 1 11,6 A2,2 0 1 1 15,6 M9,14 A2,2 0 1 1 5,14 A2,2 0 1 1 9,14",
        ["Plus"] = "M10,4.5 L10,15.5 M4.5,10 L15.5,10",
        ["Refresh"] = "M15.5,8.2 A6,6 0 1 0 16,11.5 M16,4 L16,8.2 L11.8,8.2",
        ["More"] = "M5,10 L5.01,10 M10,10 L10.01,10 M15,10 L15.01,10",
        ["Down"] = "M6,8 L10,12 L14,8",
        ["Arrow"] = "M4,10 L15,10 M11,6 L15,10 L11,14",
        ["Folder"] = "M3,6.5 Q3,5 4.5,5 L8,5 L9.5,6.5 L15.5,6.5 Q17,6.5 17,8 L17,14.5 Q17,16 15.5,16 L4.5,16 Q3,16 3,14.5 Z",
        ["Copy"] = "M8.5,7 L14.5,7 Q16,7 16,8.5 L16,14.5 Q16,16 14.5,16 L8.5,16 Q7,16 7,14.5 L7,8.5 Q7,7 8.5,7 Z M13,7 L13,5.5 Q13,4 11.5,4 L5.5,4 Q4,4 4,5.5 L4,11.5 Q4,13 5.5,13 L7,13",
        ["Search"] = "M14,9 A5,5 0 1 1 4,9 A5,5 0 1 1 14,9 M13,13 L16.5,16.5",
        ["Help"] = "M17,10 A7,7 0 1 1 3,10 A7,7 0 1 1 17,10 M8.2,8 A1.9,1.9 0 1 1 10.8,9.8 C10.3,10 10,10.4 10,10.9 L10,11.3 M10,13.7 L10.01,13.7",
        ["Alert"] = "M10,3.5 L17,16 L3,16 Z M10,8.3 L10,11.7 M10,13.8 L10.01,13.8",
        ["Info"] = "M17,10 A7,7 0 1 1 3,10 A7,7 0 1 1 17,10 M10,9 L10,13.5 M10,6.6 L10.01,6.6",
        ["Check"] = "M5,10.5 L8,13.5 L15,6.5",
        ["Play"] = "M7,5 L15,10 L7,15 Z",
        ["Trash"] = "M4.5,6 L15.5,6 M8,6 L8,4.5 L12,4.5 L12,6 M6,6 L6.7,16 L13.3,16 L14,6",
        ["Close"] = "M5.5,5.5 L14.5,14.5 M14.5,5.5 L5.5,14.5",
        ["Bridge"] = "M8.5,11.5 L11.5,8.5 M9.5,6.5 L10.5,5.5 A3,3 0 0 1 14.7,9.7 L13.7,10.7 M10.5,13.5 L9.5,14.5 A3,3 0 0 1 5.3,10.3 L6.3,9.3",
        ["Bolt"] = "M11,3 L5,11 L9.5,11 L9,17 L15,9 L10.5,9 Z",
        ["Edit"] = "M4,16 L7,16 L15.5,7.5 L12.5,4.5 L4,13 Z M11,6 L14,9",
        ["Doc"] = "M6,3.5 L11.5,3.5 L15,7 L15,16.5 L6,16.5 Z M11,3.5 L11,7 L15,7",
        ["Pulse"] = "M3,10 L6,10 L8,6 L12,14 L14,10 L17,10",
        ["Model"] = "M7,4 L13,4 Q16,4 16,7 L16,13 Q16,16 13,16 L7,16 Q4,16 4,13 L4,7 Q4,4 7,4 Z M8,8 L12,8 L12,12 L8,12 Z",
        ["Bell"] = "M6,13.5 L6,9 A4,4 0 0 1 14,9 L14,13.5 L15.5,15 L4.5,15 Z M8.5,17 L11.5,17",
        ["Select"] = "M5.5,3.5 L14.5,3.5 Q16.5,3.5 16.5,5.5 L16.5,14.5 Q16.5,16.5 14.5,16.5 L5.5,16.5 Q3.5,16.5 3.5,14.5 L3.5,5.5 Q3.5,3.5 5.5,3.5 Z M6.5,10 L9,12.5 L13.5,7.5"
    };

    /// <summary>Strokes follow the nearest control's Foreground unless a brush or a theme resource key is given.</summary>
    public static FrameworkElement Create(string name, Brush? stroke = null, double size = 18, double thickness = 1.5, string? brushKey = null)
    {
        var path = new Path
        {
            Data = Geometry.Parse(Shapes[name]), StrokeThickness = name == "More" ? 2.4 : thickness,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round
        };
        if (brushKey is not null) path.SetResourceReference(Shape.StrokeProperty, brushKey);
        else if (stroke is null) path.SetBinding(Shape.StrokeProperty, new System.Windows.Data.Binding(nameof(Control.Foreground))
            { RelativeSource = new(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Control), 1) });
        else path.Stroke = stroke;
        var grid = new Canvas { Width = 20, Height = 20, Children = { path } };
        return new Viewbox { Width = size, Height = size, Child = grid, VerticalAlignment = VerticalAlignment.Center, SnapsToDevicePixels = true };
    }

    /// <summary>An icon followed by a label, for buttons that keep their English label in Tag for automation.</summary>
    public static StackPanel WithLabel(string icon, string label, double size = 16)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Create(icon, size: size));
        row.Children.Add(new TextBlock { Text = label, Margin = new(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        return row;
    }
}
