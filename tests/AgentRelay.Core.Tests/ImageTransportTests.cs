using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using AgentRelay.Core;

namespace AgentRelay.Core.Tests;

[TestClass]
public sealed class ImageTransportTests
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=");
    private string root = null!;
    private RelayService service = null!;

    [TestInitialize]
    public void Setup()
    {
        root = Path.Combine(Path.GetTempPath(), "AgentRelay.Tests", Guid.NewGuid().ToString());
        service = new RelayService(Path.Combine(root, "relay")); service.Initialize();
        service.RegisterManual("agent-a", "fixture", "甲");
        service.RegisterManual("agent-b", "fixture", "乙");
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(root, true);

    private TaskImage Image(string fileName, byte[] data)
    {
        var path = Path.Combine(root, fileName);
        File.WriteAllBytes(path, data);
        return new(Guid.NewGuid().ToString(), fileName, "image/png", path);
    }

    private const string AcpAgent = """
        param([string]$Mode, [string]$Log)
        $inputStream=[Console]::In; $outputStream=[Console]::Out
        function Send($value) { $outputStream.WriteLine(($value | ConvertTo-Json -Compress -Depth 20)); $outputStream.Flush() }
        while ($null -ne ($line=$inputStream.ReadLine())) {
          [IO.File]::AppendAllText($Log, $line + "`n", [Text.UTF8Encoding]::new($false))
          $frame=$line | ConvertFrom-Json
          switch ($frame.method) {
            'initialize' {
              $caps=@{loadSession=$true}
              if ($Mode -ne 'missing') { $caps.promptCapabilities=@{image=($Mode -eq 'supported')} }
              Send @{jsonrpc='2.0';id=$frame.id;result=@{protocolVersion=1;agentCapabilities=$caps}}
            }
            'session/new' { Send @{jsonrpc='2.0';id=$frame.id;result=@{sessionId='session-fixture'}} }
            'session/load' { Send @{jsonrpc='2.0';id=$frame.id;result=@{}} }
            'session/prompt' {
              Send @{jsonrpc='2.0';method='session/update';params=@{sessionId=$frame.params.sessionId;update=@{sessionUpdate='agent_message_chunk';content=@{type='text';text='ok'}}}}
              Send @{jsonrpc='2.0';id=$frame.id;result=@{stopReason='end_turn'}}
              exit 0
            }
          }
        }
        """;

    private CliCommand AcpCommand(string mode, string log)
    {
        var script = Path.Combine(root, "image-acp.ps1");
        File.WriteAllText(script, AcpAgent);
        return new("pwsh", ["-NoLogo", "-NoProfile", "-NonInteractive", "-File", script, mode, log], null, root);
    }

    private static JsonNode[] Frames(string path) => File.ReadAllLines(path).Select(line => JsonNode.Parse(line)!).ToArray();

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AcpSendsTheOriginalBytesAndTextOnNewAndResumedSessions(bool resume)
    {
        var first = Image("一.png", Png);
        var secondBytes = Png.Concat(new byte[] { 1, 2, 3 }).ToArray();
        var second = Image("two.png", secondBytes);
        var log = Path.Combine(root, "acp-wire.jsonl");
        var result = await CliRecipes.InvokeAsync(AcpCommand("supported", log), "acp", "请检查两张图片", resume ? "existing" : null,
            root, TimeSpan.FromSeconds(20), CancellationToken.None, [first, second]);
        Assert.AreEqual(0, result.ExitCode, result.Error);
        var frames = Frames(log);
        CollectionAssert.AreEqual(resume ? new[] { "initialize", "session/load", "session/prompt" } :
            new[] { "initialize", "session/new", "session/prompt" }, frames.Select(frame => frame["method"]!.GetValue<string>()).ToArray());
        var content = frames[^1]["params"]!["prompt"]!.AsArray();
        Assert.AreEqual("请检查两张图片", content[0]!["text"]!.GetValue<string>());
        Assert.AreEqual("image", content[1]!["type"]!.GetValue<string>());
        Assert.AreEqual("image/png", content[1]!["mimeType"]!.GetValue<string>());
        CollectionAssert.AreEqual(Png, Convert.FromBase64String(content[1]!["data"]!.GetValue<string>()));
        CollectionAssert.AreEqual(secondBytes, Convert.FromBase64String(content[2]!["data"]!.GetValue<string>()));
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("unsupported")]
    public async Task AcpRejectsImagesBeforeCreatingOrLoadingASession(string mode)
    {
        var image = Image("fixture.png", Png);
        foreach (var session in new string?[] { null, "existing" })
        {
            var log = Path.Combine(root, Guid.NewGuid() + ".jsonl");
            var error = await Assert.ThrowsExactlyAsync<RelayException>(() => CliRecipes.InvokeAsync(AcpCommand(mode, log), "acp", "look", session,
                root, TimeSpan.FromSeconds(20), CancellationToken.None, [image]));
            Assert.AreEqual("cli_image_unsupported", error.Code);
            CollectionAssert.AreEqual(new[] { "initialize" }, Frames(log).Select(frame => frame["method"]!.GetValue<string>()).ToArray());
        }
    }

    [TestMethod]
    public async Task TextCliReceivesImagePathsAndExplicitInstructions()
    {
        var image = Image("图片.png", Png);
        var capture = Path.Combine(root, "prompt.txt");
        var command = TextCommand(capture);
        var result = await CliRecipes.InvokeAsync(command, "relay-json", "请分析这张图片", null,
            root, TimeSpan.FromSeconds(20), CancellationToken.None, [image]);
        Assert.AreEqual(0, result.ExitCode, result.Error);
        var prompt = File.ReadAllText(capture);
        StringAssert.Contains(prompt, "请分析这张图片");
        StringAssert.Contains(prompt, image.Path);
        StringAssert.Contains(prompt, "Open and inspect each image with your image-reading tool");
        Assert.IsFalse(prompt.Contains(Convert.ToBase64String(Png), StringComparison.Ordinal));
    }

    private static CliCommand TextCommand(string capture) => new("pwsh", ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command",
        "[Console]::InputEncoding=[Text.UTF8Encoding]::new($false);$p=[Console]::In.ReadToEnd();" +
        "[IO.File]::WriteAllText($env:RELAY_IMAGE_CAPTURE,$p,[Text.UTF8Encoding]::new($false));" +
        "[Console]::Out.Write('{\"session_id\":\"fixture-session\",\"reply\":\"ok\",\"accepted\":true}')"],
        "{prompt}", null, new() { ["RELAY_IMAGE_CAPTURE"] = capture });

    private CliRecipe Enable(CliCommand command, string format, CliCommand? resume = null)
    {
        var recipe = new CliRecipe(1, "image fixture", "fixture", command, format is "acp" or "dsh-desktop" ? command : resume,
            [], "fixture", "tests", "MIT", "", format);
        service.ImportCliRecipe("agent-b", RelayJson.Write(recipe));
        var extension = service.CliExtension("agent-b")!;
        var enabled = extension with { CreateEvidence = new("fixture", 0, "fixture-session", DateTimeOffset.UtcNow) };
        new RelayStore(service.Home).Write(store =>
        {
            store.Execute("UPDATE cli_extensions SET json=$json WHERE agent_id=$id", ("$json", RelayJson.Write(enabled)), ("$id", "agent-b"));
            return true;
        });
        return recipe;
    }

    private TaskRecord Handoff() => service.CreateCliHandoff(new("agent-a", "agent-b", "请分析图片", EnableDirection: true,
        Images: [new("原图.png", "image/png", Png)])).Task;

    [TestMethod]
    public async Task FileHandoffPassesAttachedImagesToTheTransport()
    {
        var capture = Path.Combine(root, "handoff.txt");
        Enable(TextCommand(capture), "relay-json");
        var task = Handoff();
        var sent = await service.SendFileHandoffAsync(task.TaskId, "自定义任务提示");
        Assert.AreEqual("submitted", sent.Task.CliFileStatus);
        var prompt = File.ReadAllText(capture);
        StringAssert.Contains(prompt, "自定义任务提示");
        StringAssert.Contains(prompt, task.Images![0].Path);
    }

    [TestMethod]
    public async Task CollaborationResendsTheImagesWhenTheAuthorRevisesInTheSameSession()
    {
        var capture = Path.Combine(root, "collaboration.txt");
        var create = TextCommand(capture);
        var resumeScript = Path.Combine(root, "resume-image.ps1");
        File.WriteAllText(resumeScript, """
            param([string]$Session)
            [Console]::InputEncoding=[Text.UTF8Encoding]::new($false)
            $prompt=[Console]::In.ReadToEnd()
            [IO.File]::WriteAllText($env:RELAY_IMAGE_CAPTURE,$prompt,[Text.UTF8Encoding]::new($false))
            @{session_id=$Session;reply='ok';accepted=$true}|ConvertTo-Json -Compress
            """);
        var resume = create with { Arguments = ["-NoLogo", "-NoProfile", "-NonInteractive", "-File", resumeScript, "{session_id}"] };
        Enable(create, "relay-json", resume);
        var task = service.CreateCliHandoff(new("agent-a", "agent-b", "请分析图片", EnableDirection: true,
            Collaboration: new CollaborationOptions("agent-b", "图片内容得到分析"),
            Images: [new("原图.png", "image/png", Png)])).Task;
        var first = await service.SendFileHandoffAsync(task.TaskId, "第一轮");
        Assert.AreEqual("submitted", first.Task.CliFileStatus);
        StringAssert.Contains(File.ReadAllText(capture), task.Images![0].Path);

        var reply = service.TurnReplyPath(first.Task);
        File.WriteAllText(reply, "待审核的分析");
        File.WriteAllText(service.ManualReceiptPath(first.Task), RelayJson.Write(new ManualReceipt(task.TaskId, task.AttemptId,
            "agent-b", first.Task.ManualTurn, "done", ResultFile: reply, Outcome: "submitted")));
        Assert.IsEmpty(service.ImportManualReceipts().Errors);
        var review = service.Status(task.TaskId).Task;
        Assert.AreEqual("agent-a", review.AwaitingAgentId);
        File.WriteAllText(service.ManualReceiptPath(review), RelayJson.Write(new ManualReceipt(task.TaskId, task.AttemptId,
            "agent-a", review.ManualTurn, "done", Outcome: "changes_requested", OpenItems: ["补充截图中的文字"])));
        Assert.IsEmpty(service.ImportManualReceipts().Errors);

        var revision = service.Status(task.TaskId).Task;
        Assert.AreEqual("agent-b", revision.AwaitingAgentId);
        var second = await service.SendFileHandoffAsync(task.TaskId, "返工轮");
        Assert.AreEqual("submitted", second.Task.CliFileStatus);
        var prompt = File.ReadAllText(capture);
        StringAssert.Contains(prompt, "返工轮");
        StringAssert.Contains(prompt, task.Images[0].Path);
    }

    [TestMethod]
    public async Task MissingImageIsRejectedBeforeClaimingOrStartingTheTransport()
    {
        var capture = Path.Combine(root, "handoff.txt");
        Enable(TextCommand(capture), "relay-json");
        var task = Handoff();
        File.Delete(task.Images![0].Path);
        var error = await Assert.ThrowsExactlyAsync<RelayException>(() => service.SendFileHandoffAsync(task.TaskId, "custom prompt"));
        Assert.AreEqual("task_image_unavailable", error.Code);
        Assert.IsNull(service.Status(task.TaskId).Task.CliFileStatus);
        Assert.IsFalse(File.Exists(capture));
    }

    [TestMethod]
    public async Task UnsupportedAcpIsRecordedAsRejectedInsteadOfUnknown()
    {
        var log = Path.Combine(root, "unsupported.jsonl");
        Enable(AcpCommand("unsupported", log), "acp");
        var task = Handoff();
        var result = await service.SendFileHandoffAsync(task.TaskId, "look");
        Assert.AreEqual("failed", result.Task.CliFileStatus);
        StringAssert.Contains(result.Task.CliFileError!, "未声明支持图片");
        CollectionAssert.AreEqual(new[] { "initialize" }, Frames(log).Select(frame => frame["method"]!.GetValue<string>()).ToArray());
    }

    [TestMethod]
    public async Task DshDesktopSendsTheImagesAndTextInItsPromptContent()
    {
        var image = Image("截图.png", Png);
        var handler = new DshHandler(false);
        using var host = Host(handler);
        var images = await ImageDelivery.PrepareAsync([image], CancellationToken.None);
        var result = await DshDesktopClient.RunTurnAsync(host, "查看截图", "session-fixture", root, CancellationToken.None, images);
        StringAssert.Contains(result.Error, "prompt accepted; observation failed");
        var content = handler.Prompt!["payload"]!["args"]!["request"]!["content"]!.AsArray();
        Assert.AreEqual("image", content[0]!["type"]!.GetValue<string>());
        Assert.AreEqual("image/png", content[0]!["mediaType"]!.GetValue<string>());
        Assert.AreEqual("截图.png", content[0]!["name"]!.GetValue<string>());
        CollectionAssert.AreEqual(Png, Convert.FromBase64String(content[0]!["data"]!.GetValue<string>()));
        Assert.AreEqual("查看截图", content[1]!["text"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task DshModelWithoutImageSupportIsAConfirmedRejection()
    {
        var image = Image("fixture.png", Png);
        var handler = new DshHandler(true);
        using var host = Host(handler);
        var images = await ImageDelivery.PrepareAsync([image], CancellationToken.None);
        var error = await Assert.ThrowsExactlyAsync<RelayException>(() => DshDesktopClient.RunTurnAsync(host, "look", "session-fixture", root,
            CancellationToken.None, images));
        Assert.AreEqual("cli_image_unsupported", error.Code);
        CollectionAssert.AreEqual(new[] { "/api/session/projections", "/api/session/prompt" }, handler.Paths.ToArray());
    }

    private DshDesktopClient.Host Host(HttpMessageHandler handler)
    {
        var credential = Path.Combine(root, "fixture-credential.yaml"); File.WriteAllText(credential, "fixture");
        return new(new(5004, 5014, DateTimeOffset.UtcNow), RandomNumberGenerator.GetBytes(32), credential, handler);
    }

    private sealed class DshHandler(bool unsupported) : HttpMessageHandler
    {
        public JsonNode? Prompt { get; private set; }
        public List<string> Paths { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            var frame = JsonNode.Parse(await request.Content!.ReadAsStringAsync(token))!;
            if (Paths[^1] == "/api/session/prompt") Prompt = frame;
            if (Paths.Count > 2) return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            var response = Paths.Count == 1 ? "{\"result\":{\"ok\":true,\"value\":{\"asOfSeq\":0}}}" : unsupported
                ? "{\"result\":{\"ok\":false,\"error\":{\"code\":\"session/attachment-invalid\",\"message\":\"unsupported image\",\"details\":{\"reason\":\"MODEL_DOES_NOT_SUPPORT_IMAGES\"}}}}"
                : "{\"result\":{\"ok\":true,\"value\":{\"accepted\":true}}}";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }
}
