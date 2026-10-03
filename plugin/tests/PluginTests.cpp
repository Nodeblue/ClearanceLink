// Tests for the parts of the plugin that need no X-Plane: the protocol, the CSL folder search and the ground fit.
#include "CslFolders.h"
#include "GroundFit.h"
#include "Motion.h"
#include "Protocol.h"

#include <cmath>
#include <cstdio>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <set>
#include <sstream>
#include <string>

namespace {

int g_failures = 0;

void Check(bool ok, const char* what, int line) {
    if (!ok) {
        std::printf("FAIL line %d: %s\n", line, what);
        ++g_failures;
    }
}

#define CHECK(x) Check((x), #x, __LINE__)

void ParsesPos() {
    auto m = xpc::Parse("pos 7 -27.384 153.117 23.5 13.0 2.5 -1.25 15.5 1 1 0.25 0 0.3 19");
    CHECK(m.has_value());
    auto pos = std::get<xpc::Pos>(*m);
    CHECK(pos.id == 7);
    CHECK(std::fabs(pos.lat + 27.384) < 1e-9);
    CHECK(std::fabs(pos.lon - 153.117) < 1e-9);
    CHECK(pos.altFt == 23.5);
    CHECK(pos.bank == -1.25f);
    CHECK(pos.onGround);
    CHECK(pos.flaps == 0.25f);
    CHECK(pos.lights == (xpc::LightNav | xpc::LightBeacon | xpc::LightTaxi));
}

void ParsesOffset() {
    auto offset = std::get<xpc::Offset>(*xpc::Parse("offset 9 -1.5"));
    CHECK(offset.id == 9);
    CHECK(offset.groundFt == -1.5f);
    std::string error;
    CHECK(!xpc::Parse("offset 9 900", &error) && error.find("bad offset") != std::string::npos);
    CHECK(!offset.groundPitch);
    auto trimmed = std::get<xpc::Offset>(*xpc::Parse("offset 9 0.00 -1.50"));
    CHECK(trimmed.groundPitch && *trimmed.groundPitch == -1.5f);
    CHECK(!xpc::Parse("offset 9 0 45", &error));
}

void ParsesAddWithLabelAndUnknowns() {
    auto add = std::get<xpc::Add>(*xpc::Parse("add 3 B738 QFA - VH-XZE Qantas 1"));
    CHECK(add.id == 3);
    CHECK(add.icaoType == "B738");
    CHECK(add.icaoAirline == "QFA");
    CHECK(add.livery.empty());
    CHECK(add.label == "VH-XZE Qantas 1");
    auto bare = std::get<xpc::Add>(*xpc::Parse("add 4 C172 - -"));
    CHECK(bare.icaoAirline.empty() && bare.label.empty());
}

void RejectsBadLines() {
    std::string error;
    CHECK(!xpc::Parse("pos 7 1 2", &error) && error.find("14 fields") != std::string::npos);
    CHECK(!xpc::Parse("pos 0 1 2 3 4 5 6 7 0 0 0 0 0 0", &error));  // id 0 is not allowed
    CHECK(!xpc::Parse("pos 1 95 2 3 4 5 6 7 0 0 0 0 0 0", &error));  // latitude out of range
    CHECK(!xpc::Parse("pos 1 1,5 2 3 4 5 6 7 0 0 0 0 0 0", &error)); // decimal comma
    CHECK(!xpc::Parse("add 1 - QFA -", &error));
    CHECK(!xpc::Parse("warp 1", &error) && error == "unknown message: warp");
    CHECK(!xpc::Parse("   ", &error) && error.empty());
    CHECK(std::holds_alternative<xpc::Clear>(*xpc::Parse("clear")));
    CHECK(std::get<xpc::Del>(*xpc::Parse("del 9")).id == 9);
    CHECK(std::get<xpc::Hello>(*xpc::Parse("hello 0.4.0")).version == "0.4.0");
}

void SplitsDatagrams() {
    const char data[] = "hello 1\r\npos 1 0 0 0 0 0 0 0 0 0 0 0 0 0\n\nclear";
    auto lines = xpc::Lines(data, std::strlen(data));
    CHECK(lines.size() == 3);
    CHECK(lines[0] == "hello 1");
    CHECK(lines[2] == "clear");
}

void FormatsReplies() {
    CHECK(xpc::FormatGround(5, 12.345) == "ground 5 12.3");
    CHECK(xpc::FormatStatus("0.1.0", 2, 0, {}) == "status 0.1.0 2 0 -");
    CHECK(xpc::FormatStatus("0.1.0", 0, 900, {"C:\\a", "D:\\b c"}) == "status 0.1.0 0 900 C:\\a;D:\\b c");
    CHECK(xpc::FormatError(5, "no\nmodel") == "error 5 no model");
}

void FindsCslFolders() {
    std::set<std::string> existing;
#ifdef _WIN32
    const std::string root = "C:\\X-Plane 12\\";
    existing = {"D:\\CSL\\Bluebell", "C:\\X-Plane 12\\Resources\\plugins\\LiveTraffic\\Resources\\CSL"};
#else
    const std::string root = "/xp/";
    existing = {"/csl/Bluebell", "/xp/Resources/plugins/LiveTraffic/Resources/CSL", "/xp/Custom Data/CSL"};
#endif
    auto exists = [&](const std::string& p) { return existing.count(p) > 0; };
#ifdef _WIN32
    auto found = xpc::CslFolders(root, {"# comment", " D:\\CSL\\Bluebell ", "", "D:\\missing"}, exists);
    CHECK(found.size() == 2 && found[0] == "D:\\CSL\\Bluebell");
#else
    auto found = xpc::CslFolders(root, {"# comment", " /csl/Bluebell/ ", "", "/missing", "Custom Data/CSL"}, exists);
    CHECK(found.size() == 3);
    CHECK(found[0] == "/csl/Bluebell");
    CHECK(found[1] == "/xp/Custom Data/CSL");  // listed once although also a default
    CHECK(found[2] == "/xp/Resources/plugins/LiveTraffic/Resources/CSL");
#endif
}

// A 36 m airliner in OBJ8 coordinates (nose at -z): fuselage, engines under the wings, nose and main wheels.
std::vector<xpc::Vertex> Airliner(float noseWheelY) {
    std::vector<xpc::Vertex> v;
    for (float z = -18; z <= 18; z += 1) v.push_back({0, 1.5f + (z > 10 ? z - 10 : 0) * 0.2f, z});
    for (float x : {-5.0f, 5.0f}) v.push_back({x, -1.4f, -3});    // engines: low, but off the centre line
    for (float x : {-0.2f, 0.2f}) v.push_back({x, noseWheelY, -14});
    for (float x : {-3.0f, 3.0f}) v.push_back({x, -2.0f, 1});
    return v;
}

void FitsNoseAndMainWheels() {
    auto wheels = xpc::FindWheels(xpc::PosedModel{Airliner(-1.9f), {}});
    CHECK(wheels.has_value());
    CHECK(wheels->noseZ == -14 && wheels->mainZ == 1);
    const float pitch = wheels->LevelPitchDeg();
    CHECK(pitch < -0.37f && pitch > -0.39f);  // nose wheel 0.1 m high over 15 m: nose down a little
    // Pitched and at that height above the ground, both wheels touch it.
    const double a = pitch * 3.14159265358979 / 180;
    const float height = wheels->GroundHeightM(pitch);
    const double nose = -1.9 * std::cos(a) + 14 * std::sin(a) + height;
    const double main = -2.0 * std::cos(a) - 1 * std::sin(a) + height;
    CHECK(std::fabs(nose) < 1e-4 && std::fabs(main) < 1e-4);
    CHECK(height > 1.99f && height < 2.0f);
    // Rotating for take-off lifts the nose; the model rises so the main wheels stay on the ground.
    CHECK(wheels->GroundHeightM(8) > height + 0.1f);
    CHECK(std::fabs(xpc::Wheels{-14, -2, 1, -2}.GroundHeightM(0) - 2) < 1e-6f);
}

void LeavesOddModelsLevel() {
    std::string why;
    CHECK(!xpc::FindWheels(xpc::PosedModel{Airliner(-1.0f), {}}, &why) && why.find("3 degrees") != std::string::npos);
    CHECK(!xpc::FindWheels(xpc::PosedModel{{{0, 0, 0}}, {}}, &why) && why == "too few vertices");
}

// A cut-down Bluebell B738 (gear lines as in B738_QFA.obj, read on Ben's PC 2026-10-03): a fuselage, then the nose
// gear stored retracted and swung down 110 degrees about -x by gear_ratio, a door that only moves with the door ratio,
// a main leg rotated 3 to -81 degrees about z with its tyre in a nested tyre-rotation block, and a part shown only on
// touch-down.
const char* const PosedB738 =
    "I\n800\nOBJ\n\nTEXTURE B738.dds\nPOINT_COUNTS 8 0 0 8\n"
    "VT 0 1 -18 0 1 0 0 0\n"         // 0 nose of the fuselage
    "VT 0 1 18 0 1 0 0 0\n"          // 1 tail
    "VT 0 -0.716 -16.173 0 1 0 0 0\n" // 2 nose wheel axle, retracted (0.47 m above and 1.19 m ahead of the hinge)
    "VT 0.4 -1.2 -15.76 0 1 0 0 0\n"  // 3 nose gear door
    "VT -0.70 -1.08 0.13 0 1 0 0 0\n" // 4 main wheel, retracted
    "VT 0 -9 0 0 1 0 0 0\n"          // 5 touch-down smoke, hidden
    "VT 0 0 0 0 1 0 0 0\n"
    "VT 0 0 0 0 1 0 0 0\n"
    "IDX10 0 1 2 3 4 5 6 7 6 7\n"
    "TRIS 0 2\n"
    "ANIM_begin\n"
    "ANIM_trans 0.001322 -1.186396 -14.984040 0.001322 -1.186396 -14.984040 0 0 none\n"
    "ANIM_rotate -1 0 0 0 110 0 1 libxplanemp/controls/gear_ratio\n"
    "ANIM_trans -0.001322 1.186396 14.984040 -0.001322 1.186396 14.984040 0 0 none\n"
    "ANIM_begin\n"
    "ANIM_trans 0 0 0 0 -0.086201 0.236050 0 1 libxplanemp/gear/nose_gear_deflection_mtr\n"
    "TRIS 2 1\n"
    "ANIM_end\n"
    "ANIM_end\n"
    "ANIM_begin\n"
    "ANIM_trans 0.438059 -1.217359 -15.759441 0.438059 -1.217359 -15.759441 0 0 none\n"
    "ANIM_rotate_begin 0.013180 -0.113369 0.993466 cjs/world_traffic/gear_door_ratio2\n"
    "ANIM_rotate_key 0 0\nANIM_rotate_key 0.2 73\nANIM_rotate_key 1 73\nANIM_rotate_end\n"
    "ANIM_trans -0.438059 1.217359 15.759441 -0.438059 1.217359 15.759441 0 0 none\n"
    "TRIS 3 1\n"
    "ANIM_end\n"
    "ANIM_begin\n"
    "ANIM_trans -2.791830 -0.614505 0.069210 -2.791830 -0.614505 0.069210 0 0 none\n"
    "ANIM_rotate 0 0 1 3 -81 0 1 libxplanemp/controls/gear_ratio\n"
    "ANIM_trans 2.791830 0.614505 -0.069210 2.791830 0.614505 -0.069210 0 0 none\n"
    "ANIM_begin\n"
    "ANIM_trans -0.698894 -1.076541 0.134115 -0.698894 -1.076541 0.134115 0 0 none\n"
    "ANIM_rotate -0.156543 -0.987671 0 0 1 0 1 libxplanemp/gear/tire_rotation_angle_deg\n"
    "ANIM_trans 0.698894 1.076541 -0.134115 0.698894 1.076541 -0.134115 0 0 none\n"
    "TRIS 4 1\n"
    "ANIM_end\n"
    "ANIM_end\n"
    "ANIM_begin\n"
    "ANIM_hide 0 0.5 libxplanemp/misc/touch_down\n"
    "ANIM_show 0.5 1 libxplanemp/misc/touch_down\n"
    "TRIS 5 1\n"
    "ANIM_end\n";

void PosesGearDown() {
    std::istringstream obj(PosedB738);
    xpc::PosedModel model;
    xpc::ReadPosedObj(obj, model);
    CHECK(model.all.size() == 5);   // the touch-down part is hidden
    CHECK(model.gear.size() == 2);  // nose and main wheel, not the door
    // Nose wheel: 0.47 up and 1.19 ahead of the hinge, swung down 110 degrees: 1.28 m below and 0.04 m ahead of it.
    const auto& nose = model.gear[0];
    CHECK(std::fabs(nose.y - (-1.186f - 1.279f)) < 0.01f);
    CHECK(std::fabs(nose.z - (-14.984f - 0.035f)) < 0.01f);
    // Main wheel: 2.09 m inboard of its hinge, swung down to 81 degrees: about 2.14 m below it.
    const auto& main = model.gear[1];
    CHECK(std::fabs(main.y - (-0.6145f - 2.137f)) < 0.01f);
    CHECK(main.x > -2.95f && main.x < -2.9f);
    // The door turned 73 degrees about its own hinge, so it is no longer where the file has it.
    CHECK(std::fabs(model.all[3].x - 0.4f) > 0.01f || std::fabs(model.all[3].y + 1.2f) > 0.01f);

    std::string why;
    auto wheels = xpc::FindWheels(model, &why);
    CHECK(wheels.has_value());
    CHECK(wheels && std::fabs(wheels->noseY - nose.y) < 1e-6f && std::fabs(wheels->mainY - main.y) < 1e-6f);
}

void ReadsOnlyTrianglePoints() {
    std::istringstream obj("I\r\n800\r\nOBJ\r\nTEXTURE a.png\r\nVT 1.5 -2.25 3 0 1 0 0.5 0.5\r\nVLINE 9 -9 9 1 1 1\r\n"
                           "VT\t0 1 -4 0 1 0 0 0\r\nVT 0 -50 0 0 1 0 0 0\r\nIDX 1\r\nIDX 0\r\nTRIS 0 2\r\n");
    xpc::PosedModel model;
    xpc::ReadPosedObj(obj, model);
    CHECK(model.all.size() == 2);  // the unused VT at -50 is not drawn
    CHECK(model.all[0].z == -4);
    CHECK(model.all[1].x == 1.5f && model.all[1].y == -2.25f && model.all[1].z == 3);
    CHECK(model.gear.empty());
}

void FindsModelObjFiles() {
    namespace fs = std::filesystem;
    const auto root = fs::temp_directory_path() / "xpconnect-plugin-tests";
    fs::remove_all(root);
    fs::create_directories(root / "Bluebell" / "BB_E190" / "E190");
    fs::create_directories(root / "Bluebell" / "BB_Shared");
    std::ofstream(root / "Bluebell" / "BB_Shared" / "xsb_aircraft.txt") << "EXPORT_NAME BB_Shared\n";
    std::ofstream(root / "Bluebell" / "BB_E190" / "xsb_aircraft.txt")
        << "EXPORT_NAME BB_E190\r\n"
        << "OBJ8_AIRCRAFT BB_E190_UTY\r\n"                      // line 2
        << "OBJ8 SOLID YES BB_E190/E190/E190.obj UTY.png\r\n"
        << "OBJ8 GLASS YES BB_Shared:glass.obj\r\n"
        << "OBJ8 SOLID YES Missing/x.obj\r\n"
        << "ICAO E190\r\n"
        << "OBJ8_AIRCRAFT BB_E190_BAW\r\n"                      // line 7
        << "OBJ8 SOLID YES BB_E190/E190/E190.obj BAW.png\r\n";

    const auto packages = xpc::FindCslPackages({(root / "Bluebell").u8string()});
    CHECK(packages.size() == 2);
    const auto e190 = (root / "Bluebell" / "BB_E190").u8string();
    CHECK(packages.find("BB_E190")->second == e190);
    auto files = xpc::ModelObjFiles(e190, 2, packages);
    CHECK(files.size() == 2);
    CHECK(fs::u8path(files[0]) == fs::u8path(e190) / "E190" / "E190.obj");
    CHECK(fs::u8path(files[1]) == root / "Bluebell" / "BB_Shared" / "glass.obj");
    CHECK(xpc::ModelObjFiles(e190, 7, packages).size() == 1);
    CHECK(xpc::ModelObjFiles(e190, 3, packages).empty());  // not an OBJ8_AIRCRAFT line
    fs::remove_all(root);
}

xpc::Pos At(double lat, double altFt, float heading, float pitch, float gsKt = 0) {
    xpc::Pos p;
    p.id = 1;
    p.lat = lat;
    p.lon = 153;
    p.altFt = altFt;
    p.heading = heading;
    p.pitch = pitch;
    p.groundSpeedKt = gsKt;
    return p;
}

void BlendsBetweenUpdates() {
    auto a = At(-27.0, 1000, 350, 2), b = At(-26.999, 1100, 10, 6);
    auto mid = xpc::Blend(a, b, 0.5f);
    CHECK(std::fabs(mid.lat + 26.9995) < 1e-9);
    CHECK(std::fabs(mid.altFt - 1050) < 1e-9);
    CHECK(std::fabs(mid.heading - 0) < 1e-3 || std::fabs(mid.heading - 360) < 1e-3);  // short way round through north
    CHECK(std::fabs(mid.pitch - 4) < 1e-6);
    CHECK(std::fabs(xpc::Blend(At(0, 0, 10, 0), At(0, 0, 350, 0), 0.25f).heading - 5) < 1e-3);

    // Drawn one update interval (0.1 s) behind: halfway through it, halfway between the two updates.
    auto drawn = xpc::DrawnPosition(a, 10.0f, b, 10.1f, 10.15f);
    CHECK(std::fabs(drawn.altFt - 1050) < 1e-3);
    CHECK(std::fabs(xpc::DrawnPosition(a, 10.0f, b, 10.1f, 10.1f).altFt - 1000) < 1e-3);
    CHECK(std::fabs(xpc::DrawnPosition(a, 10.0f, b, 10.1f, 10.2f).altFt - 1100) < 1e-3);
}

void MovesOnWhenUpdatesStop() {
    auto a = At(0, 0, 0, 0, 100), b = At(0.00005, 0, 0, 0, 100);  // 5.6 m in 0.1 s
    // 0.5 s after the blend ended, moved on as far as 5 updates; never more than 1 s ahead.
    auto later = xpc::DrawnPosition(a, 10.0f, b, 10.1f, 10.7f);
    CHECK(std::fabs(later.lat - 0.00005 * 6) < 1e-7);  // float times
    auto stale = xpc::DrawnPosition(a, 10.0f, b, 10.1f, 60.0f);
    CHECK(std::fabs(stale.lat - 0.00005 * 11) < 1e-7);
    // A parked aircraft stays put, whatever speed BATC gives it (it is not always 0).
    auto parked = At(0, 0, 0, 0, 12);
    CHECK(xpc::DrawnPosition(parked, 10.0f, parked, 12.0f, 13.5f).lat == 0);
    // A jump of more than a kilometre is drawn at once, not slid across.
    auto far = At(0.1, 0, 0, 0);
    CHECK(xpc::DrawnPosition(a, 10.0f, far, 10.1f, 10.11f).lat == 0.1);
}

void FindsObjFilesInSplitPackages() {
    // Bluebell's __Bluebell_Airbus spans BB_Airbus and BB_Airbus1: one xsb_aircraft.txt, the .obj files spread out.
    namespace fs = std::filesystem;
    const auto root = fs::temp_directory_path() / "xpconnect-plugin-split";
    fs::remove_all(root);
    for (const char* part : {"BB_Airbus", "BB_Airbus1"}) {
        fs::create_directories(root / part / "A320");
        std::ofstream(root / part / "xsb_aircraft.txt")
            << "EXPORT_NAME __Bluebell_Airbus\nOBJ8_AIRCRAFT A320_QFA\nOBJ8 SOLID YES __Bluebell_Airbus/A320/A320_QFA.obj\n";
    }
    std::ofstream(root / "BB_Airbus1" / "A320" / "A320_QFA.obj") << "I\n800\nOBJ\n";
    const auto packages = xpc::FindCslPackages({root.u8string()});
    CHECK(packages.count("__Bluebell_Airbus") == 2);
    const auto files = xpc::ModelObjFiles((root / "BB_Airbus").u8string(), 2, packages);
    CHECK(files.size() == 1 && fs::u8path(files[0]) == root / "BB_Airbus1" / "A320" / "A320_QFA.obj");
    fs::remove_all(root);
}

void LevelsTiltingBogies() {
    // A 777-style bogie: tilted 20 degrees nose-down unloaded, level at 0.67 m of tyre deflection.
    std::istringstream obj(
        "I\n800\nOBJ\n\nVT 0 0 -2 0 1 0 0 0\nVT 0 0 2 0 1 0 0 0\nIDX 0\nIDX 1\n"
        "ANIM_begin\nANIM_rotate 0 0 1 3 -80 0 1 libxplanemp/controls/gear_ratio\n"
        "ANIM_begin\nANIM_rotate 1 0 0 -20 10 0 1 libxplanemp/gear/tire_vertical_deflection_mtr\nTRIS 0 2\n"
        "ANIM_end\nANIM_end\n");
    xpc::PosedModel model;
    xpc::ReadPosedObj(obj, model);
    CHECK(model.bogieDataref == xpc::TireDeflectionDataref);
    CHECK(std::fabs(model.bogieLevel - 0.6667f) < 0.001f);
    CHECK(model.gear.size() == 2 && std::fabs(model.gear[0].y - model.gear[1].y) < 1e-4f);  // level
    CHECK(xpc::PoseValue("libxplanemp/gear/tire_vertical_deflection_mtr", xpc::TireDeflectionDataref, 0.5f) == 0.5f);
    CHECK(xpc::PoseValue("libxplanemp/controls/flap_ratio", xpc::TireDeflectionDataref, 0.5f) == 0);
}

void LevelsWorldTrafficBogies() {
    // Bluebell's virtual-airline repaints (B772_PAY at YBBN, 2026-10-03) tilt the same bogie on World Traffic's
    // main gear deflection, with keyed rotations, instead of XPMP2's tyre deflection.
    std::istringstream obj(
        "I\n800\nOBJ\n\nVT 0 0 -2 0 1 0 0 0\nVT 0 0 2 0 1 0 0 0\nIDX 0\nIDX 1\n"
        "ANIM_begin\nANIM_rotate 0 0 1 3 -80 0 1 libxplanemp/controls/gear_ratio\n"
        "ANIM_begin\nANIM_rotate_begin 1 0 0 cjs/world_traffic/main_gear_deflection\n"
        "ANIM_rotate_key 0 -20\nANIM_rotate_key 0.5 0\nANIM_rotate_key 1 5\nANIM_rotate_end\n"
        "ANIM_begin\nANIM_rotate 1 0 0 0 30 0 1 cjs/world_traffic/nose_gear_steering_angle\nANIM_end\n"
        "TRIS 0 2\nANIM_end\nANIM_end\n");
    xpc::PosedModel model;
    xpc::ReadPosedObj(obj, model);
    CHECK(model.bogieDataref == xpc::WorldTrafficDeflectionDataref);
    CHECK(std::fabs(model.bogieLevel - 0.5f) < 0.001f);
    CHECK(model.gear.size() == 2 && std::fabs(model.gear[0].y - model.gear[1].y) < 1e-4f);  // level
    // The libxplanemp tyre deflection does not move this model's bogies, and vice versa.
    CHECK(xpc::PoseValue("libxplanemp/gear/tire_vertical_deflection_mtr", model.bogieDataref, model.bogieLevel) == 0);
}

void LevelsBogiesModelledTilted() {
    // Bluebell's 747 (B744_QFA, B744_XSN at YBBN, 2026-10-03) models its wing-gear bogies already tilted and turns them
    // 30 to 73 degrees on the tyre deflection, so the angle never reads 0: level is where the wheels end up level.
    // These two points are level after a 50 degree turn, at deflection (50 - 30) / 43.
    std::istringstream obj(
        "I\n800\nOBJ\n\nVT 0 -1.532089 -1.285575 0 1 0 0 0\nVT 0 1.532089 1.285575 0 1 0 0 0\nIDX 0\nIDX 1\n"
        "ANIM_begin\nANIM_rotate 1 0 0 0 0 0 1 libxplanemp/controls/gear_ratio\n"
        "ANIM_begin\nANIM_rotate 1 0 0 30 73 0 1 libxplanemp/gear/tire_vertical_deflection_mtr\nTRIS 0 2\n"
        "ANIM_end\nANIM_end\n");
    xpc::PosedModel model;
    xpc::ReadPosedObj(obj, model);
    CHECK(model.bogieDataref == xpc::TireDeflectionDataref);
    CHECK(std::fabs(model.bogieLevel - 0.4651f) < 0.001f);
    CHECK(model.all.size() == 2 && std::fabs(model.all[0].y - model.all[1].y) < 1e-4f);  // level
}

void SkipsLinksThatTurnFurtherThanTheBogie() {
    // A strut link keyed to the deflection that turns 40 degrees but is level unloaded, then a 777-style bogie.
    std::istringstream obj(
        "I\n800\nOBJ\n\nVT 0 0 -2 0 1 0 0 0\nVT 0 0 2 0 1 0 0 0\nIDX 0\nIDX 1\n"
        "ANIM_begin\nANIM_rotate 1 0 0 0 40 0 1 libxplanemp/gear/tire_vertical_deflection_mtr\nTRIS 0 2\nANIM_end\n"
        "ANIM_begin\nANIM_rotate 1 0 0 -20 10 0 1 libxplanemp/gear/tire_vertical_deflection_mtr\nTRIS 0 2\nANIM_end\n");
    xpc::PosedModel model;
    xpc::ReadPosedObj(obj, model);
    CHECK(std::fabs(model.bogieLevel - 0.6667f) < 0.001f);
}

void LeavesBogiesModelledLevelAtZero() {
    // A bogie modelled level that only tilts as it loads: nothing to level, drawn at 0.
    std::istringstream obj(
        "I\n800\nOBJ\n\nVT 0 0 -2 0 1 0 0 0\nVT 0 0 2 0 1 0 0 0\nIDX 0\nIDX 1\n"
        "ANIM_begin\nANIM_rotate 1 0 0 0 -20 0 1 libxplanemp/gear/tire_vertical_deflection_mtr\nTRIS 0 2\nANIM_end\n");
    xpc::PosedModel model;
    xpc::ReadPosedObj(obj, model);
    CHECK(model.bogieDataref.empty() && model.bogieLevel == 0);
    CHECK(model.all.size() == 2 && std::fabs(model.all[0].y - model.all[1].y) < 1e-4f);
}

void SkipsLinksKeyedToDeflection() {
    // A torque link keyed to the same deflection (0 to 5 degrees) before the bogie must not be taken for it.
    std::istringstream obj(
        "I\n800\nOBJ\n\nVT 0 0 -2 0 1 0 0 0\nVT 0 0 2 0 1 0 0 0\nIDX 0\nIDX 1\n"
        "ANIM_begin\nANIM_rotate 1 0 0 0 5 0 1 libxplanemp/gear/tire_vertical_deflection_mtr\nANIM_end\n"
        "ANIM_begin\nANIM_rotate 1 0 0 -20 10 0 1 libxplanemp/gear/tire_vertical_deflection_mtr\nTRIS 0 2\nANIM_end\n");
    xpc::PosedModel model;
    xpc::ReadPosedObj(obj, model);
    CHECK(std::fabs(model.bogieLevel - 0.6667f) < 0.001f);
    CHECK(std::fabs(model.bogieTiltDeg + 20) < 0.001f);
    CHECK(model.all.size() == 2 && std::fabs(model.all[0].y - model.all[1].y) < 1e-4f);  // level
}

void KeepsOneBogieLevelAcrossFiles() {
    // A model split over files: the bogie in the first, a small part keyed to World Traffic's dataref in the second.
    std::istringstream gear(
        "I\n800\nOBJ\n\nVT 0 0 -2 0 1 0 0 0\nVT 0 0 2 0 1 0 0 0\nIDX 0\nIDX 1\n"
        "ANIM_begin\nANIM_rotate 1 0 0 -20 10 0 1 libxplanemp/gear/tire_vertical_deflection_mtr\nTRIS 0 2\nANIM_end\n");
    std::istringstream other(
        "I\n800\nOBJ\n\nVT 0 0 -2 0 1 0 0 0\nVT 0 0 2 0 1 0 0 0\nIDX 0\nIDX 1\n"
        "ANIM_begin\nANIM_rotate 1 0 0 -6 0 0 1 cjs/world_traffic/main_gear_deflection\nTRIS 0 2\nANIM_end\n");
    xpc::PosedModel model;
    xpc::ScanBogies(other, model);
    xpc::ScanBogies(gear, model);
    CHECK(model.bogieDataref == xpc::TireDeflectionDataref);
    CHECK(std::fabs(model.bogieLevel - 0.6667f) < 0.001f);
}

void ReadsAnimationsKeyedHighToLow() {
    // A single ANIM_trans may run from v1 = 1 down to v2 = 0: at 0 (flaps up) the part is at its v2 offset.
    std::istringstream obj(
        "I\n800\nOBJ\n\nVT 0 0 0 0 1 0 0 0\nIDX 0\n"
        "ANIM_begin\nANIM_trans 0 -1 0 0 0 0 1 0 libxplanemp/controls/flap_ratio\nTRIS 0 1\nANIM_end\n");
    xpc::PosedModel model;
    xpc::ReadPosedObj(obj, model);
    CHECK(model.all.size() == 1 && std::fabs(model.all[0].y) < 1e-6f);
}

}  // namespace

int main() {
    ParsesPos();
    ParsesOffset();
    ParsesAddWithLabelAndUnknowns();
    RejectsBadLines();
    SplitsDatagrams();
    FormatsReplies();
    FindsCslFolders();
    FitsNoseAndMainWheels();
    LeavesOddModelsLevel();
    PosesGearDown();
    ReadsOnlyTrianglePoints();
    FindsObjFilesInSplitPackages();
    LevelsTiltingBogies();
    LevelsWorldTrafficBogies();
    LevelsBogiesModelledTilted();
    SkipsLinksKeyedToDeflection();
    SkipsLinksThatTurnFurtherThanTheBogie();
    LeavesBogiesModelledLevelAtZero();
    KeepsOneBogieLevelAcrossFiles();
    ReadsAnimationsKeyedHighToLow();
    FindsModelObjFiles();
    BlendsBetweenUpdates();
    MovesOnWhenUpdatesStop();
    std::printf(g_failures == 0 ? "All plugin tests passed\n" : "%d failures\n", g_failures);
    return g_failures == 0 ? 0 : 1;
}
