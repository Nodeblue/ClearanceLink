# Sample recordings

SimConnect traffic between an ATC app (its 1.8.21) and MSFS 2024, recorded with `clearancelink record` on 2026-09-30 and
used as test fixtures (the facility encoder is checked byte for byte against them).

- `atc-app-1.8.21-setup.jsonl`: connection setup and the SimVars and events the app uses.
- `atc-app-1.8.22-setup.jsonl`: the app's 1.8.22 against ClearanceLink 0.7.4 (Ben's PC, 2026-10-07): its three
  connections opening (aircraft and radios, airport data, traffic timing; twice) and the traffic-timing connection's
  data definitions and `Frame` subscription. The client names inside the files are recorded as sent.
- `atc-app-1.8.21-facilities.jsonl`: the airport data MSFS sent (runways, parking, taxiways, frequencies and
  procedures for YBBN, YTWB and surrounding airports).

The airport and procedure values in these files were produced by MSFS 2024 and its navigation data. They are not
covered by this repository's GPLv3 licence. They are used only by the tests and are not part of the ClearanceLink release.
