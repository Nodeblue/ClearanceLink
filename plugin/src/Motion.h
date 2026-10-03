// Smooth movement between XPConnect's position updates. X-Plane-independent so it can be tested.
#pragma once

#include "Protocol.h"

namespace xpc {

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
// and BATC's altitude in the air at lift-off and touchdown: when the target jumps, the drawn value carries on from
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

// One aircraft's updates from XPConnect and where to draw it between them. It is drawn one update interval behind,
// blending from where it was drawn when the latest update came to that update, so turns, climbs and pitch changes are
// smooth. Once that runs out (updates stopped), it moves on at the speed measured between the last two updates
// received, for at most a second. A move far faster than its speed (BATC placing an aircraft) is drawn at once.
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

}  // namespace xpc
