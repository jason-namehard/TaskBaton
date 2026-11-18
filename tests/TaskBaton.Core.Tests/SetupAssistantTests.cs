using System.Text;
using System.Text.Json;
using TaskBaton.Core;

namespace TaskBaton.Core.Tests;

[TestClass]
public sealed class SetupAssistantTests
{
    private const string Response = """
        {"relay_setup_version":1,"context":"agents","fields":{"AgentId":"agent-a","AgentHost":"codex"}}
        """;
    private static readonly string[] AllowedFields = ["AgentId", "AgentHost", "AgentConfigPath"];

    [TestMethod]
    public void ParseReturnsOnlyProvidedRequestedFields()
    {
        var fields = SetupAssistant.Parse(Response, "agents", AllowedFields);
        Assert.HasCount(2, fields);
        Assert.AreEqual("agent-a", fields["AgentId"]);
        Assert.AreEqual("codex", fields["AgentHost"]);
        Assert.IsFalse(fields.ContainsKey("AgentConfigPath"));
    }

    [TestMethod]
    public void ParsePreservesQuotedPathsWhitespaceAndEmptyStrings()
    {
        const string path = "  \"C:\\Users\\某人\\Agent Config\\settings.json\"  ";
        var json = RelayJson.Write(new { relay_setup_version = 1, context = "agents", fields = new Dictionary<string, string>
        {
            ["AgentConfigPath"] = path, ["AgentId"] = ""
        } });
        var fields = SetupAssistant.Parse(json, "agents", AllowedFields);
        Assert.AreEqual(path, fields["AgentConfigPath"]);
        Assert.AreEqual("", fields["AgentId"]);
    }

    [TestMethod]
    [DataRow("\n")]
    [DataRow("\r\n")]
    public void ParseAcceptsOneCompleteJsonFence(string newline)
    {
        var fields = SetupAssistant.Parse($"  \n```json{newline}{Response}{newline}```\n ", "agents", AllowedFields);
        Assert.AreEqual("agent-a", fields["AgentId"]);
    }

    [TestMethod]
    [DataRow("```json\n{}", "setup_invalid_fence")]
    [DataRow("```\n{}\n```", "setup_invalid_fence")]
    [DataRow("```JSON\n{}\n```", "setup_invalid_fence")]
    [DataRow("```json {} ```", "setup_invalid_fence")]
    [DataRow("Here is the response: {}", "setup_invalid_json")]
    [DataRow("{} trailing prose", "setup_invalid_json")]
    [DataRow("{} {}", "setup_invalid_json")]
    [DataRow("```json\n{}\n```\n```json\n{}\n```", "setup_invalid_json")]
    [DataRow("{}\n```json\n{}\n```", "setup_invalid_json")]
    [DataRow("{", "setup_invalid_json")]
    [DataRow("", "setup_invalid_json")]
    [DataRow("[]", "setup_invalid_response")]
    [DataRow("{}", "setup_invalid_response")]
    public void ParseRejectsIncompleteOrMixedResponses(string response, string code) => AssertError(response, code);

    [TestMethod]
    [DataRow("{\"relay_setup_version\":1,\"context\":\"agents\",\"fields\":{},\"extra\":true}", "setup_unknown_property")]
    [DataRow("{\"relay_setup_version\":2,\"context\":\"agents\",\"fields\":{}}", "setup_invalid_version")]
    [DataRow("{\"relay_setup_version\":\"1\",\"context\":\"agents\",\"fields\":{}}", "setup_invalid_version")]
    [DataRow("{\"relay_setup_version\":1.5,\"context\":\"agents\",\"fields\":{}}", "setup_invalid_version")]
    [DataRow("{\"relay_setup_version\":1,\"context\":\"settings\",\"fields\":{}}", "setup_context_mismatch")]
    [DataRow("{\"relay_setup_version\":1,\"context\":null,\"fields\":{}}", "setup_context_mismatch")]
    [DataRow("{\"relay_setup_version\":1,\"context\":\"agents\",\"fields\":[]}", "setup_invalid_fields")]
    [DataRow("{\"relay_setup_version\":1,\"context\":\"agents\",\"fields\":{\"Command\":\"anything\"}}", "setup_unknown_field")]
    [DataRow("{\"relay_setup_version\":1,\"context\":\"agents\",\"fields\":{\"agentid\":\"agent-a\"}}", "setup_unknown_field")]
    [DataRow("{\"relay_setup_version\":1,\"context\":\"agents\",\"fields\":{\"AgentId\":null}}", "setup_invalid_field_value")]
    [DataRow("{\"relay_setup_version\":1,\"context\":\"agents\",\"fields\":{\"AgentId\":123}}", "setup_invalid_field_value")]
    [DataRow("{\"relay_setup_version\":1,\"context\":\"agents\",\"fields\":{\"AgentId\":{}}}", "setup_invalid_field_value")]
    [DataRow("{\"relay_setup_version\":1,\"context\":\"agents\",\"fields\":{},\"context\":\"agents\"}", "setup_duplicate_property")]
    [DataRow("{\"relay_setup_version\":1,\"context\":\"agents\",\"fields\":{\"AgentId\":\"one\",\"AgentId\":\"two\"}}", "setup_duplicate_property")]
    [DataRow("{\"relay_setup_version\":1,\"context\":\"agents\",\"fields\":{\"AgentId\":\"one\",\"Agent\\u0049d\":\"two\"}}", "setup_duplicate_property")]
    public void ParseRejectsProtocolViolations(string response, string code) => AssertError(response, code);

