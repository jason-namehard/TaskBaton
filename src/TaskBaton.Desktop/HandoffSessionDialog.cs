using System.Windows;
using System.Windows.Controls;
using TaskBaton.Core;

namespace TaskBaton.Desktop;

public sealed class HandoffSessionDialog : Window
{
    public SessionRecord? Origin { get; private set; }
    public string OriginBinding { get; private set; }="user_selected";
    public SessionRecord? Target { get; private set; }
    public string TargetBinding { get; private set; }="user_selected";
    public bool TargetIsNew { get; private set; }=true;

    public HandoffSessionDialog(Window owner,RelayService service,ManualTransferDraft draft,string language)
    {
        var ui=new EntryDialogLayout(this,owner,UiText.Translate("Conversation routing",language),language);
        string Name(string id)=>service.Agents().Single(a=>a.AgentId==id).DisplayName??id;
        ui.Body.Children.Add(ui.Note(Name(draft.From)+" → "+Name(draft.To)));
        ui.Body.Children.Add(ui.Note("A pasted handoff does not identify its source conversation. Choose it once if replies should resume it."));
        var originLabel=ui.Note("Source conversation is not identified.");ui.Body.Children.Add(originLabel);
        ui.Button(ui.Body,service.SessionDiscoverySupported(draft.From)?"Choose source conversation":"Enter source Session ID","ChooseOriginSession",()=>
        {
            var picker=new SessionPickerDialog(this,service,draft.From,language,System.IO.Path.GetDirectoryName(draft.File));
            if(picker.ShowDialog()!=true)return;
            Origin=picker.SelectedSession;OriginBinding=picker.Binding;
            originLabel.Text=(Origin!.Title??Origin.SessionId)+"\n"+Origin.Cwd;
        });
        var create=new RadioButton{Name="TargetNewSession",Content=ui.T("New conversation"),IsChecked=true,Margin=new(0,20,0,8)};
        var existing=new RadioButton{Name="TargetExistingSession",Content=ui.T("Use existing conversation"),Margin=new(0,4,0,8)};
        ui.Body.Children.Add(create);ui.Body.Children.Add(existing);
        var targetLabel=ui.Note("Choose an existing conversation once. Later turns reuse that exact ID.");
        ui.Body.Children.Add(targetLabel);
        var choose=ui.Button(ui.Body,service.SessionDiscoverySupported(draft.To)?"Choose existing conversation":"Enter Session ID manually","ChooseTargetSession",()=>
        {
            var picker=new SessionPickerDialog(this,service,draft.To,language,System.IO.Path.GetDirectoryName(draft.File));
            if(picker.ShowDialog()!=true)return;
            Target=picker.SelectedSession;TargetBinding=picker.Binding;
            targetLabel.Text=(Target!.Title??Target.SessionId)+"\n"+Target.Cwd;existing.IsChecked=true;
        });
        choose.IsEnabled=false;
        create.Checked+=(_,_)=>choose.IsEnabled=false;existing.Checked+=(_,_)=>choose.IsEnabled=true;
        ui.Body.Children.Add(ui.Note("Resume inherits the conversation's model and settings. Missing sessions require a new explicit choice; Relay never chooses the latest conversation."));
        ui.Button(ui.Actions,"Create handoff","ConfirmSessionHandoff",()=>
        {
            TargetIsNew=create.IsChecked==true;
            if(!TargetIsNew&&Target is null)throw new RelayException("session_selection_required",ui.T("Select a conversation first."));
            DialogResult=true;
        },true);
        var cancel=ui.Button(ui.Actions,"Cancel","CancelSessionHandoff",()=>DialogResult=false);cancel.IsCancel=true;
    }
}
