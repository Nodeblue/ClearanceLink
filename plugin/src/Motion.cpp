#include "Motion.h"

#include <algorithm>
#include <cmath>

namespace xpc {
namespace {

constexpr double Pi = 3.14159265358979323846;
constexpr double MetersPerKnotSecond = 0.514444;
// Update intervals are clamped to this range: shorter is jitter, longer means the aircraft was parked or updates
// paused, and it should then catch up quickly rather than crawl.
constexpr float MinInterval = 0.02f;
constexpr float MaxInterval = 0.5f;
// Further apart than this is a jump (a new position from BATC), drawn at once.
constexpr double MaxBlendMeters = 1000;

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

void MoveAlong(double& lat, double& lon, double headingDeg, double meters) {
    const double heading = headingDeg * Pi / 180.0;
    const double metersPerDegLat = 111132.92;
    const double metersPerDegLon = 111412.84 * std::cos(lat * Pi / 180.0);
    lat += meters * std::cos(heading) / metersPerDegLat;
    if (metersPerDegLon > 1) lon += meters * std::sin(heading) / metersPerDegLon;
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

Pos DrawnPosition(const Pos& prev, float prevTime, const Pos& last, float lastTime, float now, float maxAhead) {
    const float interval = std::clamp(lastTime - prevTime, MinInterval, MaxInterval);
    const float since = std::max(0.0f, now - lastTime);
    if (since < interval && DistanceMeters(prev, last) <= MaxBlendMeters) return Blend(prev, last, since / interval);

    Pos p = last;
    const float ahead = std::min(since - interval, maxAhead);
    if (ahead > 0) MoveAlong(p.lat, p.lon, p.heading, p.groundSpeedKt * MetersPerKnotSecond * ahead);
    return p;
}

}  // namespace xpc
