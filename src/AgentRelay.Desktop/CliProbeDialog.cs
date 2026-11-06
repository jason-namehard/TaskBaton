using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AgentRelay.Core;

namespace AgentRelay.Desktop;

public sealed class CliProbeDialog : Window
{
    private readonly RelayService service;
    private readonly string agentId;
    private readonly string language;
    private readonly bool demo;
    private readonly EntryDialogLayout layout;
    private bool running;
    private readonly Action<string>? reportActivity;

    public CliProbeDialog(Window owner, RelayService service, string agentId, string language, bool demo=false,
        Action<string>? reportActivity=null)
    {
        this.service=service;this.agentId=agentId;this.language=language;this.demo=demo;
        this.reportActivity=reportActivity;
        layout=new EntryDialogLayout(this,owner,T("Test CLI"),language);
        layout.Status.Name="CliProbeStatus";
        System.Windows.Automation.AutomationProperties.SetAutomationId(layout.Status,"CliProbeStatus");
        Closing+=(_,e)=>{if(running)e.Cancel=true;};
        Render();
    }

    private string T(string text)=>UiText.Translate(text,language);
    private TextBlock Note(string text)=>new(){Text=T(text),TextWrapping=TextWrapping.Wrap,Margin=new(0,8,0,10)};
    private Button Action(Panel parent,string label,string name,Func<Task<string>> action)
    {
        var button=new Button{Name=name,Content=T(label),MinHeight=38,Height=double.NaN};
        System.Windows.Automation.AutomationProperties.SetAutomationId(button,name);
        button.Click+=async(_,_)=>
        {
            var activity=name switch
            {
                "ProbeCreate" or "ProbeResume" => "Running CLI probe…",
                "CopyBridgeDiagnostic" => "Copy bridge diagnostic prompt",
                "CleanupCliProbe" => "Cleaning up probe sessions…",
                _ => "Saving visibility confirmation…"
            };
            running=true;layout.Body.IsEnabled=false;layout.Actions.IsEnabled=false;layout.Status.Text=T(activity);
            reportActivity?.Invoke(activity);
            try
            {
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
                var result=await action();
                Render();layout.Status.Text=T(result);reportActivity?.Invoke(result);
            }
            catch(Exception error) when(error is RelayException or IOException or UnauthorizedAccessException or Win32Exception or ExternalException)
            {layout.Status.Text=T(error.Message);reportActivity?.Invoke("CLI operation failed · See the test window");}
            finally{running=false;layout.Body.IsEnabled=true;layout.Actions.IsEnabled=true;}
        };
        parent.Children.Add(button);return button;
    }

