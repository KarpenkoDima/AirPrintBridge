# AirPrint Bridge for Windows

AirPrint Bridge exposes a printer installed in Windows 10/11 to iPhone and iPad as an AirPrint printer.

The service advertises the queue over mDNS/DNS-SD, accepts IPP requests, receives PDF print jobs and renders them through the normal Windows print spooler. The physical printer itself does not need AirPrint support.

## Current MVP

- automatically uses the default Windows printer, or a configured queue;
- discovers an active LAN IPv4 address without assuming a `192.168.*` subnet;
- advertises `_ipp._tcp` and the AirPrint `_universal` subtype;
- responds to IPP `Get-Printer-Attributes`, `Validate-Job`, `Print-Job`, `Get-Jobs` and `Cancel-Job`;
- derives color, duplex and copy capabilities from the Windows printer driver;
- accepts PDF jobs and sends them to the Windows spooler without a print dialog;
- can run as a console process or Windows Service.

Apple URF/PWG Raster decoding is not implemented yet. The bridge declares URF because iOS uses it during AirPrint discovery, but PDF is the preferred and currently printable input format. Photo-only jobs that iOS sends as URF will fail cleanly instead of being reported as printed.

## Configuration

Edit `appsettings.json`:

```json
{
  "Printer": {
    "DisplayName": "",
    "WindowsPrinterName": "",
    "IppPort": 8631,
    "ResourcePath": "/printers/default",
    "PreferredIpAddress": "",
    "SaveIncomingJobs": false
  }
}
```

Empty `WindowsPrinterName` selects the Windows default printer. Empty `DisplayName` produces `<Windows queue> (AirPrint)`. Set `PreferredIpAddress` only when automatic LAN selection chooses the wrong adapter, for example because of a VPN.

## Run locally

Requirements: Windows 10/11 x64 and .NET 9 SDK/runtime.

```powershell
dotnet restore
dotnet run
```

Open `http://localhost:8631/` to confirm that the HTTP endpoint is alive. The iPhone and Windows PC must be on the same LAN, client isolation must be disabled, and Windows Firewall must allow inbound TCP on the configured IPP port and inbound UDP 5353 for mDNS.

When `HttpListener` reports access denied, run the process elevated or reserve the URL for the service account. Example for the default port:

```powershell
netsh http add urlacl url=http://+:8631/ user="NT AUTHORITY\LOCAL SERVICE"
```

## Install as a Windows Service

Publish first:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -o .\publish
```

Then, from an elevated PowerShell prompt:

```powershell
sc.exe create AirPrintBridge binPath= "C:\path\to\publish\AirPrintBridge.exe" start= auto
sc.exe start AirPrintBridge
```

The service account must be able to access the selected Windows printer. For printers installed only in one user's profile, running the bridge under that user is usually simpler than `LocalSystem`.

## Architecture

```text
iPhone/iPad
   |  mDNS discovery (UDP 5353)
   |  IPP Print-Job over HTTP
   v
AirPrint Bridge (.NET Worker Service)
   |  PDF rendering (PDFium)
   |  GDI/Windows Print Spooler
   v
Any printer installed in Windows
```

## Next milestones

1. Add an installer and a small tray/configuration UI.
2. Implement Apple URF and PWG Raster decoding for reliable photo printing.
3. Track real Windows spooler job IDs and implement truthful job state/cancellation.
4. Add IPP conformance tests and test on multiple iOS/Windows versions.
5. Support exposing several Windows queues at the same time.
