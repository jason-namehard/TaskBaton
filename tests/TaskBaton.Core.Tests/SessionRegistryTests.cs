using TaskBaton.Core;
using Microsoft.Data.Sqlite;

namespace TaskBaton.Core.Tests;

[TestClass]
public sealed class SessionRegistryTests
{
    private string root = null!;
    private RelayService service = null!;
    private string source = null!;

    [TestInitialize]
    public void Setup()
    {
        root=Path.Combine(Path.GetTempPath(),"TaskBaton.Tests",Guid.NewGuid().ToString());
        service=new RelayService(Path.Combine(root,"relay"));service.Initialize();
        service.RegisterManual("a","fixture","甲");service.RegisterManual("b","fixture","乙");
        source=Path.Combine(root,"source.md");File.WriteAllText(source,"Review the work.");
        service.SetPermission("a","b","allow");service.SetPermission("b","a","allow");
    }

    [TestCleanup] public void Cleanup()=>Directory.Delete(root,true);

    private string SessionStore(string agent,string id)
    {
        var directory=Path.Combine(root,agent);Directory.CreateDirectory(directory);
        var path=Path.Combine(directory,"state.db");
        using var connection=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Pooling=false}.ToString());connection.Open();
        using var command=connection.CreateCommand();
        command.CommandText="""
            CREATE TABLE sessions(id TEXT,title TEXT,cwd TEXT,profile_name TEXT,last_activity_at REAL,model TEXT,source TEXT,hidden INTEGER,archived INTEGER);
            INSERT INTO sessions VALUES($id,'Chosen review',$cwd,'original-profile',1000,'original-model','desktop',0,0);
            INSERT INTO sessions VALUES('newest-unrelated','Unrelated',$cwd,'other-profile',9999,'other-model','desktop',0,0);
            """;
        command.Parameters.AddWithValue("$id",id);command.Parameters.AddWithValue("$cwd",root);command.ExecuteNonQuery();
        service.ConfigureSessionDiscovery(agent,"hermes",directory);
        return path;
    }

    private void Recipe(string agent,string createdId,bool wrongResume=false)
    {
        var code="[Console]::InputEncoding=[Text.UTF8Encoding]::new($false);[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false);"+
            "$inputText=[Console]::In.ReadToEnd();$kind=$inputText.Split([char]10,2)[0].Trim();"+
            "[IO.File]::AppendAllText($env:RELAY_TEST_LOG,$kind+[Environment]::NewLine);"+
            "$id=if($kind -eq 'create'){$env:RELAY_TEST_CREATED}else{"+(wrongResume?"'wrong-session'":"$kind")+"};"+
            "@{session_id=$id;reply='Work received';accepted=$true}|ConvertTo-Json -Compress";
        var command=new CliCommand("pwsh",["-NoProfile","-NonInteractive","-Command",code],"create\n{prompt}",root,
            new Dictionary<string,string>{{"RELAY_TEST_LOG",Path.Combine(root,agent+".log")},{"RELAY_TEST_CREATED",createdId}});
        service.ImportCliRecipe(agent,RelayJson.Write(new CliRecipe(1,"Fixture","fixture",command,
            command with{Input="{session_id}\n{prompt}"},[],"fixture","tests","MIT","No model calls.")));
    }

    private TaskRecord Create(bool origin=true)=>service.CreateFileHandoff(new("a","b",source,"Review"),
        origin?new OriginSession("fixture","source-a","original-profile",root):null).Task;

    private void Next(TaskRecord task,string sender)
    {
        var current=service.Status(task.TaskId).Task;
        var result=Path.Combine(root,"reply-"+current.ManualTurn+".md");File.WriteAllText(result,"Work remains.");
        File.WriteAllText(service.ManualReceiptPath(current),RelayJson.Write(new ManualReceipt(task.TaskId,task.AttemptId,
            sender,current.ManualTurn,"done",true,result,"Perform the remaining review.")));
        Assert.IsEmpty(service.ImportManualReceipts().Errors);
    }

    [TestMethod]
    public void AdapterOriginIsRegisteredAutomaticallyButClipboardInputDoesNotGuess()
    {
        var task=Create();var origin=service.TaskSession(task.TaskId,"a")!;
        Assert.AreEqual("automatic",origin.Binding);Assert.AreEqual("source-a",origin.SessionId);
        Assert.AreEqual(root,origin.Cwd);Assert.AreEqual("original-profile",origin.Profile);
        var another=Path.Combine(root,"another.md");File.WriteAllText(another,"Another job.");
        var clipboard=service.CreateFileHandoff(new("a","b",another)).Task;
        Assert.IsNull(service.TaskSession(clipboard.TaskId,"a"));
    }

    [TestMethod]
    public void UserSelectedAndManualBindingsPersistWithoutSelectingTheNewestSession()
    {
        var task=Create(false);
        service.BindTaskSession(task.TaskId,"b",new("selected-b","Review",root,"original-profile",null,"unknown",null));
        Assert.AreEqual("selected-b",new RelayService(service.Home).TaskSession(task.TaskId,"b")!.SessionId);
        service.BindManualTaskSession(task.TaskId,"a","source-a",root,"original-profile");
        Assert.AreEqual("manual",service.TaskSession(task.TaskId,"a")!.Binding);
        Assert.HasCount(2,service.SessionBindings(task.TaskId));
    }

    [TestMethod]
    public async Task NewTargetIsRegisteredAndBothDirectionsResumeTheirExactBoundSessions()
    {
        SessionStore("a","source-a");SessionStore("b","created-b");Recipe("a","created-a");Recipe("b","created-b");
        var task=Create();await service.SendFileHandoffAsync(task.TaskId,"Read the work.");
        Assert.AreEqual("created-b",service.TaskSession(task.TaskId,"b")!.SessionId);
        Assert.AreEqual("created",service.TaskSession(task.TaskId,"b")!.Binding);
        Next(task,"b");await service.SendFileHandoffAsync(task.TaskId,"Read B's reply.");
        Assert.AreEqual("source-a",File.ReadAllLines(Path.Combine(root,"a.log")).Single());
        Next(task,"a");await service.SendFileHandoffAsync(task.TaskId,"Read A's reply.");
        CollectionAssert.AreEqual(new[]{"create","created-b"},File.ReadAllLines(Path.Combine(root,"b.log")));
        Assert.AreEqual("created-b",service.TaskSession(task.TaskId,"b")!.SessionId);
        Assert.HasCount(2,service.SessionBindings(task.TaskId).Where(b=>b.AgentId=="b").ToArray());
    }

    [TestMethod]
    public async Task ExistingSelectedSessionIsResumedWithoutAProbeOrAnImplicitLatestFallback()
    {
        SessionStore("b","selected-b");Recipe("b","should-not-create");var task=Create(false);
        service.BindManualTaskSession(task.TaskId,"b","selected-b",root);
        var sent=await service.SendFileHandoffAsync(task.TaskId,"Read the work.");
        Assert.AreEqual("selected-b",sent.Task.CliFileSessionId);
        Assert.AreEqual("selected-b",File.ReadAllLines(Path.Combine(root,"b.log")).Single());
    }

    [TestMethod]
    public async Task DeletedSessionIsBlockedEvenWhenTheDisplayCacheStillContainsIt()
    {
        var database=SessionStore("b","selected-b");Recipe("b","created-b");var task=Create(false);
        _=await service.QuerySessionsAsync("b",true);service.BindManualTaskSession(task.TaskId,"b","selected-b",root);
        using(var connection=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=database,Pooling=false}.ToString()))
        {
            connection.Open();using var command=connection.CreateCommand();command.CommandText="DELETE FROM sessions WHERE id='selected-b'";command.ExecuteNonQuery();
        }
        var error=await Assert.ThrowsExactlyAsync<RelayException>(()=>service.SendFileHandoffAsync(task.TaskId,"Read the work."));
        Assert.AreEqual("session_rebind_required",error.Code);Assert.IsFalse(File.Exists(Path.Combine(root,"b.log")));
        Assert.IsNull(service.Status(task.TaskId).Task.CliFileTurn);
    }

    [TestMethod]
    public async Task UnsupportedDiscoveryPreservesManualBindingButDoesNotInventAnExistenceCheck()
    {
        Recipe("b","created-b");var task=Create(false);service.BindManualTaskSession(task.TaskId,"b","manual-b",root);
        var error=await Assert.ThrowsExactlyAsync<RelayException>(()=>service.SendFileHandoffAsync(task.TaskId,"Read the work."));
        Assert.AreEqual("session_rebind_required",error.Code);Assert.AreEqual("manual-b",service.TaskSession(task.TaskId,"b")!.SessionId);
        Assert.IsFalse(File.Exists(Path.Combine(root,"b.log")));
    }

    [TestMethod]
    public async Task WrongReturnedResumeIdNeverReplacesTheBoundSession()
    {
        SessionStore("b","selected-b");Recipe("b","created-b",true);var task=Create(false);
        service.BindManualTaskSession(task.TaskId,"b","selected-b",root);
        var sent=await service.SendFileHandoffAsync(task.TaskId,"Read the work.");
        Assert.AreEqual("unknown",sent.Task.CliFileStatus);Assert.AreEqual("selected-b",service.TaskSession(task.TaskId,"b")!.SessionId);
        Assert.IsFalse(service.CanSendFileHandoff(sent.Task));
    }

    [TestMethod]
    public void ANewAttemptDoesNotReuseBindingsFromThePreviousAttempt()
    {
        var task=Create();service.BindManualTaskSession(task.TaskId,"b","selected-b",root);
        new RelayStore(service.Home).Write(s=>{var current=s.Task(task.TaskId);current.AttemptId=Guid.NewGuid().ToString();s.Save(current);return true;});
        Assert.IsNull(service.TaskSession(task.TaskId,"a"));Assert.IsNull(service.TaskSession(task.TaskId,"b"));
        Assert.HasCount(2,service.SessionBindings(task.TaskId));
    }

    [TestMethod]
    public void InitializeBackfillsLegacyIndependentReviewPurpose()
    {
        var home = Path.Combine(root, "legacy-review"); Directory.CreateDirectory(home);
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(home, "relay.db"), Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE task_session_bindings (task_id TEXT NOT NULL, attempt_id TEXT NOT NULL,
                    turn INTEGER NOT NULL, agent_id TEXT NOT NULL, json TEXT NOT NULL,
                    PRIMARY KEY(task_id,attempt_id,turn,agent_id));
                INSERT INTO task_session_bindings VALUES('t','a',0,'r','{"task_id":"t","attempt_id":"a","turn":0,"agent_id":"r","host":"fixture","session_id":null,"binding":"new","visibility":"unknown","independent_review":true}');
                """;
            command.ExecuteNonQuery();
        }
        var legacy = new RelayService(home); legacy.Initialize();
        legacy.Initialize();
        using var check = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(home, "relay.db"), Pooling = false }.ToString());
        check.Open();
        using var query = check.CreateCommand();
        query.CommandText = "SELECT purpose FROM task_session_bindings WHERE agent_id='r'";
        Assert.AreEqual("review", query.ExecuteScalar());
    }
}
