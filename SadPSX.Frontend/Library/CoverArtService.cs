using System.Security.Cryptography;
using System.Text;

namespace SadPSX.Frontend.Library;

internal sealed class CoverArtService : IDisposable
{
    private const string BoxArtRoot =
        "https://thumbnails.libretro.com/Sony%20-%20PlayStation/Named_Boxarts/";

    private readonly HttpClient _httpClient;
    private readonly LibretroGameMetadataService _metadataService;
    private readonly string _cacheDirectory;
    private readonly Dictionary<string, Task> _downloads =
        new(StringComparer.OrdinalIgnoreCase);

    public CoverArtService(HttpMessageHandler? handler = null, string? cache = null)
    {
        _httpClient = handler is null
            ? new HttpClient()
            : new HttpClient(handler);
        _httpClient.Timeout = TimeSpan.FromSeconds(8);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "SadPSX/0.0.1 (+https://github.com/kadu04t/SadPSX)");
        _metadataService = new LibretroGameMetadataService();
        _cacheDirectory = cache ?? GetDefaultCacheDirectory();
    }

    public string? GetCachedPath(GameLibraryEntry game)
    {
        string path = GetCachePath(game);
        return File.Exists(path) ? path : null;
    }

    public void Request(GameLibraryEntry game)
    {
        string cachePath = GetCachePath(game);
        if (File.Exists(cachePath) || _downloads.ContainsKey(cachePath))
            return;

        _downloads.Add(cachePath, DownloadAsync(game, cachePath));
    }

    public void Dispose()
    {
        _metadataService.Dispose();
        _httpClient.Dispose();
    }

    public Task<LibretroGameMetadata?> ResolveMetadataAsync(string serial) =>
        _metadataService.ResolveAsync(serial);

    internal static IReadOnlyList<string> GetCandidateNames(string displayName)
    {
        string normalized = NormalizeRomanNumerals(displayName.Trim());
        if (normalized.Length == 0)
            return [];

        var names = new List<string> { normalized };
        if (!normalized.Contains('('))
        {
            names.Add($"{normalized} (USA)");
            names.Add($"{normalized} (Europe)");
        }

        return names;
    }

    private async Task DownloadAsync(GameLibraryEntry game, string cachePath)
    {
        try
        {
            var candidates = new List<string>();
            if (game.Serial is not null)
            {
                LibretroGameMetadata? metadata =
                    await _metadataService.ResolveAsync(game.Serial)
                        .ConfigureAwait(false);
                if (metadata is not null)
                    candidates.Add(metadata.Name);
            }

            candidates.AddRange(GetCandidateNames(game.Title));
            foreach (string candidate in candidates.Distinct(
                         StringComparer.OrdinalIgnoreCase))
            {
                string url = BoxArtRoot + Uri.EscapeDataString(candidate) + ".png";
                using HttpResponseMessage response = await _httpClient
                    .GetAsync(url, HttpCompletionOption.ResponseHeadersRead)
                    .ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    continue;

                byte[] data = await response.Content
                    .ReadAsByteArrayAsync()
                    .ConfigureAwait(false);
                if (data.Length == 0)
                    continue;

                Directory.CreateDirectory(_cacheDirectory);
                string temporaryPath = cachePath + ".download";
                await File.WriteAllBytesAsync(temporaryPath, data)
                    .ConfigureAwait(false);
                File.Move(temporaryPath, cachePath, overwrite: true);
                return;
            }
        }
        catch (HttpRequestException)
        {
        }
        catch (TaskCanceledException)
        {
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private string GetCachePath(GameLibraryEntry game)
    {
        byte[] digest = SHA256.HashData(
            Encoding.UTF8.GetBytes(
                game.Serial ?? game.DiscPath.ToUpperInvariant()));
        string key = Convert.ToHexString(digest.AsSpan(0, 10));
        return Path.Combine(_cacheDirectory, $"{key}.png");
    }

    private static string NormalizeRomanNumerals(string title)
    {
        string[] numerals = ["Viii", "Vii", "Iii", "Ii", "Iv", "Vi", "Ix"];
        foreach (string numeral in numerals)
        {
            title = title.Replace(
                numeral,
                numeral.ToUpperInvariant(),
                StringComparison.Ordinal);
        }

        title = title
            .Replace("(Usa)", "(USA)", StringComparison.Ordinal)
            .Replace("(Uk)", "(UK)", StringComparison.Ordinal)
            .Replace("(Br)", "(Brazil)", StringComparison.Ordinal);

        return title;
    }

    private static string GetDefaultCacheDirectory()
    {
        string root = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(root, "SadPSX", "Cache", "Covers");
    }
}
