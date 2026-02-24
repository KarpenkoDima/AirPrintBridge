using AirPrintBridge;

var builder = Host.CreateApplicationBuilder(args);

// Чтобы работало как Windows Service (фоновый процесс)
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "AirPrint Bridge";
});

// Конфигурация нашего принтера бер1ётся из appsettings.json
builder.Services.Configure<PrinterConfig>(builder.Configuration.GetSection("Printer"));

// Два независимых hosted service: один занимается mDNS, другой -  IPP сервером
builder.Services.AddHostedService<MdnsAdvertiser>();
builder.Services.AddHostedService<IppServer>();

builder.Services.AddSingleton<WindowsPrintDispatcher>();

var host = builder.Build();
host.Run();
