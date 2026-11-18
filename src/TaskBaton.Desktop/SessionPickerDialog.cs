using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using TaskBaton.Core;

namespace TaskBaton.Desktop;

public sealed class SessionPickerDialog : Window
{
    private sealed record SessionRow(SessionRecord Session,string Title,string Cwd,string Updated,string Model,string Visibility);
    private readonly RelayService service;
    private readonly string agentId;
    private readonly string? preferredCwd;
    private readonly EntryDialogLayout ui;
    private readonly DataGrid table;
    private readonly TextBox search;
    private readonly Button refreshButton;
    private readonly CancellationTokenSource cancellation=new();
    private IReadOnlyList<SessionRecord> sessions=[];
    private bool refreshing;
    public SessionRecord? SelectedSession { get; private set; }
    public string Binding { get; private set; }="user_selected";

    public SessionPickerDialog(Window owner,RelayService service,string agentId,string language,string? preferredCwd=null)
    {
        this.service=service;this.agentId=agentId;this.preferredCwd=preferredCwd;
        ui=new EntryDialogLayout(this,owner,UiText.Translate("Choose existing conversation",language),language);
        var name=service.Agents().Single(a=>a.AgentId==agentId).DisplayName??agentId;
        ui.Body.Children.Add(ui.Note(name));
        ui.Body.Children.Add(ui.Note("Only this Agent is scanned. Sorting never selects a conversation for you."));
        search=ui.Field(ui.Body,"Search title or project","SessionSearch");
        table=new DataGrid{Name="SessionsTable",AutoGenerateColumns=false,IsReadOnly=true,SelectionMode=DataGridSelectionMode.Single,
            Height=300,MinHeight=160,Margin=new(0,8,0,8),CanUserAddRows=false};
        AutomationProperties.SetAutomationId(table,table.Name);
        foreach(var (title,path) in new[]{("Conversation","Title"),("Project folder","Cwd"),("Last active","Updated"),("Model","Model"),("Visibility","Visibility")})
            table.Columns.Add(new DataGridTextColumn{Header=ui.T(title),Binding=new System.Windows.Data.Binding(path),Width=new DataGridLength(1,DataGridLengthUnitType.Star)});
        ui.Body.Children.Add(table);
        search.TextChanged+=(_,_)=>ShowSessions();
        refreshButton=ui.Button(ui.Actions,"Refresh","RefreshSessions",()=>_=RefreshAsync());
        ui.Button(ui.Actions,"Use this conversation","UseSelectedSession",()=>
        {
            if(table.SelectedItem is not SessionRow row)throw new RelayException("session_selection_required",ui.T("Select a conversation first."));
            SelectedSession=row.Session;Binding="user_selected";DialogResult=true;
        },true);
        var manual=new StackPanel{Margin=new(10)};
        ui.Body.Children.Add(new Expander{Header=ui.T("Enter Session ID manually"),Content=manual});
        var id=ui.Field(manual,"Session ID","ManualSessionId");
        var cwd=ui.Field(manual,"Project folder (optional)","ManualSessionCwd",preferredCwd??"");
        ui.Button(manual,"Use Session ID","UseManualSession",()=>
        {
            if(string.IsNullOrWhiteSpace(id.Text))throw new RelayException("session_id_required",ui.T("Enter an exact Session ID."));
            if(!string.IsNullOrWhiteSpace(cwd.Text)&&!System.IO.Path.IsPathFullyQualified(cwd.Text))throw new RelayException("session_cwd_invalid",ui.T("Use an absolute project path."));
            SelectedSession=new(id.Text.Trim(),null,string.IsNullOrWhiteSpace(cwd.Text)?null:cwd.Text.Trim(),null,null,"unknown",null);
            Binding="manual";DialogResult=true;
        });
        ui.Body.Children.Add(ui.Note("Manual IDs are checked before CLI resume. Unsupported discovery does not prove that a session exists."));
        var cancel=ui.Button(ui.Actions,"Cancel","CancelSessionPicker",()=>DialogResult=false);cancel.IsCancel=true;
        Closed+=(_,_)=>cancellation.Cancel();
        if(service.CachedSessions(agentId) is {} cached){sessions=cached.Sessions;ShowSessions();ui.Status.Text=ui.T("Showing cached conversations; refreshing…");}
        Loaded+=async(_,_)=>await RefreshAsync();
    }

    private async Task RefreshAsync()
        {
            if(refreshing)return;
            refreshing=true;refreshButton.IsEnabled=false;ui.Status.Text=ui.T("Refreshing conversations…");
            try
            {
                var result=await service.QuerySessionsAsync(agentId,true,cancellation.Token);
                if(cancellation.IsCancellationRequested)return;
                if(result.Message is null||!result.Supported||result.Sessions.Count>0)sessions=result.Sessions;
                ShowSessions();
                ui.Status.Text=result.Message??ui.T(result.Supported?"Choose a conversation explicitly. No conversation is selected automatically.":"Session discovery is unavailable. You can enter an exact Session ID.");
            }
            catch(OperationCanceledException) when(cancellation.IsCancellationRequested) { }
            catch(Exception error) when(error is RelayException or System.IO.IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
            {ui.Status.Text=ui.T("Conversation refresh failed. Check the configured storage folder.")+"\n"+error.Message;}
            finally{refreshing=false;refreshButton.IsEnabled=true;}
        }

    private void ShowSessions()
    {
        var selected=(table.SelectedItem as SessionRow)?.Session.SessionId;
        var query=search.Text.Trim();
        var rows=sessions.Where(s=>query.Length==0||new[]{s.Title,s.Cwd,s.SessionId}.Any(v=>v?.Contains(query,StringComparison.OrdinalIgnoreCase)==true))
            .OrderByDescending(s=>preferredCwd is not null&&string.Equals(s.Cwd,preferredCwd,StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(s=>s.UpdatedAt)
            .Select(s=>new SessionRow(s,s.Title??s.SessionId,s.Cwd??"",s.UpdatedAt?.ToLocalTime().ToString("g",CultureInfo.CurrentUICulture)??"",s.Model??"",s.Visibility)).ToArray();
        table.ItemsSource=rows;
        table.SelectedItem=selected is null?null:rows.SingleOrDefault(r=>r.Session.SessionId==selected);
    }
}
