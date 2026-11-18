using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using TaskBaton.Core;

namespace TaskBaton.Desktop;

public partial class MainWindow
{
    /// <summary>The graph on the left and a read-only explanation of the selected pair's two directions on the right.</summary>
    private FrameworkElement ConnectionsPage()
    {
        avatarSlots = null;
        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new(16) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new(340) });
        var edges = service.Connections();
        var side = new StackPanel();
        var sideScroll = new ScrollViewer { Content = side, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetColumn(sideScroll, 2); layout.Children.Add(sideScroll);
        void Select(ConnectionView edge)
        {
            connectionFrom = edge.From; connectionTo = edge.To; side.Children.Clear();
            var directions = new StackPanel();
            side.Children.Add(Card(directions));
            foreach (var direction in new[] { edge, edges.Single(e => e.From == edge.To && e.To == edge.From) })
            {
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var frame = new Border { Child = row, Padding = new(16, 14, 16, 14), BorderThickness = new(0, directions.Children.Count == 0 ? 0 : 1, 0, 0) };
                frame.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
                directions.Children.Add(frame);
                var route = RouteLine(direction.From, direction.To, 20, 13); route.Margin = new(0, 0, 10, 0); row.Children.Add(route);
                var available = direction.Display != "Unavailable";
                var explanation = Muted(T(available
                    ? "A calling method is registered. Relay sends automatically only when this recipient's bridge is verified; otherwise copy the prompt."
                    : "No calling recipe. Copy a handoff prompt, or set up a bridge for the recipient."), 12.5, new(0, 8, 0, 0));
                Grid.SetRow(explanation, 1); Grid.SetColumnSpan(explanation, 2); row.Children.Add(explanation);
                var stateName = "ConnectionState_" + direction.From.Replace('-', '_') + "_" + direction.To.Replace('-', '_');
                var pill = Pill(GraphText.State(direction.Display, preferences.Language), available ? "ok" : "off");
                pill.Name = stateName; AutomationProperties.SetAutomationId(pill, stateName);
                Grid.SetColumn(pill, 1); row.Children.Add(pill);
                if (!available)
                {
                    var actions = new WrapPanel { Margin = new(0, 10, 0, -8) };
                    Grid.SetRow(actions, 2); Grid.SetColumnSpan(actions, 2); row.Children.Add(actions);
                    var target = direction.To;
                    var extend = Add(actions, "Agent bridges", () => { agentId = target; Go("Agents"); return Task.CompletedTask; });
                    extend.Content = Icons.WithLabel("Bridge", string.Format(System.Globalization.CultureInfo.CurrentUICulture, T("Set up a bridge for {0}"), AgentName(target)));
                    extend.Name = "SetupBridge_" + direction.From.Replace('-', '_') + "_" + direction.To.Replace('-', '_');
                    AutomationProperties.SetAutomationId(extend, extend.Name);
                }
            }
            side.Children.Add(Muted(T("This graph is informational. Relay no longer pauses individual directions; automatic delivery follows the recipient's verified bridge, and copying a prompt by hand always works."), 12.5, new(2, 10, 2, 6)));
            Styled(Add(Row(side), "Manual handoff", () => { Go("Home"); return Task.CompletedTask; }), "LinkButtonStyle");
        }
        var graph = new ConnectionGraph(service.Agents(), edges, Select, preferences.Language, this, AgentName, service.CliExtensions().ToDictionary(e => e.AgentId), AvatarKey);
        layout.Children.Add(Card(graph, new Thickness(14)));
        var selected = edges.SingleOrDefault(e => e.From == connectionFrom && e.To == connectionTo) ?? edges.FirstOrDefault();
        if (selected is not null) Select(selected);
        UpdateSidebar();
        return layout;
    }
}
