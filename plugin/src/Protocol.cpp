#include "Protocol.h"

#include <algorithm>
#include <charconv>
#include <cmath>
#include <locale>
#include <sstream>

namespace clink {
namespace {

std::vector<std::string> Words(const std::string& line, size_t maxWords, std::string* rest) {
    std::vector<std::string> words;
    size_t i = 0;
    while (i < line.size() && words.size() < maxWords) {
        while (i < line.size() && line[i] == ' ') ++i;
        if (i >= line.size()) break;
        size_t end = line.find(' ', i);
        if (end == std::string::npos) end = line.size();
        words.push_back(line.substr(i, end - i));
        i = end;
    }
    if (rest) {
        while (i < line.size() && line[i] == ' ') ++i;
        *rest = i < line.size() ? line.substr(i) : std::string();
    }
    return words;
}

// Locale-independent number parsing: X-Plane may run with a decimal-comma locale. std::from_chars never looks at the
// locale and allocates nothing (a pos line has 13 numbers, ~640 lines a second at 64 aircraft); standard libraries
// without floating-point from_chars (older libc++) use a stream imbued with the classic locale instead.
bool Number(const std::string& text, double& out) {
    if (text.empty()) return false;
#if defined(__cpp_lib_to_chars) && __cpp_lib_to_chars >= 201611L
    const char* const end = text.data() + text.size();
    const auto [ptr, ec] = std::from_chars(text.data(), end, out, std::chars_format::general);
    return ec == std::errc() && ptr == end && std::isfinite(out);
#else
    std::istringstream stream(text);
    stream.imbue(std::locale::classic());
    stream >> out;
    return !stream.fail() && stream.eof() && std::isfinite(out);
#endif
}

float Ratio(double value) { return static_cast<float>(std::clamp(value, 0.0, 1.0)); }

bool Id(const std::string& text, uint32_t& out) {
    double value = 0;
    if (!Number(text, value) || value < 1 || value > 0xFFFFFF || value != std::floor(value)) return false;
    out = static_cast<uint32_t>(value);
    return true;
}

std::string OrEmpty(const std::string& word) { return word == "-" ? std::string() : word; }

std::string Fixed(double value, int decimals) {
    std::ostringstream out;
    out.imbue(std::locale::classic());
    out.setf(std::ios::fixed);
    out.precision(decimals);
    out << value;
    return out.str();
}

}  // namespace

std::optional<Message> Parse(const std::string& line, std::string* error) {
    auto fail = [&](const std::string& why) -> std::optional<Message> {
        if (error) *error = why;
        return std::nullopt;
    };
    if (error) error->clear();
    std::string rest;
    auto head = Words(line, 1, &rest);
    if (head.empty()) return std::nullopt;
    const std::string& type = head[0];

    if (type == "hello") return Hello{rest};
    if (type == "clear") return Clear{};
    if (type == "del") {
        auto w = Words(rest, 1, nullptr);
        Del del;
        if (w.size() != 1 || !Id(w[0], del.id)) return fail("bad del: " + line);
        return del;
    }
    if (type == "offset") {
        auto w = Words(rest, 4, nullptr);
        Offset offset;
        double feet = 0, pitch = 0;
        if (w.size() < 2 || w.size() > 3 || !Id(w[0], offset.id) || !Number(w[1], feet) || std::fabs(feet) > 50)
            return fail("bad offset: " + line);
        if (w.size() == 3) {
            if (!Number(w[2], pitch) || std::fabs(pitch) > 10) return fail("bad offset: " + line);
            offset.groundPitch = static_cast<float>(pitch);
        }
        offset.groundFt = static_cast<float>(feet);
        return offset;
    }
    if (type == "add") {
        std::string label;
        auto w = Words(rest, 4, &label);
        Add add;
        if (w.size() != 4 || !Id(w[0], add.id) || w[1] == "-") return fail("bad add: " + line);
        add.icaoType = w[1];
        add.icaoAirline = OrEmpty(w[2]);
        add.livery = OrEmpty(w[3]);
        add.label = label;
        return add;
    }
    if (type == "pos") {
        auto w = Words(rest, 15, nullptr);
        if (w.size() != 14) return fail("bad pos (need 14 fields): " + line);
        Pos pos;
        double v[13];
        for (int i = 0; i < 13; ++i)
            if (!Number(w[i + 1], v[i])) return fail("bad pos number '" + w[i + 1] + "': " + line);
        // Lights is a bit mask of the five lights the protocol knows: anything else is a broken line (casting a negative
        // or huge double to unsigned is undefined).
        if (!Id(w[0], pos.id) || std::fabs(v[0]) > 90 || std::fabs(v[1]) > 180 || v[12] < 0 || v[12] > 31 ||
            v[12] != std::floor(v[12]))
            return fail("bad pos: " + line);
        pos.lat = v[0];
        pos.lon = v[1];
        pos.altFt = v[2];
        pos.heading = static_cast<float>(v[3]);
        pos.pitch = static_cast<float>(v[4]);
        pos.bank = static_cast<float>(v[5]);
        pos.groundSpeedKt = static_cast<float>(v[6]);
        pos.onGround = v[7] != 0;
        pos.gear = Ratio(v[8]);
        pos.flaps = Ratio(v[9]);
        pos.spoilers = Ratio(v[10]);
        pos.thrust = Ratio(v[11]);
        pos.lights = static_cast<unsigned>(v[12]);
        return pos;
    }
    return fail("unknown message: " + type);
}

std::vector<std::string> Lines(const char* data, size_t size) {
    std::vector<std::string> lines;
    std::string current;
    for (size_t i = 0; i < size; ++i) {
        char c = data[i];
        if (c == '\n') {
            if (!current.empty() && current.back() == '\r') current.pop_back();
            if (!current.empty()) lines.push_back(current);
            current.clear();
        } else {
            current.push_back(c);
        }
    }
    if (!current.empty() && current.back() == '\r') current.pop_back();
    if (!current.empty()) lines.push_back(current);
    return lines;
}

std::string FormatStatus(const std::string& version, size_t aircraft, int models, const std::vector<std::string>& cslFolders) {
    std::string folders;
    for (const auto& folder : cslFolders) folders += (folders.empty() ? "" : ";") + folder;
    return "status " + version + " " + std::to_string(aircraft) + " " + std::to_string(models) + " " +
           (folders.empty() ? "-" : folders);
}

std::string FormatGround(uint32_t id, double elevationFt) {
    return "ground " + std::to_string(id) + " " + Fixed(elevationFt, 1);
}

std::string FormatError(uint32_t id, const std::string& message) {
    std::string clean = message;
    for (auto& c : clean)
        if (c == '\n' || c == '\r') c = ' ';
    return "error " + std::to_string(id) + " " + clean;
}

}  // namespace clink
