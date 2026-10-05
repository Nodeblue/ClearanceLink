// ClearanceLink X-Plane plugin: draws BeyondATC's AI traffic sent by ClearanceLink, and reports terrain height under it.
// Protocol: docs/protocol/plugin.md.
#include "CslFolders.h"
#include "Protocol.h"
#include "TrafficAircraft.h"
#include "Udp.h"

#include "XPLMMenus.h"
#include "XPLMPlugin.h"
#include "XPLMProcessing.h"
#include "XPLMUtilities.h"
#include "XPMPMultiplayer.h"

#include <cstring>
#include <filesystem>
#include <fstream>
#include <map>
#include <memory>
#include <set>
#include <string>
#include <vector>

namespace {

constexpr float HelloTimeoutSeconds = 10.0f;
// ClearanceLink re-sends each aircraft's add every 10 s and its position at least every 2 s. One that hears neither for
// this long was removed by a `del` that UDP dropped, and is removed here instead of staying drawn where it was.
constexpr float AircraftTimeoutSeconds = 15.0f;
constexpr float GroundReportSeconds = 0.25f;
constexpr float StatusReportSeconds = 1.0f;

const char* const Version = CLEARANCELINK_PLUGIN_VERSION;

clink::UdpSocket g_socket;
std::map<uint32_t, std::unique_ptr<clink::TrafficAircraft>> g_aircraft;
std::vector<std::string> g_cslFolders;
std::set<std::string> g_loggedOnce;
std::set<uint32_t> g_refusedForNoModels;  // ids told once that there is nothing to draw them with
float g_lastHello = -1;
float g_lastGround = 0;
float g_lastStatus = 0;
bool g_tcas = false;

void Log(const std::string& text) {
    XPLMDebugString(("ClearanceLink: " + text + "\n").c_str());
}

// Messages logged once each. The texts include types, airlines and unknown message names from the network, so the set
// is capped: past this many, one last line says so and further new messages are not logged until the plugin restarts.
constexpr size_t MaxLoggedOnce = 500;

void LogOnce(const std::string& text) {
    if (g_loggedOnce.size() >= MaxLoggedOnce) return;
    if (!g_loggedOnce.insert(text).second) return;
    Log(text);
    if (g_loggedOnce.size() == MaxLoggedOnce) Log("further one-time messages suppressed");
}

std::filesystem::path PluginFolder() {
    char path[1024] = {};
    XPLMGetPluginInfo(XPLMGetMyID(), nullptr, path, nullptr, nullptr);
    return std::filesystem::path(path).parent_path().parent_path();  // .../plugins/ClearanceLink/64/win.xpl -> ClearanceLink
}

std::string XPlaneRoot() {
    char path[1024] = {};
    XPLMGetSystemPath(path);
    return path;
}

void LoadCsl() {
    std::vector<std::string> extra;
    std::ifstream file(PluginFolder() / "csl_paths.txt");
    for (std::string line; std::getline(file, line);) extra.push_back(line);

    auto exists = [](const std::string& path) {
        std::error_code error;
        return std::filesystem::is_directory(std::filesystem::u8path(path), error);
    };
    g_cslFolders.clear();
    for (const auto& folder : clink::CslFolders(XPlaneRoot(), extra, exists)) {
        const char* error = XPMPLoadCSLPackage(folder.c_str());
        if (error && *error) {
            Log("CSL " + folder + ": " + error);
        } else {
            g_cslFolders.push_back(folder);
            Log("CSL loaded " + folder);
        }
    }
    clink::TrafficAircraft::SetCslPackages(clink::FindCslPackages(g_cslFolders));
    Log(std::to_string(XPMPGetNumberOfInstalledModels()) + " CSL models from " + std::to_string(g_cslFolders.size()) + " folders");
}

// Loads an X-Plane .fms file into X-Plane's own FMS/GPS (pilot side). XPLMLoadFMSFlightPlan is X-Plane 12.1+ (XPLM410),
// so it is looked up at run time: on an older X-Plane the plugin still loads and answers with an error.
std::string LoadFlightPlan(const std::string& path) {
    using LoadFn = void (*)(int, const char*, unsigned int);
    static auto load = reinterpret_cast<LoadFn>(XPLMFindSymbol("XPLMLoadFMSFlightPlan"));
    if (!load) return "this X-Plane cannot load flight plans from plugins (needs 12.1 or later)";

    std::error_code error;
    const auto file = std::filesystem::u8path(path);
    if (file.extension() != ".fms" || !std::filesystem::is_regular_file(file, error)) return "not an .fms file: " + path;
    const auto size = std::filesystem::file_size(file, error);
    if (error || size == 0 || size > 512 * 1024) return "flight plan file is empty or too large";
    std::ifstream in(file, std::ios::binary);
    std::string text(static_cast<size_t>(size), '\0');
    if (!in.read(text.data(), static_cast<std::streamsize>(text.size()))) return "cannot read " + path;
    if (!clink::LooksLikeFms(text)) return "not an X-Plane flight plan: " + path;

    load(0, text.data(), static_cast<unsigned int>(text.size()));
    Log("flight plan loaded into the FMS from " + path);
    return {};
}

void RemoveAll() {
    g_aircraft.clear();
    g_refusedForNoModels.clear();
}

void Handle(const clink::Message& message, float now) {
    if (auto hello = std::get_if<clink::Hello>(&message)) {
        if (g_lastHello < 0) Log("ClearanceLink " + hello->version + " connected");
        g_lastHello = now;
    } else if (auto add = std::get_if<clink::Add>(&message)) {
        // Without a CSL model XPMP2 cannot create anything; say so once per aircraft instead of trying (and logging
        // a failure) every time ClearanceLink re-sends the add.
        if (XPMPGetNumberOfInstalledModels() == 0 && g_aircraft.find(add->id) == g_aircraft.end()) {
            if (g_refusedForNoModels.insert(add->id).second)
                g_socket.Reply(clink::FormatError(add->id, "no CSL models installed"));
            LogOnce("no CSL aircraft models found; BeyondATC's traffic is not drawn (install Bluebell or convert FSLTL)");
            return;
        }
        try {
            auto existing = g_aircraft.find(add->id);
            if (existing != g_aircraft.end()) {
                existing->second->ChangeTo(*add);
                existing->second->Heard(now);
            } else {
                auto& aircraft = g_aircraft[add->id] = std::make_unique<clink::TrafficAircraft>(*add);
                aircraft->Heard(now);
                if (!g_tcas) {
                    const char* error = XPMPMultiplayerEnable();
                    g_tcas = !(error && *error);
                    if (!g_tcas) LogOnce(std::string("TCAS not available: ") + error);
                }
            }
        } catch (const std::exception& e) {
            g_socket.Reply(clink::FormatError(add->id, e.what()));
            LogOnce(std::string("cannot create ") + add->icaoType + " " + add->icaoAirline + ": " + e.what());
        }
    } else if (auto pos = std::get_if<clink::Pos>(&message)) {
        auto found = g_aircraft.find(pos->id);
        if (found != g_aircraft.end()) {
            found->second->Apply(*pos, now);
            found->second->Heard(now);
        }
    } else if (auto offset = std::get_if<clink::Offset>(&message)) {
        auto found = g_aircraft.find(offset->id);
        if (found != g_aircraft.end()) found->second->SetGroundTrim(offset->groundFt, offset->groundPitch);
    } else if (auto del = std::get_if<clink::Del>(&message)) {
        g_aircraft.erase(del->id);
        g_refusedForNoModels.erase(del->id);
    } else if (std::holds_alternative<clink::Clear>(message)) {
        RemoveAll();
    } else if (auto fms = std::get_if<clink::Fms>(&message)) {
        const auto problem = LoadFlightPlan(fms->path);
        if (!problem.empty()) Log("flight plan not loaded: " + problem);
        g_socket.Reply(clink::FormatFms(problem));
    }
}

void FlightLoopBody(float now);

float FlightLoop(float, float, int, void*) {
    // Nothing may escape into X-Plane: an exception out of a flight loop callback ends the whole simulator.
    try {
        FlightLoopBody(XPLMGetElapsedTime());
    } catch (const std::exception& e) {
        LogOnce(std::string("flight loop failed: ") + e.what());
    }
    return -1.0f;  // every frame
}

void FlightLoopBody(float now) {
    static char buffer[65536];
    for (size_t size; (size = g_socket.Receive(buffer, sizeof(buffer))) > 0;) {
        for (const auto& line : clink::Lines(buffer, size)) {
            std::string error;
            if (auto message = clink::Parse(line, &error)) {
                Handle(*message, now);
            } else if (!error.empty()) {
                LogOnce(error.substr(0, error.find(' ', 12)));
            }
        }
    }

    if (g_lastHello >= 0 && now - g_lastHello > HelloTimeoutSeconds) {
        Log("ClearanceLink stopped sending; removing its traffic");
        RemoveAll();
        g_lastHello = -1;
        // Hand TCAS and AI control back, so LiveTraffic or xPilot can take over while ClearanceLink is closed; the next
        // add takes it again.
        if (g_tcas) XPMPMultiplayerDisable();
        g_tcas = false;
    }

    for (auto it = g_aircraft.begin(); it != g_aircraft.end();) {
        if (now - it->second->LastHeard() > AircraftTimeoutSeconds) {
            LogOnce("removing aircraft ClearanceLink stopped updating (a lost del)");
            it = g_aircraft.erase(it);
        } else {
            ++it;
        }
    }

    if (now - g_lastGround >= GroundReportSeconds && !g_aircraft.empty()) {
        g_lastGround = now;
        std::string lines;
        for (const auto& [id, aircraft] : g_aircraft) {
            if (auto ground = aircraft->GroundElevationFt()) lines += clink::FormatGround(id, *ground) + "\n";
            if (lines.size() > 60000) {
                g_socket.Reply(lines);
                lines.clear();
            }
        }
        if (!lines.empty()) g_socket.Reply(lines);
    }

    if (now - g_lastStatus >= StatusReportSeconds) {
        g_lastStatus = now;
        g_socket.Reply(clink::FormatStatus(Version, g_aircraft.size(), XPMPGetNumberOfInstalledModels(), g_cslFolders));
    }
}

int Preferences(const char*, const char*, int defaultValue) { return defaultValue; }

// Plugins > ClearanceLink > Show tail labels. Off by default; the choice is kept in settings.txt next to the plugin.
XPLMMenuID g_menu = nullptr;
int g_labelsItem = -1;

bool LabelsSetting() {
    std::ifstream file(PluginFolder() / "settings.txt");
    std::string line;
    while (std::getline(file, line))
        if (line.rfind("labels=", 0) == 0) return line.substr(7) == "1";
    return false;
}

void ApplyLabels(bool on, bool save) {
    XPMPEnableAircraftLabels(on);
    if (g_menu) XPLMCheckMenuItem(g_menu, g_labelsItem, on ? xplm_Menu_Checked : xplm_Menu_Unchecked);
    if (save) std::ofstream(PluginFolder() / "settings.txt") << "labels=" << (on ? 1 : 0) << "\n";
}

void MenuHandler(void*, void* item) {
    if (item == &g_labelsItem) ApplyLabels(!XPMPDrawingAircraftLabels(), true);
}

}  // namespace

