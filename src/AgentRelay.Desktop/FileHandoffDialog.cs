using System.Windows;
using System.Windows.Controls;
using AgentRelay.Core;
using Microsoft.Win32;

namespace AgentRelay.Desktop;

public sealed class FileHandoffDialog : Window
{
    public ManualTransferDraft? Result { get; private set; }
    public bool ReuseExisting { get; private set; }
    public FileHandoffOptions Options { get; private set; } = new(ManualContinuous: true);

    public FileHandoffDialog(Window owner,RelayService service,string language)
    {
        var ui=new EntryDialogLayout(this,owner,UiText.Translate("New manual delivery task",language),language);
        var agents=service.Agents();
        ui.Body.Children.Add(ui.Note("Write the work request below, copy the complete prompt to the sender Agent, then paste its handoff JSON here. Relay keeps tracking later manual replies until no further work remains."));
        ComboBox Node(string label,string name,int selected)
        {
            ui.Body.Children.Add(ui.Note(label));
            var box=new ComboBox{Name=name,ItemsSource=agents.Select(a=>new HandoffAgentOption(a.AgentId,a.DisplayName??a.AgentId)).ToArray(),
                DisplayMemberPath="Name",SelectedValuePath="Id",SelectedIndex=selected,MinHeight=38};
            ui.Body.Children.Add(box);return box;
        }
        var from=Node("Sender","FileHandoffFrom",0);var to=Node("Recipient","FileHandoffTo",1);
        var work=ui.Field(ui.Body,"Work to give the sender Agent","ManualWorkRequest",multiline:true);work.Height=150;
        ui.Button(ui.Body,"Copy complete prompt","CopyFileHandoffFormat",()=>
        {
            if(from.SelectedValue is not string sender||to.SelectedValue is not string recipient)
                throw new RelayException("manual_agent_unknown",ui.T("Select an Agent."));
            Clipboard.SetText(service.ManualTaskPrompt(sender,recipient,work.Text));
            ui.Status.Text=ui.T("Complete prompt copied. Send it to the sender Agent, then paste its JSON below.");
        });
        ui.Body.Children.Add(ui.Note("Paste the sender Agent's handoff JSON below. Later replies use the task's context and receipt, so another handoff does not need to be created for each turn."));
        var reply=ui.Field(ui.Body,"Agent handoff JSON","FileHandoffReply",multiline:true);reply.Height=150;
        var relayPanel=new StackPanel{Margin=new(10)};
        ui.Body.Children.Add(new Expander{Header=ui.T("Relay template"),Content=relayPanel,Margin=new(0,4,0,8)});
        relayPanel.Children.Add(ui.Note("Leave custom steps empty for a continuous manual handoff."));
        var template=new RelayTemplateEditor(ui,relayPanel,"FileHandoff",from,to,"FileHandoffRelayNewSession");
        void Accept(ManualTransferDraft draft, bool reuseExisting)
        {
            if(template.UsesPreset&&(draft.From!=(string?)from.SelectedValue||draft.To!=(string?)to.SelectedValue))
                throw new RelayException("relay_template_agents",ui.T("Select the sender and recipient from the pasted handoff before applying a template."));
            Options=template.HasPlan ? new(RelayPlan:template.Build(draft.From,draft.To)) : new(ManualContinuous:true);
            ReuseExisting=reuseExisting&&Options.ManualContinuous;
            Result=string.IsNullOrWhiteSpace(draft.Message)&&!string.IsNullOrWhiteSpace(work.Text)
                ?draft with {Message=work.Text.Trim()}:draft;
            DialogResult=true;
        }
        ui.Button(ui.Body,"Parse and create handoff","ParseFileHandoff",()=>
        {Accept(ManualTransfers.Parse(reply.Text,agents),true);},true);

        var manual=new StackPanel{Margin=new(10)};
        var expander=new Expander{Header=ui.T("Or select an existing file"),Content=manual,Margin=new(0,16,0,8)};ui.Body.Children.Add(expander);
        manual.Children.Add(ui.Note("Use the sender and recipient selected above. An existing file can start the same continuous manual handoff."));
        var file=ui.Field(manual,"Handoff file","FileHandoffFile");
        ui.Button(manual,"Choose file","ChooseHandoffFile",()=>{var picker=new OpenFileDialog();if(picker.ShowDialog(this)==true)file.Text=picker.FileName;});
        var message=ui.Field(manual,"Short work request","FileHandoffMessage",multiline:true);message.Height=75;
        ui.Button(manual,"Create handoff","CreateFileHandoff",()=>
        {
            Accept(ManualTransfers.Parse(RelayJson.Write(new ManualTransferDraft(from.SelectedValue as string??"",to.SelectedValue as string??"",file.Text,message.Text)),agents),false);
        });
        var cancel=ui.Button(ui.Actions,"Cancel","CancelFileHandoff",()=>DialogResult=false);cancel.IsCancel=true;
    }
}
