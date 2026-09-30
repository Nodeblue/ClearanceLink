using XPConnect.Protocol;
using XPConnect.Recorder;
using XPConnect.Server;

namespace XPConnect.Server.Tests;

/// <summary>Paths to the repo's mapping tables and recorded samples, found by walking up from the test binaries.</summary>
public static class Fixtures
{
    public static string RepoRoot { get; } = FindRoot();

    public static string MappingDir => Path.Combine(RepoRoot, "mapping");

    public static string BatcSetup => Path.Combine(RepoRoot, "recordings", "samples", "batc-1.8.21-setup.jsonl");

    public static MappingSet Mappings() => MappingSet.LoadDirectory(MappingDir);

    /// <summary>The client packets of one connection in the BATC setup recording, in order.</summary>
    public static List<Packet> ClientPackets(int connection) =>
        JsonlRecorder.ReadAll(BatcSetup)
            .Where(e => e.Connection == connection && e.Dir == "c2s")
            .Select(e => new Packet(Direction.ClientToServer, Convert.FromHexString(e.Hex)))
            .ToList();

    /// <summary>A plausible X-Plane state: an SF50 parked at a YBBN gate.</summary>
    public static InMemoryXPlane ParkedAtYbbn()
    {
        var xp = new InMemoryXPlane
        {
            ["sim/flightmodel/position/latitude"] = -27.4016,
            ["sim/flightmodel/position/longitude"] = 153.1122,
            ["sim/flightmodel/position/elevation"] = 4.67,
            ["sim/flightmodel/position/y_agl"] = 0.87,
            ["sim/cockpit2/gauges/indicators/altitude_ft_pilot"] = 15.3,
            ["sim/flightmodel2/position/pressure_altitude"] = -358.16,
            ["sim/weather/barometer_sealevel_inhg"] = 1026.9 / 33.8638866667,
            ["sim/cockpit2/gauges/actuators/barometer_setting_in_hg_pilot"] = 1026.9 / 33.8638866667,
            ["sim/flightmodel/position/true_psi"] = 297.0,
            ["sim/flightmodel/position/mag_psi"] = 285.9,
            ["sim/flightmodel/position/true_theta"] = 1.5,
            ["sim/flightmodel/position/true_phi"] = -0.5,
            ["sim/flightmodel/position/indicated_airspeed"] = 0,
            ["sim/flightmodel/position/groundspeed"] = 0,
            ["sim/flightmodel/failures/onground_any"] = 1,
            ["sim/cockpit2/radios/actuators/com1_frequency_hz_833"] = 127850,
            ["sim/cockpit2/radios/actuators/com2_frequency_hz_833"] = 124850,
            ["sim/cockpit2/radios/actuators/com1_standby_frequency_hz_833"] = 118850,
            ["sim/cockpit2/radios/actuators/com2_standby_frequency_hz_833"] = 121700,
            ["sim/cockpit2/radios/actuators/audio_volume_com1"] = 1,
            ["sim/cockpit2/radios/actuators/audio_volume_com2"] = 0.5,
            ["sim/cockpit2/switches/avionics_power_on"] = 1,
            ["sim/cockpit2/radios/actuators/audio_selection_com1"] = 1,
            ["sim/cockpit2/radios/actuators/audio_selection_com2"] = 0,
            ["sim/cockpit2/radios/actuators/audio_com_selection"] = 6,
            ["sim/cockpit2/radios/actuators/transponder_code"] = 1200,
            ["sim/cockpit2/radios/actuators/transponder_mode"] = 1,
            ["sim/cockpit2/radios/indicators/transponder_id"] = 0,
            ["sim/time/zulu_time_sec"] = 3600,
            ["sim/time/local_time_sec"] = 39600,
            ["sim/time/local_date_days"] = 272,
            ["sim/graphics/scenery/sun_pitch_degrees"] = 40,
            ["sim/weather/aircraft/wind_now_direction_degt"] = 60,
            ["sim/weather/aircraft/wind_now_speed_msc"] = 7.1,
            ["sim/flightmodel2/engines/engine_is_burning_fuel[0]"] = 0,
            ["sim/flightmodel2/engines/engine_is_burning_fuel[1]"] = 0,
            ["sim/aircraft/view/acf_Vso"] = 67,
            ["sim/aircraft/view/acf_Vs"] = 86,
        };
        xp.SetText("sim/aircraft/view/acf_ICAO", "SF50");
        return xp;
    }

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "XPConnect.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException("XPConnect.slnx not found above the test binaries");
    }
}
