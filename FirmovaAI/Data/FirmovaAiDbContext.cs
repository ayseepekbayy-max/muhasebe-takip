using FirmovaAI.Models;
using Microsoft.EntityFrameworkCore;

namespace FirmovaAI.Data
{
    public class FirmovaAiDbContext : DbContext
    {
        public FirmovaAiDbContext(DbContextOptions<FirmovaAiDbContext> options)
            : base(options)
        {
        }

        public DbSet<ChatLog> ChatLogs => Set<ChatLog>();
    }
}