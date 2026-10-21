using ClipNest.Platform;

namespace ClipNest.Platform.Tests;

public class UpdateCheckerTests
{
    private static readonly Version Current = new(1, 0, 0, 0);

    [Theory]
    [InlineData("v1.1.0", true)]
    [InlineData("1.0.1", true)]
    [InlineData("v2.0", true)]
    [InlineData("v1.0.0", false)]
    [InlineData("v0.9.9", false)]
    [InlineData("v1.1.0-beta", true)]
    public void Newer_tags_are_detected(string tag, bool newer)
    {
        var json = $$"""{"tag_name":"{{tag}}","html_url":"https://github.com/tsix2019/ClipNest/releases/tag/{{tag}}"}""";
        var info = UpdateChecker.Parse(json, Current);
        Assert.Equal(newer, info is not null);
        if (info is not null) Assert.EndsWith(tag, info.PageUrl);
    }

    [Theory]
    [InlineData("""{"message":"Not Found"}""")]
    [InlineData("""{"tag_name":"latest"}""")]
    public void Unusable_responses_mean_no_update(string json) =>
        Assert.Null(UpdateChecker.Parse(json, Current));

    [Fact]
    public void Missing_page_url_falls_back_to_releases()
    {
        var info = UpdateChecker.Parse("""{"tag_name":"v9.0.0"}""", Current);
        Assert.Equal(UpdateChecker.ReleasesUrl, info!.PageUrl);
        Assert.Equal(new Version(9, 0, 0), info.Version);
    }
}
