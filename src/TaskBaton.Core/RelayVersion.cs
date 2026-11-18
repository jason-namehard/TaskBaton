using System.Reflection;

namespace TaskBaton.Core;

public static class RelayVersion
{
    public static string Current => typeof(RelayVersion).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
}
