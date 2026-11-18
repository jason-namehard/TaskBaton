using System.Text.Json;
using TaskBaton.Core;

namespace TaskBaton.Core.Tests;

[TestClass]
public sealed class HandoffAssistantTests
{
    private static readonly HandoffAgentOption[] Agents = [new("agent-a", "我的助手"), new("agent-b", "任意名称")];

    private static string Reply(string from = "agent-a", string to = "agent-b", string content = "请读取项目说明并回复。") =>
        RelayJson.Write(new { relay_setup_version = 1, context = "handoff", fields = new Dictionary<string, string>
        { ["From"] = from, ["To"] = to, ["Content"] = content } });

    [TestMethod]
    public void ReplyFillsKnownNodesAndPreservesMessageWithoutEnablingDelivery()
    {
        const string content = "  原始内容\n保留换行与 C:\\工作目录\\说明.md  ";
        var draft = HandoffAssistant.Parse(Reply(content: content), Agents);
        Assert.AreEqual("agent-a", draft.From);
        Assert.AreEqual("agent-b", draft.To);
        Assert.AreEqual(content, draft.Content);
        Assert.IsFalse(draft.Automatic);
    }

    [TestMethod]
    public void CompleteJsonFenceIsAccepted()
    {
        var draft = HandoffAssistant.Parse("```json\n" + Reply() + "\n```", Agents);
        Assert.AreEqual("agent-b", draft.To);
    }

    [TestMethod]
    [DataRow("agent-x", "agent-b", "message", "handoff_unknown_agent")]
    [DataRow("agent-a", "agent-x", "message", "handoff_unknown_agent")]
    [DataRow("agent-a", "agent-a", "message", "handoff_same_agent")]
    [DataRow("agent-a", "agent-b", " \n", "handoff_empty_content")]
    public void ReplyRejectsUnusableHandoff(string from, string to, string content, string code)
    {
        Assert.AreEqual(code, Assert.ThrowsExactly<RelayException>(() => HandoffAssistant.Parse(Reply(from, to, content), Agents)).Code);
    }

    [TestMethod]
    public void ReplyRequiresAllThreeFields()
    {
        const string reply = """{"relay_setup_version":1,"context":"handoff","fields":{"From":"agent-a","To":"agent-b"}}""";
        Assert.AreEqual("handoff_missing_fields", Assert.ThrowsExactly<RelayException>(() => HandoffAssistant.Parse(reply, Agents)).Code);
    }

    [TestMethod]
    public void ReplyCannotSetDeliveryModeOrOtherUnrequestedFields()
    {
        var reply = Reply().Replace("\"From\":", "\"Automatic\":\"true\",\"From\":", StringComparison.Ordinal);
        Assert.AreEqual("setup_unknown_field", Assert.ThrowsExactly<RelayException>(() => HandoffAssistant.Parse(reply, Agents)).Code);
    }

    [TestMethod]
    [DataRow("en")]
    [DataRow("zh-CN")]
    [DataRow("zh-TW")]
    [DataRow("ja")]
    [DataRow("ko")]
    [DataRow("es")]
    public void PromptKeepsUserNamesAndProducesParseableTemplate(string language)
    {
        var prompt = HandoffAssistant.CreatePrompt(Agents, "agent-a", "agent-b", "当前交接", language);
        StringAssert.Contains(prompt, "我的助手");
        StringAssert.Contains(prompt, "任意名称");
        var template = prompt[(prompt.IndexOf("\nJSON:\n", StringComparison.Ordinal) + "\nJSON:\n".Length)..];
        var parsed = HandoffAssistant.Parse(template, Agents);
        Assert.AreEqual("当前交接", parsed.Content);
        Assert.IsFalse(parsed.Automatic);
    }

    [TestMethod]
    public void AgentRegistrationPromptDoesNotAskForInternalIds()
    {
        var prompt = SetupAssistant.CreatePrompt("agents", new Dictionary<string, string>
        {
            ["AgentName"] = "中文名", ["AgentHost"] = "", ["AgentConfig"] = ""
        }, "zh-CN");
        Assert.IsFalse(prompt.Contains("AgentId", StringComparison.Ordinal));
        StringAssert.Contains(prompt, "中文名");
        using var template = JsonDocument.Parse(prompt[prompt.IndexOf('{')..]);
        Assert.AreEqual("中文名", template.RootElement.GetProperty("fields").GetProperty("AgentName").GetString());
    }
}
