using System.Security.Cryptography;
using System.Text;
using System.Net;
using System.Text.Json.Nodes;
using TaskBaton.Core;

namespace TaskBaton.Core.Tests;

[TestClass]
public sealed class DshDesktopTests
{
    private string root = null!;

    [TestInitialize]
    public void Setup() => root = Path.Combine(Path.GetTempPath(), "TaskBaton.Tests", Guid.NewGuid().ToString());

    [TestCleanup]
    public void Cleanup() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private string Credentials(string secret)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, ".credentials.yaml");
        File.WriteAllText(path, $"""
            version: 1
            refs:
              SOME_API_KEY: not-read
            records:
              client-connection/browser-session:
                kind: grant
                payload:
                  version: 1
                  secret: {secret}
              other/record:
                kind: grant
                payload:
                  secret: not-this-one
            """);
        return path;
    }

    [TestMethod]
    public async Task ConnectionCheckReportsAStoppedDesktopWithoutStartingItOrPrompting()
    {
        var check = await DshDesktopClient.CheckConnectionAsync("relay-test-" + Guid.NewGuid().ToString("N") + ".exe", root);
        Assert.AreEqual("host.desktop.connection", check.Id);
        Assert.AreEqual("fail", check.Status);
        StringAssert.Contains(check.Message, "桌面没有运行");
        Assert.IsFalse(Directory.Exists(root));
    }

    [TestMethod]
    public void OnlyTheBrowserSessionSecretIsReadFromTheCredentialFile()
    {
        var secret = RandomNumberGenerator.GetBytes(32);
        CollectionAssert.AreEqual(secret, DshDesktopClient.BrowserSessionSecret(Credentials(Base64Url(secret))));
        Assert.IsNotNull(DshDesktopClient.SecretProblem(root.Replace("TaskBaton.Tests", "missing")));
        Credentials("too-short");
        Assert.IsNotNull(DshDesktopClient.SecretProblem(root));
    }

    [TestMethod]
    public void TheCookieIsTheDesktopsOwnV1BrowserCookieForThatPort()
    {
        var secret = RandomNumberGenerator.GetBytes(32);
        var now = DateTimeOffset.FromUnixTimeMilliseconds(1793082855509);
        var cookie = DshDesktopClient.Cookie(5643, secret, now);
        var (name, value) = (cookie[..cookie.IndexOf('=')], cookie[(cookie.IndexOf('=') + 1)..]);
        Assert.AreEqual("dsh-auth-" + Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes("127.0.0.1:5643"))), name);
        var parts = value.Split('.');
        Assert.AreEqual("v1", parts[0]);
        var body = Encoding.UTF8.GetString(Convert.FromBase64String(parts[1].Replace('-', '+').Replace('_', '/') + new string('=', (4 - parts[1].Length % 4) % 4)));
        // Field order matters: DSH signs the base64url text of exactly this JSON. Backdating allows for clock differences.
        Assert.AreEqual("""{"version":1,"authority":"127.0.0.1:5643","issuedAt":1793082555509,"expiresAt":1793086155509}""", body);
        Assert.AreEqual(Base64Url(HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(parts[1]))), parts[2]);
        var payload = JsonNode.Parse(body)!;
        Assert.IsLessThan(now.ToUnixTimeMilliseconds(), payload["issuedAt"]!.GetValue<long>());
        Assert.IsGreaterThan(now.ToUnixTimeMilliseconds(), payload["expiresAt"]!.GetValue<long>());
    }

    [TestMethod]
    public async Task PollingAuthenticationFailureAfterAcceptedPromptNeverBlocksBridgeOrAllowsAutomaticResend()
    {
        var path = Credentials(Base64Url(RandomNumberGenerator.GetBytes(32)));
        var handler = new AcceptedThenUnauthorizedHandler();
        using var host = new DshDesktopClient.Host(new(5004, 5014, DateTimeOffset.UtcNow),
            DshDesktopClient.BrowserSessionSecret(path), path, handler);
        var result = await DshDesktopClient.RunTurnAsync(host, "fixture", "session-fixture", root, CancellationToken.None);
        var command = new CliCommand("dsh", [], null, root);
        var recipe = new CliRecipe(1, "fixture", "DSH", command, command, [], "", "", "", "", "dsh-desktop");
        var failure = CliFailureDiagnostics.Classify(result, recipe);
        Assert.AreEqual("delivery_unconfirmed", failure.Kind);
        Assert.IsFalse(failure.Blocking);
        StringAssert.Contains(failure.Message, "不要重复发送");
        StringAssert.Contains(failure.Message, "401");
        CollectionAssert.AreEqual(new[] { "/api/session/projections", "/api/session/prompt", "/api/session/projections" }, handler.Paths);
    }

    private sealed class AcceptedThenUnauthorizedHandler : HttpMessageHandler
    {
        internal List<string> Paths { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            if (Paths.Count == 3) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            var response = Paths.Count == 1 ? "{\"result\":{\"ok\":true,\"value\":{\"asOfSeq\":0}}}" : "{\"result\":{\"ok\":true,\"value\":{}}}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response) });
        }
    }

    [TestMethod]
    public void DesktopRecipesNameTheAppAndPassPromptsOverItsApi()
    {
        var service = new RelayService(Path.Combine(root, "relay")); service.Initialize();
        service.RegisterManual("dsh", "DeepSeek Harness", "DSH");
        var command = new CliCommand(@"D:\DeepSeekHarness\DeepSeek Harness.exe", [], null, Path.Combine(root, "workspace"), new() { ["DSH_HOME"] = root });
        var recipe = new CliRecipe(1, "DSH desktop", "DeepSeek Harness", command, command, [], "", "", "", "", "dsh-desktop");
        service.ImportCliRecipe("dsh", RelayJson.Write(recipe));
        Assert.AreEqual("desktop", service.Bridge("dsh").Kind);
        Assert.AreEqual("dsh", service.DiscoverySource("dsh")!.Provider);
        Assert.AreEqual("fail", service.CliHealth("dsh").Single(c => c.Id == "host.desktop.auth").Status);
        Assert.ThrowsExactly<RelayException>(() => service.ImportCliRecipe("dsh",
            RelayJson.Write(recipe with { Create = command with { Arguments = ["{prompt}"] } })));
    }

    [TestMethod]
    public async Task DiscoverySkipsRejectedAndWrongListenersAndKeepsOnlyTheAuthenticatedDesktop()
    {
        var path = Credentials(Base64Url(RandomNumberGenerator.GetBytes(32)));
        var rejected = new DesktopHandler(HttpStatusCode.Unauthorized, "dsh web authentication required");
        var wrong = new DesktopHandler(HttpStatusCode.OK, "{\"result\":{\"ok\":true,\"value\":{\"unrelated\":true}}}");
        var desktop = new DesktopHandler(HttpStatusCode.OK, "{\"result\":{\"ok\":true,\"value\":{\"items\":[]}}}");
        var handlers = new Dictionary<int, DesktopHandler> { [5001] = rejected, [5002] = wrong, [5003] = desktop };
        var listeners = handlers.Keys.Select(port => new DshDesktopClient.DesktopListener(port, port + 10, DateTimeOffset.UtcNow)).ToArray();
        var selected = await DshDesktopClient.FindHost(listeners, DshDesktopClient.BrowserSessionSecret(path), path,
            listener => handlers[listener.Port], CancellationToken.None);
        Assert.IsNotNull(selected.Host);
        Assert.AreEqual(5003, selected.Host.Port);
        Assert.HasCount(2, selected.Failures);
        StringAssert.Contains(selected.Failures[0], "401");
        StringAssert.Contains(selected.Failures[0], "pid=5011");
        StringAssert.Contains(selected.Failures[0], "port=5001");
        StringAssert.Contains(selected.Failures[0], "credentials_modified=");
        Assert.IsTrue(rejected.Disposed); Assert.IsTrue(wrong.Disposed); Assert.IsFalse(desktop.Disposed);
        foreach (var handler in handlers.Values)
        {
            CollectionAssert.AreEqual(new[] { "/api/session/list" }, handler.Paths);
            Assert.IsTrue(handler.Authenticated);
            Assert.IsTrue(handler.EmptyListRequest);
        }
        selected.Host.Dispose(); Assert.IsTrue(desktop.Disposed);
    }

    [TestMethod]
    public async Task DiscoveryRejectsEveryUnauthorizedListenerWithoutSubmittingWork()
    {
        var path = Credentials(Base64Url(RandomNumberGenerator.GetBytes(32)));
        var handler = new DesktopHandler(HttpStatusCode.Unauthorized, "unauthorized");
        var selected = await DshDesktopClient.FindHost([new(5004, 5014, DateTimeOffset.UtcNow)],
            DshDesktopClient.BrowserSessionSecret(path), path, _ => handler, CancellationToken.None);
        Assert.IsNull(selected.Host); Assert.HasCount(1, selected.Failures); Assert.IsTrue(handler.Disposed);
        CollectionAssert.AreEqual(new[] { "/api/session/list" }, handler.Paths);
        Assert.IsFalse(selected.Failures[0].Contains("Cookie", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class DesktopHandler(HttpStatusCode status, string response) : HttpMessageHandler
    {
        internal bool Disposed { get; private set; }
        internal bool Authenticated { get; private set; }
        internal bool EmptyListRequest { get; private set; }
        internal List<string> Paths { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            Authenticated = request.Headers.Contains("Cookie");
            var document = JsonNode.Parse(await request.Content!.ReadAsStringAsync(token))!;
            EmptyListRequest = document["method"]!.GetValue<string>() == "session/list" &&
                document["payload"]!["args"]!["_request"] is JsonObject args && args.Count == 0;
            return new HttpResponseMessage(status) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }

        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
}
