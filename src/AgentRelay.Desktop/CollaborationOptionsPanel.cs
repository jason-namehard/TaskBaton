using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using AgentRelay.Core;

namespace AgentRelay.Desktop;

/// <summary>Configure a single handoff, a review loop with a selected final reviewer, or a fixed-count relay.</summary>
internal sealed class CollaborationOptionsPanel
{
    private readonly EntryDialogLayout ui;
    private readonly RadioButton loop;
    private readonly ComboBox reviewer;
    private readonly RadioButton automaticCriteria;
    private readonly TextBox criteria;
    private readonly TextBox rounds;
    private readonly TextBox turnMinutes;
    private readonly CheckBox automatic;
    private readonly RadioButton relay;
    private readonly RelayTemplateEditor relayTemplate;

    public CollaborationOptionsPanel(EntryDialogLayout ui, Panel parent, string prefix, int defaultTurnMinutes, bool automaticByDefault,
        ComboBox sender, ComboBox recipient)
    {
        this.ui = ui;
        RadioButton Choice(string text, string name, string group, bool selected)
        {
            var choice = new RadioButton { Name = prefix + name, Content = ui.T(text), GroupName = prefix + group, IsChecked = selected, Margin = new(0, 6, 0, 6) };
            AutomationProperties.SetAutomationId(choice, prefix + name);
            return choice;
        }
        parent.Children.Add(ui.Note("Collaboration"));
        var single = Choice("Single handoff", "Single", "Mode", true);
        loop = Choice("Keep revising until the review passes", "Loop", "Mode", false);
        relay = Choice("Relay with planned steps", "Relay", "Mode", false);
        parent.Children.Add(single); parent.Children.Add(loop); parent.Children.Add(relay);
        var relayDetails = new StackPanel { Margin = new(18, 4, 0, 8), Visibility = System.Windows.Visibility.Collapsed };
        parent.Children.Add(relayDetails);
        relayTemplate = new RelayTemplateEditor(ui, relayDetails, prefix, sender, recipient, prefix + "NewSessionEachTurn");
        var details = new StackPanel { Name = prefix + "ReviewOptions", Margin = new(18, 4, 0, 8), Visibility = Visibility.Collapsed };
        parent.Children.Add(details);
        details.Children.Add(ui.Note("The selected reviewer checks each version against the criteria. The task ends when that Agent approves with no unresolved issues. The author makes revisions. A third Agent can be the reviewer."));
        details.Children.Add(ui.Note("Reviewer (any registered Agent)"));
        reviewer = new ComboBox { Name = prefix + "Reviewer", ItemsSource = sender.ItemsSource, DisplayMemberPath = "Name",
            SelectedValuePath = "Id", MinHeight = 38, SelectedValue = sender.SelectedValue };
        AutomationProperties.SetAutomationId(reviewer, prefix + "Reviewer");
        details.Children.Add(reviewer);
        details.Children.Add(ui.Note("The reviewer always gets this task's dedicated review conversation. First review starts a new conversation; later reviews continue that same one. The reviewer never shares or overwrites the author's work conversation."));
        details.Children.Add(ui.Note("If the receiving Agent is the reviewer, the first prompt must include the existing work or its location; the initiating Agent makes any revisions."));
        details.Children.Add(ui.Note("Acceptance criteria describe observable conditions the reviewer must check. Relay sends them with every review and records the Agent's decision; Relay does not independently verify the work."));
        var manualCriteria=Choice("I will enter the acceptance criteria", "ManualCriteria", "CriteriaSource", true);
        automaticCriteria=Choice("Initiating Agent creates the acceptance criteria", "AutoCriteria", "CriteriaSource", false);
        details.Children.Add(manualCriteria);details.Children.Add(automaticCriteria);
        criteria = ui.Field(details, "Acceptance criteria", prefix + "Criteria", multiline: true); criteria.Height = 70;
        automaticCriteria.Checked+=(_,_)=>criteria.IsEnabled=false;
        manualCriteria.Checked+=(_,_)=>criteria.IsEnabled=true;
        details.Children.Add(ui.Note("When the initiating Agent creates the criteria, Relay adds the required prompt to the first turn and asks that Agent to write them in the requested format; you do not need to write the criteria yourself. Select the initiator's source conversation for CLI delivery; otherwise copy the prompt to it manually."));
        rounds = ui.Field(details, "Review limit (0 means no limit)", prefix + "Rounds", "5");
        details.Children.Add(ui.Note("Without a limit, automatic calls may continue and incur model charges until the reviewer approves. No-progress pauses and per-turn time limits still apply."));
        loop.Checked += (_, _) => { details.Visibility = Visibility.Visible; relayDetails.Visibility = Visibility.Collapsed; };
        relay.Checked += (_, _) => { details.Visibility = Visibility.Collapsed; relayDetails.Visibility = Visibility.Visible; };
        single.Checked += (_, _) => { details.Visibility = Visibility.Collapsed; relayDetails.Visibility = Visibility.Collapsed; };
        turnMinutes = ui.Field(parent, "Time limit per turn (minutes)", prefix + "TurnMinutes", defaultTurnMinutes.ToString(CultureInfo.InvariantCulture));
        automatic = new CheckBox { Name = prefix + "AutoDeliver", IsChecked = automaticByDefault, Margin = new(0, 8, 0, 8),
            Content = new TextBlock { Text = ui.T("Send each next turn through CLI automatically once that Agent's CLI is verified (may incur model charges)"), TextWrapping = System.Windows.TextWrapping.Wrap } };
        AutomationProperties.SetAutomationId(automatic, prefix + "AutoDeliver");
        parent.Children.Add(automatic);
    }

    public bool AutoDeliver => automatic.IsChecked == true;

    public FileHandoffOptions Build(string sender, string recipient)
    {
        int Number(TextBox box, int min, int max)
        {
            if (!int.TryParse(box.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < min || value > max)
                throw new RelayException("handoff_number_invalid", string.Format(CultureInfo.CurrentUICulture, ui.T("Enter a whole number from {0} to {1}."), min, max));
            return value;
        }
        var minutes = Number(turnMinutes, 1, 43200);
        if (relay.IsChecked == true)
            return new(null, AutoDeliver, minutes, RelayPlan: relayTemplate.Build(sender, recipient, minutes, AutoDeliver));
        if(loop.IsChecked==true&&automaticCriteria.IsChecked!=true&&string.IsNullOrWhiteSpace(criteria.Text))
            throw new RelayException("collaboration_criteria_required",ui.T("Enter the acceptance criteria or let the initiating Agent create them."));
        var collaboration = loop.IsChecked == true
            ? BuildReview(sender, recipient, Number(rounds, 0, RelayService.MaxReviewRounds))
            : null;
        return new(collaboration, AutoDeliver, minutes);
    }

    private CollaborationOptions BuildReview(string sender, string recipient, int reviewRounds)
    {
        var reviewerId = reviewer.SelectedValue as string ?? sender;
        // The reviewer must differ from the author; the other end authors the work when one end reviews it.
        var authorId = reviewerId == recipient ? sender : recipient;
        return new CollaborationOptions(authorId,
            automaticCriteria.IsChecked == true ? null : criteria.Text, reviewRounds,
            GenerateCriteria: automaticCriteria.IsChecked == true, ReviewerFinal: true, IndependentAcceptance: true,
            ReviewerId: reviewerId);
    }
}
