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
    static void SetCslPackages(CslPackages packages);
    // Drops the cached wheel positions (waits for reads still running).
    static void ForgetModelFits();
    // Adds the bogie datarefs some models use besides XPMP2's own (World Traffic's main gear deflection). Call after
    // XPMPMultiplayerInit and before any aircraft exists; returns false when XPMP2 refused.
    static bool AddModelDatarefs();

    // Throws XPMP2::XPMP2Error when no CSL model can be matched (for example none installed).
    explicit TrafficAircraft(const Add& add);
    ~TrafficAircraft() override;

    void Apply(const Pos& pos, float now);
    // When XPConnect last sent this aircraft's add or position (X-Plane elapsed seconds).
    void Heard(float now) { lastHeard_ = now; }
    float LastHeard() const { return lastHeard_; }
    void ChangeTo(const Add& add);
    // XPConnect's per-type height (feet) and, from XPConnect versions that send it, parked pitch: used for models whose
    // wheels could not be read. Without a pitch, XPConnect puts its per-type pitch in each `pos` itself.
    void SetGroundTrim(float feet, std::optional<float> pitch) {
        groundOffsetFt_ = feet;
        groundPitch_ = pitch;
    }

    // Terrain elevation (MSL feet) under the aircraft at the last frame, once known.
    std::optional<double> GroundElevationFt() const { return groundFt_; }

    void UpdatePosition(float elapsedSinceLastCall, int flightLoopCounter) override;

private:
    void SetLabel(const Add& add);
    // Pitch of the ground along the aircraft's heading (degrees, nose up positive), from two terrain probes.
    std::optional<float> GroundSlopeDeg(float centreY);
    // The model's nose and main wheels once read from its .obj files (in the background, once per model).
    const Wheels* ModelWheels();
    // Sets the model's bogie dataref: the level the fit found on the ground, 0 (unloaded, tilted) otherwise.
    void SetBogies(bool onGround);

    const XPMP2::CSLModel* fitModel_ = nullptr;
    std::shared_ptr<ModelFit> fit_;

    float groundOffsetFt_ = 0;
    std::optional<float> groundPitch_;
    std::string requested_;  // type, airline and livery last asked for, to skip re-matching on a repeated add
    // Probes the terrain under the aircraft now, or reuses the last probe while it has not moved (parked) or is
    // airborne (only the reported ground height needs it). Returns false when there is no terrain under it.
    bool ProbeGround(bool onGround, float now);

    Track track_;  // XPConnect's updates, blended between (Motion.h)
    float lastHeard_ = 0;
    XPLMProbeRef probe_ = nullptr;
    std::optional<double> groundFt_;
    // Last terrain probe: where (local x, z), when, what it found and the slope along the heading then.
    float probeX_ = 0, probeZ_ = 0, probeTime_ = -1000, probeY_ = 0;
    float probeHeading_ = 0;
    std::optional<float> slope_;
    // Propeller and wheel angles the plugin turns each frame (XPMP2 does not turn them from rpm itself).
    float engineAngle_ = 0, tireAngle_ = 0;
};

}  // namespace xpc
