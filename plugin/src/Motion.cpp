#include "Motion.h"

#include <algorithm>
#include <cmath>

namespace clink {
namespace {

constexpr double Pi = 3.14159265358979323846;
// Less movement than this between two updates is a parked aircraft: it is not moved on.
constexpr double MinMoveMeters = 0.01;
// Update intervals are clamped to this range: shorter is jitter, longer means the aircraft was parked or updates
// paused, and it should then catch up quickly rather than crawl.
constexpr float MinInterval = 0.02f;
constexpr float MaxInterval = 0.5f;
// Further apart than this is a jump (a new position from BATC), drawn at once.
constexpr double MaxBlendMeters = 1000;
// So is a move faster than twice its speed plus this (metres a second): BATC placing a parked aircraft at its gate
// moved it 100 m and more, which the blend slid across and the move-on then overshot, back and forth
// (YBBN, 2026-10-03). Speeds are measured over at least a tenth of a second (two updates can land in one frame).
constexpr double JumpMarginMps = 15;
constexpr float MinSpeedInterval = 0.1f;
// Updates further apart than this are ClearanceLink's keep-alives for an aircraft that is not moving: no speed.
constexpr float MaxMovingInterval = 1.0f;
// Moves on for at most this long after the updates stop.
constexpr float MaxAheadSeconds = 1.0f;
constexpr double KnotsToMps = 0.514444;
// Terrain probes (ShouldReprobeGround): moved or turned this much on the ground, at most this often; parked or
// airborne, after this long.
constexpr float ReprobeMeters = 0.25f;
constexpr float ReprobeTurnDeg = 1.0f;
constexpr float MinReprobeSeconds = 0.1f;
constexpr float ReprobeParkedSeconds = 5.0f;
constexpr float ReprobeAirborneSeconds = 0.25f;

template <typename T>
T Lerp(T a, T b, float f) {
    return a + (b - a) * f;
}

double DistanceMeters(const Pos& a, const Pos& b) {
    const double dy = (b.lat - a.lat) * 111132.92;
    const double dx = (b.lon - a.lon) * 111412.84 * std::cos(a.lat * Pi / 180.0);
    return std::sqrt(dx * dx + dy * dy);
}

}  // namespace

float AdvanceAngle(float deg, float rpm, float seconds, float maxDegPerSecond) {
    const float degPerSecond = std::min(std::fabs(rpm) * 6.0f, maxDegPerSecond);
    float next = std::fmod(deg + degPerSecond * std::max(seconds, 0.0f), 360.0f);
    if (next < 0) next += 360.0f;
    return next;
}

float SteeringAngleDeg(float yawDegPerSecond, float speedKt, float wheelbaseM, float maxDeg) {
    constexpr float MetersPerSecondPerKnot = 0.514444f;
    const float speed = speedKt * MetersPerSecondPerKnot;
    if (speed < 0.5f || wheelbaseM <= 0) return 0;
    const float yaw = yawDegPerSecond * 3.14159265f / 180.0f;
    const float deg = std::atan(wheelbaseM * yaw / speed) * 180.0f / 3.14159265f;
    return std::clamp(deg, -maxDeg, maxDeg);
}

float Smoothed::Next(float target, float elapsed, bool sourceChanged, bool snap, float seconds) {
    if (!has_ || snap) {
        offset_ = 0;
    } else {
        if (sourceChanged) offset_ = last_ - target;  // carry on from where it was drawn
        offset_ *= std::exp(-3.0f * std::max(elapsed, 0.0f) / std::max(seconds, 0.01f));
        if (std::fabs(offset_) < 0.001f) offset_ = 0;
    }
    has_ = true;
    last_ = target + offset_;
    return last_;
}

float HeadingDelta(float from, float to) {
    float d = std::fmod(to - from, 360.0f);
    if (d > 180) d -= 360;
    if (d < -180) d += 360;
    return d;
}

bool ShouldReprobeGround(bool onGround, bool probedOnGround, float movedM, float turnedDeg, float ageS) {
    if (onGround != probedOnGround) return true;  // touchdown needs the slope; lift-off the airborne interval
    if (!onGround) return ageS >= ReprobeAirborneSeconds;
    if (ageS >= ReprobeParkedSeconds) return true;
    if (movedM <= ReprobeMeters && std::fabs(turnedDeg) <= ReprobeTurnDeg) return false;
    // Moving: no more than every MinReprobeSeconds, as GroundYAlongSlope carries the height along the slope between
    // probes; but at once beyond the measured slope, where that would guess.
    return ageS >= MinReprobeSeconds || movedM >= GroundSlopeProbeMeters;
}

float GroundYAlongSlope(float probeY, float slopeDeg, float headingDeg, float dx, float dz) {
    const double heading = headingDeg * Pi / 180.0;
    // Forward in X-Plane local coordinates (x east, z south) is (sin, -cos).
    const double along = std::clamp(dx * std::sin(heading) - dz * std::cos(heading), -double(GroundSlopeProbeMeters),
                                    double(GroundSlopeProbeMeters));
    return float(probeY + along * std::tan(slopeDeg * Pi / 180.0));
}

Pos Blend(const Pos& a, const Pos& b, float f) {
    Pos p = b;
    p.lat = Lerp(a.lat, b.lat, f);
    p.lon = Lerp(a.lon, b.lon, f);
    p.altFt = Lerp(a.altFt, b.altFt, f);
    float turn = std::fmod(b.heading - a.heading, 360.0f);
    if (turn > 180) turn -= 360;
    if (turn < -180) turn += 360;
    p.heading = std::fmod(a.heading + turn * f + 360.0f, 360.0f);
    p.pitch = Lerp(a.pitch, b.pitch, f);
    p.bank = Lerp(a.bank, b.bank, f);
    p.groundSpeedKt = Lerp(a.groundSpeedKt, b.groundSpeedKt, f);
    p.gear = Lerp(a.gear, b.gear, f);
    p.flaps = Lerp(a.flaps, b.flaps, f);
    p.spoilers = Lerp(a.spoilers, b.spoilers, f);
    p.thrust = Lerp(a.thrust, b.thrust, f);
    return p;
}

void Track::Update(const Pos& pos, float now) {
    if (!has_) {
        from_ = to_ = pos;
        fromTime_ = now - MinInterval;
        toTime_ = now;
        has_ = true;
        return;
    }
    const float since = now - toTime_;
    const double meters = DistanceMeters(to_, pos);
    const double speed = meters / std::max(since, MinSpeedInterval);
    const double reported = std::max(std::fabs(to_.groundSpeedKt), std::fabs(pos.groundSpeedKt)) * KnotsToMps;
    if (meters > MaxBlendMeters || speed > 2 * reported + JumpMarginMps) {
        from_ = pos;  // drawn there at once
        fromTime_ = now - MinInterval;
        latPerSecond_ = lonPerSecond_ = 0;
    } else {
        from_ = Drawn(now);  // blend on from where it is drawn now, so a late or early update never jumps
        // Two updates read in one frame (after an X-Plane stutter) are still about ClearanceLink's 0.1 s apart.
        const float interval = std::max(since, MinSpeedInterval);
        fromTime_ = now - interval;
        // Not at gsKt: BATC's speed for a stopped aircraft is not always 0, which slid parked aircraft forward and
        // back again with each update (VH-XZA at YBBN, 2026-10-03).
        const bool moving = since <= MaxMovingInterval && meters > MinMoveMeters;
        latPerSecond_ = moving ? (pos.lat - to_.lat) / interval : 0;
        lonPerSecond_ = moving ? (pos.lon - to_.lon) / interval : 0;
    }
    to_ = pos;
    toTime_ = now;
}

Pos Track::Drawn(float now) const {
    const float interval = std::clamp(toTime_ - fromTime_, MinInterval, MaxInterval);
    const float since = std::max(0.0f, now - toTime_);
    if (since < interval) return Blend(from_, to_, since / interval);
    Pos p = to_;
    const float ahead = std::min(since - interval, MaxAheadSeconds);
    p.lat += latPerSecond_ * ahead;
    p.lon += lonPerSecond_ * ahead;
    return p;
}

}  // namespace clink
