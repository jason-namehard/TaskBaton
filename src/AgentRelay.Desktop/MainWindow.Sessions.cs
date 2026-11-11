using System.Windows;
using System.Windows.Controls;
using AgentRelay.Core;

namespace AgentRelay.Desktop;

public partial class MainWindow
{
    /// <summary>One row per Agent: the bound conversation, how it was bound, and the actions to change it.</summary>
    private void AddSessionBindings(Panel section,TaskRecord task)
    {
        Caption(section,"Conversations");
        var finished=task.State is TaskState.Succeeded or TaskState.Cancelled or TaskState.TimedOut or TaskState.Failed;
        foreach(var id in new[]{task.RequesterId,task.RecipientId})
        {
            var binding=service.TaskSession(task.TaskId,id);
            var needsDedicatedReview=RelayService.UsesIndependentReviewSession(task,id);
            if(needsDedicatedReview&&binding?.IndependentReview!=true)binding=null;
            var row=new Grid{Margin=new(0,0,0,10)};
            row.ColumnDefinitions.Add(new ColumnDefinition{Width=new(140)});
            row.ColumnDefinitions.Add(new ColumnDefinition{Width=new(1,GridUnitType.Star)});
            row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            var who=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Top,Margin=new(0,3,0,0)};
            who.Children.Add(Avatar(id));
            who.Children.Add(new TextBlock{Text=AgentName(id),FontWeight=FontWeights.SemiBold,Margin=new(8,0,0,0),VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis,MaxWidth=110});
            row.Children.Add(who);
            var value=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new(0,0,12,0)};
            var description=binding?.SessionId is {} session?session:needsDedicatedReview||binding?.Binding=="new"||id==task.RecipientId?T("New conversation"):T("Source conversation is not identified.");
            var label=binding?.SessionId is null?Muted(description,12.5):new TextBlock{Text=description,FontFamily=new("Cascadia Mono, Consolas"),FontSize=12,TextTrimming=TextTrimming.CharacterEllipsis,ToolTip=description};
            label.Margin=new(0,4,0,0);value.Children.Add(label);
            if(binding?.Cwd is not null)value.Children.Add(Brushed(new TextBlock{Text=binding.Cwd,FontSize=11.5,TextTrimming=TextTrimming.CharacterEllipsis,ToolTip=binding.Cwd,Margin=new(0,2,0,0)},"FaintBrush"));
            Grid.SetColumn(value,1);row.Children.Add(value);
            var dedicatedReview=needsDedicatedReview||binding?.IndependentReview==true;
            if(binding?.Binding is {} kind)
            {
                var pill=Pill(T(binding.IndependentReview?"Dedicated review conversation":
                    kind switch{"user_selected"=>"Chosen by you","manual"=>"Entered manually","new"=>"New conversation",_=>kind}),"off");
                pill.VerticalAlignment=VerticalAlignment.Top;pill.Margin=new(0,3,0,0);Grid.SetColumn(pill,2);row.Children.Add(pill);
            }
            var actions=new StackPanel{Orientation=Orientation.Horizontal,Margin=new(-6,2,0,0)};
            value.Children.Add(actions);
            section.Children.Add(row);
            if(finished||(id==(task.AwaitingAgentId??task.RecipientId)&&WaitingForFileReceipt(task)))continue;
            if(!dedicatedReview) StyleLink(Add(actions,service.SessionDiscoverySupported(id)?"Change conversation":"Enter Session ID manually",()=>
            {
                var picker=new SessionPickerDialog(this,service,id,preferences.Language,binding?.Cwd);
                if(picker.ShowDialog()==true)service.BindTaskSession(task.TaskId,id,picker.SelectedSession!,picker.Binding);
                else reviewCancelled=true;
                return Task.CompletedTask;
            }));
            var fresh=Add(actions,"New conversation",()=>
            {
                if(ThemedMessageBox.Show(this,T("Create a new conversation on the next CLI delivery? The previous conversation is kept."),T("New conversation"),MessageBoxButton.OKCancel)==MessageBoxResult.OK)
                    service.UseNewTaskSession(task.TaskId,id);
                else reviewCancelled=true;
                return Task.CompletedTask;
            });
            StyleLink(fresh);
        }
        void StyleLink(Button button){Styled(button,"LinkButtonStyle");button.Margin=new(0,0,0,0);button.FontSize=12.5;}
    }
}
