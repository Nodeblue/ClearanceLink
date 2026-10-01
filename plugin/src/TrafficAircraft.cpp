#include "TrafficAircraft.h"

#include "XPLMGraphics.h"
#include "XPLMProcessing.h"

#include <algorithm>
#include <cmath>
#include <cstring>

namespace xpc {
namespace {

constexpr double FeetPerMeter = 3.28083989501;
constexpr double MetersPerKnotSecond = 0.514444;
constexpr double Pi = 3.14159265358979323846;
// Keep moving an aircraft along its last heading for at most this long when updates stop.
constexpr float MaxExtrapolationSeconds = 1.0f;
// Ground slope is measured this far ahead of and behind the aircraft's reference point.
constexpr double SlopeProbeMeters = 8.0;
// Steeper "slopes" are terrain mesh edges or buildings, not aprons.
constexpr double MaxGroundSlopeDeg = 4.0;

std::string Requested(const Add& add) { return add.icaoType + ' ' + add.icaoAirline + ' ' + add.livery; }

}  // namespace

void MoveAlong(double& lat, double& lon, double headingDeg, double meters) {
    const double heading = headingDeg * Pi / 180.0;
    const double metersPerDegLat = 111132.92;
    const double metersPerDegLon = 111412.84 * std::cos(lat * Pi / 180.0);
    lat += meters * std::cos(heading) / metersPerDegLat;
    if (metersPerDegLon > 1) lon += meters * std::sin(heading) / metersPerDegLon;
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
    state_ = pos;
    stateTime_ = now;
    if (!hasState_) {
        hasState_ = true;
        SetVisible(true);
    }
}

void TrafficAircraft::UpdatePosition(float, int) {
    if (!hasState_) return;

    const float now = XPLMGetElapsedTime();
    const float age = std::clamp(now - stateTime_, 0.0f, MaxExtrapolationSeconds);
    double lat = state_.lat;
    double lon = state_.lon;
    MoveAlong(lat, lon, state_.heading, state_.groundSpeedKt * MetersPerKnotSecond * age);

    SetLocation(lat, lon, state_.altFt);
    SetHeading(state_.heading);
    SetPitch(state_.pitch);
    SetRoll(state_.bank);

    XPLMProbeInfo_t info{};
    info.structSize = sizeof(info);
    if (XPLMProbeTerrainXYZ(probe_, drawInfo.x, drawInfo.y, drawInfo.z, &info) == xplm_ProbeHitTerrain) {
        double groundLat = 0, groundLon = 0, groundAltM = 0;
        XPLMLocalToWorld(info.locationX, info.locationY, info.locationZ, &groundLat, &groundLon, &groundAltM);
        groundFt_ = groundAltM * FeetPerMeter;
        if (state_.onGround) {
            drawInfo.y = info.locationY + GetVertOfs();
            // Follow the slope of the apron or taxiway along the aircraft, so both nose and main wheels touch it.
            if (auto slope = GroundSlopeDeg(info.locationY)) SetPitch(state_.pitch + *slope);
        }
    }
    SetOnGrnd(state_.onGround);

    SetGearRatio(state_.gear);
    SetFlapRatio(state_.flaps);
    SetSlatRatio(state_.flaps);
    SetSpoilerRatio(state_.spoilers);
    SetSpeedbrakeRatio(state_.spoilers);
    SetThrustRatio(state_.thrust);
    SetLightsNav(state_.lights & LightNav);
    SetLightsBeacon(state_.lights & LightBeacon);
    SetLightsStrobe(state_.lights & LightStrobe);
    SetLightsLanding(state_.lights & LightLanding);
    SetLightsTaxi(state_.lights & LightTaxi);
    SetTireRotRpm(state_.onGround ? state_.groundSpeedKt * 4.5f : 0.0f);
    SetEngineRotRpm(state_.thrust > 0.01f || state_.groundSpeedKt > 1 ? 1000.0f + 4000.0f * state_.thrust : 0.0f);
}

}  // namespace xpc