    private void Render()
    {
        layout.Body.Children.Clear();layout.Actions.Children.Clear();layout.Status.Text="";
        var agent=service.Agents().Single(a=>a.AgentId==agentId);
        layout.Body.Children.Add(Note(agent.DisplayName??agent.AgentId));
        var extension=service.CliExtension(agentId);
        if(extension is null)
        {
            layout.Body.Children.Add(Note("No CLI calling method is registered. Use One-click setup in Agents, or Edit Agent to enter the program and arguments. This is not a failed probe."));
        }
        else
        {
            layout.Body.Children.Add(Note("This sends a real, short probe to the selected CLI. Model charges may apply, including host context. It does not run project work. Check the command before starting."));
            var zcode=CliRecipes.IsZcode(extension.Recipe);
            if(!zcode)
            {
                var visibility=Note(VisibilityAdvice(extension.Recipe));
                visibility.Name="ProbeVisibilityHint";
                System.Windows.Automation.AutomationProperties.SetAutomationId(visibility,visibility.Name);
                layout.Body.Children.Add(visibility);
            }
            if(BridgePresentation.HasFailure(extension))
                Action(layout.Body,"Copy bridge diagnostic prompt","CopyBridgeDiagnostic",()=>
                {
                    Clipboard.SetText(service.BridgeDiagnosticPrompt(agentId,AgentEntryDialog.ResolveRelayCommand()));
                    return Task.FromResult("Diagnostic prompt copied. Paste it into an Agent for local checks; do not start new probes or tasks.");
                });
            var plan=EntryDialogLayout.Input("ProbeRecipe",RelayJson.Write(extension.Recipe),true);plan.IsReadOnly=true;plan.Height=170;
            layout.Body.Children.Add(new Expander{Header=T("Calling recipe"),Content=plan});
            if(demo)layout.Body.Children.Add(Note("Demo mode does not run external CLI calls. Use a normal workspace to test a real Agent."));
            var probeRow=new StackPanel{Orientation=Orientation.Horizontal};
            layout.Body.Children.Add(probeRow);
            var create=Action(probeRow,"Run new-session probe","ProbeCreate",()=>RunProbe("create"));
            create.IsEnabled=true;
            if(demo)create.ToolTip=T("Demo mode blocks real CLI calls. Keep this workspace and switch to normal use to test your registered Agents.");
            if(zcode)
            {
                var advice=T(VisibilityAdvice(extension.Recipe));
                var help=new Border{Name="ProbeVisibilityHint",Margin=new(8,0,0,0),Cursor=Cursors.Help,
                    Child=Icons.Create("Help",size:16,brushKey:"MutedBrush"),
                    ToolTip=new TextBlock{Text=advice,TextWrapping=TextWrapping.Wrap,MaxWidth=380}};
                System.Windows.Automation.AutomationProperties.SetAutomationId(help,help.Name);
                System.Windows.Automation.AutomationProperties.SetName(help,advice);
                probeRow.Children.Add(help);
            }
            if(extension.CreateProbe is {} probe)ProbeResult(probe);
            if(extension.CreateProbe is {MachinePassed:true,UserConfirmed:true} && CliRecipes.ResumeCommand(extension.Recipe) is not null)
            {
                var resume=Action(layout.Body,"Run same-session probe","ProbeResume",()=>RunProbe("resume"));
                resume.IsEnabled=!demo && !extension.CreateProbe.SessionCleaned;
                if(extension.CreateProbe.SessionCleaned)
                    resume.ToolTip=T("The probe session was cleaned up. Run a new-session probe before testing resume again.");
                if(extension.ResumeProbe is {} resumed)ProbeResult(resumed);
            }
            if(extension.ProbeArtifacts is {Length:>0} || extension.CreateProbe is {MachinePassed:true,UserConfirmed:true})
            {
                if(CliRecipes.OutputFormatFor(extension.Recipe)=="dsh-desktop")
                    layout.Body.Children.Add(new TextBlock{Name="ProbeCleanupDshHint",Text=T("To clean up DSH probes, save your work and fully quit DSH, including the tray, then run cleanup. Relay never closes the host. Shared images and data whose ownership cannot be confirmed are kept."),TextWrapping=TextWrapping.Wrap,Margin=new(0,8,0,10)});
                var cleanup=Action(layout.Body,"Clean up probe sessions and files","CleanupCliProbe",CleanupProbe);
                cleanup.IsEnabled=!demo;
                if(demo)cleanup.ToolTip=T("Demo mode does not clean real host sessions.");
            }
            layout.Body.Children.Add(Note(extension.CanResume?"New session and resume verified":extension.CanCreate?"New session verified":"A probe or one real handoff whose Agent writes its receipt verifies the CLI. Only verified CLIs receive turns automatically; you can still send by hand."));
            layout.Body.Children.Add(Note("A probe does not choose the conversation for a task. Choose an existing conversation in the handoff; later turns reuse that exact ID."));
        }
        var close=new Button{Name="CloseCliProbe",Content=T("Close"),IsCancel=true};
        close.Click+=(_,_)=>DialogResult=true;
        layout.Actions.Children.Add(close);
    }

    private static string VisibilityAdvice(CliRecipe recipe)=>CliRecipes.IsZcode(recipe)
        ? "Relay syncs eligible Zcode CLI conversations to the sidebar index after each turn. Bring Zcode to the foreground to refresh the sidebar. If syncing is off or fails, save your work and fully quit and restart Zcode if needed. If a conversation shows old content, switch conversations and reopen it. Do not repeat a paid probe just to refresh the view."
        : CliRecipes.OutputFormatFor(recipe) switch
    {
        "dsh-desktop"=>"The DSH desktop bridge runs inside the open DSH client, so new sessions and continued turns normally appear without restarting. Check the returned session ID and refresh or reopen that conversation if needed. Do not restart DSH while a turn is running.",
        _=>"A successful machine receipt does not confirm that the host's desktop has refreshed. For new and continued sessions, refresh the session list or reopen the conversation; if needed, save your work and restart that client. A missing marker in a stale view is not a reason to run the probe again."
    };

    private async Task<string> RunProbe(string operation)
    {
        if(demo)throw new RelayException("demo_cli_disabled",T("Demo mode blocks real CLI calls. Keep this workspace and switch to normal use to test your registered Agents."));
        var probe=await service.RunCliProbeAsync(agentId,operation);
        return probe.MachinePassed?"CLI probe passed · Confirm session visibility":"CLI probe failed · See the test result";
    }

