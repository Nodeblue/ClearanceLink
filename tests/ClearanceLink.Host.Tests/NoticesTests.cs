using System.Text.Json;

namespace ClearanceLink.Host.Tests;

public sealed class NoticesTests
{
    // Every NuGet package that ends up in ClearanceLink.exe must be named in the release's third-party notices, so a
    // new dependency cannot ship without its licence notice.
    [Fact]
    public void Every_shipped_package_is_named_in_the_third_party_notices()
    {
        var root = FindRoot();
        var notices = File.ReadAllText(Path.Combine(root, "packaging", "THIRD-PARTY-NOTICES.txt"));
        using var assets = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(root, "src", "ClearanceLink.Host", "obj", "project.assets.json")));

        var missing = assets.RootElement.GetProperty("libraries").EnumerateObject()
            .Where(lib => lib.Value.GetProperty("type").GetString() == "package")
            .Select(lib => lib.Name.Split('/')[0])
            .Select(id => id.Split(".NativeAssets.")[0])
            .Distinct()
            .Where(id => !notices.Contains(id, StringComparison.Ordinal))
            .ToList();

        Assert.Empty(missing);
    }

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ClearanceLink.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException("ClearanceLink.slnx not found above the test binaries");
    }
}
