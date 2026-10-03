#include "GroundFit.h"

#include <algorithm>
#include <cmath>
#include <filesystem>
#include <fstream>
#include <sstream>

namespace xpc {
namespace {

constexpr double Pi = 3.14159265358979323846;
// The nose wheel is in the front quarter of the model, near the centre line; the main wheels are in the middle half.
constexpr float NoseShare = 0.25f;
constexpr float MainEndShare = 0.75f;
constexpr float NoseHalfWidthM = 1.5f;
// A fit needing more pitch than this is not a wheel gap but a model we read wrongly.
constexpr float MaxFitPitchDeg = 3.0f;

std::vector<std::string> Tokens(const std::string& line) {
    std::istringstream in(line);
    std::vector<std::string> tokens;
    for (std::string t; in >> t;) tokens.push_back(t);
    return tokens;
}

bool GetLine(std::istream& in, std::string& line) {
    if (!std::getline(in, line)) return false;
    if (!line.empty() && line.back() == '\r') line.pop_back();
    return true;
}

std::filesystem::path Path(const std::string& text) { return std::filesystem::u8path(text); }

void FindPackages(const std::filesystem::path& folder, int depth, std::map<std::string, std::string>& packages) {
    std::error_code error;
    const auto xsb = folder / "xsb_aircraft.txt";
    if (std::filesystem::is_regular_file(xsb, error)) {
        std::ifstream file(xsb);
        for (std::string line; GetLine(file, line);) {
            auto tokens = Tokens(line);
            if (tokens.size() == 2 && tokens[0] == "EXPORT_NAME") packages.emplace(tokens[1], folder.u8string());
        }
        return;
    }
    if (depth <= 0) return;
    for (std::filesystem::directory_iterator it(folder, error), end; !error && it != end; it.increment(error)) {
        if (it->is_directory(error)) FindPackages(it->path(), depth - 1, packages);
    }
}

}  // namespace

float Wheels::LevelPitchDeg() const {
    return float(std::atan2(double(noseY) - mainY, double(mainZ) - noseZ) * -180.0 / Pi);
}

float Wheels::GroundHeightM(float pitchDeg) const {
    const double a = pitchDeg * Pi / 180.0;
    // Nose up turns the -z (forward) end up: y' = y cos(a) - z sin(a).
    auto pitched = [a](float y, float z) { return y * std::cos(a) - z * std::sin(a); };
    return float(-std::min(pitched(noseY, noseZ), pitched(mainY, mainZ)));
}

std::optional<Wheels> FindWheels(const std::vector<Vertex>& vertices, std::string* why) {
    auto fail = [why](const char* reason) {
        if (why) *why = reason;
        return std::optional<Wheels>();
    };
    if (vertices.size() < 20) return fail("too few vertices");

    float zMin = vertices[0].z, zMax = vertices[0].z;
    for (const auto& v : vertices) {
        zMin = std::min(zMin, v.z);
        zMax = std::max(zMax, v.z);
    }
    const float length = zMax - zMin;
    if (length < 3) return fail("model too short");

    const Vertex* nose = nullptr;
    const Vertex* main = nullptr;
    for (const auto& v : vertices) {
        const float share = (v.z - zMin) / length;
        if (share <= NoseShare) {
            if (std::fabs(v.x) <= NoseHalfWidthM && (!nose || v.y < nose->y)) nose = &v;
        } else if (share <= MainEndShare) {
            if (!main || v.y < main->y) main = &v;
        }
    }
    if (!nose || !main) return fail("no nose or main wheels found");

    Wheels wheels{nose->z, nose->y, main->z, main->y};
    if (std::fabs(wheels.LevelPitchDeg()) > MaxFitPitchDeg) return fail("wheels not level within 3 degrees");
    return wheels;
}

void ReadObjVertices(std::istream& obj, std::vector<Vertex>& vertices) {
    for (std::string line; GetLine(obj, line);) {
        // OBJ8 files are long; only "VT x y z nx ny nz s t" lines matter.
        if (line.size() < 8 || line[0] != 'V' || line[1] != 'T' || (line[2] != ' ' && line[2] != '\t')) continue;
        std::istringstream in(line.substr(3));
        Vertex v;
        if (in >> v.x >> v.y >> v.z) vertices.push_back(v);
    }
}

std::map<std::string, std::string> FindCslPackages(const std::vector<std::string>& folders) {
    std::map<std::string, std::string> packages;
    for (const auto& folder : folders) FindPackages(Path(folder), 5, packages);
    return packages;
}

std::vector<std::string> ModelObjFiles(const std::string& xsbFolder, int line,
                                       const std::map<std::string, std::string>& packages) {
    std::vector<std::string> files;
    std::ifstream xsb(Path(xsbFolder) / "xsb_aircraft.txt");
    std::string text;
    for (int n = 1; n < line && GetLine(xsb, text); ++n) {
    }
    if (!GetLine(xsb, text) || Tokens(text).empty() || Tokens(text)[0] != "OBJ8_AIRCRAFT") return files;

    while (GetLine(xsb, text)) {
        auto tokens = Tokens(text);
        if (tokens.empty()) continue;
        if (tokens[0] == "OBJ8_AIRCRAFT" || tokens[0] == "OBJECT" || tokens[0] == "AIRCRAFT") break;
        if (tokens[0] != "OBJ8" || tokens.size() < 4) continue;

        // "BB_A320:A320/a320.obj" or "BB_A320/A320/a320.obj": package name, then a path inside the package.
        const auto& packagePath = tokens[3];
        const auto split = packagePath.find_first_of(":/\\");
        if (split == std::string::npos || split == 0) continue;
        const auto package = packages.find(packagePath.substr(0, split));
        if (package == packages.end()) continue;
        std::string relative = packagePath.substr(split + 1);
        std::replace_if(relative.begin(), relative.end(), [](char c) { return c == ':' || c == '\\'; }, '/');
        files.push_back((Path(package->second) / Path(relative)).u8string());
    }
    return files;
}

}  // namespace xpc
