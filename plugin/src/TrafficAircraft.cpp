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

namespace clink {
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
    std::string bogieDataref;
    float bogieLevel = 0;
    float bogieTiltDeg = 0;
    std::string why;
};

// XPMP2's index for World Traffic's main gear deflection in each aircraft's dataref values, 0 when not added.
size_t g_worldTrafficDeflection = 0;

std::shared_ptr<const CslPackages> g_packages = std::make_shared<CslPackages>();

// Runs in the background: finds the model's .obj files from its xsb_aircraft.txt and reads its wheels.
FitResult ReadWheels(std::string xsbFolder, int line, std::shared_ptr<const CslPackages> packages) {
    FitResult result;
    try {
        const auto files = ModelObjFiles(xsbFolder, line, *packages);
        if (files.empty()) {
            result.why = "no .obj file found";
            return result;
        }
        const PosedModel model = PoseObjFiles(files);
        result.wheels = FindWheels(model, &result.why);
        result.bogieDataref = model.bogieDataref;
        result.bogieLevel = model.bogieLevel;
        result.bogieTiltDeg = model.bogieTiltDeg;
    } catch (const std::exception& e) {
        result = FitResult{};
        result.why = std::string("cannot read the model: ") + e.what();
    }
    return result;
}

}  // namespace

// One model's wheel positions: read once in the background, shared by every aircraft drawn with that model.
struct ModelFit {
    std::string name;
    std::future<FitResult> reading;
    std::optional<Wheels> wheels;
    std::string bogieDataref;
    float bogieLevel = 0;
    bool done = false;
};

namespace {
std::map<std::string, std::shared_ptr<ModelFit>> g_fits;
}

void TrafficAircraft::SetCslPackages(CslPackages packages) {
    g_packages = std::make_shared<const CslPackages>(std::move(packages));
}

void TrafficAircraft::ForgetModelFits() { g_fits.clear(); }

bool TrafficAircraft::AddModelDatarefs() {
    g_worldTrafficDeflection = XPMPAddModelDataRef(WorldTrafficDeflectionDataref);
    return g_worldTrafficDeflection != 0;
}

void TrafficAircraft::SetBogies(bool onGround) {
    const bool fitted = onGround && fit_ && fit_->done && fit_->wheels;
    const std::string dataref = fitted ? fit_->bogieDataref : std::string();
    const float level = fitted ? fit_->bogieLevel : 0.0f;
    SetTireDeflection(dataref == TireDeflectionDataref ? level : 0.0f);
    if (g_worldTrafficDeflection != 0 && g_worldTrafficDeflection < v.size()) {
        v[g_worldTrafficDeflection] = dataref == WorldTrafficDeflectionDataref ? level : 0.0f;
    }
}

