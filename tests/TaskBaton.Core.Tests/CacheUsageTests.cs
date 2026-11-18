using TaskBaton.Core;
using Microsoft.Data.Sqlite;

namespace TaskBaton.Core.Tests;

[TestClass]
public sealed class CacheUsageTests
{
    private string root = null!;
    private RelayService service = null!;

    [TestInitialize]
    public void Setup()
    {
        root = Path.Combine(Path.GetTempPath(), "TaskBaton.Tests", Guid.NewGuid().ToString());
        service = new RelayService(Path.Combine(root, "relay")); service.Initialize();
        service.RegisterManual("agent-z", "ZCode Desktop", "Zcode");
        service.RegisterManual("agent-h", "Hermes Agent", "Hermes");
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(root, true);
    }

    [TestMethod]
    public void ZcodeUsageShowsEachRequestOfTheSessionInOrder()
    {
        var zcode = Path.Combine(root, "zcode"); Directory.CreateDirectory(Path.Combine(zcode, "cli", "db"));
        using (var connection = new SqliteConnection("Data Source=" + Path.Combine(zcode, "cli", "db", "db.sqlite") + ";Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE model_usage (session_id TEXT, status TEXT, started_at INTEGER, model_id TEXT, input_tokens INTEGER, cache_read_input_tokens INTEGER);
                INSERT INTO model_usage VALUES ('s1','completed',1793082000000,'m',20000,19800),('s1','completed',1793082060000,'m',21000,0),
                  ('s1','error',1793082070000,'m',5,0),('s2','completed',1793082080000,'m',9,9);
                """;
            command.ExecuteNonQuery();
        }
        service.ConfigureSessionDiscovery("agent-z", "zcode", zcode);
        var report = service.CacheUsage("agent-z", "s1");
        CollectionAssert.AreEqual(new long[] { 19800, 0 }, report.Requests.Select(r => r.CacheReadTokens).ToArray());
        StringAssert.Contains(report.Findings.Single(), "可能是两次请求的前缀不同");
        StringAssert.Contains(report.Findings.Single(), "不能只凭命中率确定原因");
    }

    [TestMethod]
    public void HermesUsageIsReadFromTheGatewayLogForThatSessionOnly()
    {
        var hermes = Path.Combine(root, "hermes"); Directory.CreateDirectory(Path.Combine(hermes, "logs"));
        File.WriteAllLines(Path.Combine(hermes, "logs", "agent.log.1"),
            ["2026-10-27 10:00:00,001 INFO [local-a] agent.conversation_loop: API call #1: model=m provider=custom in=1000 out=5 total=1005 latency=1s id=x"]);
        File.WriteAllLines(Path.Combine(hermes, "logs", "agent.log"),
        [
            "2026-10-27 11:00:00,001 INFO [local-a] agent.conversation_loop: API call #2: model=m provider=custom in=28858 out=376 total=29234 latency=3.6s cache=28288/28858 (98%) id=y",
            "2026-10-27 11:00:01,001 INFO [local-b] agent.conversation_loop: API call #1: model=m provider=custom in=50 out=1 total=51 latency=1s cache=0/50 (0%) id=z",
            "2026-10-27 11:00:02,001 INFO [local-a] gateway: unrelated line"
        ]);
        service.ConfigureSessionDiscovery("agent-h", "hermes", hermes);
        var report = service.CacheUsage("agent-h", "local-a");
        CollectionAssert.AreEqual(new long[] { 1000, 28858 }, report.Requests.Select(r => r.InputTokens).ToArray());
        Assert.AreEqual(28288, report.Requests[1].CacheReadTokens);
    }

    [TestMethod]
    public void ALowHitAfterALongGapIsNotBlamedOnThePrefix()
    {
        var start = DateTimeOffset.Parse("2026-10-27T10:00:00Z");
        var findings = RelayService.CacheFindings([new(start, "m", 20000, 19000), new(start.AddHours(2), "m", 21000, 0)]);
        StringAssert.Contains(findings.Single(), "可能是缓存过期");
        Assert.IsEmpty(RelayService.CacheFindings([new(start, "m", 20000, 19000), new(start.AddMinutes(1), "m", 21000, 20000)]));
    }

    [TestMethod]
    public void SetupPromptHandsTheConfiguringAgentTheCacheParitySkill()
    {
        var skill = service.Skill("relay-cache-parity");
        StringAssert.Contains(File.ReadAllText(skill.Path), "name: relay-cache-parity");
        StringAssert.Contains(service.AgentSetupPrompt(), skill.Path);
        var installed = service.InstallSkill("relay-cache-parity", Path.Combine(root, "agent-skills"));
        Assert.AreEqual(File.ReadAllText(skill.Path), File.ReadAllText(installed.Path));
        Assert.ThrowsExactly<RelayException>(() => service.Skill("missing"));
    }
}
