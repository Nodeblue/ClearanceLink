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
