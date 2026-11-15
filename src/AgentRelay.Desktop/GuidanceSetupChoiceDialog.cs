using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace AgentRelay.Desktop;

/// <summary>
/// Relay cannot know which instruction file a host actually loads. Let the Agent confirm its own file,
/// or let the user pick the file explicitly; neither path writes anything until that confirmation.
/// </summary>
public sealed class GuidanceSetupChoiceDialog : Window
{
    /// <summary>"prompt", "manual", or null when cancelled.</summary>
    public string? Choice { get; private set; }

    public GuidanceSetupChoiceDialog(Window owner, string language, string agentName)
    {
        string T(string text) => UiText.Translate(text, language);
        Owner = owner; Title = T("Install handoff rule"); Width = 560; SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = owner.FontFamily; FontSize = 13.5; Resources = owner.Resources; UseLayoutRounding = true;
        SetResourceReference(BackgroundProperty, "PageBrush"); SetResourceReference(ForegroundProperty, "TextBrush");
        var body = new StackPanel { Margin = new(26, 24, 26, 20) };
        body.Children.Add(new TextBlock { Text = T("Install handoff rule"), FontSize = 19, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 40, 4) });
        var intro = new TextBlock
        {
            Text = T("Relay cannot know which instruction file this host actually loads. Choose how to confirm it. Nothing is written until the file and the one-line rule are confirmed.") + "\n\n" + agentName,
            TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Margin = new(0, 0, 0, 18)
        };
        intro.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        body.Children.Add(intro);

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

        Option("ChooseGuidancePrompt", "Bolt", "Copy a deployment prompt",
            "Paste it into this Agent's conversation. The Agent must identify its own instruction file and get your approval before changing it; then it installs the rule. Relay shows the rule as pending until that confirmation is recorded.", "prompt", true);
        Option("ChooseGuidanceManual", "Edit", "Choose the instruction file myself",
            "Pick the md file your host actually loads, review the one-line rule, and let Relay write it. Other instructions and a backup are preserved.", "manual", false);
        var cancel = new Button { Name = "CancelGuidanceSetup", Content = T("Cancel"), HorizontalAlignment = HorizontalAlignment.Right, MinHeight = 34, Height = double.NaN, Margin = new(0, 4, 0, 0) };
        cancel.Click += (_, _) => DialogResult = false;
        body.Children.Add(cancel);
        Content = body;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) DialogResult = false; };
        ThemedChrome.ApplyToDialog(this, resizable: false);
    }
}
