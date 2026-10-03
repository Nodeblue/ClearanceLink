#include "GroundFit.h"

#include <algorithm>
#include <cmath>
#include <filesystem>
#include <fstream>
#include <cstdlib>
#include <sstream>

namespace xpc {
namespace {

constexpr double Pi = 3.14159265358979323846;
// Without gear animation: the nose wheel is in the front quarter of the model, near the centre line; the main wheels
// are in the middle half.
constexpr float NoseShare = 0.25f;
constexpr float MainEndShare = 0.75f;
constexpr float NoseHalfWidthM = 1.5f;
// With gear animation: the nose gear is in the front third, the main gear behind it.
constexpr float GearNoseShare = 0.35f;
// A fit needing more pitch than this is not a wheel gap but a model we read wrongly.
constexpr float MaxFitPitchDeg = 3.0f;

// A 3x4 affine transform (rotation and translation), applied as OBJ8 animations are: each ANIM_ command multiplies
// onto the current transform, so the last command is applied to a point first.
struct Affine {
    double m[3][4] = {{1, 0, 0, 0}, {0, 1, 0, 0}, {0, 0, 1, 0}};

    Affine operator*(const Affine& b) const {
        Affine r;
        for (int i = 0; i < 3; ++i) {
            for (int j = 0; j < 4; ++j) {
                r.m[i][j] = m[i][0] * b.m[0][j] + m[i][1] * b.m[1][j] + m[i][2] * b.m[2][j] + (j == 3 ? m[i][3] : 0);
            }
        }
        return r;
    }

    Vertex Apply(const Vertex& v) const {
        return {float(m[0][0] * v.x + m[0][1] * v.y + m[0][2] * v.z + m[0][3]),
                float(m[1][0] * v.x + m[1][1] * v.y + m[1][2] * v.z + m[1][3]),
                float(m[2][0] * v.x + m[2][1] * v.y + m[2][2] * v.z + m[2][3])};
    }

    static Affine Translate(double x, double y, double z) {
        Affine a;
        a.m[0][3] = x;
        a.m[1][3] = y;
        a.m[2][3] = z;
        return a;
    }

