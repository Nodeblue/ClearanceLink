namespace XPConnect.Protocol;

/// <summary>
/// Turns a raw packet into named fields for logs and recordings. Covers the calls an ATC client is most likely to
/// make; anything else is still recorded with its name and hex payload. Layouts follow the MSFS SDK SimConnect.h.
/// </summary>
public static class PacketDecoder
{
    public const int NameLength = 256;

    public static Dictionary<string, object?> Decode(Packet packet) =>
        packet.Direction == Direction.ClientToServer ? DecodeClient(packet) : DecodeServer(packet);

    private static Dictionary<string, object?> DecodeClient(Packet packet)
    {
        var r = new PayloadReader(packet.Payload);
        var f = new Dictionary<string, object?>();
        switch (packet.FunctionId)
        {
            case 0x01: // Open
                f["appName"] = r.Str(NameLength);
                r.U32();
                r.U8();
                f["alias"] = r.Str(3);
                f["versionMajor"] = r.U32();
                f["versionMinor"] = r.U32();
                f["buildMajor"] = r.U32();
                f["buildMinor"] = r.U32();
                break;
            case 0x04: // MapClientEventToSimEvent
                f["eventId"] = r.U32();
                f["eventName"] = r.Str(NameLength);
                break;
            case 0x05: // TransmitClientEvent
                f["objectId"] = r.U32();
                f["eventId"] = r.U32();
                f["data"] = r.U32();
                f["groupId"] = r.U32();
                f["flags"] = r.U32();
                break;
            case 0x07: // AddClientEventToNotificationGroup
                f["groupId"] = r.U32();
                f["eventId"] = r.U32();
                f["maskable"] = r.U32();
                break;
            case 0x0C: // AddToDataDefinition
                f["defineId"] = r.U32();
                f["datumName"] = r.Str(NameLength);
                f["unitsName"] = r.Str(NameLength);
                f["dataType"] = r.U32();
                f["epsilon"] = r.F32();
                f["datumId"] = r.U32();
                break;
            case 0x0D: // ClearDataDefinition
                f["defineId"] = r.U32();
                break;
            case 0x0E: // RequestDataOnSimObject
                f["requestId"] = r.U32();
                f["defineId"] = r.U32();
                f["objectId"] = r.U32();
                f["period"] = r.U32();
                f["flags"] = r.U32();
                f["origin"] = r.U32();
                f["interval"] = r.U32();
                f["limit"] = r.U32();
                break;
            case 0x0F: // RequestDataOnSimObjectType
                f["requestId"] = r.U32();
                f["defineId"] = r.U32();
                f["radiusMeters"] = r.U32();
                f["objectType"] = r.U32();
                break;
            case 0x10: // SetDataOnSimObject
                f["defineId"] = r.U32();
                f["objectId"] = r.U32();
                f["flags"] = r.U32();
                f["arrayCount"] = r.U32();
                f["unitSize"] = r.U32();
                f["dataHex"] = Convert.ToHexString(r.Rest());
                break;
            case 0x11: // MapInputEventToClientEvent
                f["groupId"] = r.U32();
                f["inputDefinition"] = r.Str(NameLength);
                f["downEventId"] = r.U32();
                f["downValue"] = r.U32();
                f["upEventId"] = r.U32();
                f["upValue"] = r.U32();
                f["maskable"] = r.U32();
                break;
            case 0x17: // SubscribeToSystemEvent
                f["eventId"] = r.U32();
                f["eventName"] = r.Str(NameLength);
                break;
            case 0x18: // UnsubscribeFromSystemEvent
                f["eventId"] = r.U32();
                break;
            case 0x27: // AICreateParkedATCAircraft
                f["containerTitle"] = r.Str(NameLength);
                f["tailNumber"] = r.Str(12);
                f["airportId"] = r.Str(5);
                f["requestId"] = r.U32();
                break;
            case 0x28: // AICreateEnrouteATCAircraft
                f["containerTitle"] = r.Str(NameLength);
                f["tailNumber"] = r.Str(12);
                f["flightNumber"] = r.I32();
                f["flightPlanPath"] = r.Str(260);
                f["flightPlanPosition"] = r.F64();
                f["touchAndGo"] = r.U32();
                f["requestId"] = r.U32();
                break;
            case 0x29: // AICreateNonATCAircraft
                f["containerTitle"] = r.Str(NameLength);
                f["tailNumber"] = r.Str(12);
                f["initPositionHex"] = Convert.ToHexString(r.Bytes(r.Remaining - 4));
                f["requestId"] = r.U32();
                break;
            case 0x2B: // AIReleaseControl
            case 0x2C: // AIRemoveObject
                f["objectId"] = r.U32();
                f["requestId"] = r.U32();
                break;
            case 0x2D: // AISetAircraftFlightPlan
                f["objectId"] = r.U32();
                f["flightPlanPath"] = r.Str(260);
                f["requestId"] = r.U32();
                break;
            case 0x35: // RequestSystemState
                f["requestId"] = r.U32();
                f["state"] = r.Str(NameLength);
                break;
            case 0x37: // MapClientDataNameToID
                f["clientDataName"] = r.Str(NameLength);
                f["clientDataId"] = r.U32();
                break;
            case 0x3F: // FlightPlanLoad
                f["fileName"] = r.Str(260);
                break;
            case 0x41: // SubscribeToFacilities
            case 0x43: // RequestFacilitiesList
            case 0x47: // SubscribeToFacilities_EX1 (first two fields)
            case 0x49: // RequestFacilitiesList_EX1
                f["listType"] = r.U32();
                f["requestId"] = r.U32();
                break;
            case 0x45: // AddToFacilityDefinition
                f["defineId"] = r.U32();
                f["fieldName"] = r.Str(NameLength);
                break;
            case 0x46: // RequestFacilityData
            case 0x4A: // RequestFacilityData_EX1
                f["defineId"] = r.U32();
                f["requestId"] = r.U32();
                f["icao"] = r.Str(16);
                f["region"] = r.Str(4);
                if (packet.FunctionId == 0x4A)
                {
                    f["type"] = r.Str(1);
                }

                break;
            case 0x57: // AICreateParkedATCAircraft_EX1
                f["containerTitle"] = r.Str(NameLength);
                f["livery"] = r.Str(NameLength);
                f["tailNumber"] = r.Str(12);
                f["airportId"] = r.Str(5);
                f["requestId"] = r.U32();
                break;
            case 0x58: // AICreateEnrouteATCAircraft_EX1
                f["containerTitle"] = r.Str(NameLength);
                f["livery"] = r.Str(NameLength);
                f["tailNumber"] = r.Str(12);
                f["flightNumber"] = r.I32();
                f["flightPlanPath"] = r.Str(260);
                f["flightPlanPosition"] = r.F64();
                f["touchAndGo"] = r.U32();
                f["requestId"] = r.U32();
                break;
            case 0x59: // AICreateNonATCAircraft_EX1
                f["containerTitle"] = r.Str(NameLength);
                f["livery"] = r.Str(NameLength);
                f["tailNumber"] = r.Str(12);
                f["initPositionHex"] = Convert.ToHexString(r.Bytes(r.Remaining - 4));
                f["requestId"] = r.U32();
                break;
            case 0x5B: // EnumerateSimObjectsAndLiveries
            case 0x5E: // RequestAllFacilities
                f["requestId"] = r.U32();
                f["type"] = r.U32();
                break;
            default:
                return f;
        }

        if (r.Overrun)
        {
            f["decodeWarning"] = "payload shorter than expected layout";
        }

        return f;
    }

