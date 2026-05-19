using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace RemoteCapture.Lib.GDI;

public class ScreenCapture
{
    private static IntPtr _originalDesktop = IntPtr.Zero;
    private static IntPtr _inputDesktop = IntPtr.Zero;

    public static byte[] CaptureScreenWithCursor(int quality = 75, bool captureLogonUI = false)
    {
        IntPtr currentDesktop = IntPtr.Zero;

        try
        {
            if (captureLogonUI)
            {
                // 現在のデスクトップを保存
                _originalDesktop = NativeMethods.GetThreadDesktop(NativeMethods.GetCurrentThreadId());

                // 入力デスクトップ（アクティブなデスクトップ）を取得
                _inputDesktop = NativeMethods.OpenInputDesktop(0, false, 
                    NativeMethods.DESKTOP_READOBJECTS | 
                    NativeMethods.DESKTOP_WRITEOBJECTS | 
                    NativeMethods.DESKTOP_SWITCHDESKTOP);

                if (_inputDesktop != IntPtr.Zero)
                {
                    // 入力デスクトップに切り替え
                    NativeMethods.SetThreadDesktop(_inputDesktop);
                    currentDesktop = _inputDesktop;
                }
            }

            var screenWidth = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
            var screenHeight = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);
            var screenLeft = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
            var screenTop = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);

            using var bitmap = new Bitmap(screenWidth, screenHeight);
            using var graphics = Graphics.FromImage(bitmap);

            graphics.CopyFromScreen(screenLeft, screenTop, 0, 0, new Size(screenWidth, screenHeight));

            DrawCursor(graphics, screenLeft, screenTop);

            return CompressToJpeg(bitmap, quality);
        }
        finally
        {
            // 元のデスクトップに戻す
            if (captureLogonUI && _originalDesktop != IntPtr.Zero)
            {
                NativeMethods.SetThreadDesktop(_originalDesktop);
            }

            if (currentDesktop != IntPtr.Zero && currentDesktop != _originalDesktop)
            {
                NativeMethods.CloseDesktop(currentDesktop);
            }
        }
    }

    private static void DrawCursor(Graphics graphics, int screenLeft, int screenTop)
    {
        CURSORINFO cursorInfo = new CURSORINFO();
        cursorInfo.cbSize = Marshal.SizeOf(cursorInfo);

        if (NativeMethods.GetCursorInfo(out cursorInfo) && (cursorInfo.flags & NativeMethods.CURSOR_SHOWING) != 0)
        {
            var cursorPosition = new Point(
                cursorInfo.ptScreenPos.X - screenLeft,
                cursorInfo.ptScreenPos.Y - screenTop
            );

            IntPtr hIcon = NativeMethods.CopyIcon(cursorInfo.hCursor);
            if (hIcon != IntPtr.Zero)
            {
                ICONINFO iconInfo;
                if (NativeMethods.GetIconInfo(hIcon, out iconInfo))
                {
                    var x = cursorPosition.X - iconInfo.xHotspot;
                    var y = cursorPosition.Y - iconInfo.yHotspot;

                    using (var cursorIcon = Icon.FromHandle(hIcon))
                    {
                        graphics.DrawIcon(cursorIcon, x, y);
                    }

                    if (iconInfo.hbmColor != IntPtr.Zero)
                        NativeMethods.DeleteObject(iconInfo.hbmColor);
                    if (iconInfo.hbmMask != IntPtr.Zero)
                        NativeMethods.DeleteObject(iconInfo.hbmMask);
                }

                NativeMethods.DeleteObject(hIcon);
            }
        }
    }

    private static byte[] CompressToJpeg(Bitmap bitmap, int quality)
    {
        using var ms = new MemoryStream();
        var encoderParameters = new EncoderParameters(1);
        encoderParameters.Param[0] = new EncoderParameter(Encoder.Quality, quality);
        var jpegCodec = GetEncoderInfo("image/jpeg");

        if (jpegCodec != null)
        {
            bitmap.Save(ms, jpegCodec, encoderParameters);
        }
        else
        {
            bitmap.Save(ms, ImageFormat.Jpeg);
        }

        return ms.ToArray();
    }

    private static ImageCodecInfo? GetEncoderInfo(string mimeType)
    {
        var encoders = ImageCodecInfo.GetImageEncoders();
        return encoders.FirstOrDefault(e => e.MimeType == mimeType);
    }
}

[StructLayout(LayoutKind.Sequential)]
struct CURSORINFO
{
    public int cbSize;
    public int flags;
    public IntPtr hCursor;
    public Point ptScreenPos;
}

[StructLayout(LayoutKind.Sequential)]
struct ICONINFO
{
    public bool fIcon;
    public int xHotspot;
    public int yHotspot;
    public IntPtr hbmMask;
    public IntPtr hbmColor;
}

static class NativeMethods
{
    public const int CURSOR_SHOWING = 0x00000001;
    public const int SM_CXVIRTUALSCREEN = 78;
    public const int SM_CYVIRTUALSCREEN = 79;
    public const int SM_XVIRTUALSCREEN = 76;
    public const int SM_YVIRTUALSCREEN = 77;

    // Desktop access rights
    public const int DESKTOP_READOBJECTS = 0x0001;
    public const int DESKTOP_WRITEOBJECTS = 0x0080;
    public const int DESKTOP_SWITCHDESKTOP = 0x0100;

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    public static extern bool GetCursorInfo(out CURSORINFO pci);

    [DllImport("user32.dll")]
    public static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);

    [DllImport("user32.dll")]
    public static extern IntPtr CopyIcon(IntPtr hIcon);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr hObject);

    // Desktop switching functions
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr OpenInputDesktop(int dwFlags, bool fInherit, int dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr GetThreadDesktop(int dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetThreadDesktop(IntPtr hDesktop);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool CloseDesktop(IntPtr hDesktop);

    [DllImport("kernel32.dll")]
    public static extern int GetCurrentThreadId();
}
