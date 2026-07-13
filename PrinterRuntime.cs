using Microsoft.Extensions.Options;
using System.Drawing.Printing;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace AirPrintBridge;

/// <summary>
/// Validated printer and LAN endpoint shared by mDNS, IPP and the Windows spooler.
/// </summary>
public sealed class PrinterRuntime
{
    public PrinterRuntime(IOptions<PrinterConfig> options)
    {
        Config = options.Value;
        if (Config.IppPort is < 1 or > 65535)
            throw new InvalidOperationException("Printer:IppPort must be between 1 and 65535.");

        WindowsPrinterName = ResolvePrinterName(Config.WindowsPrinterName);
        var settings = new PrinterSettings { PrinterName = WindowsPrinterName };
        if (!settings.IsValid)
            throw new InvalidOperationException($"Windows printer '{WindowsPrinterName}' is not available.");

        DisplayName = string.IsNullOrWhiteSpace(Config.DisplayName)
            ? $"{WindowsPrinterName} (AirPrint)"
            : Config.DisplayName.Trim();
        ResourcePath = NormalizeResourcePath(Config.ResourcePath);
        LanAddress = ResolveLanAddress(Config.PreferredIpAddress);
        SupportsColor = settings.SupportsColor;
        SupportsDuplex = settings.CanDuplex;
        MaximumCopies = Math.Clamp(settings.MaximumCopies, 1, 999);
        Uuid = CreateStableUuid($"{Environment.MachineName}\n{WindowsPrinterName}");
        HostName = $"airprint-{SanitizeDnsLabel(Environment.MachineName)}.local";
    }

    public PrinterConfig Config { get; }
    public string WindowsPrinterName { get; }
    public string DisplayName { get; }
    public string ResourcePath { get; }
    public IPAddress LanAddress { get; }
    public bool SupportsColor { get; }
    public bool SupportsDuplex { get; }
    public int MaximumCopies { get; }
    public Guid Uuid { get; }
    public string HostName { get; }
    public string PrinterUri => $"ipp://{HostName}:{Config.IppPort}{ResourcePath}";

    private static string ResolvePrinterName(string configuredName)
    {
        var installed = PrinterSettings.InstalledPrinters.Cast<string>().ToArray();
        if (installed.Length == 0)
            throw new InvalidOperationException("No Windows printers are installed.");

        if (!string.IsNullOrWhiteSpace(configuredName))
        {
            var match = installed.FirstOrDefault(p =>
                string.Equals(p, configuredName.Trim(), StringComparison.OrdinalIgnoreCase));
            return match ?? throw new InvalidOperationException(
                $"Windows printer '{configuredName}' was not found. Installed: {string.Join(", ", installed)}");
        }

        var defaultPrinter = new PrinterSettings().PrinterName;
        return installed.FirstOrDefault(p =>
                   string.Equals(p, defaultPrinter, StringComparison.OrdinalIgnoreCase))
               ?? installed[0];
    }

    private static IPAddress ResolveLanAddress(string configuredAddress)
    {
        if (!string.IsNullOrWhiteSpace(configuredAddress))
        {
            if (!IPAddress.TryParse(configuredAddress, out var parsed) ||
                parsed.AddressFamily != AddressFamily.InterNetwork ||
                IPAddress.IsLoopback(parsed))
                throw new InvalidOperationException("Printer:PreferredIpAddress must be a non-loopback IPv4 address.");
            return parsed;
        }

        var candidates = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                        n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                        n.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(a => new
                {
                    a.Address,
                    HasGateway = n.GetIPProperties().GatewayAddresses.Any(g =>
                        g.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !g.Address.Equals(IPAddress.Any))
                }))
            .Where(x => !x.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
            .OrderByDescending(x => x.HasGateway)
            .ThenByDescending(x => IsPrivate(x.Address))
            .Select(x => x.Address)
            .ToArray();

        return candidates.FirstOrDefault()
               ?? throw new InvalidOperationException(
                   "No active LAN IPv4 address was found. Set Printer:PreferredIpAddress explicitly.");
    }

    private static bool IsPrivate(IPAddress address)
    {
        var b = address.GetAddressBytes();
        return b[0] == 10 || b[0] == 192 && b[1] == 168 || b[0] == 172 && b[1] is >= 16 and <= 31;
    }

    private static string NormalizeResourcePath(string value)
    {
        var path = string.IsNullOrWhiteSpace(value) ? "/printers/default" : value.Trim();
        return "/" + path.Trim('/');
    }

    private static Guid CreateStableUuid(string seed)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(seed))[..16];
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }

    private static string SanitizeDnsLabel(string value)
    {
        var chars = value.ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();
        var label = new string(chars).Trim('-');
        return string.IsNullOrEmpty(label) ? "windows" : label[..Math.Min(label.Length, 50)];
    }
}