    private static Dictionary<string, object?> DecodeServer(Packet packet)
    {
        var r = new PayloadReader(packet.Payload);
        var f = new Dictionary<string, object?>();
        switch ((RecvId)packet.TypeField)
        {
            case RecvId.Open:
                f["applicationName"] = r.Str(NameLength);
                f["appVersionMajor"] = r.U32();
                f["appVersionMinor"] = r.U32();
                f["appBuildMajor"] = r.U32();
                f["appBuildMinor"] = r.U32();
                f["simConnectVersionMajor"] = r.U32();
                f["simConnectVersionMinor"] = r.U32();
                f["simConnectBuildMajor"] = r.U32();
                f["simConnectBuildMinor"] = r.U32();
                f["reserved1"] = r.U32();
                f["reserved2"] = r.U32();
                break;
            case RecvId.Exception:
                f["exception"] = r.U32();
                f["sendId"] = r.U32();
                f["index"] = r.U32();
                break;
            case RecvId.Event:
                f["groupId"] = r.U32();
                f["eventId"] = r.U32();
                f["data"] = r.U32();
                break;
            case RecvId.SimObjectData:
            case RecvId.SimObjectDataByType:
                f["requestId"] = r.U32();
                f["objectId"] = r.U32();
                f["defineId"] = r.U32();
                f["flags"] = r.U32();
                f["entryNumber"] = r.U32();
                f["outOf"] = r.U32();
                f["defineCount"] = r.U32();
                f["dataHex"] = Convert.ToHexString(r.Rest());
                break;
            case RecvId.AssignedObjectId:
                f["requestId"] = r.U32();
                f["objectId"] = r.U32();
                break;
            case RecvId.SystemState:
                f["requestId"] = r.U32();
                f["integer"] = r.I32();
                f["float"] = r.F32();
                f["string"] = r.Str(260);
                break;
            case RecvId.FacilityData:
                f["userRequestId"] = r.U32();
                f["uniqueRequestId"] = r.U32();
                f["parentUniqueRequestId"] = r.U32();
                f["type"] = r.U32();
                f["isListItem"] = r.U32();
                f["itemIndex"] = r.U32();
                f["listSize"] = r.U32();
                f["dataHex"] = Convert.ToHexString(r.Rest());
                break;
            case RecvId.FacilityDataEnd:
                f["requestId"] = r.U32();
                break;
            default:
                return f;
        }

        return f;
    }
}
