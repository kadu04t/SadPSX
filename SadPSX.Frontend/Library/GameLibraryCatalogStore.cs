using System.Text.Json;

namespace SadPSX.Frontend.Library;

internal sealed class GameLibraryCatalogStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _path;

    public GameLibraryCatalogStore(string? path = null)
    {
        _path = path ?? GetDefaultPath();
    }

    public IReadOnlyList<GameLibraryEntry> Load()
    {
        if (!File.Exists(_path))
            return [];

        try
        {
            string json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<List<GameLibraryEntry>>(
                       json,
                       SerializerOptions) ?? [];
        }
        catch (IOException)
        {
            return [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public void Save(IReadOnlyList<GameLibraryEntry> games)
    {
        ArgumentNullException.ThrowIfNull(games);
        string? directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(
            _path,
            JsonSerializer.Serialize(games, SerializerOptions));
    }

    public static IReadOnlyList<GameLibraryEntry> Merge(
        IReadOnlyList<GameLibraryEntry> scanned,
        IReadOnlyList<GameLibraryEntry> cached)
    {
        var cachedByPath = cached.ToDictionary(
            game => Path.GetFullPath(game.DiscPath),
            StringComparer.OrdinalIgnoreCase);
        return scanned.Select(game =>
        {
            if (!cachedByPath.TryGetValue(
                    Path.GetFullPath(game.DiscPath),
                    out GameLibraryEntry? previous))
            {
                return game;
            }

            return game with
            {
                Serial = game.Serial ?? previous.Serial,
                Region = game.Region == "Unknown"
                    ? previous.Region
                    : game.Region,
                DiscNumber = game.DiscNumber ?? previous.DiscNumber,
                ExecutablePath = game.ExecutablePath ?? previous.ExecutablePath,
                CatalogName = previous.CatalogName,
                Revision = previous.Revision,
            };
        }).ToArray();
    }

    private static string GetDefaultPath()
    {
        string root = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(root, "SadPSX", "library.json");
    }
}
