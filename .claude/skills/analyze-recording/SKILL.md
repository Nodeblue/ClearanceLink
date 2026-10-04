---
name: analyze-recording
description: Use when asked to look at an ClearanceLink recording (.jsonl) or work out what BeyondATC asked for, why a session failed, or what to map next.
---

1. Summarise: `dotnet run --project src/ClearanceLink.Host -- summarize <file.jsonl>`, then read the `.md` it writes.
2. For a failure, find the last few `c2s` calls before the client disconnected. Also find any `Exception` replies; the summary maps each one back to the call that caused it.
3. Cross-check with BATC's `Player.log` for the same time window if it is available.
4. List the SimVars, events and facility fields that have no entry in `mapping/*.yaml` yet. That list is the to-do list.
5. Put findings in `docs/research/` and protocol facts in `docs/protocol/simconnect.md`, citing the recording.
