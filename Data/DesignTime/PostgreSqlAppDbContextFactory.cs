using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MuhasebeTakip2.App.Data.DesignTime;

public sealed class PostgreSqlAppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default");
        if (string.IsNullOrWhiteSpace(connectionString) ||
            !connectionString.Contains("Host=", StringComparison.OrdinalIgnoreCase) ||
            !connectionString.Contains("Database=", StringComparison.OrdinalIgnoreCase))
        {
            connectionString = "Host=localhost;Database=firmova_design_time;Username=postgres";
        }

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new AppDbContext(options);
    }
}
