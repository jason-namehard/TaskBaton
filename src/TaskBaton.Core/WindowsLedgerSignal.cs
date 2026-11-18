using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace TaskBaton.Core;

// The ledger remains authoritative. This optional message only asks open windows to reread it.
public static class WindowsLedgerSignal
{
    public const string MessageName = "TaskBaton.LedgerChanged.v1";

    [SupportedOSPlatform("windows")]
    public static uint Register() => RegisterWindowMessage(MessageName);

    internal static void Publish()
    {
        if (!OperatingSystem.IsWindows()) return;
        var message = Register();
        // A failed UI hint must not turn an already committed operation into a retryable failure.
        if (message == 0 || !PostMessage(new nint(0xffff), message, Environment.ProcessId, 0))
            Trace.TraceWarning("Ledger committed; desktop refresh hint failed: {0}", Marshal.GetLastWin32Error());
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterWindowMessage(string text);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);
}
