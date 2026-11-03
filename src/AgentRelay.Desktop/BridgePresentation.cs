using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using AgentRelay.Core;

namespace AgentRelay.Desktop;

/// <summary>One visual language for "how does a turn reach this Agent": a coloured dot, a short label and, in forms, an explanation card.</summary>
internal static class BridgePresentation
{
    private static readonly Brush Pending = Frozen("#8C691F");

    public static bool HasFailure(CliExtensionRecord? extension) => extension is
        { Block: not null } or { CreateProbe.MachinePassed: false } or { ResumeProbe.MachinePassed: false };

    private static Brush Frozen(string color)
    {
        var brush = (Brush)new BrushConverter().ConvertFromString(color)!;
        brush.Freeze();
        return brush;
    }

    public static Brush Accent(FrameworkElement owner, AgentBridge bridge) => bridge.State switch
    {
        "verified" => (Brush)owner.FindResource("AllowedBrush"),
        "unverified" => Pending,
        "blocked" => (Brush)owner.FindResource("DeniedBrush"),
        _ => (Brush)owner.FindResource("MutedBrush")
    };

    public static string Label(AgentBridge bridge, Func<string, string> t) => bridge.Kind switch
    {
        "none" => t("None · Copy prompts manually"),
        _ => (bridge.Kind switch { "acp" => "ACP", "desktop" => t("Desktop"), _ => "CLI" }) + " · " + t(bridge.State switch
        {
            "verified" => "Verified",
            "blocked" => "Needs attention",
            _ => "Not yet verified"
        })
    };

    public static FrameworkElement Badge(FrameworkElement owner, AgentBridge bridge, Func<string, string> t)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = Accent(owner, bridge), Margin = new(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock { Text = Label(bridge, t), VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    /// <summary>Explains, before a task is created, how its turns will reach the recipient.</summary>
    public static Border Card(FrameworkElement owner, string name)
    {
        var title = new TextBlock { FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        var body = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 4, 0, 0), Foreground = (Brush)owner.FindResource("MutedBrush") };
        var text = new StackPanel(); text.Children.Add(title); text.Children.Add(body);
        var bar = new Border { Width = 3, CornerRadius = new(2), Margin = new(0, 2, 12, 2) };
        var layout = new DockPanel(); DockPanel.SetDock(bar, Dock.Left); layout.Children.Add(bar); layout.Children.Add(text);
        var card = new Border
        {
            Name = name, Child = layout, CornerRadius = new(8), Padding = new(12, 10, 14, 10), Margin = new(0, 8, 0, 12),
            Background = (Brush)owner.FindResource("SurfaceAltBrush"), BorderBrush = (Brush)owner.FindResource("BorderBrush"), BorderThickness = new(1)
        };
        AutomationProperties.SetAutomationId(card, name);
        card.Tag = (title, body, bar);
        return card;
    }

    public static void Show(Border card, AgentBridge bridge, string agentName, Func<string, string> t)
    {
        var (title, body, bar) = ((TextBlock, TextBlock, Border))card.Tag;
        bar.Background = Accent(card, bridge);
        string Format(string text) => string.Format(CultureInfo.CurrentUICulture, t(text), agentName, bridge.Kind == "desktop" ? t("Desktop") : bridge.Kind.ToUpperInvariant());
        (title.Text, body.Text) = bridge switch
        {
            { Kind: "none", HostNote: { } note } => (Format("{0} has no Agent bridge"), note),
            { Kind: "none" } => (Format("{0} has no Agent bridge"),
                t("Relay cannot start or continue its conversations. After you create the task, copy the prompt and paste it into the conversation yourself; Relay follows the receipt it writes back.")),
            { State: "blocked" } => (Format("{0}'s bridge needs attention"), bridge.BlockMessage + " " + t("Until then, copy the prompt manually.")),
            { State: "unverified" } => (Format("{0} · {1} bridge, not yet verified"),
                t("Creating the task sends the first turn through this bridge. Once the Agent writes its receipt, the bridge counts as verified and later turns can go automatically.")),
            _ => (Format("{0} · {1} bridge"),
                t("Relay delivers each turn into the chosen conversation, new or existing, and waits for the Agent's receipt.") + " " +
                t("Conversations Relay creates are saved by the host right away, but its window may list them only after you reopen it."))
        };
    }
}
