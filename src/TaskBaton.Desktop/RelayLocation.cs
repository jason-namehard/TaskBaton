using TaskBaton.Core;

namespace TaskBaton.Desktop;

public static class RelayLocation
{
    public static string FilePath => TaskBatonHome.DefaultLocationFile;

    public static string? Load() =>
        TaskBatonHome.ReadLocation(FilePath) ?? TaskBatonHome.ReadLocation(TaskBatonHome.LegacyLocationFile);

    public static void Save(string home) => TaskBatonHome.SaveLocation(home);
}
