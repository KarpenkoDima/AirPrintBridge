using Makaretu.Dns;

namespace AirPrintBridge;

public sealed class MdnsAdvertiser : BackgroundService
{
    private readonly ILogger<MdnsAdvertiser> _logger;
    private readonly PrinterRuntime _printer;
    private MulticastService? _mdns;
    private ServiceDiscovery? _discovery;

    public MdnsAdvertiser(ILogger<MdnsAdvertiser> logger, PrinterRuntime printer)
    {
        _logger = logger;
        _printer = printer;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _mdns = new MulticastService();
        _discovery = new ServiceDiscovery(_mdns);

        var profile = new ServiceProfile(
            _printer.DisplayName,
            "_ipp._tcp",
            (ushort)_printer.Config.IppPort);

        profile.Resources.Clear();
        profile.Resources.Add(new SRVRecord
        {
            Name = profile.FullyQualifiedName,
            Port = (ushort)_printer.Config.IppPort,
            Target = _printer.HostName
        });
        profile.Resources.Add(new ARecord
        {
            Name = _printer.HostName,
            Address = _printer.LanAddress
        });
        profile.Subtypes.Add("_universal");

        var txt = new TXTRecord { Name = profile.FullyQualifiedName };
        txt.Strings.Add("txtvers=1");
        txt.Strings.Add("qtotal=1");
        txt.Strings.Add($"rp={_printer.ResourcePath.TrimStart('/')}");
        txt.Strings.Add($"ty={_printer.DisplayName}");
        txt.Strings.Add("note=Windows printer via AirPrint Bridge");
        txt.Strings.Add($"product=({_printer.WindowsPrinterName})");
        txt.Strings.Add("pdl=application/pdf,image/urf");
        txt.Strings.Add($"URF={BuildUrfCapabilities()}");
        txt.Strings.Add("air=none");
        txt.Strings.Add($"UUID={_printer.Uuid:D}");
        txt.Strings.Add($"Color={ToTxtBool(_printer.SupportsColor)}");
        txt.Strings.Add($"Duplex={ToTxtBool(_printer.SupportsDuplex)}");
        txt.Strings.Add("Scan=F");
        txt.Strings.Add("Fax=F");
        profile.Resources.Add(txt);

        _discovery.Advertise(profile);

        // Makaretu announces subtypes but does not answer this AirPrint discovery query.
        var subtypeFqdn = "_universal._sub._ipp._tcp.local";
        _mdns.QueryReceived += (_, e) =>
        {
            if (!e.Message.Questions.Any(q => q.Type == DnsType.PTR &&
                    string.Equals(q.Name.ToString().TrimEnd('.'), subtypeFqdn,
                        StringComparison.OrdinalIgnoreCase)))
                return;

            var response = new Message { AA = true };
            response.Answers.Add(new PTRRecord
            {
                Name = subtypeFqdn,
                DomainName = profile.FullyQualifiedName,
                TTL = TimeSpan.FromMinutes(75)
            });
            _mdns.SendAnswer(response);
        };

        _mdns.Start();
        _logger.LogInformation(
            "Advertising '{DisplayName}' at {Uri} ({Address}); Windows queue: '{Queue}'",
            _printer.DisplayName, _printer.PrinterUri, _printer.LanAddress, _printer.WindowsPrinterName);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal service shutdown.
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping mDNS advertiser");
        _discovery?.Dispose();
        _mdns?.Stop();
        return base.StopAsync(cancellationToken);
    }

    private string BuildUrfCapabilities()
    {
        var values = new List<string> { "V1.4", "W8", "RS600" };
        values.Add(_printer.SupportsColor ? "SRGB24" : "CP1");
        if (_printer.SupportsDuplex)
            values.Add("DM1");
        return string.Join(',', values);
    }

    private static string ToTxtBool(bool value) => value ? "T" : "F";
}