    private async Task<string> CleanupProbe()
    {
        if(ThemedMessageBox.Show(this,T("Clean up this Agent's recorded probe sessions and files? Verification stays enabled. Sessions used by real tasks, items whose ownership cannot be confirmed, and unsupported items are kept and reported. Some hosts can archive a session but cannot delete it. Once a probe session is removed, a new-session probe is needed to test resume again."),
            T("Clean up probe sessions and files"),MessageBoxButton.OKCancel,MessageBoxImage.Warning)!=MessageBoxResult.OK)
            return "Probe cleanup cancelled";
        var result=await Task.Run(()=>service.CleanupCliProbeAsync(agentId));
        var summary=T(result.Complete?"Probe cleanup completed.":"Probe cleanup finished with retained or failed items.");
        var items=result.Items.Select(item=>
        {
            var kind=T(item.Kind switch
            {
                "session"=>"Probe session", "file"=>"Probe file", "index"=>"Host session index",
                _=>throw new InvalidOperationException("Unknown probe cleanup item kind: "+item.Kind)
            });
            var status=T(item.Status switch
            {
                "removed"=>"Removed", "absent"=>"Already absent", "synced"=>"Index updated", "shared"=>"Shared attachment kept",
                "unsupported"=>"Not supported", "unverified"=>"Ownership unverified", "in_use"=>"Used by a task",
                "failed"=>"Cleanup failed", "pending"=>"Cleanup pending",
                _=>throw new InvalidOperationException("Unknown probe cleanup item status: "+item.Status)
            });
            var detail=kind+" · "+status+(item.SessionId is null?"":"\n"+T("Session")+": "+item.SessionId)+"\n"+item.Message;
            if(item.Path is not null)detail+="\n"+T("Path")+": "+item.Path;
            if(item.BackupPath is not null)detail+="\n"+T("Backup path")+": "+item.BackupPath;
            return detail;
        }).ToArray();
        var message=summary+"\n\n"+(items.Length==0?T("No recorded probe items are eligible for cleanup."):string.Join("\n\n",items));
        ThemedMessageBox.Show(this,message,T("Probe cleanup result"),MessageBoxButton.OK,
            result.Complete?MessageBoxImage.Information:MessageBoxImage.Warning);
        return result.Complete?"Probe cleanup completed.":"Probe cleanup finished with retained or failed items.";
    }

    private void ProbeResult(CliProbe probe)
    {
        var stamp=Note(string.Format(System.Globalization.CultureInfo.CurrentUICulture,T("Probe tested at {0}. This is a saved result, separate from the current connection check."),probe.TestedAt.ToLocalTime().ToString("g")));
        stamp.Name=probe.Operation=="create"?"CreateProbeTestedAt":"ResumeProbeTestedAt";
        layout.Body.Children.Add(stamp);
        var text=EntryDialogLayout.Input("Probe"+(probe.Operation=="create"?"Create":"Resume")+"Result",
            T("Marker")+": "+probe.Marker+"\n"+T("Session")+": "+probe.SessionId+"\n"+
            (probe.MachinePassed?T("Machine receipt passed"):probe.Error),true);
        text.IsReadOnly=true;text.Height=100;layout.Body.Children.Add(text);
        if(probe.SidebarSync is { } sync)
        {
            var status=sync.Status switch
            {
                "synced"=>"The sidebar index was synced for this probe. Bring Zcode to the foreground to refresh the sidebar.",
                "disabled"=>"Sidebar sync was off for this probe. The session may not appear in Zcode's sidebar.",
                "skipped"=>"The sidebar index was not updated for this probe. The session may already be managed by Zcode or may not be eligible for sync.",
                "failed"=>"Sidebar sync failed for this probe. The CLI call result is unchanged.",
                _=>throw new InvalidOperationException("Unknown sidebar sync status: "+sync.Status)
            };
            var detail=sync.Status=="failed" && sync.Message is not null ? "\n"+sync.Message : "";
            if(sync.Status=="failed" && sync.IndexPath is not null)detail+="\n"+T("Path")+": "+sync.IndexPath;
            if(sync.WindowActivated==false)detail+="\n"+T("Zcode could not be brought to the foreground. Switch to Zcode to refresh the sidebar.");
            var note=Note(status);note.Text+=detail;
            note.Name=probe.Operation=="create"?"CreateProbeSidebarSync":"ResumeProbeSidebarSync";
            System.Windows.Automation.AutomationProperties.SetAutomationId(note,note.Name);
            layout.Body.Children.Add(note);
        }
        if(probe.CleanupWarning is not null)layout.Body.Children.Add(Note(probe.CleanupWarning));
        if(probe.Diagnostic is not null)
        {
            var diagnostic=EntryDialogLayout.Input("ProbeDiagnostics",probe.Diagnostic,true);
            diagnostic.IsReadOnly=true;diagnostic.Height=170;
            layout.Body.Children.Add(new Expander{Header=T("CLI diagnostics"),Content=diagnostic});
        }
        if(!probe.MachinePassed)return;
        layout.Body.Children.Add(Note("The call succeeded. Confirm visibility only after you see this marker in the correct conversation. If the desktop has not refreshed, leave the confirmation unchecked; it is not a failed call."));
        var confirm=new CheckBox{Content=new TextBlock{Text=T(probe.Operation=="create"?
            "I found this marker in the target Agent's session list.":"I found this marker in the SAME session, not a new session."),TextWrapping=TextWrapping.Wrap},
            IsChecked=probe.UserConfirmed,Margin=new(0,8,0,10),Name=probe.Operation=="create"?"ConfirmCreateVisible":"ConfirmResumeVisible"};
        layout.Body.Children.Add(confirm);
        var save=Action(layout.Body,"Save visibility confirmation",probe.Operation=="create"?"SaveCreateConfirmation":"SaveResumeConfirmation",()=>
        {service.ConfirmCliProbe(agentId,probe.ProbeId,confirm.IsChecked==true);return Task.FromResult("Visibility confirmation saved");});
        save.IsEnabled=false;
        confirm.Checked+=(_,_)=>save.IsEnabled=true;
        confirm.Unchecked+=(_,_)=>save.IsEnabled=probe.UserConfirmed;
    }
}
