using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
using TaskBaton.Core;

namespace TaskBaton.Desktop;

/// <summary>Building blocks shared by the pages: cards, labelled sections, setting rows, state pills, avatars and menus.</summary>
public partial class MainWindow
{
    private Border Card(UIElement child, Thickness? padding = null)
    {
        var card = new Border { Child = child, CornerRadius = new(12), BorderThickness = new(1), Padding = padding ?? new Thickness(0) };
        card.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        return card;
    }

    /// <summary>A titled group of setting rows inside one card, as in Windows Settings.</summary>
    private StackPanel Group(Panel page, string title, string? anchor = null, bool danger = false)
    {
        var heading = new TextBlock { Text = T(title), FontSize = 14.5, FontWeight = FontWeights.SemiBold, Margin = new(0, 18, 0, 10) };
        if (anchor is not null) heading.Name = anchor;
        page.Children.Add(heading);
        var rows = new StackPanel();
        var card = Card(rows);
        if (danger) card.SetResourceReference(Border.BorderBrushProperty, "BadSoftBrush");
        page.Children.Add(card);
        return rows;
    }

    /// <summary>One setting: a label and optional description on the left, its control on the right.</summary>
    private Grid SettingRow(Panel group, string title, string? description, params UIElement[] controls)
    {
        var row = new Grid { Margin = new(0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var frame = new Border { Child = row, Padding = new(18, 13, 18, 13), BorderThickness = new(0, group.Children.Count == 0 ? 0 : 1, 0, 0) };
        frame.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 24, 0) };
        text.Children.Add(new TextBlock { Text = T(title), FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (description is not null) text.Children.Add(Muted(description, 12.5, new(0, 2, 0, 0)));
        row.Children.Add(text);
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var control in controls)
        {
            if (control is FrameworkElement element)
            {
                if (element is Button or ComboBox or TextBox) element.Margin = new(8, 0, 0, 0);
                element.VerticalAlignment = VerticalAlignment.Center;
            }
            right.Children.Add(control);
        }
        Grid.SetColumn(right, 1); row.Children.Add(right);
        group.Children.Add(frame);
        return row;
    }

    /// <summary>Adds an action to the right side of a setting row.</summary>
    private Button RowAction(Grid row, string label, Func<Task> action, bool refresh = true)
    {
        var button = Add((Panel)row.Children[1], label, action, refresh);
        button.Margin = new(8, 0, 0, 0); button.VerticalAlignment = VerticalAlignment.Center;
        return button;
    }

    private TextBlock Muted(string text, double size = 12.5, Thickness? margin = null) => Brushed(new TextBlock
    {
        Text = T(text), FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = margin ?? new Thickness(0)
    }, "MutedBrush");

    private static T Brushed<T>(T element, string foreground) where T : FrameworkElement
    {
        element.SetResourceReference(TextBlock.ForegroundProperty, foreground);
        return element;
    }

    /// <summary>A small caption above a detail section, with an optional value on the right.</summary>
    private Grid Caption(Panel parent, string text, string? right = null)
    {
        var row = new Grid { Margin = new(0, 0, 0, 10) };
        row.Children.Add(Brushed(new TextBlock { Text = T(text), FontSize = 12, FontWeight = FontWeights.SemiBold }, "FaintBrush"));
        if (right is not null)
            row.Children.Add(Brushed(new TextBlock { Text = right, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new(90, 0, 0, 0) }, "MutedBrush"));
        parent.Children.Add(row);
        return row;
    }

    /// <summary>A section of the detail card, separated from the previous one by a hairline.</summary>
    private StackPanel DetailSection(Panel card)
    {
        var body = new StackPanel();
        var frame = new Border { Child = body, Padding = new(20, 16, 20, 16), BorderThickness = new(0, card.Children.Count == 0 ? 0 : 1, 0, 0) };
        frame.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        card.Children.Add(frame);
        return body;
    }

    private static (string Back, string Fore) ToneKeys(string tone) => tone switch
    {
        "ok" => ("OkSoftBrush", "OkBrush"), "warn" => ("WarnSoftBrush", "WarnBrush"), "bad" => ("BadSoftBrush", "BadBrush"),
        "info" or "run" => ("InfoSoftBrush", "InfoBrush"), _ => ("SurfaceAltBrush", "MutedBrush")
    };

    private Border Pill(string text, string tone)
    {
        var (back, fore) = ToneKeys(tone);
        var label = Brushed(new TextBlock { Text = text, FontSize = 11.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis }, fore);
        var pill = new Border { Child = label, CornerRadius = new(10), Padding = new(8, 2, 8, 2), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, ToolTip = text };
        pill.SetResourceReference(Border.BackgroundProperty, back);
        return pill;
    }

    private Dictionary<string, int>? avatarSlots;

    /// <summary>Each Agent keeps one of four avatar colors, assigned in registration order.</summary>
    private string AvatarKey(string agentId)
    {
        avatarSlots ??= service.Agents().Select((a, i) => (a.AgentId, i)).ToDictionary(p => p.AgentId, p => p.i, StringComparer.Ordinal);
        return "Avatar" + ((avatarSlots.TryGetValue(agentId, out var slot) ? slot : Math.Abs(agentId.GetHashCode())) % 4 + 1) + "Brush";
    }

    private static string Initial(string name) => name.Length == 0 ? "?" : char.ToUpperInvariant(name.TrimStart()[0]).ToString();

    private Border Avatar(string agentId, double size = 22)
    {
        var name = AgentName(agentId);
        var letter = Brushed(new TextBlock { Text = Initial(name), FontSize = size * 0.48, FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, "AvatarTextBrush");
        var avatar = new Border { Width = size, Height = size, CornerRadius = new(size * 0.28), Child = letter, VerticalAlignment = VerticalAlignment.Center };
        avatar.SetResourceReference(Border.BackgroundProperty, AvatarKey(agentId));
        return avatar;
    }

    /// <summary>"Avatar Name → Avatar Name", the way every handoff is named.</summary>
    private Grid RouteLine(string from, string to, double size = 22, double fontSize = 13.5)
    {
        var row = new Grid { VerticalAlignment = VerticalAlignment.Center };
        foreach (var width in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star) })
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        void Agent(string id, int avatarColumn)
        {
            var avatar = Avatar(id, size); Grid.SetColumn(avatar, avatarColumn); row.Children.Add(avatar);
            var name = AgentName(id);
            var label = new TextBlock { Text = name, FontSize = fontSize, FontWeight = FontWeights.SemiBold, Margin = new(7, 0, 0, 0),
                TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, ToolTip = name };
            AutomationProperties.SetAutomationId(label, "RouteName_" + from + "_" + to + "_" + id);
            Grid.SetColumn(label, avatarColumn + 1); row.Children.Add(label);
        }
        Agent(from, 0);
        var arrow = Icons.Create("Arrow", size: 15, brushKey: "FaintBrush");
        arrow.Margin = new(9, 0, 9, 0);
        Grid.SetColumn(arrow, 2);
        row.Children.Add(arrow);
        Agent(to, 3);
        return row;
    }

