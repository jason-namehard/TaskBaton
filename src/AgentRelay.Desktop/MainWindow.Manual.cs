using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AgentRelay.Core;
using Microsoft.Win32;

namespace AgentRelay.Desktop;

public partial class MainWindow
{
    private FileSystemWatcher? receiptWatcher;
    private readonly Dictionary<string,string> receiptImportErrors=[];
    private bool receiptRefreshQueued;
    private string? migrationSummary;

    private void RecordMigration(string previousHome)
    {
        var plan=service.RelocationCleanupPlans().Single(item=>string.Equals(item.Source,previousHome,StringComparison.OrdinalIgnoreCase));
        migrationSummary=string.Format(CultureInfo.CurrentUICulture,T("Relay moved to {0}. Previous folder: {1}. Cleanup is scheduled for {2}; changed and new files will be kept."),
            service.Home,previousHome,plan.DeleteAfter.ToLocalTime().ToString("f",CultureInfo.CurrentUICulture));
        ShowApplicationNotification(new DesktopNotificationContent(T("Relay folder moved"),migrationSummary));
    }

    private void CompleteRelocation(RelayService next,string previousHome)
    {
        service=next;
        if(!demo)RelayLocation.Save(service.Home);
        WatchReceipts();
        RecordMigration(previousHome);
        service.RefreshGuidance();
    }

    private void WatchReceipts()
    {
        receiptWatcher?.Dispose();
        var path=Path.Combine(service.Home,"workspace");Directory.CreateDirectory(path);
        receiptWatcher=new FileSystemWatcher(path,"receipt.json"){IncludeSubdirectories=true,NotifyFilter=NotifyFilters.FileName|NotifyFilters.LastWrite|NotifyFilters.Size};
        receiptWatcher.Created+=(_,_)=>ReceiptChanged();receiptWatcher.Changed+=(_,_)=>ReceiptChanged();receiptWatcher.Renamed+=(_,_)=>ReceiptChanged();
        receiptWatcher.Error+=(_,_)=>Dispatcher.BeginInvoke(new Action(()=>LedgerHint.Text=T("Receipt monitoring needs a refresh. Click Refresh to read saved receipts.")));
        receiptWatcher.EnableRaisingEvents=true;
    }

    private void ReceiptChanged()
    {
        Dispatcher.BeginInvoke(new Action(async()=>
        {
            if(receiptRefreshQueued)return;
            receiptRefreshQueued=true;
            if(busy)return;
            await ReadReceipts();
        }));
    }

    private async Task ReadReceipts()
    {
        receiptRefreshQueued=false;
        var drafts=CaptureInputs();
        await Run(()=>Task.CompletedTask,"Ready",refresh:Navigation.SelectedItem is ListBoxItem {Tag:"Home"},clearError:false,reportStatus:false);
        RestoreInputs(drafts);
    }

    private string FileState(TaskRecord task)
    {
        var agent=AgentName(task.AwaitingAgentId??task.RecipientId);
        if(receiptImportErrors.ContainsKey(task.TaskId))return T("Receipt needs attention");
        var progress=task.IsFileHandoff?service.FileHandoffProgress(task):null;
        if(sendingTasks.Contains(task.TaskId) || progress?.Delivery=="cli_in_flight")
            return string.Format(CultureInfo.CurrentUICulture,T("Sending to {0}…"),agent);
        if(task.State==TaskState.Paused && progress?.DeliveryMessage is not null)return T("Paused · Bridge needs attention");
        if(progress?.Delivery=="cli_blocked")return string.Format(CultureInfo.CurrentUICulture,T("Bridge blocked · {0}"),agent);
        if(progress?.Delivery=="cli_unconfirmed")return string.Format(CultureInfo.CurrentUICulture,T("Delivery unconfirmed · {0}"),agent);
        if(progress?.Delivery=="cli_ready" && progress.DeliveryMessage is not null)
            return string.Format(CultureInfo.CurrentUICulture,T("Automatic delivery not verified · {0}"),agent);
        string Phase(string phase)=>T(phase switch{"criteria"=>"Preparing acceptance criteria","implement"=>"Implementing","revise"=>"Revising","review"=>"Reviewing","accept"=>"Accepting the result",_=>phase});
        return task.State switch
        {
            TaskState.Paused=>T("Paused · Waiting for your decision"),
            TaskState.Succeeded when task.Collaboration?.Deliverable is not null=>T("Delivered"),
            TaskState.AwaitingManual or TaskState.AwaitingReply when task.Collaboration is {} c=>string.Format(CultureInfo.CurrentUICulture,T("Waiting for {0} · {1}"),agent,Phase(c.Phase)),
            TaskState.Dispatched or TaskState.Acknowledged or TaskState.Running when task.Collaboration is {} c=>string.Format(CultureInfo.CurrentUICulture,T("{0} is processing · {1}"),agent,Phase(c.Phase)),
            TaskState.AwaitingManual=>string.Format(CultureInfo.CurrentUICulture,T("Waiting for {0} to receive and reply"),agent),
            TaskState.Dispatched or TaskState.Acknowledged or TaskState.Running=>string.Format(CultureInfo.CurrentUICulture,T("{0} is processing"),agent),
            TaskState.AwaitingReply=>string.Format(CultureInfo.CurrentUICulture,T("Waiting for {0} to handle the reply"),agent),
            _=>T(task.State.ToString())
        };
    }

