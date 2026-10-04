// Fits a CSL model to the ground from its 3D files: where its nose and main wheels are, and the pitch and height
// that put both on the ground. X-Plane-independent so it can be tested; see docs/protocol/plugin.md.
#pragma once

#include <istream>
#include <map>
#include <optional>
#include <string>
#include <vector>

namespace clink {

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

// A model's drawn points with its gear down: `all` of them, and `gear`, those its gear animation moves.
struct PosedModel {
    std::vector<Vertex> all;
    std::vector<Vertex> gear;
    // The dataref that tilts its main bogies, and the value it was posed with: the one that levels them (777, A340,
    // A380 tilt theirs about 20 degrees when unloaded), else empty and 0. Draw it with this too. Standard models use
    // libxplanemp/gear/tire_vertical_deflection_mtr; Bluebell's virtual-airline repaints use World Traffic's
    // cjs/world_traffic/main_gear_deflection (B772_PAY at YBBN, 2026-10-03: bogies drawn tilted at 0).
    std::string bogieDataref;
    float bogieLevel = 0;
    float bogieTiltDeg = 0;      // the bogie wheels' tilt unloaded (front against rear), for the log
    float bogieRangeDeg = 0;     // how far its rotation turns: the bogie is the one that turns furthest
    bool bogiesScanned = false;  // bogie level already found (ScanBogies over all of a model's files)
};

// Finds the wheels among a model's points. With gear points, the nose wheel is the lowest gear point in the front
// third and the main wheels the lowest gear point behind it; without, the lowest points of the front quarter near the
// centre line and of the middle half. Empty when the model does not look like a tricycle-gear aircraft or would need
// more than a few degrees of pitch (tail-draggers, helicopters, odd models): those are drawn as before.
std::optional<Wheels> FindWheels(const PosedModel& model, std::string* why = nullptr);

// Adds the points of an OBJ8 file's triangles as drawn with the gear down: every ANIM_ transform applied with gear
// and gear door datarefs at 1 and all others (tyre deflection, steering, flaps...) at 0, and parts that ANIM_hide /
// ANIM_show then hide left out. Bluebell and most CSL models keep their gear retracted in the file and lower it by
// animation (B738 and E190 at YBBN, 2026-10-03), so the raw VT points are not where the wheels are drawn.
// A model whose bogies were not scanned yet takes them from this file alone.
void ReadPosedObj(std::istream& obj, PosedModel& model);

// Finds the bogie tilt in one of a model's .obj files, keeping the most tilted across files: a model split over
// several files is posed with one bogie level, whichever file its gear is in.
void ScanBogies(std::istream& obj, PosedModel& model);

// A model's .obj files posed with the gear down: bogies scanned in all of them first, then each file posed.
PosedModel PoseObjFiles(const std::vector<std::string>& files);

// The datarefs a model may tilt its main bogies with, by how much they are loaded.
constexpr const char* TireDeflectionDataref = "libxplanemp/gear/tire_vertical_deflection_mtr";
constexpr const char* WorldTrafficDeflectionDataref = "cjs/world_traffic/main_gear_deflection";

// Value a posed model uses for a dataref, given the bogie dataref and the level it is posed with.
float PoseValue(const std::string& dataref, const std::string& bogieDataref = "", float bogieLevel = 0);

// CSL package names (EXPORT_NAME) and their folders. One package can span several folders, each with the same
// xsb_aircraft.txt but different .obj files (Bluebell's __Bluebell_Airbus is BB_Airbus, BB_Airbus1 and BB_Airbus2).
using CslPackages = std::multimap<std::string, std::string>;

// Finds the CSL packages in these folders, searching each like XPMP2 does.
CslPackages FindCslPackages(const std::vector<std::string>& folders);

// The .obj files of the model whose OBJ8_AIRCRAFT line is line `line` (1-based) of `xsbFolder`/xsb_aircraft.txt,
// each from the first of its package's folders that has it.
std::vector<std::string> ModelObjFiles(const std::string& xsbFolder, int line, const CslPackages& packages);

}  // namespace clink
