using RemoteCapture.Service;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "RemoteCapture Service";
});
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
