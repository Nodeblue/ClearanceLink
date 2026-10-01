// One BATC traffic aircraft, drawn by XPMP2 from the latest state XPConnect sent.
#pragma once

#include "GroundFit.h"
#include "Motion.h"
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
    // Probes the terrain under the aircraft now, or reuses the last probe while it has not moved (parked) or is
    // airborne (only the reported ground height needs it). Returns false when there is no terrain under it.
    bool ProbeGround(bool onGround, float now);
    Pos Drawn(float now) const;

    Pos prev_{}, state_{};  // the last two updates from XPConnect, blended between (Motion.h)
    float prevTime_ = 0, stateTime_ = 0;
    bool hasState_ = false;
    XPLMProbeRef probe_ = nullptr;
    std::optional<double> groundFt_;
    // Last terrain probe: where (local x, z), when, what it found and the slope along the heading then.
    float probeX_ = 0, probeZ_ = 0, probeTime_ = -1000, probeY_ = 0;
    float probeHeading_ = 0;
    std::optional<float> slope_;
};

}  // namespace xpc
