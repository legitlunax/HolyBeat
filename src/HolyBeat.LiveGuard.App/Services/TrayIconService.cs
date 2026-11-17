using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace HolyBeat.LiveGuard.App.Services;

/// <summary>A single entry of the tray context menu.</summary>
public sealed class TrayMenuItem
{
    public string Header { get; init; } = string.Empty;
    public Action? Click { get; init; }
    public bool IsSeparator { get; init; }
}

/// <summary>
/// Minimal Windows notification area icon implemented directly on top of
/// Shell_NotifyIcon, so the app stays a pure WPF application.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private const int NIM_ADD = 0x00000000;
    private const int NIM_DELETE = 0x00000002;
    private const int NIM_SETVERSION = 0x00000004;
    private const int NIF_MESSAGE = 0x00000001;
    private const int NIF_ICON = 0x00000002;
    private const int NIF_TIP = 0x00000004;
    private const int NOTIFYICON_VERSION_4 = 4;
    private const int WM_TRAY = 0x8000 + 23;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_CONTEXTMENU = 0x007B;
    private const int MF_SEPARATOR = 0x00000800;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint TPM_RIGHTBUTTON = 0x0002;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public int uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int message, ref NOTIFYICONDATA data);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index, out IntPtr large, out IntPtr small, uint count);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr menu, uint flags, IntPtr id, string text);

    [DllImport("shell32.dll")]
    private static extern uint TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr window, IntPtr rect);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr instance, IntPtr icon);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    private readonly IntPtr _windowHandle;
    private readonly List<TrayMenuItem> _items = new List<TrayMenuItem>();
    private readonly HwndSource? _source;
    private IntPtr _iconHandle;
    private bool _added;

    public TrayIconService(Window window, string tooltip, IReadOnlyList<TrayMenuItem> items)
    {
        _items.AddRange(items);
        _windowHandle = new WindowInteropHelper(window).Handle;
        _iconHandle = CreateAppIcon();

        var data = BuildData(tooltip);
        _added = Shell_NotifyIcon(NIM_ADD, ref data);

        if (_added)
        {
            data.uTimeoutOrVersion = NOTIFYICON_VERSION_4;
            Shell_NotifyIcon(NIM_SETVERSION, ref data);
        }

        _source = HwndSource.FromHwnd(_windowHandle);
        _source?.AddHook(WndProc);
    }

    private NOTIFYICONDATA BuildData(string tooltip)
    {
        return new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _windowHandle,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAY,
            hIcon = _iconHandle,
            szTip = tooltip
        };
    }

    /// <summary>Uses the executable icon when present, otherwise the standard Windows app icon.</summary>
    private static IntPtr CreateAppIcon()
    {
        try
        {
            string? executable = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(executable) &&
                ExtractIconEx(executable, 0, out IntPtr large, out IntPtr small, 1) > 0)
            {
                if (large != IntPtr.Zero)
                {
                    if (small != IntPtr.Zero)
                        DestroyIcon(small);
                    return large;
                }

                if (small != IntPtr.Zero)
                    return small;
            }
        }
        catch
        {
            // Fall through to the shared system icon.
        }

        return LoadIcon(IntPtr.Zero, new IntPtr(32512));
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_TRAY)
        {
            int mouseMessage = (int)(long)lParam;
            handled = true;

            if (mouseMessage == WM_LBUTTONUP || mouseMessage == WM_LBUTTONDBLCLK)
            {
                DoubleClick?.Invoke();
            }
            else if (mouseMessage == WM_RBUTTONUP || mouseMessage == WM_CONTEXTMENU)
            {
                ShowContextMenu();
            }
        }

        return IntPtr.Zero;
    }

    public event Action? DoubleClick;

    private void ShowContextMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
            return;

        try
        {
            uint commandId = 1;
            var actions = new Dictionary<uint, Action>();

            foreach (var item in _items)
            {
                if (item.IsSeparator)
                {
                    AppendMenu(menu, MF_SEPARATOR, IntPtr.Zero, string.Empty);
                    continue;
                }

                AppendMenu(menu, 0, new IntPtr(commandId), item.Header);
                if (item.Click is not null)
                    actions[commandId] = item.Click;
                commandId++;
            }

            GetCursorPos(out var point);
            uint selected = TrackPopupMenu(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON, point.X, point.Y, 0, _windowHandle, IntPtr.Zero);

            if (selected != 0 && actions.TryGetValue(selected, out var action))
                action();
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    public void Dispose()
    {
        if (_added)
        {
            var data = BuildData("HolyBeat Live Guard");
            Shell_NotifyIcon(NIM_DELETE, ref data);
            _added = false;
        }

        _source?.RemoveHook(WndProc);

        if (_iconHandle != IntPtr.Zero)
        {
            DestroyIcon(_iconHandle);
            _iconHandle = IntPtr.Zero;
        }
    }
}
