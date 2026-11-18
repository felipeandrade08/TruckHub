using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace TruckHub;

/// <summary>Ícone na bandeja do Windows (área de ícones ocultos) via Shell_NotifyIcon, sem depender do WinForms.</summary>
internal sealed class TrayIcon : IDisposable
{
    private const int WmApp = 0x8001, WmLButtonUp = 0x202, WmLButtonDblClk = 0x203, WmRButtonUp = 0x205;
    private const uint NifMessage = 1, NifIcon = 2, NifTip = 4, NimAdd = 0, NimDelete = 2;
    private const uint IdOpen = 1, IdExit = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int cbSize; public IntPtr hWnd; public uint uID, uFlags, uCallbackMessage; public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState, dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags; public Guid guidItem; public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIconW(uint msg, ref NotifyIconData data);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern uint ExtractIconExW(string file, int index, out IntPtr large, out IntPtr small, uint count);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenuW(IntPtr menu, uint flags, UIntPtr id, string text);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point p);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr hWnd, IntPtr rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessageW(string name);

    private readonly HwndSource _source;
    private readonly uint _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
    private IntPtr _icon;
    private readonly string _tip;
    private bool _added;

    public event Action? OpenRequested;
    public event Action? ExitRequested;

    public TrayIcon(string tooltip)
    {
        _tip = tooltip.Length > 120 ? tooltip[..120] : tooltip;
        _source = new HwndSource(new HwndSourceParameters("TruckHubTray") { WindowStyle = 0, Width = 0, Height = 0, ParentWindow = new IntPtr(-3) });
        _source.AddHook(WndProc);
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe) && ExtractIconExW(exe, 0, out _, out var small, 1) > 0) _icon = small;
        }
        catch { }
    }

    public void Show()
    {
        if (_added) return;
        var d = Build();
        _added = Shell_NotifyIconW(NimAdd, ref d);
    }

    public void Hide()
    {
        if (!_added) return;
        var d = Build();
        Shell_NotifyIconW(NimDelete, ref d);
        _added = false;
    }

    private NotifyIconData Build() => new()
    {
        cbSize = Marshal.SizeOf<NotifyIconData>(), hWnd = _source.Handle, uID = 1,
        uFlags = NifMessage | NifIcon | NifTip, uCallbackMessage = WmApp, hIcon = _icon, szTip = _tip, szInfo = "", szInfoTitle = ""
    };

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_taskbarCreated != 0 && (uint)msg == _taskbarCreated && _added) { _added = false; Show(); }
        else if (msg == WmApp)
        {
            var ev = (int)(lParam.ToInt64() & 0xFFFF);
            if (ev == WmLButtonUp || ev == WmLButtonDblClk) { OpenRequested?.Invoke(); handled = true; }
            else if (ev == WmRButtonUp) { ShowMenu(); handled = true; }
        }
        return IntPtr.Zero;
    }

    private void ShowMenu()
    {
        var menu = CreatePopupMenu();
        try
        {
            AppendMenuW(menu, 0, (UIntPtr)IdOpen, "Abrir TruckHub");
            AppendMenuW(menu, 0, (UIntPtr)IdExit, "Sair");
            GetCursorPos(out var p);
            SetForegroundWindow(_source.Handle);
            var cmd = TrackPopupMenu(menu, 0x100 | 0x80 | 0x20, p.X, p.Y, 0, _source.Handle, IntPtr.Zero); // RETURNCMD|NONOTIFY|BOTTOMALIGN
            if (cmd == IdOpen) OpenRequested?.Invoke();
            else if (cmd == IdExit) ExitRequested?.Invoke();
        }
        finally { DestroyMenu(menu); }
    }

    public void Dispose()
    {
        try { Hide(); } catch { }
        try { _source.RemoveHook(WndProc); _source.Dispose(); } catch { }
        if (_icon != IntPtr.Zero) { DestroyIcon(_icon); _icon = IntPtr.Zero; }
    }
}
