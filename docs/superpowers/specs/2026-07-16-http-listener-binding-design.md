# AirPrint HTTP Listener Binding Fix

## Problem

The AirPrint service is discoverable over mDNS, but iOS rejects it after selection. Packet captures show that iOS reaches `192.168.0.93:8631` and sends IPP `Get-Printer-Attributes`; Windows HTTP.sys returns `HTTP/1.1 503 Service Unavailable` before the request reaches `IppServer`.

The service advertises a DNS-SD hostname, while `HttpListener` registers only literal loopback and LAN-IP prefixes. HTTP.sys routes requests by URI prefix, including the HTTP `Host` value, so a request addressed to the advertised hostname does not reliably match the literal-IP registrations.

## Selected Design

Register one strong-wildcard listener prefix:

```text
http://+:<configured-port>/
```

This makes the HTTP listener accept the advertised DNS-SD hostname and literal local addresses on the configured port. The URL namespace will be reserved for the interactive Windows account used to run the bridge, because that account already has access to the shared printer queue `\\192.168.11.12\Canon MF3010_Kab12_1`.

The printer backend, IPP resource path, mDNS records, and print dispatch behavior remain unchanged.

## Code Structure

Introduce a small pure helper that constructs the listener prefix from the configured port. `IppServer` will use that helper when configuring `HttpListener`. Keeping prefix construction separate makes the regression test independent of HTTP.sys and administrator privileges.

## Testing

Use a test-first regression test asserting that port `8631` produces `http://+:8631/`. The test must fail against the current literal-IP behavior before production code changes.

After implementation:

1. Run the complete automated test suite.
2. Build `AirPrintBridge.sln` successfully.
3. Rebuild `C:\Users\Администратор\Documents\AirPrint\bin\Debug\net9.0\AirPrintBridge.exe`.
4. Start that executable with the matching URL ACL.
5. Send an IPP `Get-Printer-Attributes` request using the advertised hostname and verify that the response is IPP, not HTTP 503.

## URL ACL

The existing reservation for `http://+:8631/` must grant listen permission to the same interactive Windows account that runs the executable. Running under `LOCAL SERVICE` is not selected because that identity may not have credentials for the remote Windows printer share.

## Error Handling and Scope

If the URL ACL is absent or belongs to another identity, startup must retain the existing clear log message and fail instead of advertising an unreachable printer. No Kestrel migration, installer work, mDNS cleanup, or Windows Service account redesign is included in this fix.

## Success Criteria

- The locally built executable is the updated artifact.
- iOS can select the advertised printer without it immediately becoming unavailable.
- Packet capture contains a successful IPP response to `Get-Printer-Attributes` and no HTTP 503 from port `8631`.
- The configured shared Windows printer remains the print destination.