    private string FilePrompt(TaskRecord task)
    {
        _=service.FileHandoffPrompt(task);
        var receipt=service.ManualReceiptPath(task);
        var context=Path.Combine(Path.GetDirectoryName(receipt)!,"context.json");
        return string.Format(CultureInfo.CurrentUICulture,T("A file is waiting for you. Read {0}, perform the request and write its receipt. No extra acknowledgement message is needed."),context);
    }

    private void FileTaskPane(StackPanel card,TaskView view,TaskRow row)
    {
        var task=view.Task;var progress=view.Progress!;
        var head=DetailSection(card);
        DetailHeader(head,task,row);
        if(receiptImportErrors.TryGetValue(task.TaskId,out var receiptError))Alert(head,"warn",T("Receipt needs attention"),receiptError);
        if(task.ResumeStatus=="operator_completed")Alert(head,"info",null,T("Marked complete by the user. Agent completion was not verified."));
        if(task.ErrorMessage is not null)Alert(head,"bad",null,task.ErrorMessage);
        var footerActions=new List<Action<WrapPanel>>();
        if(!progress.Finished)
        {
            var recipient=AgentName(task.AwaitingAgentId??task.RecipientId);
            var tone=progress.Delivery=="cli_blocked"?"bad":task.State==TaskState.Paused?"warn":"info";
            var (_,actions)=Alert(head,tone,null,FileDeliveryHint(task,progress,recipient));
            if(task.State==TaskState.Paused)
            {
                var note=EntryDialogLayout.Input("ContinueNote",multiline:true);note.Height=60;note.Margin=new(0,2,0,0);
                var label=new Label{Content=T("Note for the next Agent (optional)"),Target=note,Padding=new(0,10,0,2)};
                var host=(Panel)actions.Parent;host.Children.Insert(host.Children.IndexOf(actions),label);host.Children.Insert(host.Children.IndexOf(actions),note);
                var extra=task.PauseReason=="round_limit"?2:0;
                Styled(Add(actions,extra>0?string.Format(CultureInfo.CurrentUICulture,T("Continue with {0} more rounds"),extra):T("Continue"),()=>
                {service.ContinueFileHandoff(task.TaskId,note.Text,extra);StartAutoDeliveries();return Task.CompletedTask;}),"PrimaryButtonStyle");
            }
            else Styled(Add(actions,string.Format(CultureInfo.CurrentUICulture,T("Copy prompt for {0}"),recipient),()=>{Clipboard.SetText(FilePrompt(task));return Task.CompletedTask;},false),"PrimaryButtonStyle");
            AddFileDeliveryAction(actions,task,progress);
            footerActions.Add(footer=>Styled(Add(footer,"Cancel",()=>ConfirmTaskCancellation(task.TaskId)),"GhostButtonStyle"));
        }

        var steps=DetailSection(card);
        Caption(steps,"Progress");
        if(task.Collaboration is {} c)
        {
            var completed=c.History.Count(step=>step.Outcome is not ("failed" or "criteria_ready"));
            var reviews=c.History.Count(step=>step.Phase=="review" && step.Outcome is "approved" or "changes_requested");
            steps.Children.Add(new TextBlock{Name="HandoffRoundSummary",TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,10),Text=c.MaxReviewRounds==0
                ?string.Format(CultureInfo.CurrentUICulture,T("Completed steps: {0} · Completed reviews: {1} (no limit)"),completed,reviews)
                :string.Format(CultureInfo.CurrentUICulture,T("Completed steps: {0} · Completed reviews: {1} (limit: {2})"),completed,reviews,c.MaxReviewRounds)});
            steps.Children.Add(RoundBar(c.History.Where(step=>step.Outcome is not "criteria_ready").Select(step=>step.Outcome).ToArray(),!progress.Finished));
        }
        else steps.Children.Add(new TextBlock{TextWrapping=TextWrapping.Wrap,Text=progress.Finished?T(task.State.ToString()):progress.MaxTurns is null
            ?string.Format(CultureInfo.CurrentUICulture,T("Transfer turn {0}"),task.ManualTurn+1)
            :string.Format(CultureInfo.CurrentUICulture,T("Turn {0} of at most {1}"),task.ManualTurn+1,progress.MaxTurns)});
        if(task.HandoffPlan is {} plan)steps.Children.Add(Muted(T("Planned steps")+": "+string.Join(" → ",plan.Select(AgentName))+" · "+T("Current step")+" "+(task.ManualTurn+1)+"/"+plan.Length,12.5,new(0,8,0,0)));
        if(!progress.Finished)
        {
            var next=Muted(string.Format(CultureInfo.CurrentUICulture,T("Next recipient: {0}"),AgentName(task.AwaitingAgentId??task.RecipientId)),12.5,new(0,8,0,0));
            next.Name="HandoffNextRecipient";steps.Children.Add(next);
        }
        AddSessionBindings(DetailSection(card),task);

