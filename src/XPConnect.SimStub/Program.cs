// Does nothing except exist: BeyondATC checks for a running process named FlightSimulator2024 before it starts
// its SimConnect helper. XPConnect starts it with --parent <pid> and it exits when XPConnect does, however
// XPConnect ends (closing its window kills it before it can stop the stub). Run alone, stop it with Ctrl+C.
using System.Diagnostics;

using var stop = new ManualResetEventSlim();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stop.Set();
};

if (args is ["--parent", var pidText] && int.TryParse(pidText, out var pid))
{
    Process parent;
    try
    {
        parent = Process.GetProcessById(pid);
    }
    catch (ArgumentException)
    {
        return; // XPConnect is already gone
    }

    new Thread(() =>
    {
        parent.WaitForExit();
        stop.Set();
    })
    { IsBackground = true }.Start();
}

Console.WriteLine("XPConnect stand-in process 'FlightSimulator2024' running. It closes with XPConnect.");
stop.Wait();