    // Right-handed rotation by `deg` about the axis (x, y, z), as glRotate.
    static Affine Rotate(double x, double y, double z, double deg) {
        Affine a;
        const double length = std::sqrt(x * x + y * y + z * z);
        if (length < 1e-9) return a;
        x /= length;
        y /= length;
        z /= length;
        const double c = std::cos(deg * Pi / 180.0), s = std::sin(deg * Pi / 180.0), t = 1 - c;
        const double r[3][3] = {{t * x * x + c, t * x * y - s * z, t * x * z + s * y},
                                {t * x * y + s * z, t * y * y + c, t * y * z - s * x},
                                {t * x * z - s * y, t * y * z + s * x, t * z * z + c}};
        for (int i = 0; i < 3; ++i)
            for (int j = 0; j < 3; ++j) a.m[i][j] = r[i][j];
        return a;
    }
};

// Piecewise-linear lookup of `value` in (key, result) pairs, held at the ends.
double Interpolate(const std::vector<std::pair<double, double>>& keys, double value) {
    if (keys.empty()) return 0;
    if (keys.size() == 1 || value <= keys.front().first) return keys.front().second;
    for (size_t i = 1; i < keys.size(); ++i) {
        if (value <= keys[i].first) {
            const double span = keys[i].first - keys[i - 1].first;
            const double f = span > 0 ? (value - keys[i - 1].first) / span : 1;
            return keys[i - 1].second + (keys[i].second - keys[i - 1].second) * f;
        }
    }
    return keys.back().second;
}

double Number(const std::vector<std::string>& tokens, size_t i) {
    return i < tokens.size() ? std::strtod(tokens[i].c_str(), nullptr) : 0;
}

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

float PoseValue(const std::string& dataref) {
    return dataref.find("gear_ratio") != std::string::npos || dataref.find("gear_door") != std::string::npos ? 1.0f : 0.0f;
}

std::optional<Wheels> FindWheels(const PosedModel& model, std::string* why) {
    auto fail = [why](const char* reason) {
        if (why) *why = reason;
        return std::optional<Wheels>();
    };
    const auto& vertices = model.all;
    if (model.gear.empty() && vertices.size() < 20) return fail("too few vertices");
    if (vertices.empty()) return fail("too few vertices");

    float zMin = vertices[0].z, zMax = vertices[0].z;
    for (const auto& v : vertices) {
        zMin = std::min(zMin, v.z);
        zMax = std::max(zMax, v.z);
    }
    const float length = zMax - zMin;
    if (length < 3) return fail("model too short");

    const Vertex* nose = nullptr;
    const Vertex* main = nullptr;
    if (!model.gear.empty()) {
        for (const auto& v : model.gear) {
            const float share = (v.z - zMin) / length;
            const Vertex*& wheel = share <= GearNoseShare ? nose : main;
            if (!wheel || v.y < wheel->y) wheel = &v;
        }
    } else {
        for (const auto& v : vertices) {
            const float share = (v.z - zMin) / length;
            if (share <= NoseShare) {
                if (std::fabs(v.x) <= NoseHalfWidthM && (!nose || v.y < nose->y)) nose = &v;
            } else if (share <= MainEndShare) {
                if (!main || v.y < main->y) main = &v;
            }
        }
    }
    if (!nose || !main) return fail("no nose or main wheels found");

    Wheels wheels{nose->z, nose->y, main->z, main->y};
    if (std::fabs(wheels.LevelPitchDeg()) > MaxFitPitchDeg) return fail("wheels not level within 3 degrees");
    return wheels;
}

void ReadPosedObj(std::istream& obj, PosedModel& model) {
    std::vector<Vertex> vt;
    std::vector<int> idx;
    struct Frame {
        Affine transform;
        bool visible = true;
        bool gear = false;
    };
    std::vector<Frame> stack(1);
    // Keyed animations collect their keys until their _end line.
    std::vector<std::pair<double, double>> rotateKeys;
    std::vector<std::pair<double, Vertex>> transKeys;
    double axis[3] = {0, 0, 0};
    std::string keyedRef;

    // Only the gear legs and what hangs from them (wheels), not the gear doors.
    auto isGear = [](const std::string& ref) { return ref.find("gear_ratio") != std::string::npos; };
    auto rotate = [&](double x, double y, double z, double deg, const std::string& ref) {
        stack.back().transform = stack.back().transform * Affine::Rotate(x, y, z, deg);
        if (ref != "none" && isGear(ref)) stack.back().gear = true;
    };
    auto translate = [&](const Vertex& by, const std::string& ref) {
        stack.back().transform = stack.back().transform * Affine::Translate(by.x, by.y, by.z);
        if (ref != "none" && isGear(ref) && (by.x != 0 || by.y != 0 || by.z != 0)) stack.back().gear = true;
    };

    for (std::string line; GetLine(obj, line);) {
        if (line.size() < 3) continue;
        const auto tokens = Tokens(line);
        if (tokens.empty()) continue;
        const auto& cmd = tokens[0];
        if (cmd == "VT") {
            if (tokens.size() >= 4) vt.push_back({float(Number(tokens, 1)), float(Number(tokens, 2)), float(Number(tokens, 3))});
        } else if (cmd == "IDX10" || cmd == "IDX") {
            for (size_t i = 1; i < tokens.size(); ++i) idx.push_back(std::atoi(tokens[i].c_str()));
        } else if (cmd == "ANIM_begin") {
            stack.push_back(stack.back());
        } else if (cmd == "ANIM_end") {
            if (stack.size() > 1) stack.pop_back();
        } else if (cmd == "ANIM_rotate" && tokens.size() >= 9) {
            const double v = PoseValue(tokens[8]);
            const double deg = Interpolate({{Number(tokens, 6), Number(tokens, 4)}, {Number(tokens, 7), Number(tokens, 5)}}, v);
            rotate(Number(tokens, 1), Number(tokens, 2), Number(tokens, 3), deg, tokens[8]);
        } else if (cmd == "ANIM_trans" && tokens.size() >= 10) {
            const double v = PoseValue(tokens[9]);
            const double v1 = Number(tokens, 7), v2 = Number(tokens, 8);
            Vertex by;
            by.x = float(Interpolate({{v1, Number(tokens, 1)}, {v2, Number(tokens, 4)}}, v));
            by.y = float(Interpolate({{v1, Number(tokens, 2)}, {v2, Number(tokens, 5)}}, v));
            by.z = float(Interpolate({{v1, Number(tokens, 3)}, {v2, Number(tokens, 6)}}, v));
            translate(by, tokens[9]);
        } else if (cmd == "ANIM_rotate_begin" && tokens.size() >= 5) {
            axis[0] = Number(tokens, 1);
            axis[1] = Number(tokens, 2);
            axis[2] = Number(tokens, 3);
            keyedRef = tokens[4];
            rotateKeys.clear();
        } else if (cmd == "ANIM_rotate_key") {
            rotateKeys.emplace_back(Number(tokens, 1), Number(tokens, 2));
        } else if (cmd == "ANIM_rotate_end") {
            rotate(axis[0], axis[1], axis[2], Interpolate(rotateKeys, PoseValue(keyedRef)), keyedRef);
        } else if (cmd == "ANIM_trans_begin" && tokens.size() >= 2) {
            keyedRef = tokens[1];
            transKeys.clear();
        } else if (cmd == "ANIM_trans_key") {
            transKeys.emplace_back(Number(tokens, 1), Vertex{float(Number(tokens, 2)), float(Number(tokens, 3)), float(Number(tokens, 4))});
        } else if (cmd == "ANIM_trans_end") {
            const double v = PoseValue(keyedRef);
            auto axisKeys = [&](int k) {
                std::vector<std::pair<double, double>> keys;
                for (const auto& [at, p] : transKeys) keys.emplace_back(at, k == 0 ? p.x : k == 1 ? p.y : p.z);
                return keys;
            };
            translate({float(Interpolate(axisKeys(0), v)), float(Interpolate(axisKeys(1), v)), float(Interpolate(axisKeys(2), v))}, keyedRef);
        } else if ((cmd == "ANIM_hide" || cmd == "ANIM_show") && tokens.size() >= 4) {
            const double v = PoseValue(tokens[3]);
            const double lo = std::min(Number(tokens, 1), Number(tokens, 2)), hi = std::max(Number(tokens, 1), Number(tokens, 2));
            if (v >= lo && v <= hi) stack.back().visible = cmd == "ANIM_show";
        } else if (cmd == "TRIS" && tokens.size() >= 3) {
            const Frame& frame = stack.back();
            if (!frame.visible) continue;
            const long first = std::atol(tokens[1].c_str()), count = std::atol(tokens[2].c_str());
            for (long i = first; i < first + count && i >= 0 && size_t(i) < idx.size(); ++i) {
                const int n = idx[size_t(i)];
                if (n < 0 || size_t(n) >= vt.size()) continue;
                const Vertex v = frame.transform.Apply(vt[size_t(n)]);
                model.all.push_back(v);
                if (frame.gear) model.gear.push_back(v);
            }
        }
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