const Wheels* TrafficAircraft::ModelWheels() {
    if (GetModel() != fitModel_ || !fit_) {
        fitModel_ = GetModel();
        const auto info = GetModelInfo();
        const auto key = info.xsbAircraftPath + '#' + std::to_string(info.xsbAircraftLn);
        auto& fit = g_fits[key];
        if (!fit) {
            fit = std::make_shared<ModelFit>();
            fit->name = info.cslId;
            fit->reading = std::async(std::launch::async, ReadWheels, info.xsbAircraftPath, info.xsbAircraftLn, g_packages);
        }
        fit_ = fit;
    }
    if (!fit_->done && fit_->reading.valid() && fit_->reading.wait_for(std::chrono::seconds(0)) == std::future_status::ready) {
        const auto result = fit_->reading.get();
        fit_->wheels = result.wheels;
        fit_->bogieDataref = result.bogieDataref;
        fit_->bogieLevel = result.bogieLevel;
        fit_->done = true;
        char text[512];
        if (result.wheels) {
            const auto& w = *result.wheels;
            char bogies[200] = "";
            if (!result.bogieDataref.empty()) {
                std::snprintf(bogies, sizeof(bogies), ", bogies tilted %.1f deg levelled at %s %.3f", result.bogieTiltDeg,
                              result.bogieDataref.c_str(), result.bogieLevel);
            }
            std::snprintf(text, sizeof(text),
                          "ClearanceLink: ground fit %s: nose wheel %.2f m above mains, pitch %.2f deg, wheels %.2f m below the model origin (CSL offset %.2f m)%s\n",
                          fit_->name.c_str(), w.noseY - w.mainY, w.LevelPitchDeg(), w.GroundHeightM(w.LevelPitchDeg()), GetVertOfs(), bogies);
        } else {
            std::snprintf(text, sizeof(text), "ClearanceLink: ground fit %s: none (%s), drawn level\n", fit_->name.c_str(), result.why.c_str());
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
    const double heading = track_.Latest().heading * Pi / 180.0;
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
    // ClearanceLink re-sends add every few seconds so a restarted plugin gets everything; only a new model re-matches.
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
    if (!track_.HasState()) SetVisible(true);
    track_.Update(pos, now);
}

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

void TrafficAircraft::UpdatePosition(float elapsed, int) {
    if (!track_.HasState()) return;

    const float now = XPLMGetElapsedTime();
    const Pos p = track_.Drawn(now);
    SetLocation(p.lat, p.lon, p.altFt);
    SetHeading(p.heading);
    SetPitch(p.pitch);
    SetRoll(p.bank);

    if (ProbeGround(p.onGround, now) && p.onGround) {
        // Park the model on its own wheels: pitched so nose and main wheels both touch, at the height that puts them
        // on the ground, both read from its .obj files with the gear down. Models that could not be read get
        // ClearanceLink's per-type pitch and height on XPMP2's vertical offset. Above taxi speed `pos` carries BATC's
        // pitch (take-off rotation, flare) and the wheels still set the height, so the main gear stays down.
        const Wheels* wheels = ModelWheels();
        float pitch = p.pitch;
        float height = 0;
        if (wheels) {
            pitch += wheels->LevelPitchDeg();
            height = wheels->GroundHeightM(pitch);
        } else {
            pitch += groundPitch_.value_or(0.0f);
            height = GetVertOfs() + float(groundOffsetFt_ / FeetPerMeter);
        }
        drawInfo.y = probeY_ + height;
        // Follow the slope of the apron or taxiway along the aircraft, so both nose and main wheels touch it.
        SetPitch(pitch + slope_.value_or(0.0f));
    }
    // Lift-off and touchdown: on the ground the height comes from the terrain and the model's wheels, in the air from
    // BATC's altitude, and the two rarely agree, so the aircraft jumped up or down as it rotated or touched down
    // (2026-10-04). The drawn height eases from one to the other instead; a big move (BATC placing the aircraft)
    // still snaps.
    {
        const float dx = float(drawInfo.x) - lastX_, dz = float(drawInfo.z) - lastZ_;
        const bool placed = !hasLast_ || dx * dx + dz * dz > 200.0f * 200.0f;
        drawInfo.y = height_.Next(float(drawInfo.y), elapsed, hasLast_ && p.onGround != wasOnGround_, placed);
        lastX_ = float(drawInfo.x);
        lastZ_ = float(drawInfo.z);
        wasOnGround_ = p.onGround;
        hasLast_ = true;
    }

    // Drawn with the bogie value the wheels were measured with: levels tilting bogies on the ground; unloaded in the
    // air, so they tilt.
    SetBogies(p.onGround);
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
    const float tireRpm = p.onGround ? p.groundSpeedKt * 4.5f : 0.0f;
    const float engineRpm = p.thrust > 0.01f || p.groundSpeedKt > 1 ? 1000.0f + 4000.0f * p.thrust : 0.0f;
    SetTireRotRpm(tireRpm);
    SetEngineRotRpm(engineRpm);
    tireAngle_ = AdvanceAngle(tireAngle_, tireRpm, elapsed, 2000.0f);
    engineAngle_ = AdvanceAngle(engineAngle_, engineRpm, elapsed, 1500.0f);
    SetTireRotAngle(tireAngle_);

    // Steer the nose wheel along the turn, from the turn rate of the drawn heading (smoothed over about a third of a
    // second) and the wheelbase measured from the model's own wheels (15 m when it could not be read).
    if (elapsed > 0 && lastHeading_) {
        const float yaw = HeadingDelta(*lastHeading_, p.heading) / elapsed;
        yawRate_ += (yaw - yawRate_) * std::min(1.0f, elapsed * 3.0f);
    }
    lastHeading_ = p.heading;
    const Wheels* steerWheels = ModelWheels();
    const float wheelbase = steerWheels ? std::fabs(steerWheels->mainZ - steerWheels->noseZ) : 15.0f;
    SetNoseWheelAngle(p.onGround ? SteeringAngleDeg(yawRate_, p.groundSpeedKt, wheelbase) : 0.0f);
    SetEngineRotAngle(engineAngle_);
    SetPropRotAngle(engineAngle_);
}

}  // namespace clink
