using System.Text.Json;
using AgentRelay.Core;
using Microsoft.Data.Sqlite;

namespace AgentRelay.Core.Tests;

[TestClass]
[DoNotParallelize]
public sealed class CliHandoffCreationTests
{
    private string root = null!;
    private RelayService service = null!;

    [TestInitialize]
    public void Setup()
    {
        root=Path.Combine(Path.GetTempPath(),"AgentRelay.Tests",Guid.NewGuid().ToString());
        service=new RelayService(Path.Combine(root,"relay"));service.Initialize();
        service.RegisterManual("agent-a","fixture","甲");service.RegisterManual("agent-b","fixture","乙");
        var script=Path.Combine(root,"fake-cli.ps1");
        File.WriteAllText(script,"""
            param([string]$Log,[string]$Session,[string]$Created,[string]$Prompt)
            [Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
            $call=@{session=$Session;cwd=(Get-Location).Path;prompt=$Prompt}|ConvertTo-Json -Compress
            [IO.File]::AppendAllText($Log,$call+[Environment]::NewLine)
            $actual=if($Session -eq 'create'){$Created}else{$Session}
            @{session_id=$actual;reply='Request accepted';accepted=$true}|ConvertTo-Json -Compress
            """);
        foreach(var id in new[]{"agent-a","agent-b"})
        {
            CliCommand Command(string session)=>new("pwsh",["-NoLogo","-NoProfile","-NonInteractive","-File",script,
                Log(id),session,"created-"+id,"{prompt}"],null,root);
            service.ImportCliRecipe(id,RelayJson.Write(new CliRecipe(1,"Local fixture","fixture",Command("create"),
                Command("{session_id}"),[],"fixture","local tests","MIT","No model calls.")));
            ConfigureStore(id,id=="agent-a"?"source-a":"selected-b", "created-"+id);
        }
    }

    [TestCleanup] public void Cleanup()=>Directory.Delete(root,true);
    private string Log(string agent)=>Path.Combine(root,agent+".log");

