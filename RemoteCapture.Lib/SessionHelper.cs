using System.Runtime.InteropServices;

namespace RemoteCapture.Lib;

/// <summary>
/// Windowsセッション情報を取得するヘルパークラス
/// </summary>
public static class SessionHelper
{
    private const int WTS_CURRENT_SERVER_HANDLE = 0;

    /// <summary>
    /// セッション情報
    /// </summary>
    public class SessionInfo
    {
        public int SessionId { get; set; }
        public string UserName { get; set; } = string.Empty;
        public string DomainName { get; set; } = string.Empty;
        public WTS_CONNECTSTATE_CLASS State { get; set; }
        public bool IsActive => State == WTS_CONNECTSTATE_CLASS.WTSActive;
    }

    /// <summary>
    /// セッション状態
    /// </summary>
    public enum WTS_CONNECTSTATE_CLASS
    {
        WTSActive,
        WTSConnected,
        WTSConnectQuery,
        WTSShadow,
        WTSDisconnected,
        WTSIdle,
        WTSListen,
        WTSReset,
        WTSDown,
        WTSInit
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WTS_SESSION_INFO
    {
        public int SessionId;
        public IntPtr pWinStationName;
        public WTS_CONNECTSTATE_CLASS State;
    }

    private enum WTS_INFO_CLASS
    {
        WTSUserName = 5,
        WTSDomainName = 7,
    }

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSEnumerateSessions(
        IntPtr hServer,
        int Reserved,
        int Version,
        ref IntPtr ppSessionInfo,
        ref int pCount);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern void WTSFreeMemory(IntPtr pMemory);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQuerySessionInformation(
        IntPtr hServer,
        int sessionId,
        WTS_INFO_CLASS wtsInfoClass,
        out IntPtr ppBuffer,
        out int pBytesReturned);

    /// <summary>
    /// 現在のプロセスのセッションIDを取得
    /// </summary>
    public static int GetCurrentSessionId()
    {
        return System.Diagnostics.Process.GetCurrentProcess().SessionId;
    }

    /// <summary>
    /// すべてのセッション情報を取得
    /// </summary>
    public static List<SessionInfo> GetAllSessions()
    {
        var sessions = new List<SessionInfo>();
        IntPtr ppSessionInfo = IntPtr.Zero;
        int count = 0;

        try
        {
            if (WTSEnumerateSessions(IntPtr.Zero, 0, 1, ref ppSessionInfo, ref count))
            {
                IntPtr current = ppSessionInfo;
                int sessionInfoSize = Marshal.SizeOf(typeof(WTS_SESSION_INFO));

                for (int i = 0; i < count; i++)
                {
                    var sessionInfo = Marshal.PtrToStructure<WTS_SESSION_INFO>(current);

                    var session = new SessionInfo
                    {
                        SessionId = sessionInfo.SessionId,
                        State = sessionInfo.State,
                        UserName = GetSessionInfo(sessionInfo.SessionId, WTS_INFO_CLASS.WTSUserName),
                        DomainName = GetSessionInfo(sessionInfo.SessionId, WTS_INFO_CLASS.WTSDomainName)
                    };

                    sessions.Add(session);
                    current = IntPtr.Add(current, sessionInfoSize);
                }
            }
        }
        finally
        {
            if (ppSessionInfo != IntPtr.Zero)
            {
                WTSFreeMemory(ppSessionInfo);
            }
        }

        return sessions;
    }

    /// <summary>
    /// アクティブなユーザーセッションを取得
    /// </summary>
    public static SessionInfo? GetActiveUserSession()
    {
        var sessions = GetAllSessions();

        // アクティブなセッションでユーザー名が空でないものを探す
        return sessions.FirstOrDefault(s => 
            s.IsActive && 
            !string.IsNullOrWhiteSpace(s.UserName));
    }

    /// <summary>
    /// 現在のセッション情報を取得
    /// </summary>
    public static SessionInfo? GetCurrentSession()
    {
        int currentSessionId = GetCurrentSessionId();
        var sessions = GetAllSessions();
        return sessions.FirstOrDefault(s => s.SessionId == currentSessionId);
    }

    private static string GetSessionInfo(int sessionId, WTS_INFO_CLASS infoClass)
    {
        IntPtr buffer = IntPtr.Zero;
        int bytesReturned = 0;

        try
        {
            if (WTSQuerySessionInformation(IntPtr.Zero, sessionId, infoClass, out buffer, out bytesReturned))
            {
                return Marshal.PtrToStringAnsi(buffer) ?? string.Empty;
            }
        }
        finally
        {
            if (buffer != IntPtr.Zero)
            {
                WTSFreeMemory(buffer);
            }
        }

        return string.Empty;
    }
}
