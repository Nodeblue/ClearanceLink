// Text protocol between ClearanceLink and this plugin. See docs/protocol/plugin.md.
// Pure C++ (no X-Plane SDK), so it is unit-tested on its own.
#pragma once

#include <cstdint>
#include <optional>
#include <string>
#include <variant>
#include <vector>

namespace clink {

// The plugin listens on the first of these it can open; ClearanceLink sends to each until the plugin answers, and the
// plugin answers whoever wrote last (ClearanceLink listens on any free port). 0.5.3+: the old 49710/49711 sat in a
// range Windows reserves for Hyper-V/WinNAT on some boots (WSA error 10013), so the first choices are below Windows'
// dynamic ports (49152+), spread out in case one block is reserved. Keep in step with PluginLink.PluginPorts.
constexpr uint16_t PluginPorts[] = {34710, 38710, 42710, 46710, 49710};

enum Lights : unsigned { LightNav = 1, LightBeacon = 2, LightStrobe = 4, LightLanding = 8, LightTaxi = 16 };

struct Hello { std::string version; };

struct Add {
    uint32_t id = 0;
    std::string icaoType;
    std::string icaoAirline;  // empty when unknown
    std::string livery;       // empty when unknown
    std::string label;
};

struct Pos {
    uint32_t id = 0;
    double lat = 0, lon = 0, altFt = 0;
    float heading = 0, pitch = 0, bank = 0, groundSpeedKt = 0;
    bool onGround = false;
    float gear = 0, flaps = 0, spoilers = 0, thrust = 0;
    unsigned lights = 0;
};

struct Del { uint32_t id = 0; };

// Raise (+) or lower (-) aircraft `id` by this many feet when it is drawn on the ground, and (0.4.0+, optional) the
// pitch to park it at when its model's wheels could not be read (ClearanceLink's per-type tuning).
struct Offset { uint32_t id = 0; float groundFt = 0; std::optional<float> groundPitch; };

struct Clear {};

// 0.5.3+: load this X-Plane .fms flight plan file (UTF-8 path) into X-Plane's own FMS/GPS; answered with "fms ok" or
// "fms error <why>".
struct Fms { std::string path; };

using Message = std::variant<Hello, Add, Pos, Del, Clear, Offset, Fms>;

// Parses one line. Returns nothing for blank, malformed or unknown lines; `error` then says why (empty for blank).
std::optional<Message> Parse(const std::string& line, std::string* error = nullptr);

// Splits a datagram into lines (\n or \r\n separated).
std::vector<std::string> Lines(const char* data, size_t size);

std::string FormatStatus(const std::string& version, size_t aircraft, int models, const std::vector<std::string>& cslFolders);
std::string FormatGround(uint32_t id, double elevationFt);
std::string FormatError(uint32_t id, const std::string& message);
std::string FormatFms(const std::string& error);  // "fms ok" when error is empty, else "fms error <error>"

// True when the text looks like an X-Plane 11+ .fms flight plan (first line I or A, then "<n> Version").
bool LooksLikeFms(const std::string& text);

}  // namespace clink
