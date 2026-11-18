using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using TaskBaton.Core;

namespace TaskBaton.Desktop;

public sealed class HandoffDialog : Window
{
    public HandoffDraft? Result { get; private set; }

    public HandoffDialog(Window owner, string language, IReadOnlyList<HandoffAgentOption> agents,
        Func<string, string, bool> canAutomaticallySend)
    {
        var ui = new EntryDialogLayout(this, owner, EntryText.Get("New handoff", language), language);
        ComboBox Node(string label, string id, int selected)
        {
            var choice = new ComboBox { Name = id, ItemsSource = agents, DisplayMemberPath = nameof(HandoffAgentOption.Name),
                SelectedValuePath = nameof(HandoffAgentOption.Id), SelectedIndex = selected, MinHeight = 40, Height = double.NaN };
            AutomationProperties.SetAutomationId(choice, id);
            ui.Body.Children.Add(new Label { Content = ui.T(label), Target = choice, Padding = new(0, 0, 0, 2) });
            ui.Body.Children.Add(choice); return choice;
        }
        var from = Node("Sender", "HandoffFrom", agents.Count > 0 ? 0 : -1);
        var to = Node("Recipient", "HandoffTo", agents.Count > 1 ? 1 : -1);
        var content = ui.Field(ui.Body, "Message", "HandoffContent", multiline: true); content.Height = 170;
        ui.Body.Children.Add(new TextBlock { Text = ui.T("Delivery"), Margin = new(0, 4, 0, 6) });
        var manual = new RadioButton { Name = "HandoffManual", Content = ui.T("Manual copy"), IsChecked = true, GroupName = "Delivery", Margin = new(0, 0, 0, 8) };
        var automatic = new RadioButton { Name = "HandoffAutomatic", Content = ui.T("Automatic CLI"), GroupName = "Delivery", Margin = new(0, 0, 0, 8) };
        AutomationProperties.SetAutomationId(manual, manual.Name); AutomationProperties.SetAutomationId(automatic, automatic.Name);
        ui.Body.Children.Add(manual); ui.Body.Children.Add(automatic);
        var routeHint = ui.Note("Automatic delivery is unavailable or paused. You can still hand off manually."); ui.Body.Children.Add(routeHint);
        void UpdateRoute()
        {
            automatic.IsEnabled = from.SelectedValue is string source && to.SelectedValue is string target && source != target && canAutomaticallySend(source, target);
            if (!automatic.IsEnabled) manual.IsChecked = true;
            routeHint.Visibility = automatic.IsEnabled ? Visibility.Collapsed : Visibility.Visible;
        }
        from.SelectionChanged += (_, _) => UpdateRoute(); to.SelectionChanged += (_, _) => UpdateRoute(); UpdateRoute();
        ui.Assistant(ui.Body, "Generate with an Agent", "Copy this prompt to any Agent, describe the handoff, then parse its reply. Parsing does not send.", "Handoff",
            () => HandoffAssistant.CreatePrompt(agents, from.SelectedValue as string ?? "", to.SelectedValue as string ?? "", content.Text, language), reply =>
            {
                var draft = HandoffAssistant.Parse(reply, agents);
                from.SelectedValue = draft.From; to.SelectedValue = draft.To; content.Text = draft.Content;
                manual.IsChecked = true;
                ui.Status.Text = ui.T("Response parsed. Review the sender, recipient and message before creating.");
            });
        ui.Button(ui.Actions, "Create handoff", "CreateHandoff", () =>
        {
            var draft = HandoffAssistant.Validate(from.SelectedValue as string ?? "", to.SelectedValue as string ?? "", content.Text, agents);
            UpdateRoute();
            Result = draft with { Automatic = automatic.IsChecked == true };
            DialogResult = true;
        }, true);
        var cancel = ui.Button(ui.Actions, "Cancel", "CancelHandoff", () => DialogResult = false); cancel.IsCancel = true;
    }
}
