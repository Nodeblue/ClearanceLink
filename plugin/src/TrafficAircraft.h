// One BATC traffic aircraft, drawn by XPMP2 from the latest state XPConnect sent.
#pragma once

#include "GroundFit.h"
#include "Protocol.h"

#include "XPLMScenery.h"
#include "XPMPAircraft.h"

#include <map>
#include <memory>
#include <optional>
#include <string>

namespace xpc {

struct ModelFit;

class TrafficAircraft : public XPMP2::Aircraft {
public:
    // The CSL packages (FindCslPackages) for reading models' wheel positions; set after the CSL folders load.
    static void SetCslPackages(std::map<std::string, std::string> packages);
    // Drops the cached wheel positions (waits for reads still running).
    static void ForgetModelFits();

    // Throws XPMP2::XPMP2Error when no CSL model can be matched (for example none installed).
    explicit TrafficAircraft(const Add& add);
    ~TrafficAircraft() override;

    void Apply(const Pos& pos, float now);
    void ChangeTo(const Add& add);
    void SetGroundOffsetFt(float feet) { groundOffsetFt_ = feet; }

    // Terrain elevation (MSL feet) under the aircraft at the last frame, once known.
    std::optional<double> GroundElevationFt() const { return groundFt_; }

    void UpdatePosition(float elapsedSinceLastCall, int flightLoopCounter) override;

private:
    void SetLabel(const Add& add);
    // Pitch of the ground along the aircraft's heading (degrees, nose up positive), from two terrain probes.
    std::optional<float> GroundSlopeDeg(float centreY);
    // The model's nose and main wheels once read from its .obj files (in the background, once per model).
    const Wheels* ModelWheels();

    const XPMP2::CSLModel* fitModel_ = nullptr;
    std::shared_ptr<ModelFit> fit_;

    float groundOffsetFt_ = 0;
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
