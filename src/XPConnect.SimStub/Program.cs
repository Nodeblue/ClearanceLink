// Does nothing except exist: BeyondATC checks for a running process named FlightSimulator2024 before it starts
// its SimConnect helper. Run this while XPConnect stands in for MSFS; stop it with Ctrl+C.
using var stop = new ManualResetEventSlim();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stop.Set();
};
Console.WriteLine("XPConnect stand-in process 'FlightSimulator2024' running. Ctrl+C to stop.");
stop.Wait();
