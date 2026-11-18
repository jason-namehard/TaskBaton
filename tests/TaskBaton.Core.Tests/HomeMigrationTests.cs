using Microsoft.VisualStudio.TestTools.UnitTesting;
using TaskBaton.Core;

namespace TaskBaton.Core.Tests;

[TestClass]
[DoNotParallelize]
public sealed class HomeMigrationTests
{
    private string root = null!;
    private string localApplicationData = null!;

    [TestInitialize]
    public void Setup()
    {
        root = Path.Combine(Path.GetTempPath(), "TaskBaton.Tests", Guid.NewGuid().ToString());
        localApplicationData = Path.Combine(root, "localappdata");
        Directory.CreateDirectory(localApplicationData);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    [TestMethod]
    public void LegacyDefaultHomeIsCopiedVerifiedAndKeptForRollback()
    {
        var oldHome = TaskBatonHome.LegacyHomeFor(localApplicationData);
        var newHome = TaskBatonHome.DefaultHomeFor(localApplicationData);
        var seed = Path.Combine(root, "seed");
        var service = new RelayService(seed);
        service.Initialize();
        service.RegisterManual("agent-a", "manual", "A");
        service.RegisterManual("agent-b", "manual", "B");
        service.DispatchAsync(new("agent-a", "agent-b", "Review this", "manual", "manual", "legacy-task"))
            .GetAwaiter().GetResult();
        CopySeedHome(seed, oldHome);
        Directory.Delete(seed, true);

        var oldLocation = Path.Combine(oldHome, TaskBatonHome.LegacyLocationFileName);
        File.WriteAllText(oldLocation, RelayJson.Write(new { home = oldHome }));
        var oldDatabase = Path.Combine(oldHome, TaskBatonHome.LegacyDatabaseFileName);
        Assert.IsTrue(File.Exists(oldDatabase));
        var oldDatabaseBytes = File.ReadAllBytes(oldDatabase);

        var workspaceKey = Guid.NewGuid().ToString("N");
        var instructionFile = Path.Combine(root, "AGENTS.md");
        var instructionPath = Path.Combine(localApplicationData, "AgentRelay.Guidance", workspaceKey, "agent-a.md");
        Directory.CreateDirectory(Path.GetDirectoryName(instructionPath)!);
        File.WriteAllText(instructionPath, "# Old generated rule");
        var start = $"<!-- agent-relay:{workspaceKey}:agent-a:start -->";
        var end = $"<!-- agent-relay:{workspaceKey}:agent-a:end -->";
        File.WriteAllText(instructionFile, "# User instructions\n" + start + "\nRead the rule.\n" + end + "\n");
        Directory.CreateDirectory(Path.Combine(oldHome, "guidance-backups"));
        File.WriteAllText(Path.Combine(oldHome, "guidance-backups", "agent-a.md"), "# User instructions\n");
        File.WriteAllText(Path.Combine(oldHome, "agent-guidance.json"), RelayJson.Write(new
        {
            WorkspaceKey = workspaceKey,
            RelayHome = oldHome,
            Registrations = new[]
            {
                new
                {
                    AgentId = "agent-a",
                    InstructionFile = instructionFile,
                    InstructionPath = instructionPath,
                    BackupRelativePath = Path.Combine("guidance-backups", "agent-a.md"),
                    InstalledAt = DateTimeOffset.UtcNow
                }
            },
            Pending = Array.Empty<object>()
        }));

        var resolved = WithHomeEnvironment(null, null, () => TaskBatonHome.ResolveForRoot(localApplicationData));
        Assert.AreEqual(newHome, resolved);
        Assert.IsTrue(Directory.Exists(newHome));
        Assert.IsTrue(File.Exists(Path.Combine(newHome, TaskBatonHome.NewDatabaseFileName)));
        Assert.IsFalse(File.Exists(Path.Combine(newHome, TaskBatonHome.LegacyDatabaseFileName)));

        var migrated = new RelayService(newHome);
        Assert.HasCount(2, migrated.Agents());
        Assert.HasCount(1, migrated.Tasks());
        Assert.HasCount(1, migrated.GuidanceRegistrations());
        migrated.RefreshGuidance();
        StringAssert.Contains(File.ReadAllText(instructionFile), start);

        var newLocation = TaskBatonHome.DefaultLocationFileFor(localApplicationData);
        Assert.AreEqual(newHome, TaskBatonHome.ReadLocation(newLocation));

        // The old home stays byte-for-byte intact for rollback.
        CollectionAssert.AreEqual(oldDatabaseBytes, File.ReadAllBytes(oldDatabase));
        Assert.IsTrue(Directory.Exists(oldHome));
        Assert.IsTrue(File.Exists(oldLocation));
        Assert.IsTrue(File.Exists(Path.Combine(oldHome, "agent-guidance.json")));
        Assert.IsTrue(File.Exists(instructionPath));

        migrated.RemoveGuidance("agent-a");
        Assert.IsFalse(File.Exists(instructionPath));
        Assert.IsFalse(File.ReadAllText(instructionFile).Contains("agent-relay:", StringComparison.Ordinal));
        Assert.IsTrue(File.Exists(oldLocation));
    }

    [TestMethod]
    public void FailedVerificationFallsBackToLegacyHomeWithoutDeletingIt()
    {
        var oldHome = TaskBatonHome.LegacyHomeFor(localApplicationData);
        var newHome = TaskBatonHome.DefaultHomeFor(localApplicationData);
        Directory.CreateDirectory(oldHome);
        // A file named like the database makes it unreadable as SQLite, so the copy cannot be verified.
        File.WriteAllText(Path.Combine(oldHome, TaskBatonHome.LegacyDatabaseFileName), "not a database");
        File.WriteAllText(Path.Combine(oldHome, "keep.txt"), "keep");
        File.WriteAllText(Path.Combine(oldHome, TaskBatonHome.LegacyLocationFileName),
            RelayJson.Write(new { home = oldHome }));

        var resolved = WithHomeEnvironment(null, null, () => TaskBatonHome.ResolveForRoot(localApplicationData));
        Assert.AreEqual(oldHome, resolved);
        Assert.IsFalse(Directory.Exists(newHome));
        Assert.IsTrue(File.Exists(Path.Combine(oldHome, TaskBatonHome.LegacyDatabaseFileName)));
        Assert.IsTrue(File.Exists(Path.Combine(oldHome, "keep.txt")));
    }

    [TestMethod]
    public void NewLocationThenOldLocationThenDefaultsResolveInOrder()
    {
        var newLocation = TaskBatonHome.DefaultLocationFileFor(localApplicationData);
        var oldLocation = TaskBatonHome.LegacyLocationFileFor(localApplicationData);
        var newSaved = Path.Combine(root, "new-saved");
        var oldSaved = Path.Combine(root, "old-saved");
        Directory.CreateDirectory(Path.GetDirectoryName(newLocation)!);
        Directory.CreateDirectory(Path.GetDirectoryName(oldLocation)!);
        TaskBatonHome.SaveLocation(newSaved, newLocation);
        TaskBatonHome.SaveLocation(oldSaved, oldLocation);

        Assert.AreEqual(newSaved, WithHomeEnvironment(null, null,
            () => TaskBatonHome.ResolveForRoot(localApplicationData)));
        File.Delete(newLocation);
        Assert.AreEqual(oldSaved, WithHomeEnvironment(null, null,
            () => TaskBatonHome.ResolveForRoot(localApplicationData)));
        File.Delete(oldLocation);
        Assert.AreEqual(TaskBatonHome.DefaultHomeFor(localApplicationData), WithHomeEnvironment(null, null,
            () => TaskBatonHome.ResolveForRoot(localApplicationData)));
    }

    [TestMethod]
    public void TaskBatonHomeWinsOverLegacyHomeAndExplicitHomeWinsOverBoth()
    {
        var taskBatonHome = Path.Combine(root, "taskbaton-home");
        var legacyHome = Path.Combine(root, "legacy-home");
        var explicitHome = Path.Combine(root, "explicit-home");
        Assert.AreEqual(taskBatonHome, WithHomeEnvironment(taskBatonHome, legacyHome,
            () => TaskBatonHome.ResolveForRoot(localApplicationData)));
        Assert.AreEqual(explicitHome, WithHomeEnvironment(taskBatonHome, legacyHome,
            () => TaskBatonHome.ResolveForRoot(localApplicationData, explicitHome)));
        Assert.AreEqual(legacyHome, WithHomeEnvironment(null, legacyHome,
            () => TaskBatonHome.ResolveForRoot(localApplicationData)));
    }

    [TestMethod]
    public void CallerContextReadsNewPrefixThenLegacyPrefix()
    {
        var home = Path.Combine(root, "caller-home");
        var service = new RelayService(home);
        service.Initialize();
        service.RegisterManual("agent-a", "manual", "A");
        service.RegisterManual("agent-b", "manual", "B");
        var newFile = Path.Combine(root, "new-caller.md");
        var oldFile = Path.Combine(root, "old-caller.md");
        File.WriteAllText(newFile, "new caller context");
        File.WriteAllText(oldFile, "old caller context");

        var newContext = new Dictionary<string, string?>
        {
            ["TASKBATON_CALLER_AGENT_ID"] = "agent-a",
            ["TASKBATON_CALLER_SESSION_ID"] = "new-session",
            ["TASKBATON_CALLER_HOST"] = "fixture",
            ["TASKBATON_CALLER_CWD"] = root,
            ["TASKBATON_CALLER_PROFILE"] = "new-profile"
        };
        var newTask = WithEnvironment(newContext,
            () => service.CreateFileHandoffFromContext(new ManualTransferDraft("agent-a", "agent-b", newFile))).Task;
        Assert.AreEqual("new-session", newTask.OriginSession!.SessionId);
        Assert.AreEqual("fixture", newTask.OriginSession.Host);
        Assert.AreEqual("new-profile", newTask.OriginSession.ConfigProfile);

        var oldContext = new Dictionary<string, string?>
        {
            ["AGENT_RELAY_CALLER_AGENT_ID"] = "agent-a",
            ["AGENT_RELAY_CALLER_SESSION_ID"] = "old-session",
            ["AGENT_RELAY_CALLER_HOST"] = "fixture",
            ["AGENT_RELAY_CALLER_CWD"] = root
        };
        var oldTask = WithEnvironment(oldContext,
            () => service.CreateFileHandoffFromContext(new ManualTransferDraft("agent-a", "agent-b", oldFile))).Task;
        Assert.AreEqual("old-session", oldTask.OriginSession!.SessionId);
    }

    [TestMethod]
    public void ExistingLegacyDatabaseIsUsedUntilANewDatabaseExists()
    {
        var home = Path.Combine(root, "home");
        var seed = Path.Combine(root, "seed");
        var service = new RelayService(seed);
        service.Initialize();
        service.RegisterManual("agent-a", "manual", "A");
        CopySeedHome(seed, home);
        Assert.IsTrue(File.Exists(Path.Combine(home, TaskBatonHome.LegacyDatabaseFileName)));
        Assert.AreEqual(Path.Combine(home, TaskBatonHome.LegacyDatabaseFileName),
            TaskBatonHome.ResolveDatabasePath(home));

        var opened = new RelayService(home);
        Assert.HasCount(1, opened.Agents());
        File.WriteAllText(Path.Combine(home, TaskBatonHome.NewDatabaseFileName), "");
        Assert.AreEqual(Path.Combine(home, TaskBatonHome.NewDatabaseFileName),
            TaskBatonHome.ResolveDatabasePath(home));
    }

    private static T WithEnvironment<T>(Dictionary<string, string?> values, Func<T> action)
    {
        var keys = new[]
        {
            "TASKBATON_CALLER_AGENT_ID", "TASKBATON_CALLER_SESSION_ID", "TASKBATON_CALLER_HOST",
            "TASKBATON_CALLER_CWD", "TASKBATON_CALLER_PROFILE",
            "AGENT_RELAY_CALLER_AGENT_ID", "AGENT_RELAY_CALLER_SESSION_ID", "AGENT_RELAY_CALLER_HOST",
            "AGENT_RELAY_CALLER_CWD", "AGENT_RELAY_CALLER_PROFILE"
        };
        var previous = keys.ToDictionary(key => key, Environment.GetEnvironmentVariable);
        try
        {
            foreach (var key in keys) Environment.SetEnvironmentVariable(key, values.GetValueOrDefault(key));
            return action();
        }
        finally
        {
            foreach (var key in keys) Environment.SetEnvironmentVariable(key, previous[key]);
        }
    }

    private static T WithHomeEnvironment<T>(string? taskBatonHome, string? relayHome, Func<T> action)
    {
        var previousTaskBaton = Environment.GetEnvironmentVariable(TaskBatonHome.HomeVariable);
        var previousRelay = Environment.GetEnvironmentVariable(TaskBatonHome.LegacyHomeVariable);
        try
        {
            Environment.SetEnvironmentVariable(TaskBatonHome.HomeVariable, taskBatonHome);
            Environment.SetEnvironmentVariable(TaskBatonHome.LegacyHomeVariable, relayHome);
            return action();
        }
        finally
        {
            Environment.SetEnvironmentVariable(TaskBatonHome.HomeVariable, previousTaskBaton);
            Environment.SetEnvironmentVariable(TaskBatonHome.LegacyHomeVariable, previousRelay);
        }
    }

    private static void CopySeedHome(string seed, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(seed))
        {
            var name = Path.GetFileName(file);
            if (name.StartsWith(TaskBatonHome.NewDatabaseFileName, StringComparison.Ordinal))
            {
                var suffix = name[TaskBatonHome.NewDatabaseFileName.Length..];
                File.Move(file, Path.Combine(destination, TaskBatonHome.LegacyDatabaseFileName + suffix));
            }
            else File.Copy(file, Path.Combine(destination, name), true);
        }
        foreach (var directory in Directory.GetDirectories(seed))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        foreach (var directory in Directory.GetDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }
}
