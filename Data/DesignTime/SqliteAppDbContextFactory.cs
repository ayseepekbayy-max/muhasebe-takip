using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MuhasebeTakip2.App.Data.DesignTime;

public sealed class SqliteAppDbContextFactory : IDesignTimeDbContextFactory<SqliteAppDbContext>
{
    public SqliteAppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SqliteAppDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        return new SqliteAppDbContext(options);
    }
}
