// Smooth movement between ClearanceLink's position updates. X-Plane-independent so it can be tested.
#pragma once

#include "Protocol.h"

namespace clink {

// Blends two positions: `f` 0 gives `a`, 1 gives `b`. Heading takes the short way round; switches (on ground, lights)
// and the other values come from `b`.
Pos Blend(const Pos& a, const Pos& b, float f);

// Turns `deg` on by `rpm` over `seconds`, at most `maxDegPerSecond`, wrapped to [0, 360). XPMP2 only draws the
// rotation angle it is given, so the plugin turns propellers and wheels itself. Capping the drawn speed keeps a
// propeller looking like it turns instead of strobing (1,000 rpm is 100 degrees a frame at 60 fps).
float AdvanceAngle(float deg, float rpm, float seconds, float maxDegPerSecond);

// The nose wheel angle (degrees, positive turning right) for a taxiing aircraft turning at `yawDegPerSecond`
// (positive right) at `speedKt`, with `wheelbaseM` between nose and main wheels: the angle that makes the nose wheel
// roll along the turn, atan(wheelbase x yaw rate / speed), up to `maxDeg`. Straight ahead when slow or airborne. MSFS
// steers FSLTL models from the yaw rate in the same way; XPMP2 draws only the angle it is given.
float SteeringAngleDeg(float yawDegPerSecond, float speedKt, float wheelbaseM, float maxDeg = 70.0f);

// Smooths a drawn height (or any value) that switches source, e.g. between the wheels-on-terrain height on the ground
// and the app's altitude in the air at lift-off and touchdown: when the target jumps, the drawn value carries on from
// where it was and eases onto the new target, `seconds` being the time to close most (95%) of the gap.
class Smoothed {
public:
    // The value to draw this frame for `target`; `snap` (a placed or first-seen aircraft) jumps straight to it.
    float Next(float target, float elapsed, bool sourceChanged, bool snap, float seconds = 2.0f);

private:
    float offset_ = 0, last_ = 0;
    bool has_ = false;
};

// Heading change from `from` to `to` in degrees, the short way round (-180..180).
float HeadingDelta(float from, float to);

// Ground slope is measured this far ahead of and behind an aircraft (metres), with two extra terrain probes.
constexpr float GroundSlopeProbeMeters = 8.0f;

// Whether to probe the terrain under an aircraft again this frame. Each probe on the ground is three
// XPLMProbeTerrainXYZ calls (under it, and ahead and behind for the slope). An aircraft on the ground is probed again
// once it has moved 25 cm or turned 1 degree, but at most every 0.1 s, unless it has moved further than the slope
// was measured (8 m: fast on the runway, or placed by the app); parked, every 5 s. Airborne, every 0.25 s (only the
// `ground` report needs it). Taxiing at 15 m/s used to probe every frame; now it is 10 times a second.
// `movedM` and `turnedDeg` are since the last probe, `ageS` its age; `probedOnGround` whether it was taken on the
// ground (with the slope), so touchdown probes at once.
bool ShouldReprobeGround(bool onGround, bool probedOnGround, float movedM, float turnedDeg, float ageS);

// The terrain height (X-Plane local y) under an aircraft that moved (`dx`, `dz`, local metres: x east, z south) since
// the terrain was probed at `probeY`, carried along the slope measured then (`slopeDeg`, nose up positive, along
// `headingDeg` true). Between probes the drawn height follows the taxiway's slope instead of standing still and
// stepping at each probe; at the next probe only the slope's curvature over the distance moved remains (millimetres
// to a few centimetres at taxi speeds). Sideways movement keeps the height. Distances beyond the measured slope
// (GroundSlopeProbeMeters) are capped there, as nothing is known about the terrain further on.
float GroundYAlongSlope(float probeY, float slopeDeg, float headingDeg, float dx, float dz);

// One aircraft's updates from ClearanceLink and where to draw it between them. It is drawn one update interval behind,
// blending from where it was drawn when the latest update came to that update, so turns, climbs and pitch changes are
// smooth. Once that runs out (updates stopped), it moves on at the speed measured between the last two updates
// received, for at most a second. A move far faster than its speed (the app placing an aircraft) is drawn at once.
class Track {
public:
    void Update(const Pos& pos, float now);
    Pos Drawn(float now) const;
    bool HasState() const { return has_; }
    const Pos& Latest() const { return to_; }

private:
    Pos from_{}, to_{};
    float fromTime_ = 0, toTime_ = 0;
    double latPerSecond_ = 0, lonPerSecond_ = 0;  // measured between the last two updates received; 0 when parked
    bool has_ = false;
};

}  // namespace clink
