using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AgentRelay.Core;
using ZstdSharp;

namespace AgentRelay.Core.Tests;

[TestClass]
public sealed class DshProbeCleanupTests
{
    private string root = null!;
    private string dsh = null!;
    private RelayService relay = null!;
    private const string Probe = "session-probe";
    private static readonly string Exclusive = "sha256:" + new string('a', 64);
    private static readonly string Shared = "sha256:" + new string('b', 64);

    [TestInitialize]
    public void Setup()
    {
        root = Path.Combine(Path.GetTempPath(), "AgentRelay.Tests", Guid.NewGuid().ToString("N"));
        dsh = Path.Combine(root, "fake-dsh");
        relay = new RelayService(Path.Combine(root, "relay"));relay.Initialize();
        relay.RegisterManual("a", "fixture", "A");relay.RegisterManual("b", "DSH", "B");
        var command = new CliCommand("relay-isolated-dsh-" + Guid.NewGuid().ToString("N") + ".exe", [], null, root,
            new() { ["DSH_HOME"] = dsh });
        relay.ImportCliRecipe("b", RelayJson.Write(new CliRecipe(1, "DSH fixture", "DSH", command, command, [], "fixture", "fixture", "MIT", "Isolated.", "dsh-desktop")));
        Session(Probe, [Exclusive, Shared]);Session("session-other", [Shared]);
        foreach (var id in new[] { Exclusive, Shared }) Write(Attachment(id), "fake owned image");
        Write(Path.Combine(dsh, "storages", "session_projcache", "sessions", Probe + ".json"), "{}");
        Write(Index, """
            {"unit":{"name":"workspace","version":2},"global":{"initialized":true,"workspaceIds":["w"],"archivedSessionIds":["PROBE","session-other"],"pinnedSessionIds":["session-other","PROBE"]},"tables":{"workspaces":{"w":{"path":"fixture","title":"Other workspace","sessionIds":["PROBE","session-other"],"extra":"preserve"}}}}
            """.Replace("PROBE", Probe));
        var current = relay.CliExtension("b")!;
        var probe = new CliProbe("probe-create", "create", "RELAY-AAAAAAAAAA", Probe, true, true, null, DateTimeOffset.UtcNow, FileWritten: true);
        Save(current with { CreateProbe = probe, ProbeArtifacts = [new(probe.ProbeId,"create",probe.Marker,Probe,new("dsh",dsh),true,root,command,true,OutputFormat:"dsh-desktop")] });
    }

