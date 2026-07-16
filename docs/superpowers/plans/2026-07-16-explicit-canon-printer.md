# Explicit Canon Printer Configuration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make AirPrint Bridge always advertise and dispatch jobs to the installed Windows queue `\\192.168.11.12\Canon MF3010_Kab12_1`.

**Architecture:** Use the existing explicit printer settings in `appsettings.json`; no application code changes are required. Stop the running executable before rebuilding, launch one elevated instance, and verify the advertised HTTP and IPP behavior against the configured Canon identity.

**Tech Stack:** .NET 9, Microsoft.Extensions.Configuration JSON, Windows printing, HTTP.sys/HttpListener, IPP, PowerShell.

## Global Constraints

- The exact Windows queue is `\\192.168.11.12\Canon MF3010_Kab12_1`.
- The AirPrint display name is `Canon MF3010 (AirPrint)`.
- Do not change the Windows default printer.
- Do not add automatic printer-discovery code.
- Never rebuild while `bin\Debug\net9.0\AirPrintBridge.exe` is running.

---

### Task 1: Pin, deploy, and verify the Canon queue

**Files:**
- Modify: `appsettings.json:3-4`
- Verify generated copy: `bin/Debug/net9.0/appsettings.json`

**Interfaces:**
- Consumes: Existing `PrinterConfig.WindowsPrinterName` and `PrinterConfig.DisplayName` settings.
- Produces: An AirPrint service named `Canon MF3010 (AirPrint)` that dispatches to `\\192.168.11.12\Canon MF3010_Kab12_1`.

- [x] **Step 1: Verify the failing configuration state**

Run:

```powershell
Get-Content -Raw appsettings.json
```

Expected: `Printer:WindowsPrinterName` and `Printer:DisplayName` are empty, allowing the bridge to select `Print to Evernote`.

- [x] **Step 2: Stop the active AirPrint Bridge instance**

Run from an elevated PowerShell process:

```powershell
Get-Process AirPrintBridge -ErrorAction SilentlyContinue | Stop-Process -Force
```

Expected: no `AirPrintBridge` process remains and the executable is no longer locked.

- [x] **Step 3: Set the exact Canon configuration**

Change only these values in `appsettings.json`:

```json
{
  "Printer": {
    "DisplayName": "Canon MF3010 (AirPrint)",
    "WindowsPrinterName": "\\\\192.168.11.12\\Canon MF3010_Kab12_1"
  }
}
```

Keep all other existing settings unchanged.

- [x] **Step 4: Build and run regression verification**

Run:

```powershell
dotnet build AirPrintBridge.sln --no-restore
dotnet run --no-restore --project tests\AirPrintBridge.RegressionTests\AirPrintBridge.RegressionTests.csproj
```

Expected: build exits `0` with no errors; regression output includes `PASS: Wildcard listener prefix is correct.` The existing `NU1701` PdfiumViewer compatibility warning is permitted.

- [x] **Step 5: Verify the deployed configuration copy**

Run:

```powershell
Get-Content -Raw bin\Debug\net9.0\appsettings.json
```

Expected: the deployed file contains `Canon MF3010 (AirPrint)` and `\\192.168.11.12\Canon MF3010_Kab12_1`.

- [x] **Step 6: Start exactly one elevated bridge instance**

Run from an elevated PowerShell process:

```powershell
$exe = Resolve-Path bin\Debug\net9.0\AirPrintBridge.exe
Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -WindowStyle Hidden
```

Expected: one `AirPrintBridge` process remains running and listens on port `8631`.

- [x] **Step 7: Verify HTTP and IPP Canon identity**

Send an HTTP request to `http://192.168.0.93:8631/` with Host header `airprint-kab12-ws-pc4.local:8631` and an IPP Get-Printer-Attributes request to `/printers/default`.

Expected:

```text
HTTP/1.1 200 OK
Content-Type: application/ipp
printer-name = Canon MF3010 (AirPrint)
printer-make-and-model = \\192.168.11.12\Canon MF3010_Kab12_1
```

- [x] **Step 8: Commit the configuration and plan**

```powershell
git add appsettings.json docs/superpowers/plans/2026-07-16-explicit-canon-printer.md
git commit -m "fix: pin AirPrint bridge to Canon queue"
```