    /// <summary>A tinted message block for failures, pauses and next steps, with optional actions underneath.</summary>
    private (Border Box, WrapPanel Actions) Alert(Panel parent, string tone, string? title, string text, string? name = null)
    {
        var (back, fore) = ToneKeys(tone);
        var body = new StackPanel();
        if (title is not null) body.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 2) });
        var message = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        if (name is not null) message.Name = name;
        body.Children.Add(message);
        var actions = new WrapPanel { Margin = new(0, 10, 0, -8) }; // Buttons carry their own bottom margin.
        body.Children.Add(actions);
        var icon = Icons.Create(tone is "ok" ? "Check" : tone is "warn" or "bad" ? "Alert" : "Info", size: 17, brushKey: fore);
        icon.VerticalAlignment = VerticalAlignment.Top; icon.Margin = new(0, 1, 11, 0);
        var layout = new DockPanel(); DockPanel.SetDock(icon, Dock.Left); layout.Children.Add(icon); layout.Children.Add(body);
        var box = new Border { Child = layout, CornerRadius = new(9), Padding = new(13, 11, 14, 11), Margin = new(0, 12, 0, 0) };
        box.SetResourceReference(Border.BackgroundProperty, back);
        parent.Children.Add(box);
        return (box, actions);
    }

    private static Button Styled(Button button, string style, string? icon = null)
    {
        button.SetResourceReference(StyleProperty, style);
        if (icon is not null) button.Content = Icons.WithLabel(icon, button.Content as string ?? "");
        return button;
    }

    private Button IconButton(string icon, string tooltip, string? name = null)
    {
        var button = new Button { Content = Icons.Create(icon, size: 17), ToolTip = T(tooltip), Tag = tooltip };
        button.SetResourceReference(StyleProperty, "IconButtonStyle");
        AutomationProperties.SetName(button, T(tooltip));
        if (name is not null) { button.Name = name; AutomationProperties.SetAutomationId(button, name); }
        return button;
    }

    /// <summary>
    /// A drop-down menu attached to a button. The popup stays in the logical tree next to its trigger, so its actions remain
    /// ordinary buttons with stable automation IDs and labels.
    /// </summary>
    private StackPanel Flyout(Button trigger, Panel host, bool alignRight = true)
    {
        var items = new StackPanel { MinWidth = 230 };
        var frame = new Border { Child = items, Margin = new(8, 2, 8, 14), Padding = new(5), CornerRadius = new(10), BorderThickness = new(1),
            Effect = (Effect)FindResource("PopupShadow") };
        frame.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        frame.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        var popup = new Popup { PlacementTarget = trigger, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade, Child = frame,
            Placement = alignRight ? PlacementMode.Custom : PlacementMode.Bottom, HorizontalOffset = alignRight ? 0 : -8 };
        if (alignRight)
            popup.CustomPopupPlacementCallback = (popupSize, targetSize, _) => [new CustomPopupPlacement(new Point(targetSize.Width - popupSize.Width + 8, targetSize.Height), PopupPrimaryAxis.Horizontal)];
        host.Children.Add(popup);
        trigger.Click += (_, _) => popup.IsOpen = !popup.IsOpen;
        items.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((_, _) => popup.IsOpen = false));
        return items;
    }

    /// <summary>A menu entry that runs like any page action, with an icon and an optional one-line explanation.</summary>
    private Button MenuItem(Panel menu, string label, string icon, Func<Task> action, string? description = null, bool refresh = true, bool danger = false)
    {
        var button = Add(menu, label, action, refresh);
        button.SetResourceReference(StyleProperty, "MenuButtonStyle");
        var text = new StackPanel { Margin = new(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = T(label) });
        if (description is not null) text.Children.Add(Brushed(new TextBlock { Text = T(description), FontSize = 11.5, TextWrapping = TextWrapping.Wrap, MaxWidth = 250 }, "FaintBrush"));
        var glyph = Icons.Create(icon, size: 16);
        glyph.VerticalAlignment = description is null ? VerticalAlignment.Center : VerticalAlignment.Top;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Children = { glyph, text } };
        button.Content = row;
        if (danger) button.SetResourceReference(ForegroundProperty, "BadBrush");
        return button;
    }

    private static void MenuSeparator(Panel menu)
    {
        var line = new Border { Height = 1, Margin = new(4, 4, 4, 4) };
        line.SetResourceReference(Border.BackgroundProperty, "BorderBrush");
        menu.Children.Add(line);
    }
}
