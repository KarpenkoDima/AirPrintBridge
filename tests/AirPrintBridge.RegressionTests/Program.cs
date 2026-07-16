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
