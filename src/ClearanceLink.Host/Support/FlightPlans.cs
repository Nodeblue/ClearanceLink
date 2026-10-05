namespace ClearanceLink.Host.Support;

/// <summary>What the FPLN page shows: the plan last received from SimBrief and what happened to it.</summary>
public sealed record FlightPlanView(
    SimbriefFlight? Plan = null,
    DateTimeOffset? ReceivedAt = null,
    bool Requesting = false,
    string? SavedAs = null,
    DateTimeOffset? SentAt = null,
    string? FmsProblem = null);

/// <summary>
/// The window's flight plan uplink, like a jet's FMC route request: REQUEST fetches the latest SimBrief plan and shows
/// it without touching X-Plane; LOAD saves it into X-Plane's Output/FMS plans and asks the plugin to load it into
/// X-Plane's own FMS (aircraft with their own FMS load the saved file from their menus). ADR-0011.
/// </summary>
public sealed class FlightPlans(Func<AppSettings> settings, Func<string?> xplaneRoot, Func<string, bool> sendToFms, HttpMessageHandler? handler = null,
    TimeProvider? time = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Lock _gate = new();
    private FlightPlanView _view = new();
    private string? _fms;

    public FlightPlanView View
    {
        get
        {
            lock (_gate)
            {
                return _view;
            }
        }
    }

    /// <summary>Fetches the latest plan and its .fms; returns the scratchpad line.</summary>
    public async Task<string> RequestAsync(CancellationToken cancel = default)
    {
        if (SimbriefPlan.PilotId(settings()) is not { } id)
        {
            return "NO SIMBRIEF ID IN BEYONDATC";
        }

        Update(v => v with { Requesting = true });
        try
        {
            if (await SimbriefPlan.FindAsync(id, handler, cancel).ConfigureAwait(false) is not { } plan)
            {
                return "NO PLAN FROM SIMBRIEF";
            }

            if (await SimbriefPlan.DownloadAsync(plan, handler, cancel).ConfigureAwait(false) is not { } fms)
            {
                return "PLAN NOT DOWNLOADED";
            }

            lock (_gate)
            {
                var same = _view.Plan?.RequestId == plan.RequestId;
                _fms = fms;
                _view = same ? _view with { Plan = plan, ReceivedAt = _time.GetUtcNow() } : new FlightPlanView(plan, _time.GetUtcNow());
                PacketConsole.Info($"simbrief: received plan {plan.RequestId} {plan.Origin}-{plan.Destination}");
                return same ? "NO NEWER PLAN" : "PLAN RECEIVED";
            }
        }
        finally
        {
            Update(v => v with { Requesting = false });
        }
    }

    /// <summary>Saves the received plan and sends it to X-Plane's FMS; returns the scratchpad line.</summary>
    public string Load()
    {
        SimbriefFlight plan;
        string fms;
        lock (_gate)
        {
            if (_view.Plan is null || _fms is null)
            {
                return "REQUEST A PLAN FIRST";
            }

            (plan, fms) = (_view.Plan, _fms);
        }

        if (xplaneRoot() is not { } root)
        {
            return "SET X-PLANE FOLDER";
        }

        if (SimbriefPlan.Save(plan, fms, root) is not { } path)
        {
            return "NOT SAVED: SEE LIVE LOG";
        }

        PacketConsole.Info($"simbrief: saved {plan.Origin}-{plan.Destination} as {path}");
        var sent = sendToFms(path);
        Update(v => v with { SavedAs = plan.FileName, SentAt = sent ? _time.GetUtcNow() : null, FmsProblem = null });
        return sent ? "SENT TO FMS: CHECK IT" : $"SAVED: LOAD {Path.GetFileNameWithoutExtension(plan.FileName)} IN FMS";
    }

    /// <summary>The plugin's answer to a load: null when X-Plane took it, else why not.</summary>
    public void FmsAnswered(string? problem)
    {
        Update(v => v with { FmsProblem = problem, SentAt = problem is null ? v.SentAt : null });
        PacketConsole.Info(problem is null ? "simbrief: X-Plane's FMS took the flight plan" : $"simbrief: X-Plane's FMS did not take the flight plan: {problem}");
    }

    private void Update(Func<FlightPlanView, FlightPlanView> change)
    {
        lock (_gate)
        {
            _view = change(_view);
        }
    }
}
