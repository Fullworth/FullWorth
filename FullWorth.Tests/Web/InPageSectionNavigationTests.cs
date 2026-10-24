using System.Net;
using System.Text.RegularExpressions;
using FullWorth.Tests.Infrastructure;

namespace FullWorth.Tests.Web;

public sealed class InPageSectionNavigationTests
{
    [Theory]
    [InlineData("/terms", "aside", "legal-toc", 15)]
    [InlineData("/privacy", "aside", "legal-toc", 11)]
    public async Task EveryContentsLink_ResolvesWithinItsDocument_WithAnExistingTarget(
        string route, string tag, string navigationClass, int expectedSections)
    {
        using var factory = new FullWorthWebFactory();
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Add("X-FullWorth-Test-Anonymous", "true");
        using var response = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        // Resolve the actual rendered hrefs against the rendered base element,
        // as browsers do. Fragment-only hrefs resolve to / under Blazor's base,
        // even when every referenced section exists in this document.
        var baseElement = Match(html, @"<base\b[^>]*href=""([^""]+)""");
        Assert.True(baseElement.Success, "The rendered document must expose its base URL.");
        var documentUri = new Uri(client.BaseAddress!, route);
        var baseUri = new Uri(documentUri, WebUtility.HtmlDecode(baseElement.Groups[1].Value));
        var contents = Match(html, "<" + tag + @"\b[^>]*class=""" + Regex.Escape(navigationClass) + @"""[^>]*>(.*?)</" + tag + ">");
        Assert.True(contents.Success, "The page must retain its section navigation.");
        var links = Matches(contents.Groups[1].Value, @"<a\b[^>]*href=""([^""]+)""");
        Assert.Equal(expectedSections, links.Count);

        foreach (Match link in links)
        {
            var href = WebUtility.HtmlDecode(link.Groups[1].Value);
            var destination = new Uri(baseUri, href);
            Assert.Equal(documentUri.Scheme, destination.Scheme);
            Assert.Equal(documentUri.Authority, destination.Authority);
            Assert.Equal(documentUri.AbsolutePath, destination.AbsolutePath);
            Assert.Empty(destination.Query);
            Assert.False(string.IsNullOrEmpty(destination.Fragment));
            var id = Uri.UnescapeDataString(destination.Fragment[1..]);
            Assert.Single(Matches(html, @"\bid=""" + Regex.Escape(id) + @"""").Cast<Match>());
        }
    }

    [Fact]
    public async Task PlanningInitialization_DoesNotExposeShortcutsBeforeTheirTargetsExist()
    {
        using var factory = new FullWorthWebFactory();
        using var client = factory.CreateHttpsClient();
        using var response = await client.GetAsync("/app/planning");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("planning-loading", html);
        Assert.DoesNotContain("planning-jump-links", html);
    }

    private static Match Match(string html, string pattern) =>
        Regex.Match(html, pattern, RegexOptions.Singleline | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

    private static MatchCollection Matches(string html, string pattern) =>
        Regex.Matches(html, pattern, RegexOptions.Singleline | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
}
