using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SadPSX.Frontend.App;

internal sealed record FrontendUpdateInfo(
    Version CurrentVersion,
    Version LatestVersion,
    string Tag,
    string ReleaseUrl,
    string? ReleaseName)
{
    public bool IsUpdateAvailable => LatestVersion > CurrentVersion;
}

internal sealed class UpdateService : IDisposable
{
    private const string ReleasesUrl =
        "https://api.github.com/repos/kadu04t/SadPSX/releases?per_page=10";
    private readonly HttpClient _client;
    private readonly bool _ownsClient;

    public UpdateService(HttpClient? client = null)
    {
        _client = client ?? new HttpClient();
        _ownsClient = client is null;
        _client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("SadPSX", GetCurrentVersion().ToString()));
        _client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<FrontendUpdateInfo?> CheckAsync(
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _client.GetAsync(
            ReleasesUrl,
            cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return null;

        await using Stream content = await response.Content.ReadAsStreamAsync(
            cancellationToken).ConfigureAwait(false);
        using JsonDocument document = await JsonDocument.ParseAsync(
            content,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Array)
            return null;
        JsonElement? release = root.EnumerateArray().FirstOrDefault(item =>
            !item.TryGetProperty("draft", out JsonElement draft) ||
            !draft.GetBoolean());
        if (release is not JsonElement releaseNode ||
            releaseNode.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string? tag = releaseNode.TryGetProperty(
            "tag_name",
            out JsonElement tagNode)
            ? tagNode.GetString()
            : null;
        string? releaseName = releaseNode.TryGetProperty(
            "name",
            out JsonElement nameNode)
            ? nameNode.GetString()
            : null;
        string? releaseUrl = releaseNode.TryGetProperty(
            "html_url",
            out JsonElement urlNode)
            ? urlNode.GetString()
            : null;
        if ((!TryParseVersion(tag, out Version? latestVersion) &&
             !TryParseVersion(releaseName, out latestVersion)) ||
            string.IsNullOrWhiteSpace(releaseUrl))
        {
            return null;
        }

        return new FrontendUpdateInfo(
            GetCurrentVersion(),
            latestVersion!,
            tag!,
            releaseUrl,
            releaseName);
    }

    public static Version GetCurrentVersion() =>
        Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);

    internal static bool TryParseVersion(string? value, out Version? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string normalized = value.Trim();
        if (normalized.StartsWith('v') || normalized.StartsWith('V'))
            normalized = normalized[1..];
        int suffix = normalized.IndexOfAny(['-', '+']);
        if (suffix >= 0)
            normalized = normalized[..suffix];
        if (Version.TryParse(normalized, out version))
            return true;

        Match embeddedVersion = Regex.Match(
            value,
            @"(?<!\d)\d+\.\d+(?:\.\d+){0,2}(?!\d)",
            RegexOptions.CultureInvariant);
        return embeddedVersion.Success &&
            Version.TryParse(embeddedVersion.Value, out version);
    }

    public void Dispose()
    {
        if (_ownsClient)
            _client.Dispose();
    }
}
