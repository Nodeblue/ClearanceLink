using System.Globalization;

namespace XPConnect.Server;

/// <summary>A value read from the simulator: a number or a string. Numbers are in the mapping's source units.</summary>
public readonly record struct SimValue(double Number, string? Text = null)
{
    public static SimValue Zero { get; } = new(0);

    public bool IsText => Text is not null;

    public static SimValue Of(double number) => new(number);

    public static SimValue Of(string text) => new(0, text);

    public string AsText() => Text ?? Number.ToString(CultureInfo.InvariantCulture);

    public double AsNumber() =>
        Text is null ? Number : double.TryParse(Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : 0;

    public override string ToString() => AsText();
}
