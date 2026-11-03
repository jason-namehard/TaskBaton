using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace AgentRelay.Desktop;

/// <summary>Asks how a new Agent should be added before showing any form; one-click setup comes first for newcomers.</summary>
public sealed class AgentSetupChoiceDialog : Window
{
    /// <summary>"oneclick", "manual", or null when cancelled.</summary>
    public string? Choice { get; private set; }

    public AgentSetupChoiceDialog(Window owner, string language)
    {
        string T(string text) => UiText.Translate(text, language);
        Owner = owner; Title = T("New Agent"); Width = 520; SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = owner.FontFamily; FontSize = 13.5; Resources = owner.Resources; UseLayoutRounding = true;
        SetResourceReference(BackgroundProperty, "PageBrush"); SetResourceReference(ForegroundProperty, "TextBrush");
        var body = new StackPanel { Margin = new(26, 24, 26, 24) };
        body.Children.Add(new TextBlock { Text = T("New Agent"), FontSize = 19, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 40, 4) });
        var intro = new TextBlock { Text = T("Choose how to add it. If you are not sure, let the Agent set itself up."), TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Margin = new(0, 0, 0, 18) };
        intro.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        body.Children.Add(intro);
        var ruleNote = new TextBlock { Text = T("Optional handoff rule: after adding an Agent, select it and choose Install handoff rule. Relay will show the one-line rule and ask before changing its instruction file. The detailed guidance is read only when the Agent follows the rule."), TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Margin = new(0, 0, 0, 18) };
        ruleNote.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        body.Children.Add(ruleNote);

        void Option(string name, string icon, string title, string description, string choice, bool recommended)
        {
            var row = new DockPanel();
            var badge = new Border { Width = 38, Height = 38, CornerRadius = new(10), VerticalAlignment = VerticalAlignment.Top, Margin = new(0, 0, 14, 0),
                Child = Icons.Create(icon, size: 19, brushKey: recommended ? "AccentInkBrush" : "MutedBrush") };
            badge.SetResourceReference(Border.BackgroundProperty, recommended ? "AccentSoftBrush" : "SurfaceAltBrush");
            DockPanel.SetDock(badge, Dock.Left); row.Children.Add(badge);
            var arrow = Icons.Create("Arrow", size: 16, brushKey: "FaintBrush");
            DockPanel.SetDock(arrow, Dock.Right); arrow.Margin = new(12, 0, 0, 0); row.Children.Add(arrow);
            var text = new StackPanel();
            var heading = new StackPanel { Orientation = Orientation.Horizontal };
            heading.Children.Add(new TextBlock { Text = T(title), FontSize = 14.5, FontWeight = FontWeights.SemiBold });
            if (recommended)
            {
                var tag = new Border { CornerRadius = new(9), Padding = new(7, 1, 7, 1), Margin = new(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = T("Recommended"), FontSize = 11, FontWeight = FontWeights.SemiBold } };
                tag.SetResourceReference(Border.BackgroundProperty, "OkSoftBrush");
                ((TextBlock)tag.Child).SetResourceReference(TextBlock.ForegroundProperty, "OkBrush");
                heading.Children.Add(tag);
                var advice = T("Each new node has a unique ID. Copy a fresh setup prompt for each node; do not give the same prompt to two Agents.");
                var help = new Border { Name = "SetupPromptHelp", Margin = new(8, 0, 0, 0), Cursor = Cursors.Help,
                    Child = Icons.Create("Help", size: 16, brushKey: "MutedBrush"),
                    ToolTip = new TextBlock { Text = advice, TextWrapping = TextWrapping.Wrap, MaxWidth = 380 } };
                AutomationProperties.SetAutomationId(help, help.Name); AutomationProperties.SetName(help, advice);
                heading.Children.Add(help);
            }
            text.Children.Add(heading);
            var detail = new TextBlock { Text = T(description), TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Margin = new(0, 4, 0, 0) };
            detail.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            text.Children.Add(detail);
            row.Children.Add(text);
            var card = new Button { Name = name, Content = row, Margin = new(0, 0, 0, 10) };
            card.SetResourceReference(StyleProperty, "OptionCardStyle");
            AutomationProperties.SetAutomationId(card, name); AutomationProperties.SetName(card, T(title));
            card.Click += (_, _) => { Choice = choice; DialogResult = true; };
            body.Children.Add(card);
        }
        Option("ChooseOneClickSetup", "Bolt", "Let the Agent set itself up",
            "Relay copies a setup prompt. Paste it into a new conversation with the Agent; it checks how it can be called and registers itself.", "oneclick", true);
        Option("ChooseManualSetup", "Edit", "Fill in by hand",
            "Enter the name, configuration path and calling method yourself. Suits users who know how the Agent is set up.", "manual", false);
        Content = body;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) DialogResult = false; };
        ThemedChrome.ApplyToDialog(this, resizable: false);
    }
}
