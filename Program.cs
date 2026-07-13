using AirPrintBridge;
using Microsoft.Extensions.Hosting.WindowsServices;
using System.Runtime.Versioning;

[assembly: SupportedOSPlatform("windows")]

var builder = Host.CreateApplicationBuilder(args);

// Чтобы работало как Windows Service (фоновый процесс)
if (WindowsServiceHelpers.IsWindowsService())
{
    builder.Services.AddWindowsService(options =>
    {
        options.ServiceName = "AirPrint Bridge";
    });
}

// Конфигурация принтера берётся из appsettings.json.
builder.Services.Configure<PrinterConfig>(builder.Configuration.GetSection("Printer"));
builder.Services.AddSingleton<PrinterRuntime>();

// Два независимых hosted service: один занимается mDNS, другой -  IPP сервером
builder.Services.AddHostedService<MdnsAdvertiser>();
builder.Services.AddHostedService<IppServer>();

builder.Services.AddSingleton<WindowsPrintDispatcher>();

var host = builder.Build();
host.Run();
