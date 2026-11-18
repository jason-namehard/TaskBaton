namespace TaskBaton.Protocol.Tests;

[TestClass]
public sealed class L4SessionBindingTests
{
    private static SessionBinding Binding(string id = "01a16428-7b00-7aa0-bb35-83d2018018cf") => new()
    {
        Host = "codex",
        SessionId = id,
        Cwd = "C:/work/project",
    };

    [TestMethod]
    public void Exact_session_that_exists_passes()
    {
        var store = new FakeSessionStore();
        store.Add("codex", "01a16428-7b00-7aa0-bb35-83d2018018cf");

        var result = SessionBindingVerifier.VerifyBeforeResume(Binding(), store);

        Assert.AreEqual(0, result.ExitCode, Describe(result));
        Assert.IsFalse(result.HasErrors);
    }

    [TestMethod]
    public void Latest_and_case_variants_are_rejected()
    {
        foreach (var id in new[] { "latest", "LATEST", "recent", "Recent", "*", " ", " id " })
        {
            var result = SessionBindingVerifier.VerifyBeforeResume(Binding(id));
            Assert.AreEqual(3, result.ExitCode, id);
            Assert.IsTrue(result.HasCode(ProtocolDiagnosticCodes.HandoffSessionExactRequired), id);
        }
    }

    [TestMethod]
    public void Missing_exact_session_is_a_state_conflict()
    {
        var store = new FakeSessionStore();

        var result = SessionBindingVerifier.VerifyBeforeResume(Binding(), store);

        Assert.AreEqual(5, result.ExitCode, Describe(result));
        Assert.IsTrue(result.HasCode(ProtocolDiagnosticCodes.HandoffSessionNotFound), Describe(result));
    }

    [TestMethod]
    public void Resume_that_returns_a_different_id_is_rejected()
    {
        var binding = Binding();
        var resumer = new FakeSessionResumer { SessionId = "some-other-session" };

        var result = SessionBindingVerifier.VerifyResume(binding, resumer);

        Assert.AreEqual(5, result.ExitCode, Describe(result));
        Assert.IsTrue(result.HasCode(ProtocolDiagnosticCodes.HandoffSessionMismatch), Describe(result));
    }

    [TestMethod]
    public void Resume_that_returns_the_exact_id_passes()
    {
        var binding = Binding();
        var resumer = new FakeSessionResumer();

        var result = SessionBindingVerifier.VerifyResume(binding, resumer);

        Assert.AreEqual(0, result.ExitCode, Describe(result));
        Assert.IsFalse(result.HasErrors);
    }

    [TestMethod]
    public void Failed_resume_is_not_treated_as_success()
    {
        var binding = Binding();
        var resumer = new FakeSessionResumer { Resumed = false };

        var result = SessionBindingVerifier.VerifyResume(binding, resumer);

        Assert.AreEqual(5, result.ExitCode, Describe(result));
        Assert.IsTrue(result.HasCode(ProtocolDiagnosticCodes.HandoffSessionNotFound), Describe(result));
    }

    private static string Describe(VerificationResult result) =>
        $"exit={result.ExitCode}; " + string.Join(
            " | ",
            result.Diagnostics.Select(d => $"{d.Code}@{d.Field}: {d.Message}"));

    private sealed class FakeSessionStore : ISessionStore
    {
        private readonly Dictionary<string, string> sessions = new(StringComparer.Ordinal);

        public void Add(string host, string sessionId) => sessions[$"{host}\n{sessionId}"] = sessionId;

        public SessionLookupResult LookupExact(string host, string sessionId) =>
            sessions.TryGetValue($"{host}\n{sessionId}", out var actual)
                ? new SessionLookupResult(true, actual)
                : new SessionLookupResult(false, null);
    }

    private sealed class FakeSessionResumer : ISessionResumer
    {
        public bool Resumed { get; init; } = true;

        public string? SessionId { get; init; }

        public SessionResumeResult Resume(SessionBinding binding) =>
            new(Resumed, SessionId ?? binding.SessionId);
    }
}