    private void ConfigureStore(string agent,params string[] sessionIds)
    {
        var folder=Path.Combine(root,agent+"-sessions");Directory.CreateDirectory(folder);
        using var connection=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=Path.Combine(folder,"state.db"),Pooling=false}.ToString());
        connection.Open();
        using var command=connection.CreateCommand();
        command.CommandText="CREATE TABLE sessions(id TEXT,title TEXT,cwd TEXT,profile_name TEXT,last_activity_at REAL,model TEXT,source TEXT,hidden INTEGER,archived INTEGER)";
        command.ExecuteNonQuery();
        foreach(var session in sessionIds.Append("latest-unrelated"))
        {
            using var insert=connection.CreateCommand();
            insert.CommandText="INSERT INTO sessions VALUES($id,'Exact session',$cwd,'existing-profile',$time,'existing-model','desktop',0,0)";
            insert.Parameters.AddWithValue("$id",session);insert.Parameters.AddWithValue("$cwd",root);
            insert.Parameters.AddWithValue("$time",session=="latest-unrelated"?9999:1000);insert.ExecuteNonQuery();
        }
        service.ConfigureSessionDiscovery(agent,"hermes",folder);
    }

    private CliHandoffRequest Request(bool review=false,string? target="selected-b")=>
        new("agent-a","agent-b","检查这份要求，并执行必要修改。","source-a",target,true,root,root,
            review?new CollaborationOptions("agent-b","要求中的每一项都已落实"):null);

    [TestMethod]
    public void InvalidCollaborationCannotLeaveTasksFilesOrPermissionsBehind()
    {
        foreach(var request in new[]{Request() with {TurnTimeoutMinutes=0},
            Request() with {Collaboration=new("unregistered")},
            Request() with {Collaboration=new("agent-b",MaxReviewRounds:-1)}})
        {
            Assert.ThrowsExactly<RelayException>(()=>service.CreateCliHandoff(request));
            Assert.IsEmpty(service.Tasks());
            Assert.IsEmpty(service.ConnectionPermissions());
            Assert.IsFalse(Directory.Exists(Path.Combine(service.Home,".cli-drafts")));
        }
    }

    [TestMethod]
    public void AutoCriteriaWithoutSourceConversationWaitsForManualInitiatorAndThenRoutesToRecipient()
    {
        var request=Request() with {OriginSessionId=null,OriginCwd=null,
            Collaboration=new("agent-b",MaxReviewRounds:0,GenerateCriteria:true,ReviewerFinal:true)};
        var task=service.CreateCliHandoff(request).Task;
        Assert.AreEqual("agent-a",task.AwaitingAgentId);Assert.AreEqual("criteria",task.Collaboration!.Phase);
        Assert.IsFalse(service.CanSendFileHandoff(task));
        var receipt=new ManualReceipt(task.TaskId,task.AttemptId,"agent-a",0,"done",Outcome:"criteria_ready",AcceptanceCriteria:"The requested changes pass their tests.");
        File.WriteAllText(service.ManualReceiptPath(task),RelayJson.Write(receipt));
        Assert.IsEmpty(service.ImportManualReceipts().Errors);
        task=service.Status(task.TaskId).Task;
        Assert.AreEqual("agent-b",task.AwaitingAgentId);Assert.AreEqual("implement",task.Collaboration!.Phase);
        Assert.IsTrue(service.CanSendFileHandoff(task));
    }

    private async Task<(int Code,JsonElement Json)> Invoke(params string[] arguments)
    {
        var original=Console.Out;using var output=new StringWriter();Console.SetOut(output);
        try
        {
            var code=await RelayCli.RunAsync(["--home",service.Home,..arguments]);
            using var document=JsonDocument.Parse(output.ToString());
            return(code,document.RootElement.Clone());
        }
        finally{Console.SetOut(original);}
    }

    private string Manifest(CliHandoffRequest request)
    {
        var path=Path.Combine(root,"request-"+Guid.NewGuid().ToString("N")+".json");
        File.WriteAllText(path,RelayJson.Write(request));return path;
    }

    private void FinishStep(TaskRecord task,string outcome)
    {
        var file=Path.Combine(root,"step-"+task.ManualTurn+".md");
        File.WriteAllText(file,"Concrete output for step "+task.ManualTurn);
        var receipt=new ManualReceipt(task.TaskId,task.AttemptId,task.AwaitingAgentId!,task.ManualTurn,"done",null,
            outcome=="submitted"?file:null,null,outcome,Deliverable:outcome=="accepted"?"项目中的 README 已更新":null);
        File.WriteAllText(service.ManualReceiptPath(task),RelayJson.Write(receipt));
        Assert.IsEmpty(service.ImportManualReceipts().Errors);
    }

    [TestMethod]
    public void PromptCreationStoresWorkAndExactBindingsWithoutInvokingTheCli()
    {
        var request=Request(true);var task=service.CreateCliHandoff(request).Task;
        Assert.AreEqual(request.Prompt,File.ReadAllText(task.RequestPath));
        Assert.AreEqual("cli",task.Mode);Assert.AreEqual("cli:agent-b",task.AdapterId);
        Assert.AreEqual("source-a",service.TaskSession(task.TaskId,"agent-a")!.SessionId);
        Assert.AreEqual("selected-b",service.TaskSession(task.TaskId,"agent-b")!.SessionId);
        Assert.IsNull(task.HandoffPlan);
        Assert.AreEqual("agent-b",task.Collaboration!.AuthorId);Assert.AreEqual("agent-a",task.Collaboration.ReviewerId);
        Assert.AreEqual("implement",task.Collaboration.Phase);
        Assert.HasCount(2,service.ConnectionPermissions().Where(p=>p.Decision=="allow").ToArray());
        Assert.IsNull(task.CliFileTurn);Assert.IsFalse(File.Exists(Log("agent-a")));Assert.IsFalse(File.Exists(Log("agent-b")));
        Assert.IsEmpty(Directory.GetFiles(Path.Combine(service.Home,".cli-drafts")));
    }

    [TestMethod]
    public async Task CopyableImagePromptPublishesItsContextWithoutSending()
    {
        var image = new ImageInput("diagram.png", "image/png", [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0]);
        var task = service.CreateCliHandoff(Request() with { Images = [image] }).Task;
        var result = await Invoke("prompt", "show", "--task", task.TaskId);
        Assert.AreEqual(0, result.Code);
        var context = Path.Combine(Path.GetDirectoryName(service.ManualReceiptPath(task))!, "context.json");
        StringAssert.Contains(result.Json.GetProperty("data").GetProperty("text").GetString()!, context);
        using var document = JsonDocument.Parse(File.ReadAllText(context));
        Assert.AreEqual(task.Images![0].Path, document.RootElement.GetProperty("images")[0].GetProperty("path").GetString());
        Assert.IsFalse(File.Exists(Log("agent-b")));
    }

    [TestMethod]
    public void CreationRequiresAnEnabledDirectionAndExactSessionInput()
    {
        Assert.ThrowsExactly<RelayException>(()=>service.CreateCliHandoff(Request() with{EnableDirection=false}));
        Assert.ThrowsExactly<RelayException>(()=>service.CreateCliHandoff(Request(true) with{EnableDirection=false}));
        Assert.ThrowsExactly<RelayException>(()=>service.CreateCliHandoff(Request() with{Prompt=" "}));
        Assert.ThrowsExactly<RelayException>(()=>service.CreateCliHandoff(Request() with{OriginSessionId=" "}));
        Assert.ThrowsExactly<RelayException>(()=>service.CreateCliHandoff(Request() with{TargetSessionId=" "}));
        Assert.IsEmpty(service.Tasks());Assert.IsEmpty(service.ConnectionPermissions());
        service.SetPermission("agent-a","agent-b","allow");
        var created=service.CreateCliHandoff(Request() with{EnableDirection=false}).Task;
        Assert.AreEqual("selected-b",service.TaskSession(created.TaskId,"agent-b")!.SessionId);
        Assert.IsFalse(File.Exists(Log("agent-b")));
    }

    [TestMethod]
    public void CollaborationWithoutASourceConversationReturnsToTheSenderManually()
    {
        var task=service.CreateCliHandoff(Request(true) with{OriginSessionId=null}).Task;
        Assert.IsNotNull(task.Collaboration);
        Assert.AreEqual("agent-a",service.ConnectionPermissions().Single(p=>p.Decision=="allow").From);
        FinishStep(task,"submitted");
        var review=service.Status(task.TaskId);
        Assert.AreEqual("agent-a",review.Task.AwaitingAgentId);
        Assert.IsFalse(service.CanSendFileHandoff(review.Task));
        Assert.AreEqual("copy_prompt",service.FileHandoffProgress(review.Task).Delivery);
    }

    [TestMethod]
    public async Task NewConversationUsesTheExplicitProjectFolder()
    {
        var cwd=Path.Combine(root,"new-project");Directory.CreateDirectory(cwd);
        var task=service.CreateCliHandoff(Request(target:null) with{TargetCwd=cwd}).Task;
        var sent=await service.SendFileHandoffAsync(task.TaskId,service.FileHandoffPrompt(task));
        Assert.AreEqual("submitted",sent.Task.CliFileStatus);
        var call=JsonDocument.Parse(File.ReadAllLines(Log("agent-b")).Single());
        using(call)Assert.AreEqual(cwd,call.RootElement.GetProperty("cwd").GetString());
        Assert.AreEqual(cwd,service.TaskSession(task.TaskId,"agent-b")!.Cwd);
    }

    [TestMethod]
    public async Task CliCreateWithoutSendIsReachableAndDoesNotLaunchTheTarget()
    {
        var result=await Invoke("handoff","cli-create","--manifest",Manifest(Request()));
        Assert.AreEqual(0,result.Code);
        var task=RelayJson.Read<TaskRecord>(result.Json.GetProperty("data").GetProperty("task").GetRawText());
        Assert.AreEqual("cli",task.Mode);Assert.HasCount(1,service.Tasks());
        Assert.IsFalse(File.Exists(Log("agent-b")));
        var unapproved=await Invoke("handoff","send","--task",task.TaskId);
        Assert.AreEqual("approval_required",unapproved.Json.GetProperty("error").GetProperty("code").GetString());
        Assert.IsFalse(File.Exists(Log("agent-b")));
    }

    [TestMethod]
    public async Task CliCreateSendRequiresApprovalBeforeCreatingOrCallingAnything()
    {
        var result=await Invoke("handoff","cli-create","--manifest",Manifest(Request()),"--send");
        Assert.AreEqual("approval_required",result.Json.GetProperty("error").GetProperty("code").GetString());
        Assert.IsEmpty(service.Tasks());Assert.IsEmpty(service.ConnectionPermissions());
        Assert.IsFalse(File.Exists(Log("agent-b")));
    }

    [TestMethod]
    public async Task ApprovedCliCommandsReachTheSelectedSessionsThroughReviewAndAcceptance()
    {
        var created=await Invoke("handoff","cli-create","--manifest",Manifest(Request(true)),"--send","--approve");
        Assert.AreEqual(0,created.Code);
        var task=RelayJson.Read<TaskRecord>(created.Json.GetProperty("data").GetProperty("task").GetRawText());
        Assert.AreEqual("selected-b",task.CliFileSessionId);
        using(var call=JsonDocument.Parse(File.ReadAllLines(Log("agent-b")).Single()))
        {
            Assert.AreEqual("selected-b",call.RootElement.GetProperty("session").GetString());
            Assert.AreEqual(root,call.RootElement.GetProperty("cwd").GetString());
            Assert.Contains("context.json",call.RootElement.GetProperty("prompt").GetString()!);
        }
        var contextPath=Path.Combine(Path.GetDirectoryName(service.ManualReceiptPath(task))!,"context.json");
        using(var context=JsonDocument.Parse(File.ReadAllText(contextPath)))
        {
            var collaboration=context.RootElement.GetProperty("collaboration");
            Assert.AreEqual("author",collaboration.GetProperty("role").GetString());
            Assert.AreEqual("implement",collaboration.GetProperty("phase").GetString());
            Assert.AreEqual("submitted",context.RootElement.GetProperty("receipt_template").GetProperty("outcome").GetString());
            Assert.DoesNotContain("4 completion turns",context.RootElement.GetProperty("instructions").GetString()!);
        }
        FinishStep(task,"submitted");task=service.Status(task.TaskId).Task;
        var review=await Invoke("handoff","send","--task",task.TaskId,"--approve");
        Assert.AreEqual(0,review.Code);
        using(var call=JsonDocument.Parse(File.ReadAllLines(Log("agent-a")).Single()))
            Assert.AreEqual("source-a",call.RootElement.GetProperty("session").GetString());
        FinishStep(task,"approved");task=service.Status(task.TaskId).Task;
        Assert.AreEqual(TaskState.AwaitingReply,task.State);Assert.AreEqual("accept",task.Collaboration!.Phase);
        var accept=await Invoke("handoff","send","--task",task.TaskId,"--approve");
        Assert.AreEqual(0,accept.Code);Assert.HasCount(2,File.ReadAllLines(Log("agent-b")));
        FinishStep(task,"accepted");
        var finished=service.Status(task.TaskId).Task;
        Assert.AreEqual(TaskState.Succeeded,finished.State);
        Assert.AreEqual("delivered",service.FileHandoffProgress(finished).Completion);
    }

    [TestMethod]
    public async Task CliConfiguresTheLoopReportsProgressAndGuardsUnattendedDelivery()
    {
        var task=service.CreateCliHandoff(Request()).Task;
        var configured=await Invoke("handoff","configure","--task",task.TaskId,"--author","agent-b","--criteria","测试通过","--max-rounds","3","--auto-deliver");
        Assert.AreEqual(0,configured.Code);
        task=service.Status(task.TaskId).Task;
        Assert.AreEqual(3,task.Collaboration!.MaxReviewRounds);Assert.IsTrue(task.AutoDeliver);
        var progress=await Invoke("handoff","progress","--task",task.TaskId);
        Assert.AreEqual("implement",progress.Json.GetProperty("data").GetProperty("phase").GetString());
        Assert.AreEqual("agent-b",progress.Json.GetProperty("data").GetProperty("current_agent_id").GetString());
        var unapproved=await Invoke("handoff","sync","--deliver");
        Assert.AreEqual("approval_required",unapproved.Json.GetProperty("error").GetProperty("code").GetString());
        var imported=await Invoke("handoff","sync");
        Assert.AreEqual(0,imported.Code);Assert.IsFalse(File.Exists(Log("agent-b")));
        var status=await Invoke("task","status","--task",task.TaskId);
        Assert.AreEqual("cli_ready",status.Json.GetProperty("data").GetProperty("progress").GetProperty("delivery").GetString());
    }

    [TestMethod]
    public async Task NewTargetSessionIdIsRegisteredThroughThePublicCliCreationPath()
    {
        var created=await Invoke("handoff","cli-create","--manifest",Manifest(Request(target:null)),"--send","--approve");
        Assert.AreEqual(0,created.Code);
        var task=RelayJson.Read<TaskRecord>(created.Json.GetProperty("data").GetProperty("task").GetRawText());
        Assert.AreEqual("created-agent-b",service.TaskSession(task.TaskId,"agent-b")!.SessionId);
        Assert.AreEqual("created",service.TaskSession(task.TaskId,"agent-b")!.Binding);
        using var call=JsonDocument.Parse(File.ReadAllLines(Log("agent-b")).Single());
        Assert.AreEqual("create",call.RootElement.GetProperty("session").GetString());
    }
}
