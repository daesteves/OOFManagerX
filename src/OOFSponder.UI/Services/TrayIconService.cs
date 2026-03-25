using System.Drawing;
using System.Runtime.InteropServices;
using OOFManagerX.Core;

namespace OOFSponder.UI.Services;

/// <summary>
/// Windows Shell NotifyIcon wrapper for system tray functionality.
/// Creates a hidden message window to receive tray icon callbacks.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private const int WM_USER = 0x0400;
    private const int WM_TRAYICON = WM_USER + 1;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_DESTROY = 0x0002;
    private const int WM_COMMAND = 0x0111;
    
    private const int NIF_MESSAGE = 0x01;
    private const int NIF_ICON = 0x02;
    private const int NIF_TIP = 0x04;
    private const int NIM_ADD = 0x00;
    private const int NIM_MODIFY = 0x01;
    private const int NIM_DELETE = 0x02;

    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int CW_USEDEFAULT = unchecked((int)0x80000000);

    // Context menu constants
    private const int MF_STRING = 0x0000;
    private const int MF_SEPARATOR = 0x0800;
    private const int TPM_RIGHTBUTTON = 0x0002;
    private const int TPM_RETURNCMD = 0x0100;
    private const int TPM_NONOTIFY = 0x0080;
    private const int ID_OPEN = 1001;
    private const int ID_EXIT = 1002;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public int cbSize;
        public int style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(int dwExStyle, string lpClassName, string lpWindowName,
        int dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr hMenu, int uFlags, int uIDNewItem, string lpNewItem);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(IntPtr hMenu, int fuFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private NOTIFYICONDATA _notifyIconData;
    private IntPtr _iconHandle;
    private IntPtr _messageWindow;
    private WndProcDelegate? _wndProc;
    private Thread? _messageThread;
    private bool _isDisposed;
    private readonly string _iconPath;

    public event Action? OnClick;
    public event Action? OnExit;

    public TrayIconService(string tooltip, string iconPath)
    {
        _iconPath = iconPath;
        
        // Start message loop on a separate thread
        _messageThread = new Thread(() => RunMessageLoop(tooltip))
        {
            IsBackground = true,
            Name = "TrayIconMessageLoop"
        };
        _messageThread.SetApartmentState(ApartmentState.STA);
        _messageThread.Start();
    }

    private void RunMessageLoop(string tooltip)
    {
        // Register window class
        _wndProc = WndProc;
        var className = "OOFSponderTrayIcon_" + Guid.NewGuid().ToString("N");
        
        var wndClass = new WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = GetModuleHandle(null),
            lpszClassName = className
        };
        
        RegisterClassEx(ref wndClass);

        // Create hidden message window
        _messageWindow = CreateWindowEx(
            WS_EX_TOOLWINDOW,
            className,
            "OOFSponder Tray",
            WS_POPUP,
            0, 0, 0, 0,
            IntPtr.Zero, IntPtr.Zero,
            GetModuleHandle(null),
            IntPtr.Zero);

        // Create icon from image file
        _iconHandle = CreateIconFromFile(_iconPath);

        // Setup notify icon
        _notifyIconData = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _messageWindow,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = _iconHandle,
            szTip = tooltip
        };

        Shell_NotifyIcon(NIM_ADD, ref _notifyIconData);

        // Message loop
        while (GetMessage(out var msg, IntPtr.Zero, 0, 0))
        {
            if (_isDisposed) break;
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_TRAYICON)
        {
            var mouseMsg = lParam.ToInt32();
            switch (mouseMsg)
            {
                case WM_LBUTTONUP:
                case WM_LBUTTONDBLCLK:
                    OnClick?.Invoke();
                    break;
                case WM_RBUTTONUP:
                    ShowContextMenuInternal();
                    break;
            }
            return IntPtr.Zero;
        }
        
        if (msg == WM_DESTROY)
        {
            return IntPtr.Zero;
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void ShowContextMenuInternal()
    {
        var hMenu = CreatePopupMenu();
        if (hMenu == IntPtr.Zero) return;
        
        try
        {
            AppendMenu(hMenu, MF_STRING, ID_OPEN, LocalizedStrings.OpenOOFManagerX);
            AppendMenu(hMenu, MF_SEPARATOR, 0, string.Empty);
            AppendMenu(hMenu, MF_STRING, ID_EXIT, LocalizedStrings.Exit);
            
            GetCursorPos(out var pt);
            
            // Required to make menu dismiss when clicking outside
            SetForegroundWindow(_messageWindow);
            
            var cmd = TrackPopupMenuEx(
                hMenu,
                TPM_RIGHTBUTTON | TPM_RETURNCMD | TPM_NONOTIFY,
                pt.x, pt.y,
                _messageWindow,
                IntPtr.Zero);
            
            switch (cmd)
            {
                case ID_OPEN:
                    OnClick?.Invoke();
                    break;
                case ID_EXIT:
                    OnExit?.Invoke();
                    break;
            }
        }
        finally
        {
            DestroyMenu(hMenu);
        }
    }

    private static IntPtr CreateIconFromFile(string imagePath)
    {
        try
        {
            using var image = System.Drawing.Image.FromFile(imagePath);
            using var bitmap = new Bitmap(image, new Size(32, 32));
            return bitmap.GetHicon();
        }
        catch
        {
            // Fallback: create simple zzz icon
            return CreateFallbackIcon();
        }
    }

    private static IntPtr CreateFallbackIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);
        
        using var bgBrush = new SolidBrush(Color.FromArgb(0, 120, 212));
        graphics.FillEllipse(bgBrush, 0, 0, 31, 31);
        
        using var font = new Font("Segoe UI", 9, FontStyle.Bold);
        using var textBrush = new SolidBrush(Color.White);
        graphics.DrawString("zzz", font, textBrush, 4, 7);
        
        return bitmap.GetHicon();
    }

    public void UpdateTooltip(string tooltip)
    {
        if (_messageWindow == IntPtr.Zero) return;
        
        _notifyIconData.szTip = tooltip;
        Shell_NotifyIcon(NIM_MODIFY, ref _notifyIconData);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        
        Shell_NotifyIcon(NIM_DELETE, ref _notifyIconData);
        
        if (_iconHandle != IntPtr.Zero)
        {
            DestroyIcon(_iconHandle);
            _iconHandle = IntPtr.Zero;
        }

        if (_messageWindow != IntPtr.Zero)
        {
            PostMessage(_messageWindow, WM_DESTROY, IntPtr.Zero, IntPtr.Zero);
            DestroyWindow(_messageWindow);
            _messageWindow = IntPtr.Zero;
        }
    }
}
