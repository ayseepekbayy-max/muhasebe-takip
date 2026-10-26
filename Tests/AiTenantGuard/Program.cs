using System.Reflection;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;

// Inspect the real compiled top-level helper without invoking application startup.
// No database connection, migration or production configuration is used.
var assembly = typeof(AppDbContext).Assembly;
var entry = assembly.EntryPoint!.DeclaringType!;
var helper = entry.GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
    .Single(m => m.Name.Contains("g__GetAiFirmaIdAsync|", StringComparison.Ordinal));
using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
    .UseNpgsql("Host=localhost;Database=unused_offline_tenant_test;Username=unused").Options);
int checks = 0;
foreach (int? invalid in new int?[] {null, 0, -1}) {
    db.AuthenticatedAiFirmaId = invalid;
    foreach (int? request in new int?[] {null, 999}) {
        bool denied = false;
        try { await (Task<int?>)helper.Invoke(null, new object?[]{db, request})!; }
        catch(TargetInvocationException ex) when(ex.InnerException is UnauthorizedAccessException) { denied = true; }
        catch(UnauthorizedAccessException) { denied = true; }
        if(!denied) throw new Exception("Unauthenticated tenant accepted.");
        checks++;
    }
}
db.AuthenticatedAiFirmaId = 42;
foreach(int? request in new int?[]{null, 42, 999}) {
    if(await (Task<int?>)helper.Invoke(null,new object?[]{db,request})! != 42)
        throw new Exception("Request overrode authenticated tenant.");
    checks++;
}
Console.WriteLine($"PASS: {checks} compiled AI tenant guard checks; no network or database connection.");
