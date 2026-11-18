using System.Windows;
using TaskBaton.Core;

namespace TaskBaton.Desktop;

public partial class MainWindow
{
    private Task ConfirmTaskCancellation(string id)
    {
        if(ThemedMessageBox.Show(this,T("Cancel this task now? Relay will end the task immediately, stop waiting for receipts and stop scheduling further deliveries, even if a delivery is in progress. Work already started in the host may continue and must be stopped there separately. Existing files will be kept."),
            T("Cancel task"),MessageBoxButton.OKCancel,MessageBoxImage.Warning)!=MessageBoxResult.OK)
        {reviewCancelled=true;return Task.CompletedTask;}
        service.Cancel(id,"Cancelled by user");return Task.CompletedTask;
    }

    private async Task RecheckDesktopBridge(string targetAgentId)
    {
        var result=await Task.Run(()=>service.RecheckDesktopBridgeAsync(targetAgentId));
        if(!result.BlockCleared&&service.CliExtension(targetAgentId)?.Block is not null)
            throw new RelayException("bridge_not_restored",result.Message+"\n"+string.Join("\n",result.Checks.Where(c=>c.Id!="block"&&c.Status=="fail").Select(c=>c.Message)));
        ThemedMessageBox.Show(this,result.Message,T("Bridge recovery result"),MessageBoxButton.OK,MessageBoxImage.Information);
    }

    private async Task RecheckBridge(string id)
    {
        var extension=service.CliExtension(id)??throw new RelayException("cli_recipe_missing",T("No bridge is configured for this Agent."));
        if(CliRecipes.OutputFormatFor(extension.Recipe)=="dsh-desktop")
        { await RecheckDesktopBridge(id); return; }
        var checks=await Task.Run(()=>service.CliHealthAsync(id));
        var failures=checks.Where(c=>c.Id!="block"&&c.Status=="fail").Select(c=>c.Message).ToArray();
        if(failures.Length>0)throw new RelayException("bridge_not_restored",string.Join("\n",failures));
        var detail=string.Join("\n",checks.Where(c=>c.Id!="block").Select(c=>c.Message));
        if(extension.Block is null)
        {
            ThemedMessageBox.Show(this,T("Local configuration checks passed. This does not confirm that the Agent can accept a session.")+"\n\n"+detail,
                T("Bridge recovery result"),MessageBoxButton.OK,MessageBoxImage.Information);
            return;
        }
        if(ThemedMessageBox.Show(this,T("Local configuration checks passed, but cannot confirm that the Agent can accept a session. Clear the saved failure? Previously authorized automatic tasks may send again.")+"\n\n"+detail,
            T("Recheck and restore bridge"),MessageBoxButton.OKCancel,MessageBoxImage.Warning)!=MessageBoxResult.OK)
        {reviewCancelled=true;return;}
        var current=service.CliExtension(id)??throw new RelayException("cli_recipe_missing",T("No bridge is configured for this Agent."));
        if(current.Revision!=extension.Revision||current.Block!=extension.Block)
            throw new RelayException("cli_recipe_changed",T("The bridge configuration or saved failure changed during the check. Please check again."));
        service.ClearCliBlock(id);
    }

    private bool CanRecheckDesktopBridge(string id)=>service.CliExtension(id) is {Block.Kind:"auth_required"} extension&&
        CliRecipes.OutputFormatFor(extension.Recipe)=="dsh-desktop";
}
