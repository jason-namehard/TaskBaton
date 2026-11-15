namespace AgentRelay.Protocol;

public sealed record SessionLookupResult(bool Exists, string? ActualSessionId);

/// <summary>Host store lookup by exact session id. Implementations must never resolve "latest".</summary>
public interface ISessionStore
{
    SessionLookupResult LookupExact(string host, string sessionId);
}

public sealed record SessionResumeResult(bool Resumed, string? SessionId);

/// <summary>A host resume operation. The returned id must equal the requested binding exactly.</summary>
public interface ISessionResumer
{
    SessionResumeResult Resume(SessionBinding binding);
}