    [TestCleanup]
    public void Cleanup() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    private string Index => Path.Combine(dsh, "storages", "workspace.json");
    private string Attachment(string id) => Path.Combine(dsh, "attachments", "v1", "objects", id.Substring(7, 2), id[7..]);
    private static void Write(string path, string text) { Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,text); }
    private void Save(CliExtensionRecord record) => new RelayStore(relay.Home).Write(s => s.Execute(
        "UPDATE cli_extensions SET json=$json WHERE agent_id='b'", ("$json", RelayJson.Write(record))));

    private string Session(string id, string[] images, string bucket="sessions", string? parent=null, bool reused=false)
    {
        var dir=Path.Combine(dsh,bucket,"project",id);Directory.CreateDirectory(dir);
        var header=JsonSerializer.Serialize(new {type="session",version=4,id,cwd=root,createdAt=1,isSeeded=false,delegationDepth=0,parentSession=parent});
        var markerPath=Path.Combine(relay.Home,".cli-probes","RELAY-AAAAAAAAAA.txt");
        var prompt="Create the file "+markerPath+" containing exactly RELAY-AAAAAAAAAA, then reply with exactly RELAY-AAAAAAAAAA. Do nothing else.";
        var content=images.Select(image=>(object)new {type="image",attachment=new{attachmentId=image}})
            .Prepend(new {type="text",text=prompt}).ToArray();
        var body=JsonSerializer.Serialize(new {type="user/message",seq=1,time=1,data=new{source=new{kind="user"},content}});
        if(reused)body+="\n"+JsonSerializer.Serialize(new {type="user/message",seq=2,time=2,data=new{source=new{kind="user"},content=new[]{new{type="text",text="Do real project work in this conversation."}}}});
        using var compressor=new Compressor();
        // Native DSH appends independently compressed frames for the header and event batches.
        File.WriteAllBytes(Path.Combine(dir,"session.v4.jsonl.zstd"),[
            ..compressor.Wrap(Encoding.UTF8.GetBytes(header+"\n")).ToArray(),
            ..compressor.Wrap(Encoding.UTF8.GetBytes(body+"\n")).ToArray()]);
        return dir;
    }

    [TestMethod]
    public async Task OfflineCleanupRemovesProbeAndExclusiveImagesButPreservesOtherSessionsAndSharedImages()
    {
        var other=Path.Combine(dsh,"sessions","project","session-other","session.v4.jsonl.zstd");
        var before=File.ReadAllBytes(other);
        var result=await relay.CleanupCliProbeAsync("b");
        Assert.IsTrue(result.Complete,RelayJson.Write(result));
        Assert.IsFalse(Directory.Exists(Path.Combine(dsh,"sessions","project",Probe)));
        Assert.IsFalse(File.Exists(Attachment(Exclusive)));Assert.IsTrue(File.Exists(Attachment(Shared)));
        Assert.IsTrue(result.Items.Any(i=>i.Status=="shared"));
        CollectionAssert.AreEqual(before,File.ReadAllBytes(other));
        var index=JsonNode.Parse(File.ReadAllText(Index))!;
        Assert.DoesNotContain(Probe,index.ToJsonString());
        Assert.AreEqual("preserve",index["tables"]!["workspaces"]!["w"]!["extra"]!.GetValue<string>());
        Assert.AreEqual("session-other",index["global"]!["archivedSessionIds"]![0]!.GetValue<string>());
        Assert.IsTrue(relay.CliExtension("b")!.CanCreate);Assert.IsTrue(relay.CliExtension("b")!.CreateProbe!.SessionCleaned);
        Assert.IsEmpty((await relay.CleanupCliProbeAsync("b")).Items);
    }

    [TestMethod]
    public async Task RunningDesktopIsPendingAndDoesNotModifyItsSessionIndexOrImages()
    {
        var current=relay.CliExtension("b")!;
        using var process=Process.GetCurrentProcess();
        var artifact=current.ProbeArtifacts!.Single();
        Save(current with {ProbeArtifacts=[artifact with {CreateCommand=artifact.CreateCommand! with {Executable=process.MainModule!.FileName}}]});
        var before=File.ReadAllText(Index);
        var result=await relay.CleanupCliProbeAsync("b");
        Assert.IsFalse(result.Complete);Assert.IsTrue(result.Items.Any(i=>i.Status=="pending"));
        Assert.AreEqual(before,File.ReadAllText(Index));Assert.IsTrue(File.Exists(Attachment(Exclusive)));
        Assert.IsFalse(relay.CliExtension("b")!.CreateProbe!.SessionCleaned);
    }

    [TestMethod]
    public async Task TaskBoundOrUnownedProbeIsNeverDeleted()
    {
        var request=Path.Combine(root,"request.md");Write(request,"fixture");
        var task=relay.CreateFileHandoff(new("a","b",request)).Task;relay.BindManualTaskSession(task.TaskId,"b",Probe);
        var result=await relay.CleanupCliProbeAsync("b");
        Assert.IsTrue(result.Items.Any(i=>i.Status=="in_use"));Assert.IsTrue(File.Exists(Attachment(Exclusive)));
        var current=relay.CliExtension("b")!;
        Save(current with {ProbeArtifacts=[current.ProbeArtifacts!.Single() with {OwnedSession=false}]});
        result=await relay.CleanupCliProbeAsync("b");Assert.IsTrue(result.Items.Any(i=>i.Status=="unverified"));
    }

    [TestMethod]
    public async Task UnreadableOtherLogBlocksCleanupInsteadOfTreatingItsAttachmentsAsUnreferenced()
    {
        var file=Path.Combine(dsh,"sessions","project","session-other","session.v4.jsonl.zstd");File.WriteAllText(file,"not a zstd log");
        var result=await relay.CleanupCliProbeAsync("b");
        Assert.IsFalse(result.Complete);Assert.IsTrue(result.Items.Any(i=>i.Status=="failed"));
        Assert.IsTrue(File.Exists(Attachment(Exclusive)));Assert.IsTrue(Directory.Exists(Path.Combine(dsh,"sessions","project",Probe)));
        Assert.HasCount(1,relay.CliExtension("b")!.ProbeArtifacts!);
    }

    [TestMethod]
    public async Task ArchivedAndTrashedSessionsAlsoProtectSharedAttachmentsAndParentRelationships()
    {
        Session("session-archived",[Exclusive],"sessions-archive");
        Session("session-trash",[],"sessions-trash",Probe);
        var result=await relay.CleanupCliProbeAsync("b");
        Assert.IsTrue(result.Items.Any(i=>i.Status=="in_use"));Assert.IsTrue(File.Exists(Attachment(Exclusive)));
        Directory.Delete(Path.Combine(dsh,"sessions-trash","project","session-trash"),true);
        result=await relay.CleanupCliProbeAsync("b");
        Assert.IsTrue(result.Complete,RelayJson.Write(result));Assert.IsTrue(File.Exists(Attachment(Exclusive)));
    }

    [TestMethod]
    public async Task LockedImageCanBeRetriedAfterItsSessionLogHasBeenDeleted()
    {
        if(!OperatingSystem.IsWindows())Assert.Inconclusive("Requires Windows file sharing.");
        using(var held=new FileStream(Attachment(Exclusive),FileMode.Open,FileAccess.Read,FileShare.Read))
        {
            var result=await relay.CleanupCliProbeAsync("b");Assert.IsFalse(result.Complete);
            Assert.IsFalse(Directory.Exists(Path.Combine(dsh,"sessions","project",Probe)));
            CollectionAssert.Contains(relay.CliExtension("b")!.ProbeArtifacts!.Single().DshAttachmentIds!,Exclusive);
        }
        var retry=await relay.CleanupCliProbeAsync("b");Assert.IsTrue(retry.Complete,RelayJson.Write(retry));
        Assert.IsFalse(File.Exists(Attachment(Exclusive)));Assert.IsEmpty(relay.CliExtension("b")!.ProbeArtifacts!);
    }

    [TestMethod]
    public async Task ReplacedSessionOrUnknownIndexFormatRetainsAllHostData()
    {
        var current=relay.CliExtension("b")!;
        Save(current with {ProbeArtifacts=[current.ProbeArtifacts!.Single() with {SessionDirectory=Path.Combine(root,"different")} ]});
        var result=await relay.CleanupCliProbeAsync("b");Assert.IsTrue(result.Items.Any(i=>i.Status=="unverified"));
        Save(current);File.WriteAllText(Index,"{\"unit\":{\"name\":\"workspace\",\"version\":999}}");
        result=await relay.CleanupCliProbeAsync("b");Assert.IsFalse(result.Complete);
        Assert.IsTrue(File.Exists(Attachment(Exclusive)));Assert.IsTrue(Directory.Exists(Path.Combine(dsh,"sessions","project",Probe)));
    }

    [TestMethod]
    public async Task ProbeConversationReusedInTheHostIsRetainedEvenWithoutARelayTaskBinding()
    {
        var directory=Session(Probe,[Exclusive,Shared],reused:true);
        var before=File.ReadAllText(Index);
        var result=await relay.CleanupCliProbeAsync("b");
        Assert.IsTrue(result.Items.Any(i=>i.Status=="unverified"));
        Assert.IsTrue(Directory.Exists(directory));Assert.IsTrue(File.Exists(Attachment(Exclusive)));
        Assert.AreEqual(before,File.ReadAllText(Index));
    }

    [TestMethod]
    public async Task MissingProbeLogStillAllowsExactIndexCleanupButCannotClaimItsImages()
    {
        Directory.Delete(Path.Combine(dsh,"sessions","project",Probe),true);
        var result=await relay.CleanupCliProbeAsync("b");
        Assert.IsFalse(result.Complete);
        Assert.IsTrue(result.Items.Any(i=>i.Kind=="index"&&i.Status=="synced"));
        Assert.IsTrue(result.Items.Any(i=>i.Kind=="file"&&i.Status=="unverified"));
        Assert.DoesNotContain(Probe,File.ReadAllText(Index));
        Assert.IsTrue(File.Exists(Attachment(Exclusive)));
    }
}