PLUGIN_API int XPluginStart(char* name, char* signature, char* description) {
    std::strcpy(name, "ClearanceLink");
    std::strcpy(signature, "com.nodeblue.clearancelink");
    std::strcpy(description, "Draws BeyondATC traffic sent by ClearanceLink");
    XPLMEnableFeature("XPLM_USE_NATIVE_PATHS", 1);
    return 1;
}

PLUGIN_API int XPluginEnable() {
    const auto resources = (PluginFolder() / "Resources").string();
    const char* error = XPMPMultiplayerInit("ClearanceLink", resources.c_str(), Preferences, "A320", "ClearanceLink");
    if (error && *error) {
        Log(std::string("XPMP2 init failed: ") + error);
        return 0;
    }
    if (!clink::TrafficAircraft::AddModelDatarefs()) Log("XPMP2 refused the World Traffic gear dataref; those models' bogies stay tilted");
    LoadCsl();
    if (!g_menu) {
        const int root = XPLMAppendMenuItem(XPLMFindPluginsMenu(), "ClearanceLink", nullptr, 0);
        g_menu = XPLMCreateMenu("ClearanceLink", XPLMFindPluginsMenu(), root, MenuHandler, nullptr);
        g_labelsItem = XPLMAppendMenuItem(g_menu, "Show tail labels", &g_labelsItem, 0);
    }
    ApplyLabels(LabelsSetting(), false);

    std::string socketErrors;
    for (const auto port : clink::PluginPorts) {
        std::string why;
        if (g_socket.Open(port, why)) {
            Log(std::string("plugin ") + Version + " listening on 127.0.0.1:" + std::to_string(port));
            break;
        }
        socketErrors += (socketErrors.empty() ? "" : "; ") + why;
    }
    if (!g_socket.IsOpen()) Log("cannot listen for ClearanceLink (traffic will not be drawn): " + socketErrors);
    else if (!socketErrors.empty()) Log("ports not available: " + socketErrors);
    XPLMRegisterFlightLoopCallback(FlightLoop, -1.0f, nullptr);
    return 1;
}

PLUGIN_API void XPluginDisable() {
    XPLMUnregisterFlightLoopCallback(FlightLoop, nullptr);
    RemoveAll();
    clink::TrafficAircraft::ForgetModelFits();
    if (g_tcas) XPMPMultiplayerDisable();
    g_tcas = false;
    XPMPMultiplayerCleanup();
    g_socket.Close();
    g_lastHello = -1;
    g_loggedOnce.clear();
}

PLUGIN_API void XPluginStop() {}

PLUGIN_API void XPluginReceiveMessage(XPLMPluginID, int, void*) {}
