using System.Text.Json;
using System.Text.Json.Serialization;

namespace SadPSX.Frontend.Library;

internal sealed class GameActivityStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _path;

    public GameActivityStore(string? path = null)
    {
        _path = path ?? GetDefaultPath();
    }

    public GameActivityEntry Get(string discPath, string? serial = null)
    {
        string fullPath = Path.GetFullPath(discPath);
        return Load().FirstOrDefault(entry => Matches(
                   entry,
                   fullPath,
                   serial)) ??
               new GameActivityEntry(fullPath, serial);
    }

    public void BeginSession(
        string discPath,
        string? serial,
        DateTimeOffset startedAtUtc)
    {
        Update(
            discPath,
            serial,
            current => current with
            {
                LastPlayedUtc = startedAtUtc.ToUniversalTime(),
                Sessions = current.Sessions + 1,
            });
    }

    public void CompleteSession(
        string discPath,
        string? serial,
        TimeSpan playedTime)
    {
        if (playedTime <= TimeSpan.Zero)
            return;

        Update(
            discPath,
            serial,
            current => current with
            {
                TotalPlayedTicks = current.TotalPlayedTicks + playedTime.Ticks,
            });
    }

    internal IReadOnlyList<GameActivityEntry> Load()
    {
        if (!File.Exists(_path))
            return [];

        try
        {
            string json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<List<GameActivityEntry>>(
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

    private void Update(
        string discPath,
        string? serial,
        Func<GameActivityEntry, GameActivityEntry> update)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(discPath);
        ArgumentNullException.ThrowIfNull(update);
        string fullPath = Path.GetFullPath(discPath);
        List<GameActivityEntry> entries = Load().ToList();
        int index = entries.FindIndex(entry => Matches(
            entry,
            fullPath,
            serial));
        GameActivityEntry current = index >= 0
            ? entries[index]
            : new GameActivityEntry(fullPath, serial);
        GameActivityEntry updated = update(current) with
        {
            DiscPath = fullPath,
            Serial = serial ?? current.Serial,
        };
        if (index >= 0)
            entries[index] = updated;
        else
            entries.Add(updated);

        string? directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        string temporaryPath = _path + ".tmp";
        File.WriteAllText(
            temporaryPath,
            JsonSerializer.Serialize(entries, SerializerOptions));
        File.Move(temporaryPath, _path, overwrite: true);
    }

    private static bool Matches(
        GameActivityEntry entry,
        string fullPath,
        string? serial)
    {
        if (serial is not null && entry.Serial is not null)
        {
            return string.Equals(
                entry.Serial,
                serial,
                StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(
            entry.DiscPath,
            fullPath,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string GetDefaultPath()
    {
        string root = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(root, "SadPSX", "activity.json");
    }
}

internal sealed record GameActivityEntry(
    string DiscPath,
    string? Serial = null,
    DateTimeOffset? LastPlayedUtc = null,
    long TotalPlayedTicks = 0,
    int Sessions = 0)
{
    [JsonIgnore]
    public TimeSpan TotalPlayed => TimeSpan.FromTicks(TotalPlayedTicks);
}
