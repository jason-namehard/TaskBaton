namespace TaskBaton.Protocol;

/// <summary>
/// L5 ownership facts supplied by the ledger. Paths passed to these methods are absolute and have
/// already been proven to be inside the managed root.
/// </summary>
public interface IArtifactOwnershipStore
{
    bool IsCreatedByRelay(string path);

    bool IsRegisteredByHandoff(string path);

    bool HasActiveTaskReference(string path);

    bool IsProtectedUserFile(string path);
}
