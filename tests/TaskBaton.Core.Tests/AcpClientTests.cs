using TaskBaton.Core;

namespace TaskBaton.Core.Tests;

[TestClass]
public sealed class AcpClientTests
{
    private string root = null!;
    private RelayService service = null!;

    // A local ACP agent: newline-delimited JSON-RPC on stdio, asks permission before writing, replays history on session/load.
    private const string Agent = """
        param([string]$Mode)
        $in=[Console]::In;$out=[Console]::Out
        function Send($o){$out.WriteLine(($o|ConvertTo-Json -Compress -Depth 10));$out.Flush()}
        while($null -ne ($line=$in.ReadLine())){
          $m=$line|ConvertFrom-Json
          switch($m.method){
            'initialize'{Send @{jsonrpc='2.0';id=$m.id;result=@{protocolVersion=1;agentCapabilities=@{loadSession=$true}}}}
            'session/new'{
              if($Mode -eq 'auth'){Send @{jsonrpc='2.0';id=$m.id;error=@{code=-32000;message='Authentication required'}}}
              else{Send @{jsonrpc='2.0';id=$m.id;result=@{sessionId='acp-session-1'}}}}
            'session/load'{
              Send @{jsonrpc='2.0';method='session/update';params=@{sessionId=$m.params.sessionId;update=@{sessionUpdate='agent_message_chunk';content=@{type='text';text='OLD HISTORY'}}}}
              Send @{jsonrpc='2.0';id=$m.id;result=@{}}}
            'session/prompt'{
              $text=$m.params.prompt[0].text
              $marker=[regex]::Match($text,'RELAY-[A-Z0-9]+').Value
              $file=[regex]::Match($text,'Create the file (.+?) containing').Groups[1].Value
              Write-Host 'log line that is not JSON'
              Send @{jsonrpc='2.0';id='perm-1';method='session/request_permission';params=@{sessionId=$m.params.sessionId;toolCall=@{toolCallId='c1';title='Write probe file'};options=@(@{optionId='no';name='Reject';kind='reject_once'},@{optionId='yes';name='Allow once';kind='allow_once'})}}
              $answer=$in.ReadLine()|ConvertFrom-Json
              if($file -and $answer.result.outcome.optionId -eq 'yes'){[IO.File]::WriteAllText($file,$marker)}
              Send @{jsonrpc='2.0';method='session/update';params=@{sessionId=$m.params.sessionId;update=@{sessionUpdate='agent_message_chunk';content=@{type='text';text=$marker}}}}
              Send @{jsonrpc='2.0';id=$m.id;result=@{stopReason='end_turn'}}
              exit 0}
          }
        }
        """;

    [TestInitialize]
    public void Setup()
    {
        root = Path.Combine(Path.GetTempPath(), "TaskBaton.Tests", Guid.NewGuid().ToString());
        service = new RelayService(Path.Combine(root, "relay")); service.Initialize();
        service.RegisterManual("agent-b", "fixture", "乙");
        File.WriteAllText(Path.Combine(root, "acp-agent.ps1"), Agent);
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(root, true);

    private CliRecipe Recipe(string mode = "ok")
    {
        var command = new CliCommand("pwsh", ["-NoLogo", "-NoProfile", "-NonInteractive", "-File", Path.Combine(root, "acp-agent.ps1"), mode], null, root);
        return new(1, "ACP fixture", "fixture", command, command, [], "fixture", "tests", "MIT", "No model calls.", "acp");
    }

    [TestMethod]
    public async Task AcpAgentCreatesWritesWithGrantedPermissionAndResumesTheSameSession()
    {
        service.ImportCliRecipe("agent-b", RelayJson.Write(Recipe()));
        var created = await service.RunCliProbeAsync("agent-b", "create");
        Assert.IsTrue(created.MachinePassed, created.Error + created.Diagnostic);
        Assert.IsTrue(created.FileWritten);
        Assert.AreEqual("acp-session-1", created.SessionId);
        service.ConfirmCliProbe("agent-b", created.ProbeId, true);
        var resumed = await service.RunCliProbeAsync("agent-b", "resume");
        // The replayed history must not leak into the reply of the new turn.
        Assert.IsTrue(resumed.MachinePassed, resumed.Error + resumed.Diagnostic);
        Assert.AreEqual("acp-session-1", resumed.SessionId);
    }

    [TestMethod]
    public async Task AcpAuthenticationErrorBlocksAutomaticDelivery()
    {
        service.ImportCliRecipe("agent-b", RelayJson.Write(Recipe("auth")));
        var probe = await service.RunCliProbeAsync("agent-b", "create");
        Assert.IsFalse(probe.MachinePassed);
        Assert.AreEqual("auth_required", service.CliExtension("agent-b")!.Block!.Kind);
    }

    [TestMethod]
    public void AcpRecipeStartsTheServerOnlyAndCannotTemplateThePrompt()
    {
        var recipe = Recipe();
        Assert.ThrowsExactly<RelayException>(() => service.ImportCliRecipe("agent-b",
            RelayJson.Write(recipe with { Create = recipe.Create with { Arguments = [.. recipe.Create.Arguments, "{prompt}"] } })));
        Assert.ThrowsExactly<RelayException>(() => service.ImportCliRecipe("agent-b",
            RelayJson.Write(recipe with { Resume = recipe.Create with { Input = "{session_id}" } })));
        Assert.IsNull(service.CliExtension("agent-b"));
    }
}
