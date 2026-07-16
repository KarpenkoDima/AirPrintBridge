# Explicit Canon Printer Configuration Design

## Goal

AirPrint Bridge must always advertise and print through the installed Windows
queue `\\192.168.11.12\Canon MF3010_Kab12_1`. It must not depend on the
current Windows default printer or select virtual printers such as
`Print to Evernote`.

## Selected approach

Set the existing `Printer:WindowsPrinterName` configuration value to the exact
installed queue name and set `Printer:DisplayName` to
`Canon MF3010 (AirPrint)` in `appsettings.json`.

This uses the application's existing explicit-printer configuration path and
requires no printer-selection code changes. Setting the Windows default printer
was rejected because it would affect unrelated applications. Adding automatic
Canon discovery was rejected because it would introduce ambiguous selection
rules when several Canon queues are installed.

## Runtime behavior

At startup, `PrinterRuntime` resolves the configured queue and verifies that it
is installed. `MdnsAdvertiser` publishes `Canon MF3010 (AirPrint)`, while
`WindowsPrintDispatcher` sends received jobs to
`\\192.168.11.12\Canon MF3010_Kab12_1`.

If the queue is removed or unavailable to the account running the bridge,
startup must fail with the existing explicit "printer is not available" error
instead of silently falling back to another printer.

## Deployment and verification

Stop the currently running AirPrint Bridge instance before rebuilding so that
Windows does not lock `bin\Debug\net9.0\AirPrintBridge.exe`. Rebuild the
solution, start exactly one elevated instance, and verify:

1. The startup log identifies the configured Canon queue and display name.
2. The advertised-host HTTP request returns `200 OK` rather than `503`.
3. An IPP Get-Printer-Attributes request returns HTTP `200`, content type
   `application/ipp`, and reports the Canon display/model values.
4. The AirPrint Bridge process remains running after verification.

