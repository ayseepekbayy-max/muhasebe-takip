using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;

namespace MuhasebeTakip2.App.Services;

public static class CompanyDocumentRegistration
{
    public static IServiceCollection AddCompanyDocuments(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<CompanyDocumentStore>();
        if (configuration.GetValue<bool>("CompanyDocuments:CleanupEnabled"))
            services.AddHostedService<CompanyFileCleanupWorker>();
        return services;
    }
}

public sealed class CompanyFileCleanupWorker(IServiceScopeFactory scopes, ILogger<CompanyFileCleanupWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                if (!db.Database.IsNpgsql())
                    return;
                await DrainAsync(db, scope.ServiceProvider.GetRequiredService<CompanyDocumentStore>(), stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Company file cleanup will retry.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public static async Task DrainAsync(AppDbContext db, CompanyDocumentStore store, CancellationToken ct)
    {
        var pending = await db.Set<CompanyFileCleanupJob>().AsNoTracking()
            .Where(x => x.CompletedAtUtc == null).OrderBy(x => x.EnqueuedAtUtc)
            .Select(x => x.FileId).Take(32).ToListAsync(ct);
        if (pending.Count == 0)
            return;
        if (!CompanyDocumentStore.IsSafeDirectory(store.Objects))
            throw new DirectoryNotFoundException("Company document storage is unavailable.");
        CompanyDiskStorage.EnsurePrivateDirectory(store.Quarantine);
        var cleanup = new CompanyFileCleanup(store.Objects, store.Quarantine);
        foreach (var id in pending)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '5s'", ct);
            // A busy file must not hold up another company's independent pending file.
            if (!await store.TryLockObjectAsync(id, ct)) continue;
            var file = await CompanyDocumentStore.LockFileAsync(db, id, ct);
            var job = await db.Set<CompanyFileCleanupJob>().SingleAsync(x => x.FileId == id, ct);
            if (job.CompletedAtUtc is null && !store.HasPreviousObject(id))
            {
                var hasReferences = await db.Set<CompanyStoredFileReference>().AnyAsync(x => x.FileId == id, ct) ||
                    await store.HasUntrackedPathReferencesAsync(id, ct);
                var result = cleanup.Process(new(job.CompanyId, job.OperationId, id, file.Sha256),
                    new(file.OwnerCompanyId, hasReferences, file.RetiredAtUtc.HasValue), store.ReadObjectPath(id, requirePersistent: true));
                if (result == CompanyFileCleanup.Result.Complete)
                {
                    job.CompletedAtUtc = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);
                }
            }
            await tx.CommitAsync(ct);
        }
    }
}
