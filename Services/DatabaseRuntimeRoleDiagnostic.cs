using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;

namespace MuhasebeTakip2.App.Services;

internal sealed class DatabaseRuntimeRoleDiagnostic(
    IServiceScopeFactory scopes,
    ILogger<DatabaseRuntimeRoleDiagnostic> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var token = timeout.Token;

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var connection = db.Database.GetDbConnection();
            await connection.OpenAsync(token);
            await using var command = connection.CreateCommand();
            command.CommandTimeout = 5;
            command.CommandText = "SELECT session_user = 'firmova_app_runtime' AND current_user = 'firmova_app_runtime'";

            if (await command.ExecuteScalarAsync(token) is true)
                logger.LogInformation("DB_RUNTIME_ROLE_MATCH");
            else
                logger.LogWarning("DB_RUNTIME_ROLE_MISMATCH");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            logger.LogWarning("DB_RUNTIME_ROLE_CHECK_FAILED");
        }
    }
}
