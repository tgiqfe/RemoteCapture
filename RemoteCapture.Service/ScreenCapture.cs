using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace RemoteCapture.Service;

public class ScreenCapture : IDisposable
{
    private IntPtr _desktopHandle = IntPtr.Zero;
    private IntPtr _hdcScreen = IntPtr.Zero;
    private IntPtr _hdcMemDC = IntPtr.Zero;
    private IntPtr _hBitmap = IntPtr.Zero;
    private int _width;
    private int _height;
    private string _currentDesktopName = string.Empty;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenInputDesktop(uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenDesktop(string lpszDesktop, uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseDesktop(IntPtr hDesktop);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetThreadDesktop(IntPtr hDesktop);

    [DllImport("user32.dll")]
    private static extern IntPtr GetThreadDesktop(uint dwThreadId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool GetUserObjectInformation(IntPtr hObj, int nIndex, IntPtr pvInfo, uint nLength, out uint lpnLengthNeeded);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight,
        IntPtr hdcSrc, int nXSrc, int nYSrc, int dwRop);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
    private const uint DESKTOP_READOBJECTS = 0x0001;
    private const uint DESKTOP_CREATEWINDOW = 0x0002;
    private const uint DESKTOP_WRITEOBJECTS = 0x0080;
    private const int SRCCOPY = 0x00CC0020;
    private const int UOI_NAME = 2;

    private string GetCurrentDesktopName()
    {
        IntPtr desktop = GetThreadDesktop(GetCurrentThreadId());
        if (desktop == IntPtr.Zero)
            return string.Empty;

        uint needed = 0;
        GetUserObjectInformation(desktop, UOI_NAME, IntPtr.Zero, 0, out needed);

        if (needed == 0)
            return string.Empty;

        IntPtr buffer = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (GetUserObjectInformation(desktop, UOI_NAME, buffer, needed, out _))
            {
                return Marshal.PtrToStringUni(buffer) ?? string.Empty;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return string.Empty;
    }

    private bool SwitchToDesktop()
    {
        Console.WriteLine($"[SwitchToDesktop] Current Session: {GetCurrentSessionId()}");
        Console.WriteLine($"[SwitchToDesktop] Current Desktop: {GetCurrentDesktopName()}");

        // まず現在の入力デスクトップを取得
        IntPtr newDesktop = OpenInputDesktop(0, false, DESKTOP_READOBJECTS | DESKTOP_CREATEWINDOW | DESKTOP_WRITEOBJECTS);

        if (newDesktop != IntPtr.Zero)
        {
            Console.WriteLine("[SwitchToDesktop] Successfully opened InputDesktop");
        }
        else
        {
            Console.WriteLine("[SwitchToDesktop] Failed to open InputDesktop, trying Winlogon...");
            // 入力デスクトップが取得できない場合、Winlogonデスクトップを試す（ログイン画面用）
            newDesktop = OpenDesktop("Winlogon", 0, false, DESKTOP_READOBJECTS | DESKTOP_CREATEWINDOW | DESKTOP_WRITEOBJECTS);

            if (newDesktop != IntPtr.Zero)
            {
                Console.WriteLine("[SwitchToDesktop] Successfully opened Winlogon desktop");
            }
        }

        // それでも取得できない場合、Defaultデスクトップを試す
        if (newDesktop == IntPtr.Zero)
        {
            Console.WriteLine("[SwitchToDesktop] Trying Default desktop...");
            newDesktop = OpenDesktop("Default", 0, false, DESKTOP_READOBJECTS | DESKTOP_CREATEWINDOW | DESKTOP_WRITEOBJECTS);

            if (newDesktop != IntPtr.Zero)
            {
                Console.WriteLine("[SwitchToDesktop] Successfully opened Default desktop");
            }
        }

        if (newDesktop == IntPtr.Zero)
        {
            int error = Marshal.GetLastWin32Error();
            Console.WriteLine($"[SwitchToDesktop] ERROR: Failed to open any desktop. Last error: {error}");
            return false;
        }

        // 既存のデスクトップハンドルがあれば閉じる
        if (_desktopHandle != IntPtr.Zero && _desktopHandle != newDesktop)
        {
            CloseDesktop(_desktopHandle);
        }

        _desktopHandle = newDesktop;
        SetThreadDesktop(_desktopHandle);
        _currentDesktopName = GetCurrentDesktopName();

        Console.WriteLine($"[SwitchToDesktop] Successfully switched to desktop: {_currentDesktopName}");
        return true;
    }

    public void Initialize()
    {
        Console.WriteLine($"[ScreenCapture] Initializing... Current Session ID: {GetCurrentSessionId()}");

        if (!SwitchToDesktop())
        {
            Console.WriteLine("[ScreenCapture] Warning: Failed to switch desktop");
        }

        _width = GetSystemMetrics(SM_CXSCREEN);
        _height = GetSystemMetrics(SM_CYSCREEN);

        Console.WriteLine($"[ScreenCapture] Screen size: {_width}x{_height}");
        Console.WriteLine($"[ScreenCapture] Current desktop: {_currentDesktopName}");

        _hdcScreen = GetDC(IntPtr.Zero);
        if (_hdcScreen == IntPtr.Zero)
        {
            Console.WriteLine("[ScreenCapture] ERROR: Failed to get screen DC");
            return;
        }

        _hdcMemDC = CreateCompatibleDC(_hdcScreen);
        if (_hdcMemDC == IntPtr.Zero)
        {
            Console.WriteLine("[ScreenCapture] ERROR: Failed to create compatible DC");
            return;
        }

        _hBitmap = CreateCompatibleBitmap(_hdcScreen, _width, _height);
        if (_hBitmap == IntPtr.Zero)
        {
            Console.WriteLine("[ScreenCapture] ERROR: Failed to create compatible bitmap");
            return;
        }

        SelectObject(_hdcMemDC, _hBitmap);
        Console.WriteLine("[ScreenCapture] Initialization complete");
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentProcessId();

    [DllImport("kernel32.dll")]
    private static extern bool ProcessIdToSessionId(uint dwProcessId, out uint pSessionId);

    private uint GetCurrentSessionId()
    {
        uint sessionId = 0;
        ProcessIdToSessionId(GetCurrentProcessId(), out sessionId);
        return sessionId;
    }

    public byte[] CaptureScreen()
    {
        // デスクトップが変更されている可能性があるため、毎回チェック
        string currentDesktop = GetCurrentDesktopName();
        if (string.IsNullOrEmpty(currentDesktop) || currentDesktop != _currentDesktopName)
        {
            // デスクトップが切り替わった場合、リソースを再初期化
            Console.WriteLine($"[ScreenCapture] Desktop changed from '{_currentDesktopName}' to '{currentDesktop}', reinitializing...");
            ReleaseResources();
            if (!SwitchToDesktop())
            {
                Console.WriteLine("[ScreenCapture] Failed to switch desktop");
                return Array.Empty<byte>();
            }

            // GDIリソースを再作成
            _hdcScreen = GetDC(IntPtr.Zero);
            _hdcMemDC = CreateCompatibleDC(_hdcScreen);
            _hBitmap = CreateCompatibleBitmap(_hdcScreen, _width, _height);
            SelectObject(_hdcMemDC, _hBitmap);
        }

        if (_hdcScreen == IntPtr.Zero || _hdcMemDC == IntPtr.Zero || _hBitmap == IntPtr.Zero)
        {
            Console.WriteLine("[ScreenCapture] Resources not initialized, reinitializing...");
            Initialize();
        }

        if (_hdcScreen == IntPtr.Zero || _hdcMemDC == IntPtr.Zero || _hBitmap == IntPtr.Zero)
        {
            Console.WriteLine("[ScreenCapture] ERROR: Failed to initialize resources");
            return Array.Empty<byte>();
        }

        bool success = BitBlt(_hdcMemDC, 0, 0, _width, _height, _hdcScreen, 0, 0, SRCCOPY);
        if (!success)
        {
            Console.WriteLine("[ScreenCapture] ERROR: BitBlt failed");
            return Array.Empty<byte>();
        }

        try
        {
            using var bitmap = Image.FromHbitmap(_hBitmap);
            using var memoryStream = new MemoryStream();
            bitmap.Save(memoryStream, ImageFormat.Jpeg);
            byte[] result = memoryStream.ToArray();

            if (result.Length == 0)
            {
                Console.WriteLine("[ScreenCapture] WARNING: Captured image is empty");
            }

            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ScreenCapture] ERROR: Failed to encode image: {ex.Message}");
            return Array.Empty<byte>();
        }
    }

    private void ReleaseResources()
    {
        if (_hBitmap != IntPtr.Zero)
        {
            DeleteObject(_hBitmap);
            _hBitmap = IntPtr.Zero;
        }

        if (_hdcMemDC != IntPtr.Zero)
        {
            DeleteDC(_hdcMemDC);
            _hdcMemDC = IntPtr.Zero;
        }

        if (_hdcScreen != IntPtr.Zero)
        {
            ReleaseDC(IntPtr.Zero, _hdcScreen);
            _hdcScreen = IntPtr.Zero;
        }
    }

    public (int Width, int Height) GetScreenSize()
    {
        return (_width, _height);
    }

    public void Dispose()
    {
        ReleaseResources();

        if (_desktopHandle != IntPtr.Zero)
        {
            CloseDesktop(_desktopHandle);
            _desktopHandle = IntPtr.Zero;
        }
    }
}
