// Text protocol between XPConnect and this plugin. See docs/protocol/plugin.md.
// Pure C++ (no X-Plane SDK), so it is unit-tested on its own.
#pragma once

#include <cstdint>
#include <optional>
#include <string>
#include <variant>
#include <vector>

namespace xpc {

constexpr uint16_t PluginPort = 49710;     // XPConnect -> plugin
constexpr uint16_t XPConnectPort = 49711;  // plugin -> XPConnect

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

// Raise (+) or lower (-) aircraft `id` by this many feet when it is drawn on the ground (per-model fine tuning).
struct Offset { uint32_t id = 0; float groundFt = 0; };

struct Clear {};

using Message = std::variant<Hello, Add, Pos, Del, Clear, Offset>;

// Parses one line. Returns nothing for blank, malformed or unknown lines; `error` then says why (empty for blank).
std::optional<Message> Parse(const std::string& line, std::string* error = nullptr);

// Splits a datagram into lines (\n or \r\n separated).
std::vector<std::string> Lines(const char* data, size_t size);

std::string FormatStatus(const std::string& version, size_t aircraft, int models, const std::vector<std::string>& cslFolders);
std::string FormatGround(uint32_t id, double elevationFt);
std::string FormatError(uint32_t id, const std::string& message);

}  // namespace xpc
