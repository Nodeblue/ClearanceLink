using System.Buffers.Binary;
using ClearanceLink.Host;
using ClearanceLink.Host.Transport;
using ClearanceLink.Protocol;
using ClearanceLink.Protocol.Tests;
using ClearanceLink.Recorder;
using ClearanceLink.Server;

namespace ClearanceLink.Host.Tests;

public sealed class XPlaneServerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("clearancelink-tests").FullName;

    public XPlaneServerTests() => PacketConsole.Verbose = false;

    [Fact]
    public async Task Serves_xplane_values_over_tcp_and_sends_com_changes_to_xplane()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var xp = new InMemoryXPlane
        {
            ["sim/flightmodel/position/latitude"] = -27.4016,
            ["sim/cockpit2/radios/actuators/com1_frequency_hz_833"] = 127850,
        };
        var mappings = new MappingSet(
            MappingSet.ParseList<SimVarMap>("""
                - name: PLANE LATITUDE
                  dataref: sim/flightmodel/position/latitude
                  units: degrees
                - name: COM ACTIVE FREQUENCY
                  indexed: true
                  dataref: sim/cockpit2/radios/actuators/com{i}_frequency_hz_833
                  units: khz
                """),
            MappingSet.ParseList<EventMap>("""
                - name: COM_RADIO_SET_HZ
                  dataref: sim/cockpit2/radios/actuators/com1_frequency_hz_833
                  from_units: hz
                  to_units: khz
                """));

        using var recorder = new JsonlRecorder(Path.Combine(_dir, "serve.jsonl"));
        await using var listener = Transports.Listen(Endpoint.Parse("tcp:0"));
        var server = new XPlaneSimServer(listener, ServerIdentity.Msfs2024, recorder, new SimVarResolver(mappings, xp), xp);
        var run = server.RunAsync(cts.Token);

        await using var client = await Transports.ConnectAsync(listener.Endpoint, cts.Token);
        await PacketFramer.WriteAsync(client, TestPackets.Open("BATCDLLSimConnection"), cts.Token);
        await PacketFramer.WriteAsync(client, TestPackets.AddToDataDefinition(17, "PLANE LATITUDE", "degrees"), cts.Token);
        await PacketFramer.WriteAsync(client, TestPackets.AddToDataDefinition(17, "COM ACTIVE FREQUENCY:1", "Mhz"), cts.Token);
        await PacketFramer.WriteAsync(client, TestPackets.RequestDataOnSimObject(1004, 17, period: 1), cts.Token);

        var open = await PacketFramer.ReadAsync(client, Direction.ServerToClient, cts.Token);
        var data = await PacketFramer.ReadAsync(client, Direction.ServerToClient, cts.Token);

        Assert.Equal(RecvId.Open, (RecvId)open!.TypeField);
        Assert.Equal(RecvId.SimObjectData, (RecvId)data!.TypeField);
        var block = data.Payload[28..];
        Assert.Equal(-27.4016, BinaryPrimitives.ReadDoubleLittleEndian(block));
        Assert.Equal(127.85, BinaryPrimitives.ReadDoubleLittleEndian(block[8..]), 6);

        await PacketFramer.WriteAsync(client, TestPackets.MapClientEventToSimEvent(24, "COM_RADIO_SET_HZ"), cts.Token);
        await PacketFramer.WriteAsync(client, TestPackets.TransmitClientEvent(0, 24, 118_850_000), cts.Token);
        while (xp.Writes.IsEmpty)
        {
            await Task.Delay(20, cts.Token);
        }

        Assert.Equal(("sim/cockpit2/radios/actuators/com1_frequency_hz_833", 118850.0), Assert.Single(xp.Writes));
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // Windows may still hold the recording briefly.
        }
    }
}
