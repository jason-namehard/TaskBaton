using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using AgentRelay.Core;

namespace AgentRelay.Desktop;

internal sealed class EntryDialogLayout
{
    private readonly string language;
    public StackPanel Body { get; } = new();
    public WrapPanel Actions { get; } = new() { HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 0, -8, -8) };
    public TextBlock Status { get; } = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 10), FontSize = 12.5 };

    public EntryDialogLayout(Window dialog, Window owner, string title, string language)
    {
        this.language = language;
        dialog.Owner = owner; dialog.Title = title;
        dialog.Width = 690; dialog.Height = Math.Min(800, SystemParameters.WorkArea.Height - 70);
        dialog.MinWidth = 440; dialog.MinHeight = 420;
        dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        dialog.FontFamily = owner.FontFamily; dialog.FontSize = owner.FontSize; dialog.Resources = owner.Resources;
        dialog.Background = (Brush)owner.FindResource("PageBrush"); dialog.Foreground = (Brush)owner.FindResource("TextBrush");
        dialog.UseLayoutRounding = true;
        dialog.SourceInitialized += (_, _) => Appearance.ApplyTitleBar(dialog);
        var dock = new DockPanel(); dialog.Content = dock;
        var heading = new TextBlock { Text = title, FontSize = 19, FontWeight = FontWeights.SemiBold, Margin = new(26, 22, 60, 12), TextWrapping = TextWrapping.Wrap };
        DockPanel.SetDock(heading, Dock.Top); dock.Children.Add(heading);
        var footer = new StackPanel(); footer.Children.Add(Status); footer.Children.Add(Actions);
        var footerFrame = new Border { Child = footer, Padding = new(26, 14, 26, 16), BorderThickness = new(0, 1, 0, 0) };
        footerFrame.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        footerFrame.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        DockPanel.SetDock(footerFrame, Dock.Bottom); dock.Children.Add(footerFrame);
        dock.Children.Add(new ScrollViewer { Content = Body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new(26, 0, 18, 16) });
        ThemedChrome.ApplyToDialog(dialog, resizable: true);
    }

    public string T(string text) => EntryText.Get(text, language);
    public TextBlock Note(string text)
    {
        var note = new TextBlock { Text = T(text), TextWrapping = TextWrapping.Wrap, Margin = new(0, 2, 0, 10), FontSize = 12.5 };
        note.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        return note;
    }
    public TextBox Field(Panel parent, string label, string name, string value = "", bool multiline = false)
    {
        var input = Input(name, value, multiline);
        parent.Children.Add(new Label { Content = T(label), Target = input, Padding = new(0, 0, 0, 2) });
        parent.Children.Add(input);
        return input;
    }

    public static TextBox Input(string name, string value = "", bool multiline = false)
    {
        var input = new TextBox { Name = name, Text = value, AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            Height = multiline ? 120 : double.NaN, MinHeight = 34,
            VerticalContentAlignment = multiline ? VerticalAlignment.Top : VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(input, name);
        return input;
    }

    public Button Button(Panel parent, string text, string name, Action action, bool primary = false)
    {
        var button = new Button { Content = T(text), Name = name, Height = double.NaN, MinHeight = 34, HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetAutomationId(button, name);
        if (primary) button.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButtonStyle");
        button.Click += (_, _) => Handle(action);
        parent.Children.Add(button);
        return button;
    }

    public void Handle(Action action)
    {
        // Form replies and clipboard access are user-input boundaries; keep drafts when validation fails.
        try { action(); }
        catch (RelayException error)
        {
            Status.Text = error.Code.StartsWith("setup_", StringComparison.Ordinal)
                ? T("The reply could not be parsed. Check the requested JSON format.") + " (" + error.Code + ")"
                // Translate each line: the generic guidance follows the UI language, a field detail stays as written.
                : string.Join(Environment.NewLine, error.Message
                    .Split([Environment.NewLine, "\n"], StringSplitOptions.None)
                    .Select(line => T(line)));
        }
        catch (ExternalException) { Status.Text = T("Clipboard is unavailable. Try again."); }
    }

    public TextBox Assistant(Panel parent, string title, string explanation, string prefix, Func<string> prompt, Action<string> parse)
    {
        var panel = new StackPanel { Margin = new(0, 4, 0, 0) };
        var expander = new Expander { Header = T(title), IsExpanded = false, Content = panel, Margin = new(0, 8, 0, 12) };
        parent.Children.Add(expander); panel.Children.Add(Note(explanation));
        var buttons = new WrapPanel(); panel.Children.Add(buttons);
        var reply = Input(prefix + "Reply", multiline: true); reply.MaxLength = SetupAssistant.MaxResponseBytes;
        Button(buttons, "Copy prompt", prefix + "CopyPrompt", () => { Clipboard.SetText(prompt()); Status.Text = T("Prompt copied."); });
        Button(buttons, "Parse and fill", prefix + "ParseReply", () => parse(reply.Text), true);
        panel.Children.Add(reply);
        return reply;
    }
}
