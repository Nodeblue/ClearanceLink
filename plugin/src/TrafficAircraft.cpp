#include "TrafficAircraft.h"

#include "XPLMGraphics.h"
#include "XPLMProcessing.h"
#include "XPLMUtilities.h"

#include <algorithm>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <future>

namespace xpc {
namespace {

constexpr double FeetPerMeter = 3.28083989501;
constexpr double Pi = 3.14159265358979323846;
// Terrain under an aircraft is probed again when it has moved this far (or turned this much) on the ground, or after
// this long: parked aircraft every few seconds, airborne ones a few times a second for the ground height report.
constexpr float ReprobeMeters = 0.25f;
constexpr float ReprobeTurnDeg = 1.0f;
constexpr float ReprobeParkedSeconds = 5.0f;
constexpr float ReprobeAirborneSeconds = 0.25f;
// Ground slope is measured this far ahead of and behind the aircraft's reference point.
constexpr double SlopeProbeMeters = 8.0;
// Steeper "slopes" are terrain mesh edges or buildings, not aprons.
constexpr double MaxGroundSlopeDeg = 4.0;

std::string Requested(const Add& add) { return add.icaoType + ' ' + add.icaoAirline + ' ' + add.livery; }

struct FitResult {
    std::optional<Wheels> wheels;
    std::string why;
};

std::map<std::string, std::string> g_packages;

FitResult ReadWheels(const std::vector<std::string>& files) {
    if (files.empty()) return {std::nullopt, "no .obj file found"};
    std::vector<Vertex> vertices;
    for (const auto& file : files) {
        std::ifstream obj(std::filesystem::u8path(file));
        ReadObjVertices(obj, vertices);
    }
    FitResult result;
    result.wheels = FindWheels(vertices, &result.why);
    return result;
}

}  // namespace

// One model's wheel positions: read once in the background, shared by every aircraft drawn with that model.
struct ModelFit {
    std::string name;
    std::future<FitResult> reading;
    std::optional<Wheels> wheels;
    bool done = false;
};

namespace {
std::map<std::string, std::shared_ptr<ModelFit>> g_fits;
}

void TrafficAircraft::SetCslPackages(std::map<std::string, std::string> packages) { g_packages = std::move(packages); }

void TrafficAircraft::ForgetModelFits() { g_fits.clear(); }

const Wheels* TrafficAircraft::ModelWheels() {
    if (GetModel() != fitModel_ || !fit_) {
        fitModel_ = GetModel();
        const auto info = GetModelInfo();
        const auto key = info.xsbAircraftPath + '#' + std::to_string(info.xsbAircraftLn);
        auto& fit = g_fits[key];
        if (!fit) {
            fit = std::make_shared<ModelFit>();
            fit->name = info.cslId;
            fit->reading = std::async(std::launch::async, ReadWheels, ModelObjFiles(info.xsbAircraftPath, info.xsbAircraftLn, g_packages));
        }
        fit_ = fit;
    }
    if (!fit_->done && fit_->reading.valid() && fit_->reading.wait_for(std::chrono::seconds(0)) == std::future_status::ready) {
        const auto result = fit_->reading.get();
        fit_->wheels = result.wheels;
        fit_->done = true;
        char text[256];
        if (result.wheels) {
            const auto& w = *result.wheels;
            std::snprintf(text, sizeof(text), "XPConnect: ground fit %s: nose wheel %.2f m above mains, pitch %.2f deg, raise %.2f m\n",
                          fit_->name.c_str(), w.noseY - w.mainY, w.LevelPitchDeg(), w.RaiseM(w.LevelPitchDeg()));
        } else {
            std::snprintf(text, sizeof(text), "XPConnect: ground fit %s: none (%s), drawn level\n", fit_->name.c_str(), result.why.c_str());
        }
        XPLMDebugString(text);
    }
    return fit_->wheels ? &*fit_->wheels : nullptr;
}

TrafficAircraft::TrafficAircraft(const Add& add)
    : XPMP2::Aircraft(add.icaoType, add.icaoAirline, add.livery, add.id) {
    probe_ = XPLMCreateProbe(xplm_ProbeY);
    requested_ = Requested(add);
    SetLabel(add);
    SetVisible(false);  // until the first position arrives
}

std::optional<float> TrafficAircraft::GroundSlopeDeg(float centreY) {
    const double heading = state_.heading * Pi / 180.0;
    // X-Plane local coordinates: x east, z south.
    const float dx = float(std::sin(heading) * SlopeProbeMeters);
    const float dz = float(-std::cos(heading) * SlopeProbeMeters);
    XPLMProbeInfo_t ahead{};
    ahead.structSize = sizeof(ahead);
    if (XPLMProbeTerrainXYZ(probe_, drawInfo.x + dx, centreY, drawInfo.z + dz, &ahead) != xplm_ProbeHitTerrain) return std::nullopt;
    XPLMProbeInfo_t behind{};
    behind.structSize = sizeof(behind);
    if (XPLMProbeTerrainXYZ(probe_, drawInfo.x - dx, centreY, drawInfo.z - dz, &behind) != xplm_ProbeHitTerrain) return std::nullopt;
    const double slope = std::atan2(double(ahead.locationY) - behind.locationY, 2 * SlopeProbeMeters) * 180.0 / Pi;
    return float(std::clamp(slope, -MaxGroundSlopeDeg, MaxGroundSlopeDeg));
}

TrafficAircraft::~TrafficAircraft() {
    if (probe_) XPLMDestroyProbe(probe_);
}

void TrafficAircraft::ChangeTo(const Add& add) {
    // XPConnect re-sends add every few seconds so a restarted plugin gets everything; only a new model re-matches.
    if (Requested(add) != requested_) {
        ChangeModel(add.icaoType, add.icaoAirline, add.livery);
        requested_ = Requested(add);
    }
    SetLabel(add);
}

void TrafficAircraft::SetLabel(const Add& add) {
    label = add.label;
    std::strncpy(acInfoTexts.tailNum, add.label.c_str(), sizeof(acInfoTexts.tailNum) - 1);
    std::strncpy(acInfoTexts.icaoAcType, add.icaoType.c_str(), sizeof(acInfoTexts.icaoAcType) - 1);
    std::strncpy(acInfoTexts.icaoAirline, add.icaoAirline.c_str(), sizeof(acInfoTexts.icaoAirline) - 1);
}

void TrafficAircraft::Apply(const Pos& pos, float now) {
    if (hasState_) {
        prev_ = Drawn(now);  // blend on from where it is drawn now, so a late or early update never jumps
        prevTime_ = now - std::max(0.02f, now - stateTime_);
    } else {
        prev_ = pos;
        prevTime_ = now - 0.1f;
        hasState_ = true;
        SetVisible(true);
    }
    state_ = pos;
    stateTime_ = now;
}

Pos TrafficAircraft::Drawn(float now) const { return DrawnPosition(prev_, prevTime_, state_, stateTime_, now); }

bool TrafficAircraft::ProbeGround(bool onGround, float now) {
    const float dx = drawInfo.x - probeX_, dz = drawInfo.z - probeZ_;
    float turned = std::fabs(std::fmod(drawInfo.heading - probeHeading_ + 540.0f, 360.0f) - 180.0f);
    const bool moved = dx * dx + dz * dz > ReprobeMeters * ReprobeMeters || turned > ReprobeTurnDeg;
    const float maxAge = onGround ? ReprobeParkedSeconds : ReprobeAirborneSeconds;
    if (now - probeTime_ < maxAge && !(onGround && moved)) return groundFt_.has_value();

    probeTime_ = now;
    probeX_ = drawInfo.x;
    probeZ_ = drawInfo.z;
    probeHeading_ = drawInfo.heading;
    XPLMProbeInfo_t info{};
    info.structSize = sizeof(info);
    if (XPLMProbeTerrainXYZ(probe_, drawInfo.x, drawInfo.y, drawInfo.z, &info) != xplm_ProbeHitTerrain) {
        groundFt_.reset();
        return false;
    }
    double groundLat = 0, groundLon = 0, groundAltM = 0;
    XPLMLocalToWorld(info.locationX, info.locationY, info.locationZ, &groundLat, &groundLon, &groundAltM);
    groundFt_ = groundAltM * FeetPerMeter;
    probeY_ = info.locationY;
    slope_ = onGround ? GroundSlopeDeg(info.locationY) : std::nullopt;
    return true;
}

void TrafficAircraft::UpdatePosition(float, int) {
    if (!hasState_) return;

    const float now = XPLMGetElapsedTime();
    const Pos p = Drawn(now);
    SetLocation(p.lat, p.lon, p.altFt);
    SetHeading(p.heading);
    SetPitch(p.pitch);
    SetRoll(p.bank);

    if (ProbeGround(p.onGround, now) && p.onGround) {
        // Pitch the model so its nose and main wheels both touch, and raise or lower it for that pitch, from where
        // its wheels are in its .obj files. XPConnect's per-type pitch and height adjust on top of that.
        const Wheels* wheels = ModelWheels();
        const float pitch = p.pitch + (wheels ? wheels->LevelPitchDeg() : 0.0f);
        const float raise = wheels ? wheels->RaiseM(pitch) : 0.0f;
        drawInfo.y = probeY_ + GetVertOfs() + raise + float(groundOffsetFt_ / FeetPerMeter);
        // Follow the slope of the apron or taxiway along the aircraft, so both nose and main wheels touch it.
        SetPitch(pitch + slope_.value_or(0.0f));
    }
    SetOnGrnd(p.onGround);

    SetGearRatio(p.gear);
    SetFlapRatio(p.flaps);
    SetSlatRatio(p.flaps);
    SetSpoilerRatio(p.spoilers);
    SetSpeedbrakeRatio(p.spoilers);
    SetThrustRatio(p.thrust);
    SetLightsNav(p.lights & LightNav);
    SetLightsBeacon(p.lights & LightBeacon);
    SetLightsStrobe(p.lights & LightStrobe);
    SetLightsLanding(p.lights & LightLanding);
    SetLightsTaxi(p.lights & LightTaxi);
    SetTireRotRpm(p.onGround ? p.groundSpeedKt * 4.5f : 0.0f);
    SetEngineRotRpm(p.thrust > 0.01f || p.groundSpeedKt > 1 ? 1000.0f + 4000.0f * p.thrust : 0.0f);
}

}  // namespace xpc
