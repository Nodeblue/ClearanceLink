namespace ClearanceLink.Protocol;

/// <summary>SimConnect server-to-client message ids (SIMCONNECT_RECV_ID).</summary>
public enum RecvId : uint
{
    Null = 0,
    Exception = 1,
    Open = 2,
    Quit = 3,
    Event = 4,
    EventObjectAddRemove = 5,
    EventFilename = 6,
    EventFrame = 7,
    SimObjectData = 8,
    SimObjectDataByType = 9,
    WeatherObservation = 10,
    CloudState = 11,
    AssignedObjectId = 12,
    ReservedKey = 13,
    CustomAction = 14,
    SystemState = 15,
    ClientData = 16,
    EventWeatherMode = 17,
    AirportList = 18,
    VorList = 19,
    NdbList = 20,
    WaypointList = 21,
    EventMultiplayerServerStarted = 22,
    EventMultiplayerClientStarted = 23,
    EventMultiplayerSessionEnded = 24,
    EventRaceEnd = 25,
    EventRaceLap = 26,
    EventEx1 = 27,
    FacilityData = 28,
    FacilityDataEnd = 29,
    FacilityMinimalList = 30,
    JetwayData = 31,
    ControllersList = 32,
    ActionCallback = 33,
    EnumerateInputEvents = 34,
    GetInputEvent = 35,
    SubscribeInputEvent = 36,
    EnumerateInputEventParams = 37,
    EnumerateSimObjectAndLiveryList = 38,
    FlowEvent = 39,
    CameraData = 40,
    CameraStatus = 41,
    CameraDefinitionList = 42,
    CommBus = 43,
    CameraWorldLocker = 44,
}

public static class RecvNames
{
    public static string Get(uint recvId) =>
        Enum.IsDefined(typeof(RecvId), recvId) ? ((RecvId)recvId).ToString() : $"Unknown_{recvId}";
}
