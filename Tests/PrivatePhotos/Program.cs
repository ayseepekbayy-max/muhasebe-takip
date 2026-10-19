using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
using MuhasebeTakip2.App.Pages;
using MuhasebeTakip2.App.Services;

if (args.Length != 0) throw new ArgumentException("Unknown test option.");

var root = Path.Combine(Path.GetTempPath(), "private-photo-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={Path.Combine(root, "test.db")}").Options;
var store = new PrivateMediaStore(new TestEnvironment(root));
var access = new PrivateAccessService(new ConfigurationBuilder().Build(), new PrivateLoginAttemptLimiter(TimeProvider.System));
int assertions = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); assertions++; }
PanelModel Panel(AppDbContext db, string? person = "2") {
    var http = new DefaultHttpContext { Session = new TestSession() };
    if (person != null) { http.Session.SetString("PrivateMode", "1"); http.Session.SetString("PrivatePerson", person); http.Session.SetString("PrivatePersonName", "Test"); }
    return new PanelModel(access, db, store) { PageContext = new PageContext { HttpContext = http } };
}
byte[] jpeg = Enumerable.Repeat((byte)0, 40).ToArray();
jpeg[0]=255; jpeg[1]=216; jpeg[2]=255; jpeg[^2]=255; jpeg[^1]=217;
IFormFile Upload() => new FormFile(new MemoryStream(jpeg), 0, jpeg.Length, "file", "photo.jpg");
async Task<PrivateMessage> Photo(AppDbContext db, int? hours, DateTime? created = null) {
    var m = new PrivateMessage { SenderPerson=1, SenderName="Test", Kind=PrivateMessageKind.ViewOncePhoto, PhotoViewHours=hours, CreatedAtUtc=created ?? DateTime.UtcNow, MediaKey=await store.SaveAsync(jpeg, default), MediaContentType="image/jpeg" };
    db.Add(m); await db.SaveChangesAsync(); db.ChangeTracker.Clear(); return m;
}
try {
    await using var db = new AppDbContext(options);
    await db.Database.EnsureCreatedAsync();
    // Validate production provider SQL and migration metadata without a live database.
    await using (var pg = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=localhost;Database=design;Username=design").Options)) {
        Check(!pg.Database.HasPendingModelChanges(), "PostgreSQL migration snapshot matches model");
        var migrator = Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>(pg);
        var script = migrator.GenerateScript("20260923135801_AddPrivateMessageReplies", "20261016090000_AddTimedPrivatePhotos");
        Check(script.Contains("ADD \"PhotoViewHours\"") && script.Contains("ADD \"PhotoAccessExpiresAtUtc\""), "Additive production migration SQL");
        var sql = pg.PrivateMessages.Select(m => m.PhotoAccessExpiresAtUtc ?? DateTime.UtcNow.AddHours(m.PhotoViewHours!.Value)).ToQueryString();
        Check(sql.Contains("interval"), "PostgreSQL deadline expression translation");
    }
    foreach (int? hours in new int?[] { null, 1, 3, 5 }) {
        var sender = Panel(db, "1");
        Check(await sender.OnPostMediaAsync(Upload(), "photo", null, null, hours) is JsonResult, "Photo upload");
        var m = await db.PrivateMessages.AsNoTracking().OrderByDescending(m=>m.Id).FirstAsync();
        Check(m.PhotoViewHours == hours && m.ViewedAtUtc == null && m.PhotoAccessExpiresAtUtc == null, "No timer on upload");
        Check(await sender.OnPostPhotoAsync(m.Id) is ObjectResult { StatusCode:410 }, "Sender denied");
        Check(await Panel(db, null).OnPostPhotoAsync(m.Id) is UnauthorizedResult, "Session required");
        var receiver = Panel(db);
        Check(await receiver.OnPostPhotoAsync(m.Id) is FileContentResult, "First open");
        var opened = await db.PrivateMessages.AsNoTracking().SingleAsync(x=>x.Id==m.Id);
        Check(opened.ViewedAtUtc != null, "First view persisted");
        if (hours == null) {
            Check(opened.MediaKey == null, "View once removes source");
            Check(await receiver.OnPostPhotoAsync(m.Id) is ObjectResult { StatusCode:410 }, "View once replay denied");
        } else {
            Check(Math.Abs((opened.PhotoAccessExpiresAtUtc!.Value-opened.ViewedAtUtc!.Value).TotalHours-hours.Value)<0.00001, "Exact selected duration");
            Check(await receiver.OnPostPhotoAsync(m.Id) is FileContentResult, "Timed replay");
            var replay = await db.PrivateMessages.AsNoTracking().SingleAsync(x=>x.Id==m.Id);
            Check(replay.ViewedAtUtc==opened.ViewedAtUtc && replay.PhotoAccessExpiresAtUtc==opened.PhotoAccessExpiresAtUtc, "Replay does not extend deadline");
            Check(double.Parse(receiver.Response.Headers["X-Photo-Remaining-Ms"]!, System.Globalization.CultureInfo.InvariantCulture)>0, "Server remaining time");
            await db.PrivateMessages.Where(x=>x.Id==m.Id).ExecuteUpdateAsync(u=>u.SetProperty(x=>x.PhotoAccessExpiresAtUtc, DateTime.UtcNow.AddSeconds(-1)));
            Check(await receiver.OnPostPhotoAsync(m.Id) is ObjectResult { StatusCode:410 }, "Expiry denied");
        }
    }
    foreach (int? hours in new int?[]{null,1,3,5}) {
        var old = await Photo(db,hours,DateTime.UtcNow.AddHours(-25));
        Check(await Panel(db).OnPostPhotoAsync(old.Id) is ObjectResult { StatusCode:410 }, "Unopened 24 hour expiry");
    }
    Check(await Panel(db,"1").OnPostMediaAsync(Upload(),"photo",null,null,2) is BadRequestObjectResult,"Invalid duration rejected");
    var activeOld=await Photo(db,5);
    Check(await Panel(db).OnPostPhotoAsync(activeOld.Id) is FileContentResult,"Open timed before aging");
    await db.PrivateMessages.Where(x=>x.Id==activeOld.Id).ExecuteUpdateAsync(u=>u.SetProperty(x=>x.CreatedAtUtc,DateTime.UtcNow.AddHours(-25)));
    Check(await Panel(db).OnPostPhotoAsync(activeOld.Id) is FileContentResult,"Active timer survives upload age of 24 hours");
    var hidden=await Photo(db,1);
    db.PrivateMessageHiddens.Add(new PrivateMessageHidden { PersonNumber=2, PrivateMessageId=hidden.Id, HiddenAtUtc=DateTime.UtcNow }); await db.SaveChangesAsync();
    Check(await Panel(db).OnPostPhotoAsync(hidden.Id) is ObjectResult { StatusCode:410 },"Hidden denied");
    var senderPanel=Panel(db,"1");
    Check(await senderPanel.OnPostSendAsync("Text still works",null) is JsonResult,"Text send");
    var text=await db.PrivateMessages.AsNoTracking().OrderByDescending(x=>x.Id).FirstAsync();
    Check(await Panel(db).OnPostSendAsync("Reply still works",text.Id) is JsonResult,"Reply send");
    Check(await db.PrivateMessages.AnyAsync(x=>x.ReplyToMessageId==text.Id && x.IsReply),"Reply relationship");
    Check(await Panel(db).OnGetMessagesAsync() is JsonResult,"Message polling");
    var audio=new PrivateMessage { SenderPerson=1,SenderName="Test",Kind=PrivateMessageKind.Audio,CreatedAtUtc=DateTime.UtcNow,MediaKey=await store.SaveAsync(new byte[]{1,2,3},default),MediaContentType="audio/webm",DurationSeconds=1 };
    db.Add(audio);await db.SaveChangesAsync();
    Check(await Panel(db).OnGetAudioAsync(audio.Id) is FileContentResult,"Audio access");
    Check(await Panel(db,null).OnGetAudioAsync(audio.Id) is UnauthorizedResult,"Audio session protection");
    var concurrent=await Photo(db,3);
    async Task<IActionResult> OpenParallel() { await using var other=new AppDbContext(options);return await Panel(other).OnPostPhotoAsync(concurrent.Id); }
    var results=await Task.WhenAll(Task.Run(OpenParallel),Task.Run(OpenParallel));
    Check(results.All(x=>x is FileContentResult),"Concurrent timed opens");
    var concurrentOnce=await Photo(db,null);
    results=await Task.WhenAll(Task.Run(async()=>{await using var other=new AppDbContext(options);return await Panel(other).OnPostPhotoAsync(concurrentOnce.Id);}),Task.Run(async()=>{await using var other=new AppDbContext(options);return await Panel(other).OnPostPhotoAsync(concurrentOnce.Id);}));
    Check(results.Count(x=>x is FileContentResult)==1,"Concurrent once only one winner");
    await PrivateMediaCleanupService.CleanupAsync(db,store,NullLogger.Instance,default);
    Check(!await db.PrivateMessages.AnyAsync(x=>x.PhotoAccessExpiresAtUtc<DateTime.UtcNow && x.MediaKey!=null),"Timed cleanup");
    Check(!await db.PrivateMessages.AnyAsync(x=>x.Kind==PrivateMessageKind.ViewOncePhoto && x.ViewedAtUtc==null && x.CreatedAtUtc<DateTime.UtcNow.AddHours(-24) && x.MediaKey!=null),"Unopened cleanup");
    Console.WriteLine($"PASS: {assertions} assertions (SQLite; isolated temporary database).");
} finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(root,true); }

sealed class TestSession : ISession {
    readonly Dictionary<string,byte[]> values=new();
    public bool IsAvailable=>true; public string Id=>"test"; public IEnumerable<string> Keys=>values.Keys;
    public void Clear()=>values.Clear(); public Task CommitAsync(CancellationToken t=default)=>Task.CompletedTask;
    public Task LoadAsync(CancellationToken t=default)=>Task.CompletedTask;
    public void Remove(string key)=>values.Remove(key);public void Set(string key,byte[] value)=>values[key]=value;
    public bool TryGetValue(string key,[System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out byte[]? value)=>values.TryGetValue(key,out value);
}
sealed class TestEnvironment(string root) : IWebHostEnvironment {
    public string ApplicationName{get;set;}="Tests";public string EnvironmentName{get;set;}="Development";
    public string ContentRootPath{get;set;}=root;public string WebRootPath{get;set;}=Path.Combine(root,"wwwroot");
    public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();public IFileProvider WebRootFileProvider{get;set;}=new NullFileProvider();
}
