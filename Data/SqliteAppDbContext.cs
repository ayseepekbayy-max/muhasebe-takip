using Microsoft.EntityFrameworkCore;

namespace MuhasebeTakip2.App.Data;

/// <summary>
/// SQLite'a ait migration zincirini ortak uygulama modelinden ayıran ince context türü.
/// Entity modeli ve OnModelCreating davranışı AppDbContext'ten devralınır.
/// </summary>
public sealed class SqliteAppDbContext : AppDbContext
{
    public SqliteAppDbContext(DbContextOptions<SqliteAppDbContext> options) : base(options)
    {
    }
}
