using Makaretu.Dns;
using Microsoft.Extensions.Options;
using System.Threading;

public class MdnsAdvertiser :BackgroundService
{
    private readonly ILogger<MdnsAdvertiser> _logger;
    private readonly PrinterConfig _printerConfig;
    private  MulticastService _mdns;
    private  ServiceDiscovery _sd;

    public MdnsAdvertiser(ILogger<MdnsAdvertiser> logger, IOptions<PrinterConfig> options)
    {
        this._logger = logger;
        _printerConfig = options.Value; // Извлекаем сам объект из обертки
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Находим IP (ваш новый 192.168.11.15)
        var localIp = MulticastService.GetIPAddresses()
            .FirstOrDefault(x => x.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                                 && x.ToString().StartsWith("192.168")); // ОБРАТИТЕ ВНИМАНИЕ НА ПОДСЕТЬ

        if (localIp == null) return Task.CompletedTask;

        _mdns = new MulticastService();
        _sd = new ServiceDiscovery(_mdns);

        var profile = new ServiceProfile(
            instanceName: _printerConfig.DisplayName,
            serviceName: "_ipp._tcp",
            port: (ushort)_printerConfig.IppPort
        );

        profile.Resources.Clear();

        var hostName = "AirPrint-Bridge-Server.local";

        // 1. Указываем хост
        profile.Resources.Add(new SRVRecord
        {
            Name = profile.FullyQualifiedName,
            Port = (ushort)_printerConfig.IppPort,
            Target = hostName
        });

        // 2. Привязываем IP
        profile.Resources.Add(new ARecord
        {
            Name = hostName,
            Address = localIp
        });

        // 3. Сообщаем, что мы AirPrint совместимы
        profile.Resources.Add(new PTRRecord
        {
            Name = "_universal._sub._ipp._tcp.local",
            DomainName = profile.FullyQualifiedName
        });

        // 4. ИДЕАЛЬНЫЕ TXT-ЗАПИСИ
        // Создаем TXTRecord вручную, чтобы iPhone получил их в одном пакете
        var txt = new TXTRecord { Name = profile.FullyQualifiedName };
        txt.Strings.Add("txtvers=1");
        txt.Strings.Add("qtotal=1");
        // rp должно точно совпадать с маршрутом в IppServer
        txt.Strings.Add($"rp={_printerConfig.ResourcePath}");
        txt.Strings.Add($"ty={_printerConfig.DisplayName}");
        txt.Strings.Add("note=Windows Shared Printer");
        txt.Strings.Add("product=(Canon MF3010)");

        // Только PDF. URF убран намеренно: сервер не умеет декодировать растровый
        // формат Apple. Без URF в pdl iOS переключается на PDF, который умеем печатать
        // через PdfiumViewer. _universal._sub._ipp._tcp достаточен для AirPrint-совместимости.
        txt.Strings.Add("pdl=application/pdf");

        txt.Strings.Add("air=none");
        txt.Strings.Add("UUID=5365e660-f657-41a6-88a4-0994132ad372");

        // Дополнительные параметры
        txt.Strings.Add("Color=F"); // Canon MF3010 — монохромный принтер
        txt.Strings.Add("Duplex=F");
        txt.Strings.Add("Scan=F");

        profile.Resources.Add(txt);

        _sd.Advertise(profile);
        _mdns.Start();

        _logger.LogInformation("mDNS started on {IP}:{Port}", localIp, _printerConfig.IppPort);

        return Task.CompletedTask;
        /* _logger.LogInformation("Starting mDNS advertiser for '{Name}'", _printerConfig.DisplayName);

         var localIp = MulticastService.GetIPAddresses()
             .FirstOrDefault(x => x.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                                  && x.ToString().StartsWith("192.168.11"));

         if (localIp == null)
         {
             _logger.LogError("No local IP found starting with 192.168.11.x");
             return Task.CompletedTask;
         }

         _mdns = new MulticastService();
         _sd = new ServiceDiscovery(_mdns);

         var profile = new ServiceProfile(
             instanceName: _printerConfig.DisplayName,
             serviceName: "_ipp._tcp",
             port: (ushort)_printerConfig.IppPort
         );

         profile.Resources.Clear();

         var hostName = "AirPrint-Bridge-Server.local";

         // 1. SRV запись
         profile.Resources.Add(new SRVRecord
         {
             Name = profile.FullyQualifiedName,
             Port = (ushort)_printerConfig.IppPort,
             Target = hostName
         });

         // 2. A запись (IP)
         profile.Resources.Add(new ARecord
         {
             Name = hostName,
             Address = localIp
         });

         // 3. TXT запись — КРИТИЧЕСКИ ВАЖНО ДЛЯ AIRPRINT
         var txt = new TXTRecord { Name = profile.FullyQualifiedName };

         // ОБЯЗАТЕЛЬНЫЕ ПОЛЯ:
         txt.Strings.Add("txtvers=1");                                    // Версия TXT формата
         txt.Strings.Add($"rp={_printerConfig.ResourcePath}");            // Resource path
         txt.Strings.Add($"ty={_printerConfig.DisplayName}");             // Printer type
         txt.Strings.Add("pdl=application/pdf,image/urf");                // Supported formats
         txt.Strings.Add("qtotal=1");                                     // Number of queues

         // URF — Apple Universal Raster Format capabilities
         // CP1 = CMYK/RGB, W8 = 8-bit grayscale, SRGB24 = sRGB color
         // RS600 = 600dpi, DM1 = duplex manual
         txt.Strings.Add("URF=V1.4,CP1,W8,SRGB24,RS600");

         // ВОЗМОЖНОСТИ ПРИНТЕРА:
         txt.Strings.Add("Color=T");                                      // Supports color
         txt.Strings.Add("Duplex=F");                                     // No duplex (MF3010)
         txt.Strings.Add("Scan=F");                                       // No scanner
         txt.Strings.Add("Fax=F");                                        // No fax
         txt.Strings.Add("Copies=T");                                     // Supports copies
         txt.Strings.Add("Collate=F");                                    // No collate
         txt.Strings.Add("Bind=F");                                       // No binding
         txt.Strings.Add("Sort=F");                                       // No sorting
         txt.Strings.Add("Staple=F");                                     // No stapling
         txt.Strings.Add("Punch=F");                                      // No punching

         // MEDIA SUPPORT:
         txt.Strings.Add("PaperMax=legal-A4");                            // Max paper size
         txt.Strings.Add("kind=document,photo");                          // Print types

         // PRINTER STATE:
         txt.Strings.Add("priority=50");                                  // Priority
         txt.Strings.Add("note=Windows Shared Printer via AirPrint");    // Description

         // AUTHENTICATION & SECURITY:
         txt.Strings.Add("air=none");                                     // No auth required
         txt.Strings.Add("TLS=1.2");                                      // TLS version (optional)

         // UUID — фиксированный, не меняем между перезапусками
         txt.Strings.Add("UUID=5365e660-f657-41a6-88a4-0994132ad372");

         // ADMINISTRATIVE:
         txt.Strings.Add("adminurl=http://192.168.0.107:631/");           // Admin page
         txt.Strings.Add("product=(Canon MF3010)");                       // Product name

         profile.Resources.Add(txt);

         // 4. PTR для универсального поиска (_universal._sub._ipp._tcp)
         profile.Resources.Add(new PTRRecord
         {
             Name = "_universal._sub._ipp._tcp.local",
             DomainName = profile.FullyQualifiedName
         });

         // 5. PTR для основного сервиса
         profile.Resources.Add(new PTRRecord
         {
             Name = "_ipp._tcp.local",
             DomainName = profile.FullyQualifiedName
         });

         _sd.Advertise(profile);
         _mdns.Start();

         _logger.LogInformation(
             "mDNS advertising started. Printer '{Name}' on {IP}:{Port}",
             _printerConfig.DisplayName, localIp, _printerConfig.IppPort);

         return Task.CompletedTask;*/
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping mDNS advertiser");
        _sd?.Dispose();
        _mdns?.Stop();
        return Task.CompletedTask;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return StartAsync(stoppingToken);
    }
}