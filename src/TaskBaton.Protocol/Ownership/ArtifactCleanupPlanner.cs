namespace TaskBaton.Protocol;

public sealed record ArtifactCleanupCandidate(
    string Path,
    string Role,
    DateTimeOffset? ExpiresAt,
    bool IsControlFile = false,
    bool IsDeliverable = false);

public sealed record CleanupDecision(string Path, bool Delete, string? Code, string Message);

public sealed record CleanupPlan(
    IReadOnlyList<CleanupDecision> Deletable,
    IReadOnlyList<CleanupDecision> Retained)
{
    public bool CanDelete(string path) =>
        Deletable.Any(decision => string.Equals(decision.Path, path, StringComparison.Ordinal));
}

/// <summary>
/// L5 cleanup policy: only relay-owned or registered files inside the managed root, with no active
/// task reference, may be expired. User sources, deliverables, control files, and unexpired files
/// are retained. Deleting a task never deletes referenced files automatically.
/// </summary>
public sealed class ArtifactCleanupPlanner
{
    public CleanupPlan Plan(
        string managedRoot,
        IEnumerable<ArtifactCleanupCandidate> candidates,
        DateTimeOffset now)
    {
        var deletable = new List<CleanupDecision>();
        var retained = new List<CleanupDecision>();

        foreach (var candidate in candidates)
        {
            if (!PathSafety.IsContainedInRoot(managedRoot, candidate.Path, out var fullPath))
            {
                Retain(candidate, ProtocolDiagnosticCodes.CleanupOutsideRoot,
                    "Path is outside the managed root.");
                continue;
            }

            if (candidate.IsControlFile
                || candidate.IsDeliverable
                || string.Equals(candidate.Role, "result", StringComparison.Ordinal)
                || OwnershipStore?.IsProtectedUserFile(fullPath) == true)
            {
                Retain(candidate, ProtocolDiagnosticCodes.CleanupProtected,
                    "User source, deliverable, or control file is never deleted by TTL cleanup.");
                continue;
            }

            if (OwnershipStore is null
                || (!OwnershipStore.IsCreatedByRelay(fullPath)
                    && !OwnershipStore.IsRegisteredByHandoff(fullPath)))
            {
                Retain(candidate, ProtocolDiagnosticCodes.CleanupNotOwned,
                    "The relay cannot prove it created or registered this file.");
                continue;
            }

            if (OwnershipStore.HasActiveTaskReference(fullPath))
            {
                Retain(candidate, ProtocolDiagnosticCodes.CleanupActiveReference,
                    "An active task still references this file.");
                continue;
            }

            if (candidate.ExpiresAt is null || candidate.ExpiresAt.Value > now)
            {
                Retain(candidate, ProtocolDiagnosticCodes.CleanupNotExpired,
                    "The artifact has not expired.");
                continue;
            }

            deletable.Add(new CleanupDecision(candidate.Path, Delete: true, Code: null,
                Message: "Owned, unexpired, unreferenced artifact."));
        }

        return new CleanupPlan(deletable, retained);

        void Retain(ArtifactCleanupCandidate candidate, string code, string message) =>
            retained.Add(new CleanupDecision(candidate.Path, Delete: false, code, message));
    }

    /// <summary>Set by callers before <see cref="Plan"/>; a null store retains everything as unowned.</summary>
    public IArtifactOwnershipStore? OwnershipStore { get; init; }
}