    [TestMethod]
    public void ResponseLimitCountsUtf8Bytes()
    {
        var response = Response.Replace("agent-a", new string('界', SetupAssistant.MaxResponseBytes / 3));
        Assert.IsLessThan(SetupAssistant.MaxResponseBytes, response.Length);
        AssertError(response, "setup_response_too_large");
    }

    [TestMethod]
    public void ResponseAtByteLimitIsAccepted()
    {
        var response = Response + new string(' ', SetupAssistant.MaxResponseBytes - Encoding.UTF8.GetByteCount(Response));
        Assert.AreEqual("agent-a", SetupAssistant.Parse(response, "agents", AllowedFields)["AgentId"]);
        AssertError(response + " ", "setup_response_too_large");
    }

    [TestMethod]
    public void ParsedCommandTextRemainsInertData()
    {
        var path = Path.Combine(Path.GetTempPath(), $"relay-setup-{Guid.NewGuid():N}");
        var command = $"Set-Content -LiteralPath '{path}' -Value written";
        var json = RelayJson.Write(new { relay_setup_version = 1, context = "agents", fields = new Dictionary<string, string> { ["AgentConfigPath"] = command } });
        var fields = SetupAssistant.Parse(json, "agents", AllowedFields);
        Assert.AreEqual(command, fields["AgentConfigPath"]);
        Assert.IsFalse(File.Exists(path));
    }

    [TestMethod]
    [DataRow("en", "Verify local configuration", "Do not change configuration", "authentication data", "not a login name", "not approval or permission")]
    [DataRow("zh-CN", "请只读核实", "不要修改配置", "身份验证信息", "不是登录名", "不构成任何操作的批准或授权")]
    [DataRow("zh-TW", "請唯讀查證", "不要修改設定", "認證資料", "不是登入名稱", "不構成任何操作的核准或授權")]
    [DataRow("ja", "読み取り専用", "設定の変更", "認証情報", "ログイン名やアカウント識別子ではありません", "操作の承認や許可にはなりません")]
    [DataRow("ko", "읽기 전용", "설정 변경", "인증 정보", "로그인 이름이나 계정 식별자가 아닙니다", "승인이나 권한도 부여하지 않습니다")]
    [DataRow("es", "solo lectura", "No cambies la configuración", "autenticación", "no un nombre de inicio de sesión", "No aprueba ni autoriza ninguna acción")]
    public void PromptLocalizesBodyAndKeepsStableProtocol(string language, string introduction, string noChanges, string credentials, string label, string noApproval)
    {
        const string path = "  C:\\某人\\Agent Config\\settings.json\n\"quoted\"  ";
        var prompt = SetupAssistant.CreatePrompt("agents", new Dictionary<string, string> { ["AgentId"] = "agent-a", ["AgentConfigPath"] = path }, language);
        StringAssert.Contains(prompt, introduction);
        StringAssert.Contains(prompt, noChanges);
        StringAssert.Contains(prompt, credentials);
        StringAssert.Contains(prompt, label);
        StringAssert.Contains(prompt, noApproval);
        StringAssert.Contains(prompt, "API");
        if (language != "en") Assert.IsFalse(prompt.Contains("Return exactly one JSON object", StringComparison.Ordinal));
        using var template = JsonDocument.Parse(prompt[prompt.IndexOf('{')..]);
        Assert.AreEqual(1, template.RootElement.GetProperty("relay_setup_version").GetInt32());
        Assert.AreEqual("agents", template.RootElement.GetProperty("context").GetString());
        Assert.AreEqual("agent-a", template.RootElement.GetProperty("fields").GetProperty("AgentId").GetString());
        Assert.AreEqual(path, template.RootElement.GetProperty("fields").GetProperty("AgentConfigPath").GetString());
    }

    [TestMethod]
    [DataRow("agents")]
    [DataRow("connections")]
    [DataRow("adapters")]
    [DataRow("migrations")]
    [DataRow("settings")]
    [DataRow("audit")]
    public void AllSetupContextsRoundTrip(string context)
    {
        var response = RelayJson.Write(new { relay_setup_version = 1, context, fields = new Dictionary<string, string>() });
        Assert.IsEmpty(SetupAssistant.Parse(response, context, []));
        StringAssert.Contains(SetupAssistant.CreatePrompt(context, new Dictionary<string, string>(), "en"), $"context = \"{context}\"");
    }

    [TestMethod]
    public void UnsupportedContextIsReportedForBothOperations()
    {
        Assert.AreEqual("setup_invalid_context", Assert.ThrowsExactly<RelayException>(() => SetupAssistant.Parse(Response, "unknown", AllowedFields)).Code);
        Assert.AreEqual("setup_invalid_context", Assert.ThrowsExactly<RelayException>(() => SetupAssistant.CreatePrompt("unknown", new Dictionary<string, string>(), "en")).Code);
    }

    private static void AssertError(string response, string code) =>
        Assert.AreEqual(code, Assert.ThrowsExactly<RelayException>(() => SetupAssistant.Parse(response, "agents", AllowedFields)).Code);
}
