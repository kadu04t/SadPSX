namespace SadPSX.Frontend.UI.Rendering;

internal sealed class SdlTextureCache(nint renderer) : IDisposable
{
    private readonly Dictionary<string, SdlTexture> _textures =
        new(StringComparer.OrdinalIgnoreCase);

    public SdlTexture Get(string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (_textures.TryGetValue(fullPath, out SdlTexture? texture))
            return texture;

        texture = new SdlTexture(renderer, fullPath);
        _textures.Add(fullPath, texture);
        return texture;
    }

    public void Dispose()
    {
        foreach (SdlTexture texture in _textures.Values)
            texture.Dispose();
        _textures.Clear();
    }
}
