using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;

namespace TaskBaton.Core;

public static class DetachedWorker
{
    public static void Launch(RelayService service, TaskRecord task, int timeoutSeconds, IEnumerable<string> arguments)
    {
        if (!service.ScheduleExecution(task.TaskId, task.AttemptId, task.RecipientId, timeoutSeconds)) return;
        var shellLaunch = OperatingSystem.IsWindows();
        var start = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = shellLaunch, CreateNoWindow = true,
            RedirectStandardInput = !shellLaunch, RedirectStandardOutput = !shellLaunch, RedirectStandardError = !shellLaunch
        };
        if (shellLaunch) start.WindowStyle = ProcessWindowStyle.Hidden;
        if (string.Equals(Path.GetFileNameWithoutExtension(start.FileName), "dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Assembly.GetEntryAssembly()!.Location);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        try
        {
            using var process = Process.Start(start)!;
            if (!shellLaunch) process.StandardInput.Close();
            service.RecordExecutionWorker(task.TaskId, task.AttemptId, process.Id);
        }
        catch (Win32Exception error)
        {
            service.ExecutionLaunchFailed(task.TaskId, task.AttemptId, error.Message);
            throw new RelayException("worker_launch_failed", error.Message, 3);
        }
    }
}
