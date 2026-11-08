using System.Globalization;
using System.Net;
using System.Text.Json;

namespace Playground.Updates;

/// <summary>
/// Reads an app's releases from GitHub's API, without signing in: 60 requests an hour from one address, plenty for a check a day.
/// The newest is the one with the highest build among those tagged <c>{tag}-1.0.N</c> that carry the app's file, drafts and
/// pre-releases aside, so another app's releases in the same repo are never taken for this one's.
/// </summary>
internal static class GitHubReleases
{
    public static async Task<Release?> NewestAsync(HttpClient http, UpdateOptions options, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{options.Repository}/releases?per_page=30");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.UserAgent.ParseAdd("Playground-Updates");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests
            && response.Headers.TryGetValues("X-RateLimit-Remaining", out var left) && left.FirstOrDefault() == "0")
        {
            throw new UpdateException("GitHub's limit on checks from this connection is reached for now; try again in an hour.");
        }
        if (!response.IsSuccessStatusCode) throw new UpdateException($"GitHub answered {(int)response.StatusCode} {response.ReasonPhrase}; try again later.");

        await using var body = await response.Content.ReadAsStreamAsync(ct);
        using var json = await JsonDocument.ParseAsync(body, cancellationToken: ct);
        var prefix = options.Tag + "-1.0.";
        Release? newest = null;
        foreach (var release in json.RootElement.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) continue;
            var tag = release.GetProperty("tag_name").GetString() ?? "";
            if (!tag.StartsWith(prefix, StringComparison.Ordinal) || !int.TryParse(tag[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var build)) continue;
            if (newest is not null && build <= newest.Build) continue;

            var asset = release.GetProperty("assets").EnumerateArray().FirstOrDefault(a => a.GetProperty("name").GetString() == options.Asset);
            if (asset.ValueKind != JsonValueKind.Object) continue;
            // GitHub has given every release file a SHA-256 since June 2025, as "sha256:<hex>".
            var digest = asset.TryGetProperty("digest", out var d) && d.GetString() is { } value && value.StartsWith("sha256:", StringComparison.Ordinal)
                ? value["sha256:".Length..].ToLowerInvariant()
                : null;
            newest = new Release(tag, build, Text(release, "name") ?? tag, Text(release, "body") ?? "", release.GetProperty("published_at").GetDateTimeOffset(),
                new Uri(asset.GetProperty("browser_download_url").GetString()!), asset.GetProperty("size").GetInt64(), digest);
        }
        return newest;
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
