namespace AgentRelay.Protocol.Tests;

[TestClass]
public sealed class L5OwnershipTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void Owned_expired_evidence_is_deletable()
    {
        using var temp = new TempDirectory();
        var path = temp.File("evidence.txt");
        var store = new FakeOwnershipStore().Created(path);
        var candidate = new ArtifactCleanupCandidate(path, "evidence", Now.AddMinutes(-1));

        var plan = Planner(store).Plan(temp.Path, [candidate], Now);

        Assert.HasCount(1, plan.Deletable);
        Assert.IsEmpty(plan.Retained);
        Assert.IsTrue(plan.CanDelete(path));
    }

    [TestMethod]
    public void Registered_artifact_is_owned()
    {
        using var temp = new TempDirectory();
        var path = temp.File("registered.txt");
        var store = new FakeOwnershipStore().Registered(path);
        var candidate = new ArtifactCleanupCandidate(path, "evidence", Now.AddMinutes(-1));

        var plan = Planner(store).Plan(temp.Path, [candidate], Now);

        Assert.HasCount(1, plan.Deletable);
    }

    [TestMethod]
    public void Outside_managed_root_is_retained()
    {
        using var temp = new TempDirectory();
        var path = Path.GetFullPath(Path.Combine(temp.Path, "..", "outside.txt"));
        var store = new FakeOwnershipStore().Created(path);
        var candidate = new ArtifactCleanupCandidate(path, "evidence", Now.AddMinutes(-1));

        var plan = Planner(store).Plan(temp.Path, [candidate], Now);

        Assert.IsEmpty(plan.Deletable);
        Assert.AreEqual(ProtocolDiagnosticCodes.CleanupOutsideRoot, plan.Retained.Single().Code);
    }

    [TestMethod]
    public void Unproven_ownership_is_retained()
    {
        using var temp = new TempDirectory();
        var path = temp.File("unowned.txt");
        var candidate = new ArtifactCleanupCandidate(path, "evidence", Now.AddMinutes(-1));

        var plan = Planner(new FakeOwnershipStore()).Plan(temp.Path, [candidate], Now);

        Assert.IsEmpty(plan.Deletable);
        Assert.AreEqual(ProtocolDiagnosticCodes.CleanupNotOwned, plan.Retained.Single().Code);
    }

    [TestMethod]
    public void Active_reference_is_retained()
    {
        using var temp = new TempDirectory();
        var path = temp.File("active.txt");
        var store = new FakeOwnershipStore().Created(path).Active(path);
        var candidate = new ArtifactCleanupCandidate(path, "evidence", Now.AddMinutes(-1));

        var plan = Planner(store).Plan(temp.Path, [candidate], Now);

        Assert.IsEmpty(plan.Deletable);
        Assert.AreEqual(ProtocolDiagnosticCodes.CleanupActiveReference, plan.Retained.Single().Code);
    }

    [TestMethod]
    public void Protected_user_file_is_retained()
    {
        using var temp = new TempDirectory();
        var path = temp.File("user-source.cs");
        var store = new FakeOwnershipStore().Created(path).Protected(path);
        var candidate = new ArtifactCleanupCandidate(path, "input", Now.AddMinutes(-1));

        var plan = Planner(store).Plan(temp.Path, [candidate], Now);

        Assert.IsEmpty(plan.Deletable);
        Assert.AreEqual(ProtocolDiagnosticCodes.CleanupProtected, plan.Retained.Single().Code);
    }

    [TestMethod]
    public void Result_and_control_files_are_never_ttl_deleted()
    {
        using var temp = new TempDirectory();
        var resultPath = temp.File("result.md");
        var controlPath = temp.File("relay.db");
        var store = new FakeOwnershipStore().Created(resultPath).Created(controlPath);

        var plan = Planner(store).Plan(
            temp.Path,
            [
                new ArtifactCleanupCandidate(resultPath, "result", Now.AddMinutes(-1)),
                new ArtifactCleanupCandidate(controlPath, "evidence", Now.AddMinutes(-1), IsControlFile: true),
            ],
            Now);

        Assert.IsEmpty(plan.Deletable);
        Assert.HasCount(2, plan.Retained);
        Assert.IsTrue(plan.Retained.All(decision => decision.Code == ProtocolDiagnosticCodes.CleanupProtected));
    }

    [TestMethod]
    public void Unexpired_artifact_is_retained()
    {
        using var temp = new TempDirectory();
        var path = temp.File("not-expired.txt");
        var store = new FakeOwnershipStore().Created(path);
        var candidate = new ArtifactCleanupCandidate(path, "evidence", Now.AddMinutes(1));

        var plan = Planner(store).Plan(temp.Path, [candidate], Now);

        Assert.IsEmpty(plan.Deletable);
        Assert.AreEqual(ProtocolDiagnosticCodes.CleanupNotExpired, plan.Retained.Single().Code);
    }

    private static ArtifactCleanupPlanner Planner(IArtifactOwnershipStore store) =>
        new() { OwnershipStore = store };

    private sealed class FakeOwnershipStore : IArtifactOwnershipStore
    {
        private readonly HashSet<string> created = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> registered = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> active = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> protectedFiles = new(StringComparer.OrdinalIgnoreCase);

        public FakeOwnershipStore Created(string path)
        {
            created.Add(path);
            return this;
        }

        public FakeOwnershipStore Registered(string path)
        {
            registered.Add(path);
            return this;
        }

        public FakeOwnershipStore Active(string path)
        {
            active.Add(path);
            return this;
        }

        public FakeOwnershipStore Protected(string path)
        {
            protectedFiles.Add(path);
            return this;
        }

        public bool IsCreatedByRelay(string path) => created.Contains(path);

        public bool IsRegisteredByHandoff(string path) => registered.Contains(path);

        public bool HasActiveTaskReference(string path) => active.Contains(path);

        public bool IsProtectedUserFile(string path) => protectedFiles.Contains(path);
    }
}
