namespace TaskBaton.Core;

public sealed record HostSessionCapabilities(bool ExecEphemeral,bool SessionCreate,bool SessionResume,
    bool SessionDiscover,string SessionOpen,string SessionVisibility);

public sealed partial class RelayService
{
    public HostSessionCapabilities SessionCapabilities(string agentId)
    {
        _=Agents().SingleOrDefault(a=>a.AgentId==agentId)??throw new RelayException("agent_not_found","未找到 Agent："+agentId);
        var recipe=CliExtension(agentId)?.Recipe;
        return new(recipe is not null,recipe is not null&&CliRecipes.OutputFormatFor(recipe)!="text",
            recipe?.Resume is not null,SessionDiscoverySupported(agentId),"unsupported","unknown");
    }
}
