namespace XPConnect.Host.Status;

/// <summary>The status page itself, embedded so the exe needs no extra files.</summary>
internal static class StatusPage
{
    public static string Html { get; } = Load();

    private static string Load()
    {
        using var stream = typeof(StatusPage).Assembly.GetManifestResourceStream("XPConnect.Host.Status.status.html")
            ?? throw new InvalidOperationException("status.html resource missing");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
