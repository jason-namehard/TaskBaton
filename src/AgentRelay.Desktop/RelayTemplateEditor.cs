using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using AgentRelay.Core;

namespace AgentRelay.Desktop;

internal sealed class RelayTemplateEditor
{
    private readonly ComboBox templates;
    private readonly ComboBox start;
    private readonly TextBox custom;
    private readonly CheckBox newConversation;
    private readonly List<(StackPanel Panel, RelayStepRole[] Roles, Label[] Labels, TextBox[] Duties)> presets = [];
    private bool Conditional => templates.SelectedIndex == presets.Count;

    public RelayTemplateEditor(EntryDialogLayout ui, Panel parent, string prefix, ComboBox sender, ComboBox recipient,
        string sessionOptionName)
    {
        ComboBox Choice(string caption, string name, params string[] labels)
        {
            var choice = new ComboBox { Name = prefix + name, ItemsSource = labels.Select(ui.T).ToArray(), SelectedIndex = 0, MinHeight = 34 };
            AutomationProperties.SetAutomationId(choice, choice.Name);
            parent.Children.Add(new Label { Content = ui.T(caption), Target = choice, Padding = new(0, 0, 0, 2) });
            parent.Children.Add(choice);
            return choice;
        }

        templates = Choice("Relay template", "RelayTemplate", "Custom steps", "Direct handoff to the recipient",
            "Work then hand off", "Test and modify once", "Test and modify twice", "Stop if tests pass");
        start = Choice("First Agent", "RelayStart", "Sender", "Recipient");
        var startLabel = parent.Children[parent.Children.Count - 2];
        var summary = ui.Note("");
        summary.Name = prefix + "RelaySummary";
        parent.Children.Add(summary);

        var advanced = new StackPanel();
        parent.Children.Add(advanced);
        advanced.Children.Add(ui.Note("Write one step per line: agent=work, agent:test=check, or agent:modify=apply the test report."));
        custom = ui.Field(advanced, "Steps", prefix + "RelaySteps", multiline: true);
        custom.Height = 82;

        void Preset(params (RelayStepRole Role, string Duty)[] steps)
        {
            var panel = new StackPanel();
            parent.Children.Add(panel);
            var labels = new Label[steps.Length];
            var duties = new TextBox[steps.Length];
            for (var i = 0; i < steps.Length; i++)
            {
                var duty = ui.Field(panel, steps[i].Role.ToString(), $"{prefix}RelayDuty{presets.Count + 1}Step{i + 1}",
                    ui.T(steps[i].Duty), multiline: true);
                duty.Height = 50;
                duties[i] = duty;
                labels[i] = (Label)panel.Children[panel.Children.Count - 2];
            }
            presets.Add((panel, steps.Select(step => step.Role).ToArray(), labels, duties));
        }

        var work = (RelayStepRole.Work, "Do the work requested and save the result.");
        var check = (RelayStepRole.Test, "Test the latest work and record the results and any problems.");
        var modify = (RelayStepRole.Modify, "Apply the necessary changes from the test report.");
        Preset(work);
        Preset(work, (RelayStepRole.Work, "Carry out the remaining work using the previous result."));
        Preset(work, check, modify);
        Preset(work, check, modify, check, modify);
        Preset(work, check, modify);

        newConversation = new CheckBox
        {
            Name = sessionOptionName, Margin = new(0, 8, 0, 6),
            Content = new TextBlock
            {
                Text = ui.T("Start a new conversation when an Agent works again"),
                TextWrapping = TextWrapping.Wrap
            }
        };
        AutomationProperties.SetAutomationId(newConversation, newConversation.Name);
        parent.Children.Add(newConversation);
        parent.Children.Add(ui.Note("Each Agent's first turn uses its chosen conversation. The final step ends the relay."));

        void Update()
        {
            var index = templates.SelectedIndex;
            var showStart = index > 1 ? Visibility.Visible : Visibility.Collapsed;
            startLabel.Visibility = start.Visibility = showStart;
            advanced.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
            for (var i = 0; i < presets.Count; i++)
                presets[i].Panel.Visibility = i + 1 == index ? Visibility.Visible : Visibility.Collapsed;
            summary.Text = "";
            summary.Visibility = Visibility.Collapsed;
            if (index < 1) return;

            var (_, roles, labels, _) = presets[index - 1];
            var from = (sender.SelectedItem as HandoffAgentOption)?.Name ?? ui.T("Sender");
            var to = (recipient.SelectedItem as HandoffAgentOption)?.Name ?? ui.T("Recipient");
            var route = roles.Select((_, i) => UseSender(i) ? from : to).ToArray();
            for (var i = 0; i < roles.Length; i++)
                labels[i].Content = $"{i + 1}. {route[i]} · {ui.T(roles[i].ToString())}";
            var culture = CultureInfo.CurrentUICulture;
            summary.Text = Conditional
                ? string.Join(" → ", route.Take(2)) + "\n" + string.Format(culture,
                    ui.T("If tests pass, stop. If tests fail, continue with {0}."), route[2])
                : roles.Length == 1
                ? string.Format(culture, ui.T("Direct to {0} · Ends after this step"), to)
                : string.Join(" → ", route) + "\n" + string.Format(culture,
                    ui.T(roles.Length == 2 ? "{0} steps · {1} handoff · Ends with {2}" : "{0} steps · {1} handoffs · Ends with {2}"),
                    roles.Length, RelayService.HandoffCount(roles.Length), route[^1]);
            summary.Visibility = Visibility.Visible;
        }

        templates.SelectionChanged += (_, _) => Update();
        start.SelectionChanged += (_, _) => Update();
        sender.SelectionChanged += (_, _) => Update();
        recipient.SelectionChanged += (_, _) => Update();
        Update();
    }

    public bool UsesPreset => templates.SelectedIndex > 0;
    public bool HasPlan => UsesPreset || !string.IsNullOrWhiteSpace(custom.Text);

    private bool UseSender(int step) => templates.SelectedIndex > 1 && step % 2 == start.SelectedIndex;

    public RelayPlanOptions Build(string sender, string recipient, int? minutes = null, bool autoDeliver = false)
    {
        RelayStep[] steps;
        if (UsesPreset)
        {
            var (_, roles, _, duties) = presets[templates.SelectedIndex - 1];
            steps = roles.Select((role, i) => new RelayStep(UseSender(i) ? sender : recipient, role, duties[i].Text.Trim())).ToArray();
        }
        else steps = RelayService.ParseSteps(custom.Text);
        var plan = new RelayPlanOptions(steps, TurnTimeoutMinutes: minutes, AutoDeliver: autoDeliver,
            NewSessionEachTurn: newConversation.IsChecked == true, StopIfTestPassesAtStep: Conditional ? 2 : null);
        RelayService.ValidateRelayPlan(plan, sender, recipient);
        return plan;
    }
}
