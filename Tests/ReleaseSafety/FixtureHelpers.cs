using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.FileProviders;
using MuhasebeTakip2.App.Services;
sealed class SyntheticEnvironment(string root) : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "ChequeDeletion";
    public string EnvironmentName { get; set; } = "Testing";
    public string ContentRootPath { get; set; } = root;
    public string WebRootPath { get; set; } = root;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
}
sealed class UnusedStatusService : ICekDurumService
{
    public Task<CekDurumSonucu> DurumDegistirAsync(int firmaId, int cekId, bool odendiMi, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Deletion must not invoke cheque status updates.");
}
sealed class SyntheticTempData : ITempDataProvider
{
    public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
    public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
}
sealed class SyntheticSession : ISession
{
    private readonly Dictionary<string, byte[]> values = new();
    public bool IsAvailable => true;
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public IEnumerable<string> Keys => values.Keys;
    public void Clear() => values.Clear();
    public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public void Remove(string key) => values.Remove(key);
    public void Set(string key, byte[] value) => values[key] = value;
    public bool TryGetValue(string key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out byte[]? value) => values.TryGetValue(key, out value);
}