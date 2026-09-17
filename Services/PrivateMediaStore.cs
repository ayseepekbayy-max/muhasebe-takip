namespace MuhasebeTakip2.App.Services;

// Ephemeral application storage: no external volume/configuration is required.
// No client filenames or paths are used. This directory is never served by static files.
public sealed class PrivateMediaStore
{
    public const int PhotoLimit = 8 * 1024 * 1024;
    public const int AudioLimit = 16 * 1024 * 1024;
    private readonly string root;
    public PrivateMediaStore(IWebHostEnvironment environment)
    {
        root = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "App_Data", "PrivateMedia"));
        var web = Path.GetFullPath(environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot"));
        if (root.Equals(web, StringComparison.OrdinalIgnoreCase) || root.StartsWith(web + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Özel medya wwwroot altında saklanamaz.");
    }
    private string Resolve(string key)
    {
        if (!Guid.TryParseExact(key, "N", out _)) throw new InvalidOperationException("Geçersiz medya anahtarı.");
        return Path.Combine(root, key);
    }
    public async Task<string> SaveAsync(byte[] bytes, CancellationToken token)
    {
        Directory.CreateDirectory(root);
        var key = Guid.NewGuid().ToString("N");
        try { await File.WriteAllBytesAsync(Resolve(key), bytes, token); }
        catch { Delete(key); throw; }
        return key;
    }
    public Task<byte[]> ReadAsync(string key, CancellationToken token) => File.ReadAllBytesAsync(Resolve(key), token);
    public void Delete(string? key)
    {
        if (key is null) return;
        try { File.Delete(Resolve(key)); }
        catch (DirectoryNotFoundException) { /* The ephemeral directory was already lost on restart/deploy. */ }
    }

    public static string? Detect(byte[] bytes, bool photo)
    {
        var b = bytes.AsSpan();
        if (photo)
        {
            if (b.Length >= 33 && b[..8].SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}) &&
                b.Slice(12,4).SequenceEqual("IHDR"u8) && b[^8..^4].SequenceEqual("IEND"u8)) return "image/png";
            if (b.Length >= 32 && b[0] == 255 && b[1] == 216 && b[2] == 255 && b[^2] == 255 && b[^1] == 217) return "image/jpeg";
        }
        else
        {
            if (b.Length > 32 && b[..4].SequenceEqual(new byte[] {26,69,223,163}) &&
                b[..Math.Min(b.Length,4096)].IndexOf("webm"u8) >= 0 && b.IndexOf("OpusHead"u8) >= 0) return "audio/webm";
            if (b.Length > 32 && b[..4].SequenceEqual("OggS"u8) && b[..Math.Min(b.Length,4096)].IndexOf("OpusHead"u8) >= 0) return "audio/ogg";
            if (b.Length > 32 && b.Slice(4,4).SequenceEqual("ftyp"u8) &&
                (b.Slice(8,4).SequenceEqual("M4A "u8) || b.Slice(8,4).SequenceEqual("mp42"u8) || b.Slice(8,4).SequenceEqual("isom"u8) || b.Slice(8,4).SequenceEqual("iso5"u8) || b.Slice(8,4).SequenceEqual("iso6"u8)) &&
                b.IndexOf("soun"u8) >= 0 && b.IndexOf("vide"u8) < 0) return "audio/mp4";
        }
        return null;
    }
}
