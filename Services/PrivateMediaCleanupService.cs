using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;

namespace MuhasebeTakip2.App.Services;

// Runs immediately after startup and retries while the application is running.
// Access checks enforce the exact 24-hour deadline independently of the sweep.
public sealed class PrivateMediaCleanupService(
    IServiceScopeFactory scopes, PrivateMediaStore media, ILogger<PrivateMediaCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await CleanupAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), media, logger, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                // A temporarily unavailable database must not stop the ERP application.
                logger.LogError(ex, "Özel sohbet fotoğraf temizliği tamamlanamadı; yeniden denenecek.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public static async Task CleanupAsync(AppDbContext db, PrivateMediaStore media,
        ILogger logger, CancellationToken token)
    {
        var now = DateTime.UtcNow;
        var cutoff = now.AddHours(-24);
        var lastId = 0;
        while (true)
        {
            // Bounded batches; failures are retried on the next sweep, not in a tight loop.
            var ids = await db.PrivateMessages.AsNoTracking()
                .Where(m => m.Id > lastId && m.Kind == PrivateMessageKind.ViewOncePhoto &&
                    m.ViewedAtUtc == null && m.CreatedAtUtc <= cutoff &&
                    (m.ExpiredAtUtc == null || m.MediaKey != null))
                .OrderBy(m => m.Id).Select(m => m.Id).Take(100).ToListAsync(token);
            if (ids.Count == 0) return;
            foreach (var id in ids)
            {
                token.ThrowIfCancellationRequested();
                lastId = id;
                try
                {
                    await using var transaction = await db.Database.BeginTransactionAsync(token);
                    // Competes on the same row lock as viewing: only one operation wins.
                    var changed = await db.PrivateMessages.Where(m => m.Id == id &&
                        m.Kind == PrivateMessageKind.ViewOncePhoto && m.ViewedAtUtc == null &&
                        m.CreatedAtUtc <= cutoff && (m.ExpiredAtUtc == null || m.MediaKey != null))
                        .ExecuteUpdateAsync(u => u.SetProperty(m => m.ExpiredAtUtc, m => m.ExpiredAtUtc ?? now), token);
                    if (changed == 0) continue;
                    var key = await db.PrivateMessages.Where(m => m.Id == id).Select(m => m.MediaKey).SingleAsync(token);
                    media.Delete(key); // Missing files after a deploy are already gone; File.Delete is idempotent.
                    await db.PrivateMessages.Where(m => m.Id == id).ExecuteUpdateAsync(u => u
                        .SetProperty(m => m.MediaKey, (string?)null)
                        .SetProperty(m => m.MediaContentType, (string?)null), token);
                    await transaction.CommitAsync(token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    // Rollback retains the key, so an I/O failure cannot orphan the file.
                    logger.LogWarning(ex, "Süresi dolan özel fotoğraf {MessageId} silinemedi; yeniden denenecek.", id);
                }
            }
        }
    }
}
