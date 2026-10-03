// XPConnect X-Plane plugin: draws BeyondATC's AI traffic sent by XPConnect, and reports terrain height under it.
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
constexpr float GroundReportSeconds = 0.25f;
constexpr float StatusReportSeconds = 1.0f;

const char* const Version = XPCONNECT_PLUGIN_VERSION;

xpc::UdpSocket g_socket;
std::map<uint32_t, std::unique_ptr<xpc::TrafficAircraft>> g_aircraft;
std::vector<std::string> g_cslFolders;
std::set<std::string> g_loggedOnce;
float g_lastHello = -1;
float g_lastGround = 0;
float g_lastStatus = 0;
bool g_tcas = false;

void Log(const std::string& text) {
    XPLMDebugString(("XPConnect: " + text + "\n").c_str());
}

void LogOnce(const std::string& text) {
    if (g_loggedOnce.insert(text).second) Log(text);
}

std::filesystem::path PluginFolder() {
    char path[1024] = {};
    XPLMGetPluginInfo(XPLMGetMyID(), nullptr, path, nullptr, nullptr);
    return std::filesystem::path(path).parent_path().parent_path();  // .../plugins/XPConnect/64/win.xpl -> XPConnect
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
    for (const auto& folder : xpc::CslFolders(XPlaneRoot(), extra, exists)) {
        const char* error = XPMPLoadCSLPackage(folder.c_str());
        if (error && *error) {
            Log("CSL " + folder + ": " + error);
        } else {
            g_cslFolders.push_back(folder);
            Log("CSL loaded " + folder);
        }
    }
    xpc::TrafficAircraft::SetCslPackages(xpc::FindCslPackages(g_cslFolders));
    Log(std::to_string(XPMPGetNumberOfInstalledModels()) + " CSL models from " + std::to_string(g_cslFolders.size()) + " folders");
}

void RemoveAll() {
    g_aircraft.clear();
}

void Handle(const xpc::Message& message, float now) {
    if (auto hello = std::get_if<xpc::Hello>(&message)) {
        if (g_lastHello < 0) Log("XPConnect " + hello->version + " connected");
        g_lastHello = now;
    } else if (auto add = std::get_if<xpc::Add>(&message)) {
        try {
            auto existing = g_aircraft.find(add->id);
            if (existing != g_aircraft.end()) {
                existing->second->ChangeTo(*add);
            } else {
                g_aircraft[add->id] = std::make_unique<xpc::TrafficAircraft>(*add);
                if (!g_tcas) {
                    const char* error = XPMPMultiplayerEnable();
                    g_tcas = !(error && *error);
                    if (!g_tcas) LogOnce(std::string("TCAS not available: ") + error);
                }
            }
        } catch (const std::exception& e) {
            g_socket.Send(xpc::XPConnectPort, xpc::FormatError(add->id, e.what()));
            LogOnce(std::string("cannot create ") + add->icaoType + " " + add->icaoAirline + ": " + e.what());
        }
    } else if (auto pos = std::get_if<xpc::Pos>(&message)) {
        auto found = g_aircraft.find(pos->id);
        if (found != g_aircraft.end()) found->second->Apply(*pos, now);
    } else if (auto offset = std::get_if<xpc::Offset>(&message)) {
        auto found = g_aircraft.find(offset->id);
        if (found != g_aircraft.end()) found->second->SetGroundTrim(offset->groundFt, offset->groundPitch);
    } else if (auto del = std::get_if<xpc::Del>(&message)) {
        g_aircraft.erase(del->id);
    } else if (std::holds_alternative<xpc::Clear>(message)) {
        RemoveAll();
    }
}

float FlightLoop(float, float, int, void*) {
    const float now = XPLMGetElapsedTime();
    static char buffer[65536];
    for (size_t size; (size = g_socket.Receive(buffer, sizeof(buffer))) > 0;) {
        for (const auto& line : xpc::Lines(buffer, size)) {
            std::string error;
            if (auto message = xpc::Parse(line, &error)) {
                Handle(*message, now);
            } else if (!error.empty()) {
                LogOnce(error.substr(0, error.find(' ', 12)));
            }
        }
    }

    if (g_lastHello >= 0 && now - g_lastHello > HelloTimeoutSeconds) {
        Log("XPConnect stopped sending; removing its traffic");
        RemoveAll();
        g_lastHello = -1;
    }

    if (now - g_lastGround >= GroundReportSeconds && !g_aircraft.empty()) {
        g_lastGround = now;
        std::string lines;
        for (const auto& [id, aircraft] : g_aircraft) {
            if (auto ground = aircraft->GroundElevationFt()) lines += xpc::FormatGround(id, *ground) + "\n";
            if (lines.size() > 60000) {
                g_socket.Send(xpc::XPConnectPort, lines);
                lines.clear();
            }
        }
        if (!lines.empty()) g_socket.Send(xpc::XPConnectPort, lines);
    }

    if (now - g_lastStatus >= StatusReportSeconds) {
        g_lastStatus = now;
        g_socket.Send(xpc::XPConnectPort,
                      xpc::FormatStatus(Version, g_aircraft.size(), XPMPGetNumberOfInstalledModels(), g_cslFolders));
    }
    return -1.0f;  // every frame
}

int Preferences(const char*, const char*, int defaultValue) { return defaultValue; }

// Plugins > XPConnect > Show tail labels. Off by default; the choice is kept in settings.txt next to the plugin.
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
    std::strcpy(name, "XPConnect");
    std::strcpy(signature, "com.nodeblue.xpconnect");
    std::strcpy(description, "Draws BeyondATC traffic sent by XPConnect");
    XPLMEnableFeature("XPLM_USE_NATIVE_PATHS", 1);
    return 1;
}

PLUGIN_API int XPluginEnable() {
    const auto resources = (PluginFolder() / "Resources").string();
    const char* error = XPMPMultiplayerInit("XPConnect", resources.c_str(), Preferences, "A320", "XPConnect");
    if (error && *error) {
        Log(std::string("XPMP2 init failed: ") + error);
        return 0;
    }
    if (!xpc::TrafficAircraft::AddModelDatarefs()) Log("XPMP2 refused the World Traffic gear dataref; those models' bogies stay tilted");
    LoadCsl();
    if (!g_menu) {
        const int root = XPLMAppendMenuItem(XPLMFindPluginsMenu(), "XPConnect", nullptr, 0);
        g_menu = XPLMCreateMenu("XPConnect", XPLMFindPluginsMenu(), root, MenuHandler, nullptr);
        g_labelsItem = XPLMAppendMenuItem(g_menu, "Show tail labels", &g_labelsItem, 0);
    }
    ApplyLabels(LabelsSetting(), false);

    std::string socketError;
    if (!g_socket.Open(xpc::PluginPort, socketError)) {
        Log("cannot listen for XPConnect: " + socketError);
    } else {
        Log(std::string("plugin ") + Version + " listening on 127.0.0.1:" + std::to_string(xpc::PluginPort));
    }
    XPLMRegisterFlightLoopCallback(FlightLoop, -1.0f, nullptr);
    return 1;
}

PLUGIN_API void XPluginDisable() {
    XPLMUnregisterFlightLoopCallback(FlightLoop, nullptr);
    RemoveAll();
    xpc::TrafficAircraft::ForgetModelFits();
    if (g_tcas) XPMPMultiplayerDisable();
    g_tcas = false;
    XPMPMultiplayerCleanup();
    g_socket.Close();
    g_lastHello = -1;
}

PLUGIN_API void XPluginStop() {}

PLUGIN_API void XPluginReceiveMessage(XPLMPluginID, int, void*) {}
