using System.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using Npgsql;

namespace MuhasebeTakip2.App.Services;

public static class InvoicePaymentTransaction
{
    public static Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> BeginAsync(AppDbContext db, CancellationToken ct)
        // SQLite starts a non-deferred writer transaction. PostgreSQL also protects
        // reads made before the invoice lock (such as the existing movement).
        => db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

    public static async Task<T> RunAsync<T>(AppDbContext db, Func<Task<T>> operation, CancellationToken ct = default)
    {
        await using var transaction = await BeginAsync(db, ct);
        var result = await operation();
        await transaction.CommitAsync(ct);
        return result;
    }

    public static async Task<IActionResult> RunPageAsync(AppDbContext db, PageModel page, Func<Task<IActionResult>> operation)
    {
        try
        {
            return await RunAsync(db, operation, page.HttpContext.RequestAborted);
        }
        catch (Exception ex) when (IsConflict(ex))
        {
            page.TempData?.Remove("Basari");
            return new ContentResult
            {
                StatusCode = StatusCodes.Status409Conflict,
                Content = "Kayıt başka bir işlemle değişti veya kullanımda. Sayfayı yenileyip tekrar deneyin.",
                ContentType = "text/plain; charset=utf-8"
            };
        }
    }

    public static async Task LockInvoiceAsync(AppDbContext db, int companyId, int invoiceId, CancellationToken ct = default)
    {
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""SELECT 1 FROM "Faturalar" WHERE "FirmaId" = {companyId} AND "Id" = {invoiceId} FOR UPDATE""", ct);
    }

    public static bool IsConflict(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
            if (current is DbUpdateConcurrencyException ||
                current is PostgresException { SqlState: "40001" or "40P01" or "55P03" } ||
                current is SqliteException { SqliteErrorCode: 5 or 6 })
                return true;
        return false;
    }
}
