using System.Text.RegularExpressions;

namespace SadPSX.Frontend.Library;

internal sealed partial class LibretroGameMetadataService : IDisposable
{
    private const string CatalogUrl =
        "https://raw.githubusercontent.com/libretro/libretro-database/" +
        "master/metadat/redump/Sony%20-%20PlayStation.dat";

    private readonly HttpClient _httpClient;
    private readonly string _catalogPath;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    private IReadOnlyDictionary<string, LibretroGameMetadata>? _catalog;
    private bool _disposed;

    public LibretroGameMetadataService(
        HttpMessageHandler? handler = null,
        string? catalogPath = null)
    {
        _httpClient = handler is null
            ? new HttpClient()
            : new HttpClient(handler);
        _httpClient.Timeout = TimeSpan.FromSeconds(12);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "SadPSX/0.0.1 (+https://github.com/kadu04t/SadPSX)");
        _catalogPath = catalogPath ?? GetDefaultCatalogPath();
    }

    public async Task<LibretroGameMetadata?> ResolveAsync(string serial)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        ObjectDisposedException.ThrowIf(_disposed, this);
        IReadOnlyDictionary<string, LibretroGameMetadata> catalog =
            await LoadCatalogAsync().ConfigureAwait(false);
        return catalog.TryGetValue(
            serial.ToUpperInvariant(),
            out LibretroGameMetadata? metadata)
            ? metadata
            : null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _httpClient.Dispose();
        _disposed = true;
    }

    internal static IReadOnlyDictionary<string, LibretroGameMetadata>
        ParseCatalog(string contents)
    {
        ArgumentNullException.ThrowIfNull(contents);
        var games = new Dictionary<string, LibretroGameMetadata>(
            StringComparer.OrdinalIgnoreCase);
        string? name = null;
        string? region = null;
        string? serial = null;

        using var reader = new StringReader(contents);
        while (reader.ReadLine() is { } line)
        {
            string trimmed = line.Trim();
            if (trimmed == "game (")
            {
                name = null;
                region = null;
                serial = null;
                continue;
            }

            if (trimmed == ")")
            {
                if (name is not null && serial is not null)
                {
                    games.TryAdd(
                        serial,
                        new LibretroGameMetadata(
                            name,
                            region ?? GameIdentityService.GetRegion(serial),
                            serial,
                            ParseDiscNumber(name),
                            ParseRevision(name)));
                }

                continue;
            }

            Match match = PropertyPattern().Match(trimmed);
            if (!match.Success)
                continue;

            string value = Regex.Unescape(match.Groups[2].Value);
            switch (match.Groups[1].Value)
            {
                case "name":
                    name = value;
                    break;
                case "region":
                    region = value;
                    break;
                case "serial":
                    serial = value.ToUpperInvariant();
                    break;
            }
        }

        return games;
    }

    private async Task<IReadOnlyDictionary<string, LibretroGameMetadata>>
        LoadCatalogAsync()
    {
        if (_catalog is not null)
            return _catalog;

        await _loadLock.WaitAsync().ConfigureAwait(false);
        try
        {
            try
            {
                if (_catalog is not null)
                    return _catalog;

                string contents;
                if (File.Exists(_catalogPath))
                {
                    contents = await File.ReadAllTextAsync(_catalogPath)
                        .ConfigureAwait(false);
                }
                else
                {
                    contents = await _httpClient.GetStringAsync(CatalogUrl)
                        .ConfigureAwait(false);
                    string? directory = Path.GetDirectoryName(_catalogPath);
                    if (!string.IsNullOrEmpty(directory))
                        Directory.CreateDirectory(directory);
                    string temporaryPath = _catalogPath + ".download";
                    await File.WriteAllTextAsync(temporaryPath, contents)
                        .ConfigureAwait(false);
                    File.Move(temporaryPath, _catalogPath, overwrite: true);
                }

                _catalog = ParseCatalog(contents);
            }
            catch (HttpRequestException)
            {
                _catalog = new Dictionary<string, LibretroGameMetadata>();
            }
            catch (TaskCanceledException)
            {
                _catalog = new Dictionary<string, LibretroGameMetadata>();
            }
            catch (IOException)
            {
                _catalog = new Dictionary<string, LibretroGameMetadata>();
            }
            catch (UnauthorizedAccessException)
            {
                _catalog = new Dictionary<string, LibretroGameMetadata>();
            }

            return _catalog;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private static int? ParseDiscNumber(string name)
    {
        Match match = DiscPattern().Match(name);
        return match.Success && int.TryParse(match.Groups[1].Value, out int disc)
            ? disc
            : null;
    }

    private static string? ParseRevision(string name)
    {
        Match match = RevisionPattern().Match(name);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string GetDefaultCatalogPath()
    {
        string root = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(
            root,
            "SadPSX",
            "Cache",
            "Metadata",
            "Sony - PlayStation.dat");
    }

    [GeneratedRegex(
        "^(name|region|serial)\\s+\"((?:\\\\.|[^\"])*)\"$",
        RegexOptions.CultureInvariant)]
    private static partial Regex PropertyPattern();

    [GeneratedRegex(
        @"(?i)\(Disc\s+(\d+)\)",
        RegexOptions.CultureInvariant)]
    private static partial Regex DiscPattern();

    [GeneratedRegex(
        @"(?i)\(Rev\s+([^)]+)\)",
        RegexOptions.CultureInvariant)]
    private static partial Regex RevisionPattern();
}

internal sealed record LibretroGameMetadata(
    string Name,
    string Region,
    string Serial,
    int? DiscNumber,
    string? Revision);
