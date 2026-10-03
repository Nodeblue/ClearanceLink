// Smooth movement between XPConnect's position updates. X-Plane-independent so it can be tested.
#pragma once

#include "Protocol.h"

namespace xpc {

// Blends two positions: `f` 0 gives `a`, 1 gives `b`. Heading takes the short way round; switches (on ground, lights)
// and the other values come from `b`.
Pos Blend(const Pos& a, const Pos& b, float f);

// Where to draw an aircraft `now`, from its last two updates (`prev` at `prevTime`, `last` at `lastTime`). It is drawn
// one update interval behind, blending from `prev` to `last`, so turns, climbs and pitch changes are smooth; once that
// runs out (updates stopped), it moves on as it moved between the two updates, for at most `maxAhead` seconds.
Pos DrawnPosition(const Pos& prev, float prevTime, const Pos& last, float lastTime, float now, float maxAhead = 1.0f);

}  // namespace xpc
