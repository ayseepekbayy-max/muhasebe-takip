using MuhasebeTakip2.App.Data;
namespace MuhasebeTakip2.App.Services;

public sealed class PrivateMediaCleanupService(IServiceScopeFactory scopes, ILogger<PrivateMediaCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do {
            try {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<PrivateMediaStore>().CleanupAsync(token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch { logger.LogWarning("Özel medya temizliği tamamlanamadı; yeniden denenecek."); }
        } while (await timer.WaitForNextTickAsync(token));
    }
    public static Task CleanupAsync(AppDbContext db, PrivateMediaStore media, ILogger logger, CancellationToken token) => media.CleanupAsync(token);
}
