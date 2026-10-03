// Fits a CSL model to the ground from its 3D files: where its nose and main wheels are, and the pitch and height
// that put both on the ground. X-Plane-independent so it can be tested; see docs/protocol/plugin.md.
#pragma once

#include <istream>
#include <map>
#include <optional>
#include <string>
#include <vector>

namespace xpc {

// A point of a model in OBJ8 coordinates (metres): x right, y up, z aft (the nose points to -z).
struct Vertex {
    float x = 0, y = 0, z = 0;
};

// The lowest point of the nose wheel and of the main wheels, as (z, y) in OBJ8 coordinates.
struct Wheels {
    float noseZ = 0, noseY = 0;
    float mainZ = 0, mainY = 0;

    // Pitch (degrees, nose up positive) that puts both wheels on level ground.
    float LevelPitchDeg() const;
    // Height (metres) of the model's origin above level ground so that, drawn at `pitchDeg`, its lower wheel touches.
    // Used instead of XPMP2's vertical offset, which comes from the package's VERT_OFFSET when it has one and is not
    // always where the wheels are (Bluebell at YBBN, 2026-10-01 and 2026-10-03: main wheels 10 to 50 cm up).
    float GroundHeightM(float pitchDeg) const;
};

// Finds the wheels among a model's vertices. Empty when the model does not look like a tricycle-gear aircraft or
// would need more than a few degrees of pitch (tail-draggers, helicopters, odd models): those are drawn as before.
std::optional<Wheels> FindWheels(const std::vector<Vertex>& vertices, std::string* why = nullptr);

// Adds the VT vertices of an OBJ8 file.
void ReadObjVertices(std::istream& obj, std::vector<Vertex>& vertices);

// CSL package names (EXPORT_NAME) to their folders, searching each folder like XPMP2 does.
std::map<std::string, std::string> FindCslPackages(const std::vector<std::string>& folders);

// The .obj files of the model whose OBJ8_AIRCRAFT line is line `line` (1-based) of `xsbFolder`/xsb_aircraft.txt.
std::vector<std::string> ModelObjFiles(const std::string& xsbFolder, int line,
                                       const std::map<std::string, std::string>& packages);

}  // namespace xpc
