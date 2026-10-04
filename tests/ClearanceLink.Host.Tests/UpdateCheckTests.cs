using System.Net;
using ClearanceLink.Host.Panel;
using ClearanceLink.Host.Support;

namespace ClearanceLink.Host.Tests;

public sealed class UpdateCheckTests
{
    private const string Release = """
        {"tag_name":"v0.6.0","name":"ClearanceLink v0.6.0","draft":false,"prerelease":false,
         "html_url":"https://github.com/Nodeblue/ClearanceLink/releases/tag/v0.6.0"}
        """;

    [Theory]
    [InlineData("v0.6.0", "0.5.0", true)]
    [InlineData("v0.6.0", "0.6.0", false)]
    [InlineData("v0.5.9", "0.6.0", false)]
    [InlineData("v0.10.0", "0.9.0", true)]
    [InlineData("v1.0", "0.9.9", true)]
    [InlineData("0.6", "0.6.0", false)]
    [InlineData("v0.6.0", "0.6.0-beta.1", true)]
    [InlineData("v0.6.0-beta.2", "0.6.0", false)]
    [InlineData("v0.6.0", "dev", false)]
    [InlineData("nightly", "0.5.0", false)]
    public void Newer_compares_numbers_and_test_builds(string tag, string current, bool newer) =>
        Assert.Equal(newer, UpdateCheck.IsNewer(tag, current));

    [Fact]
    public void A_newer_release_is_offered_with_its_page()
    {
        var found = UpdateCheck.FromJson(Release, "0.5.0");
        Assert.Equal(new UpdateInfo("0.6.0", "https://github.com/Nodeblue/ClearanceLink/releases/tag/v0.6.0"), found);
        Assert.Null(UpdateCheck.FromJson(Release, "0.6.0"));
    }

    [Fact]
    public void Drafts_and_pre_releases_are_never_offered()
    {
        Assert.Null(UpdateCheck.FromJson(Release.Replace("\"draft\":false", "\"draft\":true", StringComparison.Ordinal), "0.5.0"));
        Assert.Null(UpdateCheck.FromJson(Release.Replace("\"prerelease\":false", "\"prerelease\":true", StringComparison.Ordinal), "0.5.0"));
    }

    [Fact]
    public async Task Asks_GitHub_for_the_latest_release()
    {
        using var github = new Answer(HttpStatusCode.OK, Release);
        var found = await UpdateCheck.FindAsync("0.5.0", github);
        Assert.Equal("0.6.0", found?.Version);
        Assert.Equal(UpdateCheck.LatestRelease, github.Asked?.RequestUri);
        Assert.Contains("ClearanceLink/0.5.0", github.Asked?.Headers.UserAgent.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "{\"message\":\"Not Found\"}")] // no release yet, or the repository is private
    [InlineData(HttpStatusCode.Forbidden, "{\"message\":\"API rate limit exceeded\"}")]
    [InlineData(HttpStatusCode.OK, "<html>captive portal</html>")]
    public async Task Any_failure_means_no_update_and_no_error(HttpStatusCode status, string body)
    {
        using var github = new Answer(status, body);
        Assert.Null(await UpdateCheck.FindAsync("0.5.0", github));
    }

    [Fact]
    public async Task Offline_means_no_update()
    {
        using var offline = new Answer(null, "");
        Assert.Null(await UpdateCheck.FindAsync("0.5.0", offline));
    }

    [Fact]
    public void About_page_shows_a_newer_version_with_a_key_to_its_download_page()
    {
        var state = new PanelState("0.5.0", true, true, "12.4.3", true, true, 0, null, null, false, ConnectionMode.Automatic,
            false, true, 0, 7, TimeSpan.Zero, null, null);
        Assert.DoesNotContain(CduPages.Build(PanelPage.About, state).Rows, r => r.KeyRight == CduKey.OpenUpdatePage);

        var about = CduPages.Build(PanelPage.About, state with { Update = new UpdateInfo("0.6.0", "https://example.invalid/") });
        var row = Assert.Single(about.Rows, r => r.KeyRight == CduKey.OpenUpdatePage);
        Assert.Equal("NEW VERSION", row.LabelRight);
        Assert.Equal("0.6.0>", row.Right.Text);
    }

    [Fact]
    public void Update_check_is_on_unless_turned_off()
    {
        var file = Path.Combine(Path.GetTempPath(), $"clink-settings-{Guid.NewGuid():N}.json");
        try
        {
            Assert.True(AppSettings.Load(file).CheckForUpdates);
            new AppSettings { CheckForUpdates = false }.Save(file);
            Assert.False(AppSettings.Load(file).CheckForUpdates);
        }
        finally
        {
            File.Delete(file);
        }
    }

    /// <summary>Answers every request with one status and body; a null status throws as if offline.</summary>
    private sealed class Answer(HttpStatusCode? status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Asked { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Asked = request;
            return status is { } s
                ? Task.FromResult(new HttpResponseMessage(s) { Content = new StringContent(body) })
                : throw new HttpRequestException("No such host is known.");
        }
    }
}
