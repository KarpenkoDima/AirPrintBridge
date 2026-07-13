namespace AirPrintBridge;

public sealed class PrinterConfig
{
    // Empty: "<Windows printer name> (AirPrint)".
    public string DisplayName { get; set; } = "";

    // Empty: use the default Windows printer.
    public string WindowsPrinterName { get; set; } = "";

    public int IppPort { get; set; } = 8631;

    public string ResourcePath { get; set; } = "/printers/default";

    // Optional LAN IPv4 override. Normally detected automatically.
    public string PreferredIpAddress { get; set; } = "";

    // Save incoming jobs to %TEMP% for diagnostics.
    public bool SaveIncomingJobs { get; set; }
}
