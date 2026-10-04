namespace ClearanceLink.Protocol.Facilities;

/// <summary>
/// One facility object to send (an airport, a runway, a taxi point...). Values are keyed by field name; fields a
/// definition asks for that are not set are sent as zeros. Children are keyed by the definition's object name.
/// </summary>
public sealed class FacilityObject(string name)
{
    public string Name { get; } = name;

    public Dictionary<string, object> Values { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, List<FacilityObject>> Children { get; } = new(StringComparer.Ordinal);

    public object? this[string field]
    {
        get => Values.GetValueOrDefault(field);
        set
        {
            if (value is null)
            {
                Values.Remove(field);
            }
            else
            {
                Values[field] = value;
            }
        }
    }

    public FacilityObject Set(string field, object value)
    {
        Values[field] = value;
        return this;
    }

    public FacilityObject Add(FacilityObject child)
    {
        if (!Children.TryGetValue(child.Name, out var list))
        {
            Children[child.Name] = list = [];
        }

        list.Add(child);
        return this;
    }

    public IReadOnlyList<FacilityObject> ChildrenOf(string name) => Children.GetValueOrDefault(name) ?? [];
}
