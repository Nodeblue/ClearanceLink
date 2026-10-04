namespace ClearanceLink.Protocol.Facilities;

/// <summary>
/// A client's facility definition as a tree, built from its AddToFacilityDefinition calls in order
/// ("OPEN AIRPORT", "LATITUDE", ..., "OPEN RUNWAY", ..., "CLOSE RUNWAY", ..., "CLOSE AIRPORT").
/// </summary>
public sealed class FacilityDefinition
{
    private readonly Stack<FacilityDefinitionNode> _open = new();

    public FacilityDefinitionNode? Root { get; private set; }

    /// <summary>Fields the schema does not know; the definition cannot be encoded while any exist.</summary>
    public List<string> UnknownFields { get; } = [];

    /// <summary>True once the root object has been closed.</summary>
    public bool IsComplete => Root is not null && _open.Count == 0;

    public static FacilityDefinition FromNames(IEnumerable<string> names)
    {
        var definition = new FacilityDefinition();
        foreach (var name in names)
        {
            definition.Add(name);
        }

        return definition;
    }

    public void Add(string entry)
    {
        var text = entry.Trim();
        if (text.StartsWith("OPEN ", StringComparison.Ordinal))
        {
            var node = new FacilityDefinitionNode(text[5..].Trim());
            if (_open.TryPeek(out var parent))
            {
                parent.Children.Add(node);
            }
            else
            {
                Root ??= node;
            }

            _open.Push(node);
        }
        else if (text.StartsWith("CLOSE ", StringComparison.Ordinal))
        {
            _open.TryPop(out _);
        }
        else if (_open.TryPeek(out var current))
        {
            if (FacilitySchema.TryGetField(current.Name, text, out var field))
            {
                current.Fields.Add(field);
            }
            else
            {
                UnknownFields.Add($"{current.Name}.{text}");
            }
        }
    }
}

public sealed class FacilityDefinitionNode(string name)
{
    public string Name { get; } = name;

    public List<FacilityField> Fields { get; } = [];

    public List<FacilityDefinitionNode> Children { get; } = [];

    public int DataSize => Fields.Sum(f => f.Size);
}
