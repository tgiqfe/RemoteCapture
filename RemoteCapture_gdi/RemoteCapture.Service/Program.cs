using RemoteCapture.Lib.WebSocket;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Hosting.WindowsServices;

var builder = WebApplication.CreateBuilder(args);

// Windowsサービスとして実行できるように設定
builder.Host.UseWindowsService();

// ログ設定を追加
builder.Logging.AddConsole();
builder.Logging.AddEventLog();

var app = builder.Build();

var logger = app.Logger;

// セッション情報をログ出力
var sessionId = GetCurrentSessionId();
var isWindowsService = WindowsServiceHelpers.IsWindowsService();
var isSystemAccount = IsRunningAsSystem();

logger.LogInformation($"Starting RemoteCapture Service in Session: {sessionId}");
logger.LogInformation($"Is Windows Service: {isWindowsService}");
logger.LogInformation($"Is SYSTEM Account: {isSystemAccount}");

// LogonUIキャプチャの可否を判定
bool captureLogonUI = isSystemAccount && sessionId > 0;

if (captureLogonUI)
{
    logger.LogInformation("LogonUI capture enabled (SYSTEM account in user session)");
}
else if (isWindowsService && sessionId == 0)
{
    logger.LogWarning("WARNING: Running as Windows Service in Session 0. GDI screen capture may not work.");
    logger.LogWarning("To capture user desktop, configure the service to run under a user account:");
    logger.LogWarning("1. Open Services (services.msc)");
    logger.LogWarning("2. Find 'RemoteCapture Service'");
    logger.LogWarning("3. Right-click -> Properties -> Log On tab");
    logger.LogWarning("4. Select 'This account' and enter user credentials");
}

app.UseWebSockets();

app.Map("/screen", async context =>
{
    if (context.WebSockets.IsWebSocketRequest)
    {
        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        var server = new ScreenStreamServer(fps: 30, quality: 75, captureLogonUI: captureLogonUI);
        await server.StreamScreenAsync(webSocket);
    }
    else
    {
        context.Response.StatusCode = 400;
    }
});

app.Run("http://0.0.0.0:5000");

[DllImport("kernel32.dll")]
static extern uint GetCurrentProcessId();

[DllImport("kernel32.dll", SetLastError = true)]
static extern uint ProcessIdToSessionId(uint dwProcessId, out uint pSessionId);

[DllImport("advapi32.dll", SetLastError = true)]
static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

[DllImport("advapi32.dll", SetLastError = true)]
static extern bool GetTokenInformation(IntPtr TokenHandle, int TokenInformationClass, IntPtr TokenInformation, uint TokenInformationLength, out uint ReturnLength);

[DllImport("kernel32.dll")]
static extern IntPtr GetCurrentProcess();

[DllImport("kernel32.dll")]
static extern bool CloseHandle(IntPtr hObject);

const uint TOKEN_QUERY = 0x0008;
const int TokenUser = 1;

static int GetCurrentSessionId()
{
    uint sessionId = 0;
    uint processId = GetCurrentProcessId();
    ProcessIdToSessionId(processId, out sessionId);
    return (int)sessionId;
}

static bool IsRunningAsSystem()
{
    IntPtr tokenHandle = IntPtr.Zero;
    try
    {
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, out tokenHandle))
            return false;

        uint returnLength;
        GetTokenInformation(tokenHandle, TokenUser, IntPtr.Zero, 0, out returnLength);

        IntPtr tokenInfo = Marshal.AllocHGlobal((int)returnLength);
        try
        {
            if (GetTokenInformation(tokenHandle, TokenUser, tokenInfo, returnLength, out returnLength))
            {
                var tokenUser = Marshal.PtrToStructure<TOKEN_USER>(tokenInfo);
                IntPtr sidString;
                if (ConvertSidToStringSid(tokenUser.User.Sid, out sidString))
                {
                    string sidStr = Marshal.PtrToStringAuto(sidString) ?? "";
                    LocalFree(sidString);
                    // S-1-5-18 is the SID for SYSTEM account
                    return sidStr == "S-1-5-18";
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(tokenInfo);
        }
    }
    finally
    {
        if (tokenHandle != IntPtr.Zero)
            CloseHandle(tokenHandle);
    }

    return false;
}

[DllImport("advapi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
static extern bool ConvertSidToStringSid(IntPtr pSid, out IntPtr ptrSid);

[DllImport("kernel32.dll")]
static extern IntPtr LocalFree(IntPtr hMem);

[StructLayout(LayoutKind.Sequential)]
struct TOKEN_USER
{
    public SID_AND_ATTRIBUTES User;
}

[StructLayout(LayoutKind.Sequential)]
struct SID_AND_ATTRIBUTES
{
    public IntPtr Sid;
    public uint Attributes;
}
