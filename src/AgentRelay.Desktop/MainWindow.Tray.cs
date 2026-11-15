using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AgentRelay.Desktop;

public partial class MainWindow
{
    private const int TrayMessage = 0x8001;
    private bool trayInstalled;
    private nint trayIcon;
    private uint taskbarCreated;

    private void InstallTray()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (trayIcon == 0)
        {
            using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/AgentRelay.Desktop;component/Assets/AgentRelay.ico")).Stream;
            using var buffer = new System.IO.MemoryStream(); stream.CopyTo(buffer);
            var icon = buffer.ToArray();
            var length = BitConverter.ToInt32(icon,14); var offset = BitConverter.ToInt32(icon,18);
            trayIcon = CreateIconFromResourceEx(icon.AsSpan(offset,length).ToArray(),(uint)length,true,0x30000,32,32,0);
            if (trayIcon == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        var data = TrayData(handle);
        if (!ShellNotifyIcon(0, ref data)) throw new Win32Exception(Marshal.GetLastWin32Error());
        trayInstalled = true;
    }

    private TrayIconData TrayData(nint handle) => new()
    {
        Size = (uint)Marshal.SizeOf<TrayIconData>(), Window = handle, Id = 1,
        Flags = 1 | 2 | 4, Callback = TrayMessage, Icon = trayIcon,
        Tip = "TaskBaton", Info = "", InfoTitle = ""
    };

    private void RemoveTray()
    {
        if (trayInstalled)
        {
            var data = TrayData(new WindowInteropHelper(this).Handle);
            _ = ShellNotifyIcon(2, ref data);
            trayInstalled = false;
        }
        if (trayIcon != 0) { _ = DestroyIcon(trayIcon); trayIcon = 0; }
    }

    private void OpenFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private bool ReceiveTrayMessage(int message, nint lParam)
    {
        if (taskbarCreated != 0 && message == taskbarCreated)
        {
            trayInstalled = false;
            InstallTray();
            return true;
        }
        if (message != TrayMessage) return false;
        if ((int)lParam == 0x203) OpenFromTray(); // WM_LBUTTONDBLCLK
        if ((int)lParam == 0x205) // WM_RBUTTONUP
        {
            var menu = CreatePopupMenu();
            try
            {
                _ = AppendMenu(menu, 0, 1, T("Open"));
                _ = AppendMenu(menu, 0, 2, T("Exit"));
                _ = GetCursorPos(out var point);
                var handle = new WindowInteropHelper(this).Handle;
                _ = SetForegroundWindow(handle);
                var command = TrackPopupMenu(menu, 0x100 | 0x2, point.X, point.Y, 0, handle, 0);
                if (command == 1) OpenFromTray();
                if (command == 2) RequestExit();
            }
            finally { _ = DestroyMenu(menu); }
        }
        return true;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TrayIconData
    {
        public uint Size; public nint Window; public uint Id; public uint Flags; public uint Callback; public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State; public uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Timeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags; public Guid Guid; public nint BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)] private struct TrayPoint { public int X; public int Y; }
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShellNotifyIcon(uint operation, ref TrayIconData data);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint CreateIconFromResourceEx(byte[] data,uint size,[MarshalAs(UnmanagedType.Bool)]bool icon,uint version,int width,int height,uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll")] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AppendMenu(nint menu, uint flags, nuint id, string text);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint window, nint rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out TrayPoint point);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string name);
}
