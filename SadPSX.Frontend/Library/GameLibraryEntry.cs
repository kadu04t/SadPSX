using System.Text.Json.Serialization;

namespace SadPSX.Frontend.Library;

internal sealed record GameLibraryEntry(
    string DisplayName,
    string DiscPath,
    string? Serial = null,
    string Region = "Unknown",
    int? DiscNumber = null,
    string? ExecutablePath = null,
    string? CatalogName = null,
    string? Revision = null)
{
    [JsonIgnore]
    public string Title => CatalogName ?? DisplayName;
}
