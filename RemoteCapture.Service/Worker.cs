using RemoteCapture.Lib.CaptureService;
using RemoteCapture.Lib.WebSocket;

namespace RemoteCapture.Service;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IConfiguration _configuration;
    private WebSocketServer? _webSocketServer;
    private CaptureServiceHelper? _captureHelper;
    private int _frameRate;
    private int _jpegQuality;

    public Worker(ILogger<Worker> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("RemoteCapture Service is starting...");

        // Load configuration
        var port = _configuration.GetValue<int>("RemoteCapture:WebSocketPort", 8080);
        var maxClients = _configuration.GetValue<int>("RemoteCapture:MaxClients", 4);
        _frameRate = _configuration.GetValue<int>("RemoteCapture:FrameRate", 30);
        _jpegQuality = _configuration.GetValue<int>("RemoteCapture:JpegQuality", 75);
        var monitorIndex = _configuration.GetValue<int>("RemoteCapture:MonitorIndex", 0);

        try
        {
            // Initialize WebSocket server
            _webSocketServer = new WebSocketServer(port, maxClients);
            _webSocketServer.MouseEventReceived += OnMouseEventReceived;
            _webSocketServer.KeyboardEventReceived += OnKeyboardEventReceived;
            await _webSocketServer.StartAsync();

            // Initialize capture
            var monitors = CaptureServiceHelper.GetMonitors().ToList();

            if (monitors.Count == 0)
            {
                throw new InvalidOperationException("No monitors found");
            }

            var selectedMonitor = monitorIndex < monitors.Count 
                ? monitors[monitorIndex] 
                : monitors[0];

            _logger.LogInformation($"Capturing monitor: {selectedMonitor.DeviceName}");

            _captureHelper = new CaptureServiceHelper();
            _captureHelper.StartCapture(selectedMonitor.Hmon);

            _logger.LogInformation("RemoteCapture Service started successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start RemoteCapture Service");
            throw;
        }

        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var frameInterval = TimeSpan.FromMilliseconds(1000.0 / _frameRate);

        _logger.LogInformation("RemoteCapture Service is running");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_webSocketServer != null && 
                    _webSocketServer.IsRunning && 
                    _webSocketServer.ConnectedClientCount > 0 &&
                    _captureHelper != null)
                {
                    // Capture and send frame
                    var frameData = _captureHelper.GetCurrentFrameAsJpeg(_jpegQuality);
                    if (frameData != null && frameData.Length > 0)
                    {
                        await _webSocketServer.BroadcastImageAsync(frameData);
                    }
                }

                await Task.Delay(frameInterval, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error in capture loop");
                await Task.Delay(1000, stoppingToken);
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("RemoteCapture Service is stopping...");

        try
        {
            _captureHelper?.Dispose();
            _webSocketServer?.Dispose();
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
            InputSimulator.SimulateMouseEvent(e);
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
            InputSimulator.SimulateKeyboardEvent(e);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing keyboard event");
        }
    }
}
