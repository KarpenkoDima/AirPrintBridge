# AirPrint HTTP Listener Binding Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Update the locally built AirPrint Bridge so requests addressed to its advertised DNS-SD hostname reach `IppServer` instead of receiving HTTP.sys 503.

**Architecture:** Keep the existing `HttpListener` server and IPP implementation. Extract wildcard prefix construction into one testable helper, configure the listener with exactly `http://+:<port>/`, reserve that namespace for the interactive Windows account, and verify the rebuilt local executable with both HTTP and IPP requests.

**Tech Stack:** .NET 9 Worker Service, `System.Net.HttpListener`, a dependency-free .NET regression-test console, Windows HTTP.sys/netsh.

## Global Constraints

- Modify and rebuild the local workspace at `C:\Users\Администратор\Documents\AirPrint`.
- Preserve the shared Windows printer backend and existing IPP behavior.
- Run the bridge under the interactive Windows account so it retains access to `\\192.168.11.12\Canon MF3010_Kab12_1`.
- Do not migrate to Kestrel or expand the fix into mDNS cleanup.
- Use test-first development and verify the actual rebuilt executable.

---

## File Map

- Modify `AirPrintBridge.csproj`: exclude regression-test sources from the production project's default recursive compile glob.
- Modify `AirPrintBridge.sln`: include the regression-test project.
- Create `tests/AirPrintBridge.RegressionTests/AirPrintBridge.RegressionTests.csproj`: dependency-free executable test project referencing the production project.
- Create `tests/AirPrintBridge.RegressionTests/Program.cs`: reflection-based regression assertion for listener prefix construction.
- Modify `IppServer.cs`: create and use `BuildListenerPrefix(int port)` and remove literal-IP listener registrations.

### Task 1: Test and implement wildcard HttpListener binding

**Files:**
- Modify: `AirPrintBridge.csproj`
- Modify: `AirPrintBridge.sln`
- Create: `tests/AirPrintBridge.RegressionTests/AirPrintBridge.RegressionTests.csproj`
- Create: `tests/AirPrintBridge.RegressionTests/Program.cs`
- Modify: `IppServer.cs:126-143`

**Interfaces:**
- Produces: `internal static string IppServer.BuildListenerPrefix(int port)` returning a complete trailing-slash URI prefix.
- Consumes: `PrinterConfig.IppPort` from the existing configuration.

- [ ] **Step 1: Create the failing regression harness**

Add this item to `AirPrintBridge.csproj` so the production project does not compile the nested test source:

```xml
<ItemGroup>
  <Compile Remove="tests/**/*.cs" />
</ItemGroup>
```

Create `tests/AirPrintBridge.RegressionTests/AirPrintBridge.RegressionTests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\AirPrintBridge.csproj" />
  </ItemGroup>
</Project>
```

Create `tests/AirPrintBridge.RegressionTests/Program.cs`:

```csharp
using AirPrintBridge;
using System.Reflection;

var method = typeof(IppServer).GetMethod(
    "BuildListenerPrefix",
    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

if (method is null)
    return Fail("IppServer.BuildListenerPrefix is missing.");

var actual = method.Invoke(null, new object[] { 8631 }) as string;
return actual == "http://+:8631/"
    ? Pass("Wildcard listener prefix is correct.")
    : Fail($"Expected 'http://+:8631/', got '{actual ?? "<null>"}'.");

static int Pass(string message)
{
    Console.WriteLine($"PASS: {message}");
    return 0;
}

static int Fail(string message)
{
    Console.Error.WriteLine($"FAIL: {message}");
    return 1;
}
```

Add the project to the solution:

```powershell
dotnet sln AirPrintBridge.sln add tests\AirPrintBridge.RegressionTests\AirPrintBridge.RegressionTests.csproj
```

- [ ] **Step 2: Run the harness and verify RED**

Run:

```powershell
dotnet run --project tests\AirPrintBridge.RegressionTests\AirPrintBridge.RegressionTests.csproj
```

Expected: exit code `1` and `FAIL: IppServer.BuildListenerPrefix is missing.`

- [ ] **Step 3: Implement the minimal wildcard binding**

In `IppServer.StartListenerAsync`, replace the active literal-IP listener setup with:

```csharp
_listener = new HttpListener();
var prefix = BuildListenerPrefix(_config.IppPort);
_listener.Prefixes.Add(prefix);
_logger.LogInformation("Binding IPP server to {Prefix}", prefix);
```

