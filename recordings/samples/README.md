# Sample recordings

SimConnect traffic between BeyondATC 1.8.21 and MSFS 2024, recorded with `clearancelink record` on 2026-09-30 and
used as test fixtures (the facility encoder is checked byte for byte against them).

- `batc-1.8.21-setup.jsonl`: connection setup and the SimVars and events BeyondATC uses.
- `batc-1.8.22-setup.jsonl`: BeyondATC 1.8.22 against ClearanceLink 0.7.4 (Ben's PC, 2026-10-07): its three
  connections opening (`BATCDLLSimConnection`, `BATC_Facilities`, `BATC_TrafficSync`, twice) and `BATC_TrafficSync`'s
  data definitions and `Frame` subscription.
- `batc-1.8.21-facilities.jsonl`: the airport data MSFS sent (runways, parking, taxiways, frequencies and
  procedures for YBBN, YTWB and surrounding airports).

The airport and procedure values in these files were produced by MSFS 2024 and its navigation data. They are not
covered by this repository's GPLv3 licence. They are used only by the tests and are not part of the ClearanceLink release.
