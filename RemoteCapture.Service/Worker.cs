using RemoteCapture.Lib;
using RemoteCapture.Lib.CaptureService;
using RemoteCapture.Lib.WebSocket;
using RemoteCapture.Lib.WindowsRuntimeHelpers;
using Windows.System;

namespace RemoteCapture.Service;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IConfiguration _configuration;
    private WebSocketServer? _webSocketServer;
    private CaptureServiceHelper? _captureHelper;
    private int _frameRate;
    private int _jpegQuality;
    private DispatcherQueueController? _dispatcherQueueController;
    private Thread? _captureThread;
    private readonly ManualResetEventSlim _captureInitialized = new ManualResetEventSlim(false);
    private Exception? _captureInitException;

    public Worker(ILogger<Worker> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        RemoteCapture.Lib.Logger.Info("RemoteCapture Service is starting...");
        _logger.LogInformation("RemoteCapture Service is starting...");

        // Load configuration
        _frameRate = _configuration.GetValue<int>("RemoteCapture:FrameRate", 30);
        _jpegQuality = _configuration.GetValue<int>("RemoteCapture:JpegQuality", 75);

        await base.StartAsync(cancellationToken);

        RemoteCapture.Lib.Logger.Info("RemoteCapture Service start initiated");
        _logger.LogInformation("RemoteCapture Service start initiated");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        RemoteCapture.Lib.Logger.Info("ExecuteAsync started");
        _logger.LogInformation("ExecuteAsync started");

        try
        {
            // Initialize WebSocket server and capture in ExecuteAsync
            // to avoid timeout in StartAsync
            await InitializeServicesAsync(stoppingToken);

            var frameInterval = TimeSpan.FromMilliseconds(1000.0 / _frameRate);

            _logger.LogInformation("RemoteCapture Service is running");
            RemoteCapture.Lib.Logger.Info($"Starting capture loop with frame rate: {_frameRate} fps, JPEG quality: {_jpegQuality}");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (_webSocketServer != null && 
                        _webSocketServer.IsRunning && 
                        _webSocketServer.ConnectedClientCount > 0 &&
                        _captureHelper != null)
                    {
                        RemoteCapture.Lib.Logger.Debug($"Attempting to capture frame (clients: {_webSocketServer.ConnectedClientCount})");

                        // Capture and send frame
                        var frameData = _captureHelper.GetCurrentFrameAsJpeg(_jpegQuality);
                        if (frameData != null && frameData.Length > 0)
                        {
                            RemoteCapture.Lib.Logger.Debug($"Frame captured: {frameData.Length} bytes, broadcasting to {_webSocketServer.ConnectedClientCount} client(s)");
                            await _webSocketServer.BroadcastImageAsync(frameData);
                            RemoteCapture.Lib.Logger.Debug("Frame broadcast completed");
                        }
                        else
                        {
                            RemoteCapture.Lib.Logger.Warning($"Frame capture returned null or empty data");
                        }
                    }
                    else
                    {
                        if (_webSocketServer == null)
                            RemoteCapture.Lib.Logger.Debug("Skipping capture: WebSocketServer is null");
                        else if (!_webSocketServer.IsRunning)
                            RemoteCapture.Lib.Logger.Debug("Skipping capture: WebSocketServer is not running");
                        else if (_webSocketServer.ConnectedClientCount == 0)
                            RemoteCapture.Lib.Logger.Debug("Skipping capture: No connected clients");
                        else if (_captureHelper == null)
                            RemoteCapture.Lib.Logger.Debug("Skipping capture: CaptureHelper is null");
                    }

                    await Task.Delay(frameInterval, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    RemoteCapture.Lib.Logger.Error($"Error in capture loop: {ex.GetType().Name} - {ex.Message}");
                    _logger.LogError(ex, "Error in capture loop");
                    await Task.Delay(1000, stoppingToken);
                }
            }
        }
        catch (Exception ex)
        {
            RemoteCapture.Lib.Logger.Error($"Fatal error in ExecuteAsync: {ex.GetType().Name} - {ex.Message}");
            RemoteCapture.Lib.Logger.Error($"Stack trace: {ex.StackTrace}");
            _logger.LogCritical(ex, "Fatal error in ExecuteAsync - service will terminate");
            throw;
        }
    }

    private async Task InitializeServicesAsync(CancellationToken cancellationToken)
    {
        RemoteCapture.Lib.Logger.Info("Initializing RemoteCapture services...");
        _logger.LogInformation("Initializing RemoteCapture services...");

        // Log session information and check session compatibility
        try
        {
            var currentSession = SessionHelper.GetCurrentSession();
            if (currentSession != null)
            {
                RemoteCapture.Lib.Logger.Info($"Current session: ID={currentSession.SessionId}, User={currentSession.UserName}, Domain={currentSession.DomainName}, State={currentSession.State}");
                _logger.LogInformation($"Current session: ID={currentSession.SessionId}, User={currentSession.UserName}, Domain={currentSession.DomainName}, State={currentSession.State}");
            }

            var activeSession = SessionHelper.GetActiveUserSession();
            if (activeSession != null)
            {
                RemoteCapture.Lib.Logger.Info($"Active user session: ID={activeSession.SessionId}, User={activeSession.UserName}, Domain={activeSession.DomainName}");
                _logger.LogInformation($"Active user session: ID={activeSession.SessionId}, User={activeSession.UserName}, Domain={activeSession.DomainName}");
            }
            else
            {
                RemoteCapture.Lib.Logger.Warning("No active user session found. Service may not function correctly.");
                _logger.LogWarning("No active user session found. Service may not function correctly.");
            }

            // Check if current session matches active user session
            if (currentSession != null && activeSession != null)
            {
                if (currentSession.SessionId != activeSession.SessionId)
                {
                    var errorMessage = $"CRITICAL: Service is running in session {currentSession.SessionId}, but active user session is {activeSession.SessionId}. " +
                                     $"Windows Graphics Capture API cannot capture across sessions. " +
                                     $"Please run this service using Task Scheduler with 'Run only when user is logged on' option, " +
                                     $"or see README.md for alternative solutions.";

                    RemoteCapture.Lib.Logger.Error(errorMessage);
                    _logger.LogError(errorMessage);

                    throw new InvalidOperationException(errorMessage);
                }
                else
                {
                    RemoteCapture.Lib.Logger.Info($"Session check passed: Service is running in the active user session (ID={currentSession.SessionId})");
                    _logger.LogInformation($"Session check passed: Service is running in the active user session (ID={currentSession.SessionId})");
                }
            }
        }
        catch (Exception sessionEx)
        {
            RemoteCapture.Lib.Logger.Error($"Failed to get session information: {sessionEx.Message}");
            _logger.LogError(sessionEx, "Failed to get session information");
            throw;
        }

        var port = _configuration.GetValue<int>("RemoteCapture:WebSocketPort", 8080);
        var maxClients = _configuration.GetValue<int>("RemoteCapture:MaxClients", 4);
        var monitorIndex = _configuration.GetValue<int>("RemoteCapture:MonitorIndex", 0);

        try
        {
            // Initialize WebSocket server
            _logger.LogInformation($"Initializing WebSocket server on port {port}...");
            _webSocketServer = new WebSocketServer(port, maxClients);
            _webSocketServer.MouseEventReceived += OnMouseEventReceived;
            _webSocketServer.KeyboardEventReceived += OnKeyboardEventReceived;
            await _webSocketServer.StartAsync();
            _logger.LogInformation("WebSocket server initialized successfully");

            // Initialize capture
            RemoteCapture.Lib.Logger.Info("Enumerating monitors using Windows Graphics Capture API...");
            _logger.LogInformation("Enumerating monitors using Windows Graphics Capture API...");

            try
            {
                var monitors = CaptureServiceHelper.GetMonitors().ToList();
                RemoteCapture.Lib.Logger.Info($"Found {monitors.Count} monitor(s)");
                _logger.LogInformation($"Found {monitors.Count} monitor(s)");

                if (monitors.Count == 0)
                {
                    throw new InvalidOperationException("No monitors found");
                }

                foreach (var monitor in monitors)
                {
                    RemoteCapture.Lib.Logger.Info($"Monitor: {monitor.DeviceName}, Primary={monitor.IsPrimary}, Size={monitor.Width}x{monitor.Height}");
                    _logger.LogInformation($"Monitor: {monitor.DeviceName}, Primary={monitor.IsPrimary}, Size={monitor.Width}x{monitor.Height}");
                }

                var selectedMonitor = monitorIndex < monitors.Count 
                    ? monitors[monitorIndex] 
                    : monitors[0];

                RemoteCapture.Lib.Logger.Info($"Selected monitor: {selectedMonitor.DeviceName}");
                _logger.LogInformation($"Selected monitor: {selectedMonitor.DeviceName}");

                // Start capture on a dedicated STA thread with DispatcherQueue
                RemoteCapture.Lib.Logger.Info("Creating capture thread with DispatcherQueue...");
                _logger.LogInformation("Creating capture thread with DispatcherQueue...");

                _captureThread = new Thread(() => CaptureThreadProc(selectedMonitor.Hmon));
                _captureThread.SetApartmentState(ApartmentState.STA);
                _captureThread.IsBackground = true;
                _captureThread.Start();

                // Wait for capture to initialize or fail
                _captureInitialized.Wait();

                if (_captureInitException != null)
                {
                    throw _captureInitException;
                }

                RemoteCapture.Lib.Logger.Info("Windows Graphics Capture started successfully");
                _logger.LogInformation("Windows Graphics Capture started successfully");
            }
            catch (Exception monitorEx)
            {
                RemoteCapture.Lib.Logger.Error($"Failed to initialize capture: {monitorEx.GetType().Name} - {monitorEx.Message}");
                RemoteCapture.Lib.Logger.Error($"Stack trace: {monitorEx.StackTrace}");
                _logger.LogError(monitorEx, $"Failed to initialize capture. Exception type: {monitorEx.GetType().Name}, Message: {monitorEx.Message}");
                if (monitorEx.InnerException != null)
                {
                    RemoteCapture.Lib.Logger.Error($"Inner exception: {monitorEx.InnerException.Message}");
                    _logger.LogError(monitorEx.InnerException, $"Inner exception: {monitorEx.InnerException.Message}");
                }
                throw;
            }

            RemoteCapture.Lib.Logger.Info("RemoteCapture services initialized successfully");
            _logger.LogInformation("RemoteCapture services initialized successfully");
        }
        catch (Exception ex)
        {
            RemoteCapture.Lib.Logger.Error($"CRITICAL: Failed to initialize services: {ex.GetType().Name} - {ex.Message}");
            RemoteCapture.Lib.Logger.Error($"Stack trace: {ex.StackTrace}");
            _logger.LogCritical(ex, $"CRITICAL: Failed to initialize RemoteCapture services. Type: {ex.GetType().Name}, Message: {ex.Message}");
            throw;
        }
    }

    private void CaptureThreadProc(nint monitorHandle)
    {
        try
        {
            RemoteCapture.Lib.Logger.Info("Capture thread started, creating DispatcherQueue...");

            // Create DispatcherQueue for the current thread
            _dispatcherQueueController = CoreMessagingHelper.CreateDispatcherQueueControllerForCurrentThread();

            if (_dispatcherQueueController == null)
            {
                throw new InvalidOperationException("Failed to create DispatcherQueueController");
            }

            RemoteCapture.Lib.Logger.Info("DispatcherQueue created successfully");

            // Initialize capture on this STA thread
            RemoteCapture.Lib.Logger.Info("Creating CaptureServiceHelper...");
            _captureHelper = new CaptureServiceHelper();

            RemoteCapture.Lib.Logger.Info("Starting capture...");
            _captureHelper.StartCapture(monitorHandle);

            RemoteCapture.Lib.Logger.Info("Capture initialized successfully on STA thread");
            _captureInitialized.Set();

            // Message loop - keep the thread alive for FrameArrived events
            RemoteCapture.Lib.Logger.Info("Starting message loop...");
            System.Windows.Forms.Application.Run();
        }
        catch (Exception ex)
        {
            RemoteCapture.Lib.Logger.Error($"Capture thread exception: {ex.GetType().Name} - {ex.Message}");
            RemoteCapture.Lib.Logger.Error($"Stack trace: {ex.StackTrace}");
            _captureInitException = ex;
            _captureInitialized.Set();
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("RemoteCapture Service is stopping...");

        try
        {
            // Stop the capture thread's message loop
            if (_captureThread != null && _captureThread.IsAlive)
            {
                System.Windows.Forms.Application.ExitThread();
                if (!_captureThread.Join(TimeSpan.FromSeconds(5)))
                {
                    RemoteCapture.Lib.Logger.Warning("Capture thread did not exit gracefully");
                }
            }

            _captureHelper?.Dispose();
            _webSocketServer?.Dispose();
            _dispatcherQueueController?.ShutdownQueueAsync();
            _captureInitialized?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during cleanup");
        }

        await base.StopAsync(cancellationToken);
        _logger.LogInformation("RemoteCapture Service stopped");
    }

    private void OnMouseEventReceived(object? sender, MouseEventMessage e)
    {
        try
        {
            MouseSimulator.ExecuteMouseEvent(e);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing mouse event");
        }
    }

    private void OnKeyboardEventReceived(object? sender, KeyboardEventMessage e)
    {
        try
        {
            KeyboardSimulator.ExecuteKeyboardEvent(e);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing keyboard event");
        }
    }
}
