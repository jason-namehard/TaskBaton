using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using AgentRelay.Core;

namespace AgentRelay.Desktop;

public partial class MainWindow
{
    private readonly HashSet<string> sendingTasks = [];

    private static bool WaitingForFileReceipt(TaskRecord task) =>
        task.CliFileTurn == task.ManualTurn ||
        (task.State == TaskState.Paused ? task.PausedFrom : task.State) is TaskState.Dispatched or TaskState.Acknowledged or TaskState.Running;

    private string FileDeliveryHint(TaskRecord task, HandoffProgress progress, string recipient) => progress.Delivery switch
    {
        _ when task.State == TaskState.Paused => task.PauseMessage! +
            (progress.DeliveryMessage is {} cause && cause != task.PauseMessage ? "\n" + cause : ""),
        "cli_blocked" => progress.DeliveryMessage!,
        "cli_in_flight" or "agent_working" => string.Format(CultureInfo.CurrentUICulture,T("Waiting for {0} to finish and write its receipt. Do not send the same request again."),recipient),
        "cli_unconfirmed" => task.CliFileError ?? T("CLI delivery is unconfirmed. Inspect the target session before sending again."),
        _ when task.ManualResendTurn == task.ManualTurn => T("One manual resend is allowed for this turn. Relay still checks for late receipts."),
        "cli_auto" => string.Format(CultureInfo.CurrentUICulture,T("Relay sends this turn to {0} through CLI automatically."),recipient),
        "cli_ready" => string.Format(CultureInfo.CurrentUICulture,T("CLI is ready for {0}. Send through CLI, or copy the prompt manually."),recipient) +
            (progress.DeliveryMessage is {} message ? "\n" + message : ""),
        _ => string.Format(CultureInfo.CurrentUICulture,T("Next: copy the prompt to {0}. Relay updates automatically when the Agent writes its receipt."),recipient)
    };

    private void AddFileDeliveryAction(Panel actions, TaskRecord task)
    {
        var targetAgentId = task.AwaitingAgentId ?? task.RecipientId;
        if (service.CliExtension(targetAgentId)?.Block is not null || task.CliFileTurn == task.ManualTurn && task.CliFileStatus == "failed")
        {
            Add(actions,"Check Agent bridge",()=>{agentId=targetAgentId;Go("Agents");return Task.CompletedTask;});
            Add(actions,"Copy bridge diagnostic prompt",()=>CopyBridgeDiagnostic(targetAgentId),false);
        }
        if(service.CliExtension(task.AwaitingAgentId??task.RecipientId) is not null)
            Add(actions,"Recheck and restore bridge",()=>RecheckBridge(task.AwaitingAgentId??task.RecipientId));
        if (!demo && service.CanSendFileHandoff(task) && !sendingTasks.Contains(task.TaskId))
            Add(actions,"Send via bridge",()=>{StartCliSend(task.TaskId,expectedTurn:task.ManualTurn);return Task.CompletedTask;});
    }

    private void AddFileRecoveryActions(Panel actions, TaskRecord task)
    {
        var binding = service.TaskSession(task.TaskId, task.AwaitingAgentId ?? task.RecipientId);
        var parent = (Panel)actions.Parent;
        if (binding?.SessionId is { } sessionId)
        {
            var details = new StackPanel();
            details.Children.Add(Note(string.Format(CultureInfo.CurrentUICulture,T("Saved conversation ID for {0}"),binding.Host)));
            details.Children.Add(new TextBox{Name="RecoverySessionId",Text=sessionId,IsReadOnly=true,TextWrapping=TextWrapping.Wrap});
            parent.Children.Insert(parent.Children.IndexOf(actions),new Expander
            {
                Name="RecoveryConversation",Header=T("View target conversation"),Content=details,Margin=new(0,8,0,0)
            });
        }
        else
            parent.Children.Insert(parent.Children.IndexOf(actions),Muted(T("The target conversation ID is still unknown."),12.5,new(0,8,0,0)));

        Add(actions,"Check for receipts now",()=>Task.CompletedTask);
        if (RelayService.CanAllowFileHandoffResend(task) && (binding?.SessionId is not null || task.CliFileStatus == "failed"))
            Add(actions,"Not received · Allow resending",()=>
            {
                if (ThemedMessageBox.Show(this,T("Relay still accepts a valid late receipt for this turn. Check the target conversation first and allow one more send only if the request did not arrive."),
                        T("Not received · Allow resending"),MessageBoxButton.OKCancel,MessageBoxImage.Warning)!=MessageBoxResult.OK)
                { reviewCancelled=true; return Task.CompletedTask; }
                service.AllowFileHandoffResend(task.TaskId,"User confirmed the turn did not arrive",true,task.ManualTurn);
                return Task.CompletedTask;
            });
    }

    private void StartAutoDeliveries()
    {
        if (demo) return;
        foreach (var id in service.AutoDeliveryCandidates()) StartCliSend(id,automatic:true);
    }

    /// <summary>Print-mode CLIs return only when the Agent finishes its turn, so sends run off the UI thread.</summary>
    private void StartCliSend(string id, bool automatic = false, int? expectedTurn = null)
    {
        if (demo) return;
        if (sendingTasks.Contains(id)) { StatusText.Text=T("This turn is already being sent. Wait for its result."); return; }
        var initialTask=service.Status(id).Task;
        if (expectedTurn is { } turn && initialTask.ManualTurn != turn)
            throw new RelayException("file_cli_changed",T("The handoff has moved to another turn. Check its current progress."));
        var prompt = FilePrompt(initialTask);
        sendingTasks.Add(id);
        StatusText.Text = T("Sending through CLI…");
        _ = Task.Run(async () =>
        {
            string? failure = null;
            // The send records its own outcome in the ledger; only refusals before launch need to be shown here.
            try
            {
                var result=await service.SendFileHandoffAsync(id,prompt,automatic:automatic,expectedTurn:initialTask.ManualTurn);
                if (result.Task.CliFileTurn==result.Task.ManualTurn && result.Task.CliFileStatus is "unknown" or "failed")
                    failure=result.Task.CliFileError;
            }
            catch (Exception error) when (error is RelayException or IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
            { failure = error is RelayException relay ? DesktopLocalizedContent.SetupError(relay, preferences.Language) : error.Message; }
            await Dispatcher.InvokeAsync(() =>
            {
                sendingTasks.Remove(id);
                if (failure is not null && IsCurrentDelivery(id,initialTask.ManualTurn))
                { ErrorText.Text = failure; ErrorBanner.Visibility = Visibility.Visible; errorTaskId=id; errorTaskTurn=initialTask.ManualTurn; }
                QueueRefresh();
            });
        });
    }

    private bool IsCurrentDelivery(string id,int turn)=>service.Tasks().FirstOrDefault(v=>v.Task.TaskId==id)?.Task is { } task &&
        task.ManualTurn==turn && task.State is not (TaskState.Succeeded or TaskState.Failed or TaskState.TimedOut or TaskState.Cancelled);
}
