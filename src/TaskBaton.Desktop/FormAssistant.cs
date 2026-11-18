using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using TaskBaton.Core;

namespace TaskBaton.Desktop;

public sealed class FormAssistant : Expander
{
    public FormAssistant(string context, IReadOnlyDictionary<string, TextBox> fields, string language, Func<Func<Task>, string, Task> run)
    {
        string T(string source) => UiText.Translate(source, language);
        Header = T("Configuration Assistant"); IsExpanded = false; Margin = new(0, 12, 0, 16);
        var panel = new StackPanel { Margin = new(12, 8, 12, 12) }; Content = panel;
        panel.Children.Add(new TextBlock { Text = T("The reply only fills this form. Review it before saving."), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = T("Only non-empty values are filled. Saving and approval are separate actions."), TextWrapping = TextWrapping.Wrap, Margin = new(0, 5, 0, 10) });
        var buttons = new WrapPanel(); panel.Children.Add(buttons);
        var copy = new Button { Content = T("Copy Setup Prompt"), Name = "CopySetupPrompt", Tag = "Copy Setup Prompt" };
        AutomationProperties.SetAutomationId(copy, context + "CopySetupPrompt"); buttons.Children.Add(copy);
        var parse = new Button { Content = T("Parse and Fill"), Name = "ParseandFill", Tag = "Parse and Fill" };
        parse.SetResourceReference(StyleProperty,"PrimaryButtonStyle");
        AutomationProperties.SetAutomationId(parse, context + "ParseandFill"); buttons.Children.Add(parse);
        var clear = new Button { Content = T("Clear Reply"), Tag = "Clear Reply" }; buttons.Children.Add(clear);
        var reply = new TextBox { Name = context + "SetupReply", AcceptsReturn = true, Height = 130,
            TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxLength = SetupAssistant.MaxResponseBytes };
        AutomationProperties.SetAutomationId(reply, reply.Name); panel.Children.Add(reply);
        copy.Click += async (_, _) => await run(() =>
        {
            Clipboard.SetText(SetupAssistant.CreatePrompt(context, fields.ToDictionary(f => f.Key, f => f.Value.Text), language));
            return Task.CompletedTask;
        }, T("Copied setup prompt. Paste it into the Agent you want to register."));
        parse.Click += async (_, _) => await run(() =>
        {
            var values = SetupAssistant.Parse(reply.Text, context, fields.Keys.ToArray());
            foreach (var (name, value) in values.Where(v => !string.IsNullOrWhiteSpace(v.Value))) fields[name].Text = value;
            return Task.CompletedTask;
        }, T("Parse succeeded"));
        clear.Click += (_, _) => reply.Clear();
    }
}
