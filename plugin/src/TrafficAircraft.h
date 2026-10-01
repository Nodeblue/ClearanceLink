// One BATC traffic aircraft, drawn by XPMP2 from the latest state XPConnect sent.
#pragma once

#include "Protocol.h"

#include "XPLMScenery.h"
#include "XPMPAircraft.h"

#include <optional>
#include <string>

namespace xpc {

class TrafficAircraft : public XPMP2::Aircraft {
public:
    // Throws XPMP2::XPMP2Error when no CSL model can be matched (for example none installed).
    explicit TrafficAircraft(const Add& add);
    ~TrafficAircraft() override;

    void Apply(const Pos& pos, float now);
    void ChangeTo(const Add& add);

    // Terrain elevation (MSL feet) under the aircraft at the last frame, once known.
    std::optional<double> GroundElevationFt() const { return groundFt_; }

    void UpdatePosition(float elapsedSinceLastCall, int flightLoopCounter) override;

private:
    void SetLabel(const Add& add);

    std::string requested_;  // type, airline and livery last asked for, to skip re-matching on a repeated add
    Pos state_{};
    float stateTime_ = 0;
    bool hasState_ = false;
    XPLMProbeRef probe_ = nullptr;
    std::optional<double> groundFt_;
};

// Moves a position on along a true heading by `meters` (flat-earth step, fine for under a kilometre).
void MoveAlong(double& lat, double& lon, double headingDeg, double meters);

}  // namespace xpc