Add this method to `IppServer`:

```csharp
internal static string BuildListenerPrefix(int port) => $"http://+:{port}/";
```

- [ ] **Step 4: Run the regression harness and verify GREEN**

Run:

```powershell
dotnet run --project tests\AirPrintBridge.RegressionTests\AirPrintBridge.RegressionTests.csproj
```

Expected: exit code `0` and `PASS: Wildcard listener prefix is correct.`

- [ ] **Step 5: Run the full solution build**

Run:

```powershell
dotnet build AirPrintBridge.sln --no-restore
```

Expected: exit code `0`, both projects built, zero errors.

- [ ] **Step 6: Commit the tested code change**

```powershell
git add AirPrintBridge.csproj AirPrintBridge.sln IppServer.cs tests
git commit -m "fix: accept IPP requests for advertised hostname"
```

### Task 2: Configure HTTP.sys and verify the local executable

**Files:**
- Build artifact: `bin/Debug/net9.0/AirPrintBridge.exe`
- No source changes.

**Interfaces:**
- Consumes: `IppServer.BuildListenerPrefix(8631)` from Task 1.
- Produces: an HTTP.sys URL reservation for the interactive account and a verified local executable.

- [ ] **Step 1: Reserve the wildcard URL for the interactive account**

Run from an elevated PowerShell process:

```powershell
$account = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
netsh http delete urlacl url=http://+:8631/
netsh http add urlacl url=http://+:8631/ user="$account"
netsh http show urlacl url=http://+:8631/
```

Expected: the reservation shows the current interactive account with `Listen: Yes`. A nonzero delete result is acceptable only when the reservation did not exist; the add and show commands must succeed.

- [ ] **Step 2: Rebuild the exact local executable**

Run:

```powershell
dotnet build AirPrintBridge.csproj -c Debug --no-restore
Get-Item bin\Debug\net9.0\AirPrintBridge.exe | Select-Object FullName,Length,LastWriteTime
```

Expected: build exit code `0`; output path is `C:\Users\Администратор\Documents\AirPrint\bin\Debug\net9.0\AirPrintBridge.exe` with a fresh timestamp.

- [ ] **Step 3: Start the rebuilt executable**

Run:

```powershell
$process = Start-Process -FilePath '.\bin\Debug\net9.0\AirPrintBridge.exe' -WorkingDirectory '.\bin\Debug\net9.0' -WindowStyle Hidden -PassThru
$process.Id
```

Expected: a live process ID and TCP port `8631` registered by HTTP.sys.

- [ ] **Step 4: Verify hostname-based HTTP routing**

Run:

```powershell
curl.exe --noproxy "*" -i -H "Host: airprint-kab12-ws-pc4.local:8631" http://192.168.0.93:8631/
```

Expected: `HTTP/1.1 200 OK` and `AirPrint Bridge is running`; no `503 Service Unavailable`.

- [ ] **Step 5: Verify a real IPP Get-Printer-Attributes response**

Run:

```powershell
[byte[]]$ipp = @(
  0x01,0x01, 0x00,0x0B, 0x00,0x00,0x00,0x01,
  0x01,
  0x47, 0x00,0x12,
  0x61,0x74,0x74,0x72,0x69,0x62,0x75,0x74,0x65,0x73,0x2D,0x63,0x68,0x61,0x72,0x73,0x65,0x74,
  0x00,0x05, 0x75,0x74,0x66,0x2D,0x38,
  0x48, 0x00,0x1B,
  0x61,0x74,0x74,0x72,0x69,0x62,0x75,0x74,0x65,0x73,0x2D,0x6E,0x61,0x74,0x75,0x72,0x61,0x6C,0x2D,0x6C,0x61,0x6E,0x67,0x75,0x61,0x67,0x65,
  0x00,0x02, 0x65,0x6E,
  0x03
)
$headers = @{ Host = 'airprint-kab12-ws-pc4.local:8631' }
$response = Invoke-WebRequest -UseBasicParsing -Uri 'http://192.168.0.93:8631/printers/default' -Method Post -ContentType 'application/ipp' -Headers $headers -Body $ipp
$response.StatusCode
$response.Headers['Content-Type']
```

Expected: status `200` and content type `application/ipp`.

- [ ] **Step 6: Confirm the process and repository state**

Run:

```powershell
Get-Process -Id $process.Id
git status --short
```

Expected: the rebuilt bridge remains running; the only uncommitted changes are generated build outputs ignored by Git.
