using System.Diagnostics;
using System.Net;
using System.IO.Compression;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Services;

if(!OperatingSystem.IsLinux())throw new PlatformNotSupportedException();
var operation=Guid.Parse("11111111-1111-1111-1111-111111111111");
if(args is ["prepare"])
{
    foreach(var mb in new[]{16,100})
    {
        using var package=new MemoryStream(mb*1024*1024);
        using(var zip=new ZipArchive(package,ZipArchiveMode.Create,leaveOpen:true))
        {
            using(var manifest=zip.CreateEntry("manifest.json",CompressionLevel.NoCompression).Open())manifest.Write("{}"u8);
            var remaining=mb*1024*1024-4096;
            var buffer=new byte[65536];
            for(var i=0;remaining>0;i++)
            {
                using var entry=zip.CreateEntry("files/payload-"+i+".pdf",CompressionLevel.NoCompression).Open();
                var amount=Math.Min(remaining,20*1024*1024);
                remaining-=amount;
                while(amount>0){var chunk=Math.Min(amount,buffer.Length);entry.Write(buffer,0,chunk);amount-=chunk;}
            }
        }
        var data=package.GetBuffer(); // Zero padding keeps exact envelope boundary sizes; all content is synthetic.
        using var archive=new CompanyRecoveryArchive("/fixtures/"+mb,new byte[32],"synthetic-v1");
        archive.SaveAccountingPackage(71,operation,data);
    }
    Console.WriteLine("PASS synthetic large encrypted fixtures prepared");return;
}
Directory.CreateDirectory("/work/form-temp",(UnixFileMode)448);
File.SetUnixFileMode("/work/form-temp",(UnixFileMode)320);
Environment.SetEnvironmentVariable("ASPNETCORE_TEMP","/work/form-temp");
var b=WebApplication.CreateBuilder();b.Logging.ClearProviders();b.WebHost.UseUrls("http://127.0.0.1:0");
await using var app=b.Build();app.UseAccountingRecoveryAdmission();
app.MapGet("/health",()=>Results.Ok());
app.MapPost("/Araclar/Yedekleme",async (HttpContext http)=>
{
    if(http.Request.Query["handler"]=="Indir")return Results.Ok();
    http.Features.Set<IFormFeature>(new FormFeature(http.Request,new FormOptions{
        MemoryBufferThreshold=(int)CompanyRecoveryArchive.MaxAccountingEnvelopeBytes,
        MultipartBodyLengthLimit=CompanyRecoveryArchive.MaxAccountingEnvelopeBytes}));
    try
    {
        var form=await http.Request.ReadFormAsync(http.RequestAborted);
        var file=form.Files["recoveryFile"] ?? throw new InvalidDataException();
        using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite("Data Source=:memory:").Options);
        using var archive=new CompanyRecoveryArchive("/work/never-created",new byte[32],"synthetic-v1");
        await using var input=file.OpenReadStream();
        await new CompanyAccountingRecovery(new AccountingBackupService(db,app.Environment,true),archive)
            .RestoreUploadedAsync(71,operation,input,file.Length,http.RequestAborted);
        return Results.StatusCode(500); // fixture is authenticated but intentionally not ZIP; must never reach DB.
    }
    catch(InvalidDataException){return Results.UnprocessableEntity();}
    catch(AccountingBackupService.BackupException){return Results.UnprocessableEntity();}
    catch(OperationCanceledException){return Results.StatusCode(499);}
});
await app.StartAsync();
var address=app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
using var client=new HttpClient{BaseAddress=new Uri(address),Timeout=TimeSpan.FromSeconds(90)};
long peak=0;
using var sampling=new CancellationTokenSource();
var sampler=Task.Run(async ()=>{try{while(true){using var p=Process.GetCurrentProcess();peak=Math.Max(peak,p.WorkingSet64);await Task.Delay(10,sampling.Token);}}catch(OperationCanceledException){}});
void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);}
HttpRequestMessage Request(int mb,HttpContent? overrideFile=null)
{
    var path=Directory.GetFiles("/fixtures/"+mb,"*.firmova-recovery").Single();
    var form=new MultipartFormDataContent();
    form.Add(overrideFile??new StreamContent(File.OpenRead(path)),"recoveryFile","synthetic.firmova-recovery");
    var request=new HttpRequestMessage(HttpMethod.Post,"/Araclar/Yedekleme?handler=SifreliDosyaGeriYukle"){Content=form};
    request.Headers.ExpectContinue=true;return request;
}
var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
using var cancel=new CancellationTokenSource();
using var held=Request(16,new HeldContent(16*1024*1024+28,entered));
var heldTask=client.SendAsync(held,HttpCompletionOption.ResponseHeadersRead,cancel.Token);
await entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
var rejected=await Task.WhenAll(Enumerable.Range(0,4).Select(async _=>{using var req=Request(100);using var r=await client.SendAsync(req,HttpCompletionOption.ResponseHeadersRead);return r.StatusCode;}));
Check(rejected.All(x=>(int)x==429),"four concurrent 100 MiB requests rejected before body buffering");
var watch=Stopwatch.StartNew();using(var health=await client.GetAsync("/health"))Check(health.IsSuccessStatusCode && watch.Elapsed<TimeSpan.FromSeconds(2),"ordinary health request unaffected during slow upload");
cancel.Cancel();try{using var ignored=await heldTask;}catch(OperationCanceledException){}
await Task.Delay(300);
using(var req=Request(16))using(var r=await client.SendAsync(req))
    Check((int)r.StatusCode==422,"admission released after cancelled body; authenticated 16 MiB envelope reaches ZIP validation");
GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
var smallLimit=GC.GetGCMemoryInfo().TotalAvailableMemoryBytes<=600L*1024*1024;
using(var req=Request(100))using(var r=await client.SendAsync(req))
    Check((int)r.StatusCode==(smallLimit?503:422),smallLimit?"100 MiB rejected by memory budget on 512 MiB container":"100 MiB authenticated envelope processed with sufficient memory");
using(var req=Request(100,new DeclaredContent(AccountingRecoveryAdmission.MaxRequestBytes+1024)))using(var r=await client.SendAsync(req))
    Check((int)r.StatusCode==413,"oversized request rejected from headers");
Check(!Directory.EnumerateFileSystemEntries("/work/form-temp").Any(),"no temporary files after cancellation, contention, size and validation failures");
if(smallLimit)Check(!AccountingRecoveryAdmission.HasRestoreMemoryBudget(65536,AccountingBackupService.MaxManifestBytes),"compressed manifest expansion is covered by memory budget");
using(var req=Request(16))using(var r=await client.SendAsync(req))Check((int)r.StatusCode is 422 or 503,"subsequent upload is processed or safely refused under memory pressure");
using(var health=await client.GetAsync("/health"))Check(health.IsSuccessStatusCode,"ordinary requests remain healthy after load");
using(var legacy=await client.PostAsync("/Araclar/Yedekleme?handler=Indir",new ByteArrayContent([])))Check(legacy.IsSuccessStatusCode,"small existing backup actions are not blocked by upload memory reservation");
sampling.Cancel();await sampler;
Console.WriteLine("MEMORY_LIMIT_MIB|"+(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes/1024/1024));
Console.WriteLine("PROCESS_PEAK_RSS_MIB|"+(peak/1024/1024));
if(File.Exists("/sys/fs/cgroup/memory.peak"))Console.WriteLine("CONTAINER_PEAK_MIB|"+(long.Parse(File.ReadAllText("/sys/fs/cgroup/memory.peak"))/1024/1024));
if(File.Exists("/sys/fs/cgroup/memory.events"))
{
    var events=File.ReadAllLines("/sys/fs/cgroup/memory.events");
    Check(events.Where(x=>x.StartsWith("oom ")||x.StartsWith("oom_kill ")).All(x=>x.EndsWith(" 0")),"no cgroup OOM events");
}
await app.StopAsync();Console.WriteLine("PASS isolated recovery load checks");

sealed class HeldContent(long length,TaskCompletionSource entered):HttpContent
{
    protected override bool TryComputeLength(out long value){value=length;return true;}
    protected override Task SerializeToStreamAsync(Stream stream,TransportContext? context)=>SerializeToStreamAsync(stream,context,CancellationToken.None);
    protected override async Task SerializeToStreamAsync(Stream stream,TransportContext? context,CancellationToken ct)
    {await stream.WriteAsync(new byte[65536],ct);await stream.FlushAsync(ct);entered.TrySetResult();await Task.Delay(Timeout.Infinite,ct);}
}
sealed class DeclaredContent(long length):HttpContent
{
    protected override bool TryComputeLength(out long value){value=length;return true;}
    protected override Task SerializeToStreamAsync(Stream stream,TransportContext? context)=>throw new InvalidOperationException("Rejected request body must not be sent.");
}
