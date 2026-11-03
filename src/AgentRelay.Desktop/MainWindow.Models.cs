using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using AgentRelay.Core;

namespace AgentRelay.Desktop;

public partial class MainWindow
{
    private sealed record ModelChoice(AgentModelOption Option, string Name);

    /// <summary>Default model and reasoning level for conversations Relay starts; continued conversations keep their own.</summary>
    private void AddModelDefaults(Panel details, string agentId)
    {
        if (service.CliExtension(agentId) is null) return;
        var group = Group(details, "Default model");
        var panel = new StackPanel { Name = "AgentDefaultModelsPanel", Margin = new(18, 0, 18, 0), Visibility = Visibility.Collapsed };
        AutomationProperties.SetAutomationId(panel, panel.Name);
        var row = SettingRow(group, "New conversations", "New conversations Relay starts for this Agent use the model below. Continued conversations keep their own model and reasoning level.");
        Button toggle = null!;
        toggle = RowAction(row, "Default model for new conversations", async () =>
        {
            if (panel.Visibility == Visibility.Visible)
            {
                panel.Visibility = Visibility.Collapsed;
                toggle.Content = T("Show models");
                AutomationProperties.SetName(toggle, T("Show models"));
                return;
            }
            var catalog = await service.AgentModelsAsync(agentId);
            ShowModelDefaults(panel, catalog);
            panel.Visibility = Visibility.Visible;
            toggle.Content = T("Hide models");
            AutomationProperties.SetName(toggle, T("Hide models"));
        }, false);
        toggle.Content = T("Show models");
        AutomationProperties.SetName(toggle, T("Show models"));
        group.Children.Add(panel);
    }

    private void ShowModelDefaults(StackPanel panel, AgentModelCatalog catalog)
    {
        panel.Children.Clear();
        panel.Margin = new(18, 0, 18, 14);
        if (!catalog.Supported) { panel.Children.Add(Muted(catalog.Message!, 12.5)); return; }
        string Describe(AgentModelSelection? s) => s is null ? T("Not set") : s.ProviderId + " · " + s.ModelId + (s.ReasoningLevel is null ? "" : " · " + s.ReasoningLevel);
        panel.Children.Add(Muted(T("Current default") + ": " + Describe(catalog.Current) + "\n" + T("Saved in the host's own configuration") + ": " + catalog.Source, 12.5, new(0, 0, 0, 8)));
        var choices = catalog.Options.Where(o => o.Usable).Select(o => new ModelChoice(o, o.ProviderName + " · " + o.ModelId)).ToArray();
        var model = new ComboBox { Name = "AgentDefaultModel", ItemsSource = choices, DisplayMemberPath = nameof(ModelChoice.Name), Margin = new(0, 2, 12, 8), MinWidth = 280 };
        var level = new ComboBox { Name = "AgentDefaultReasoning", Margin = new(0, 2, 12, 8), MinWidth = 140 };
        AutomationProperties.SetAutomationId(model, model.Name); AutomationProperties.SetAutomationId(level, level.Name);
        model.SelectionChanged += (_, _) =>
        {
            var levels = (model.SelectedItem as ModelChoice)?.Option.ReasoningLevels ?? [];
            level.ItemsSource = levels; level.IsEnabled = levels.Length > 0;
            var keep = catalog.Current is { } c && model.SelectedItem is ModelChoice m && m.Option.ProviderId == c.ProviderId && m.Option.ModelId == c.ModelId
                ? c.ReasoningLevel : null;
            level.SelectedItem = keep is not null && levels.Contains(keep) ? keep : RelayService.MiddleReasoningLevel(levels);
        };
        model.SelectedItem = choices.FirstOrDefault(c => catalog.Current is { } s && c.Option.ProviderId == s.ProviderId && c.Option.ModelId == s.ModelId) ?? choices.FirstOrDefault();
        var fields = new WrapPanel();
        StackPanel Labelled(string text, Control control)
        {
            var box = new StackPanel();
            box.Children.Add(new Label { Content = T(text), Target = control, Padding = new(0, 0, 0, 2) });
            box.Children.Add(control);
            return box;
        }
        fields.Children.Add(Labelled("Model", model));
        fields.Children.Add(Labelled("Reasoning level", level));
        panel.Children.Add(fields);
        var save = Styled(Add(Row(panel), "Save default model", async () =>
        {
            if (model.SelectedItem is not ModelChoice chosen) throw new RelayException("agent_model_required", T("Select a model."));
            ShowModelDefaults(panel, await service.SetAgentModelAsync(catalog.AgentId,
                new(chosen.Option.ProviderId, chosen.Option.ModelId, level.SelectedItem as string)));
        }, false), "PrimaryButtonStyle");
        AutomationProperties.SetAutomationId(save, "SaveAgentDefaultModel");
        if (catalog.Options.Any(o => !o.Usable))
            panel.Children.Add(Muted("Account plans the host's command line has not signed in to are hidden; they work only inside the desktop window.", 12));
    }
}
