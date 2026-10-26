using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using MuhasebeTakip2.App.Data;
using Npgsql;

// Test-only. No external connection string or production configuration is accepted.
internal sealed class TemporaryPostgres : IAsyncDisposable
{
    readonly string name="firmova-pg-media-"+Guid.NewGuid().ToString("N");
    public string ConnectionString { get; private set; }="";
    bool created;
    public DbContextOptions<AppDbContext> Options => new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options;
    public static async Task<TemporaryPostgres> StartAsync()
    {
        var lab=new TemporaryPostgres();
        try {
            var password=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            await lab.Docker(["run","--pull=never","--rm","-d","--name",lab.name,"--label","firmova.synthetic.pgmedia=true",
                "--tmpfs","/var/lib/postgresql","-e","POSTGRES_PASSWORD","-e","POSTGRES_DB=firmova_pg_media_synthetic",
                "-p","127.0.0.1::5432","postgres:18"],password);lab.created=true;
            var binding=(await lab.Docker(["port",lab.name,"5432/tcp"])).Trim();
            var match=Regex.Match(binding,@"\A127\.0\.0\.1:(\d+)\z");if(!match.Success)throw new Exception("Synthetic DB must use loopback.");
            lab.ConnectionString=new NpgsqlConnectionStringBuilder { Host="127.0.0.1",Port=int.Parse(match.Groups[1].Value),Database="firmova_pg_media_synthetic",Username="postgres",Password=password,Timeout=2,CommandTimeout=30,IncludeErrorDetail=false,Timezone="UTC" }.ConnectionString;
            for(int i=0;i<60;i++) {
                try {await using var c=new NpgsqlConnection(lab.ConnectionString);await c.OpenAsync();return lab;}
                catch(NpgsqlException){await Task.Delay(500);}
            }
            throw new Exception("Synthetic PostgreSQL did not become ready.");
        }catch {await lab.DisposeAsync();throw;}
    }
    public async Task InitializeAsync(Func<AppDbContext,Task>? seedBefore=null,Action<bool,string>? check=null)
    {
        await using var db=new AppDbContext(Options);
        // Historical main migrations expect pre-existing tables; model-based fixture is explicit.
        const string baseline="20261020103737_AddCashBankAccounts";
        var assembly=db.GetService<IMigrationsAssembly>();
        var model=assembly.CreateMigration(assembly.Migrations[baseline],db.Database.ProviderName!).TargetModel;
        await using(var schema=new SchemaContext(new DbContextOptionsBuilder<SchemaContext>().UseNpgsql(ConnectionString).UseModel(model).Options))
            await schema.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
        var history=db.GetService<IHistoryRepository>();await db.Database.ExecuteSqlRawAsync(history.GetCreateScript());
        foreach(var id in assembly.Migrations.Keys.Where(x=>string.CompareOrdinal(x,baseline)<=0))
            await db.Database.ExecuteSqlRawAsync(history.GetInsertScript(new HistoryRow(id,"10.0.4")));
        var unknown=new[]{"19000101000001_SyntheticLegacyA","19000101000002_SyntheticLegacyB","19000101000003_SyntheticLegacyC"};
        foreach(var id in unknown)await db.Database.ExecuteSqlRawAsync(history.GetInsertScript(new HistoryRow(id,"synthetic-legacy")));
        if(seedBefore!=null)await seedBefore(db);
        var old=(await history.GetAppliedMigrationsAsync()).ToArray();
        await db.GetService<IMigrator>().MigrateAsync();
        var after=await history.GetAppliedMigrationsAsync();
        check?.Invoke(after.Count-old.Length==1,"Only new PostgreSQL-media migration applied");
        check?.Invoke(old.All(x=>after.Any(y=>x.MigrationId==y.MigrationId&&x.ProductVersion==y.ProductVersion)),"All historical and unknown synthetic migration records preserved");
        check?.Invoke(!db.Database.HasPendingModelChanges(),"PostgreSQL migration snapshot matches");
    }
    async Task<string> Docker(string[] args,string? password=null)
    {
        if(!Regex.IsMatch(name,@"\Afirmova-pg-media-[0-9a-f]{32}\z"))throw new Exception("Invalid test container identity.");
        var start=new ProcessStartInfo("docker") {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var arg in args)start.ArgumentList.Add(arg);if(password!=null)start.Environment["POSTGRES_PASSWORD"]=password;
        using var p=Process.Start(start)??throw new Exception("Docker unavailable.");var output=p.StandardOutput.ReadToEndAsync();var error=p.StandardError.ReadToEndAsync();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try {await p.WaitForExitAsync(timeout.Token);}catch(OperationCanceledException){p.Kill(true);throw new Exception("Synthetic Docker command timeout.");}
        await error;if(p.ExitCode!=0)throw new Exception("Synthetic Docker command failed; check local postgres:18 image and Docker.");return await output;
    }
    public async ValueTask DisposeAsync() {if(created){NpgsqlConnection.ClearAllPools();await Docker(["rm","-f",name]);created=false;}ConnectionString="";}
    sealed class SchemaContext(DbContextOptions<SchemaContext> options):DbContext(options);
}