        var details=DetailSection(card);
        var current=task.ManualCurrentFile??task.RequestPath;
        var contents=new StackPanel();
        contents.Children.Add(Note(T("Handoff file")+": "+current));
        if(task.Collaboration is {} content)
        {
            if(content.Criteria is not null)contents.Children.Add(Note(T("Acceptance criteria")+": "+content.Criteria));
            if(content.OpenItems.Length>0)contents.Children.Add(Note(T("Open items")+":\n· "+string.Join("\n· ",content.OpenItems)));
            if(content.Deliverable is not null)contents.Children.Add(Note(T("Deliverable")+": "+content.Deliverable));
        }
        if(!string.IsNullOrWhiteSpace(task.ManualNextAction))contents.Children.Add(Note(T("Instructions for this turn")+":\n"+task.ManualNextAction));
        var text=Output("ManualFilePreview",150);
        text.Text=Path.GetExtension(current).ToLowerInvariant() is ".md" or ".txt" && File.Exists(current)
            ?File.ReadAllText(current):T("Content preview is unavailable. Open the task folder to read the file.");
        contents.Children.Add(Note(T("Current file text")));contents.Children.Add(text);
        details.Children.Add(new Expander{Name="ManualFileContents",Header=T("File contents"),Content=contents});
        if(task.Collaboration is {History.Count:>0} loop)
            details.Children.Add(new Expander{Header=T("Review history"),Content=Timeline(loop.History.Select(step=>(step.At,AgentName(step.AgentId)+" · "+T(step.Outcome)+
                (step.OpenItems is {} items?" · "+string.Join("；",items):"")+(step.ResultPath is null?"":" · "+step.ResultPath),StepTone(step.Outcome))).ToArray())});
        details.Children.Add(new Expander{Header=T("Processing history"),Content=Timeline(service.Audit(task.TaskId).Select(entry=>(entry.RecordedAt,entry.Event,"")).ToArray())});
        var footer=DetailFooter(card,task);
        foreach(var add in footerActions)add(footer);
        AddTaskOperatorActions(footer,task);
    }

    private static string StepTone(string outcome)=>outcome switch{"changes_requested"=>"warn","failed"=>"bad",_=>"ok"};

    /// <summary>One segment per finished step, colored by its outcome, plus an outlined segment for the step in progress.</summary>
    private static Grid RoundBar(string[] outcomes,bool running)
    {
        var bar=new Grid{Height=6};
        var shown=outcomes.TakeLast(16).ToArray();
        var count=shown.Length+(running?1:0);
        for(var i=0;i<count;i++)
        {
            bar.ColumnDefinitions.Add(new ColumnDefinition{Width=new(1,GridUnitType.Star)});
            var segment=new Border{CornerRadius=new(3),Margin=new(i==0?0:3,0,0,0)};
            if(i<shown.Length)segment.SetResourceReference(Border.BackgroundProperty,StepTone(shown[i]) switch{"warn"=>"WarnBrush","bad"=>"BadBrush",_=>"OkBrush"});
            else{segment.BorderThickness=new(1.2);segment.SetResourceReference(Border.BorderBrushProperty,"AccentInkBrush");}
            Grid.SetColumn(segment,i);bar.Children.Add(segment);
        }
        return bar;
    }

    private void InstallRules(string id)
    {
        var picker=new OpenFileDialog{Title=T("Choose the Agent instruction file"),Filter="Agent instructions|*.md|All files|*.*",FileName="AGENTS.md",CheckFileExists=false};
        if(picker.ShowDialog(this)!=true){reviewCancelled=true;return;}
        var preview=service.PreviewGuidance(id,picker.FileName);
        var message=T("Relay will append one small rule to this file. Detailed instructions load only when the Agent follows that rule. Removing Relay's ZIP folder cannot remove this rule; use Remove rules first.")+"\n\n"+preview.InstructionFile+"\n"+preview.Rule;
        if(ThemedMessageBox.Show(this,message,T("Install handoff rule"),MessageBoxButton.OKCancel)!=MessageBoxResult.OK){reviewCancelled=true;return;}
        service.InstallGuidance(id,picker.FileName);
    }
}
