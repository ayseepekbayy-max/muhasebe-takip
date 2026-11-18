using System.Net;
using System.Security.Cryptography;
using System.Xml.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MuhasebeTakip2.App.Services;

if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Use isolated Linux fixture.");
if (args is ["--verify-restart"])
{
    var storedRoot=File.ReadAllText("/work/restart-root");
    var services=new ServiceCollection();
    services.AddLogging();
    services.AddDataProtection().SetApplicationName(File.ReadAllText("/work/restart-discriminator"))
        .AddKeyManagementOptions(o=>{if(!OperatingSystem.IsLinux())throw new PlatformNotSupportedException();o.XmlRepository=new PrivateKeyRepository(Path.Combine(storedRoot,"wwwroot/uploads/.firmova-private/data-protection"));});
    using var provider=services.BuildServiceProvider();
    var plaintext=provider.GetRequiredService<IDataProtectionProvider>().CreateProtector("fixture-purpose").Unprotect(File.ReadAllText("/work/restart-token"));
    if(plaintext!="after-migration")throw new Exception("Restart verification failed");
    Console.WriteLine("PASS separate process restart decrypts persisted payload");return;
}
var root = Path.Combine("/work", "session-keys-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(root,"wwwroot"));
var legacy = Path.Combine(root,"legacy");
var target = Path.Combine(root,"wwwroot/uploads/.firmova-private/data-protection");
var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
int checks=0;
void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
var disabled=WebApplication.CreateBuilder(new WebApplicationOptions{ContentRootPath=root,WebRootPath="wwwroot",EnvironmentName="Testing"});
disabled.Configuration["DataProtection:PersistKeys"]="false";disabled.AddPersistentSessionKeys();
Check(!Directory.Exists(target),"disabled persistence does not create or change key storage");
disabled.Configuration["DataProtection:PersistKeys"]="true";
try{disabled.AddPersistentSessionKeys();throw new Exception("Unstaged keys accepted");}catch(InvalidOperationException e)when(e.Message.StartsWith("Existing session keys")){Check(true,"activation without staged keys rejected");}
WebApplication Build(bool persistent)
{
    var b=WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath=root,WebRootPath="wwwroot",EnvironmentName="Testing" });
    b.Configuration["DataProtection:PersistKeys"]=persistent.ToString();
    b.Services.AddSingleton<IDistributedCache>(cache);
    b.Services.AddSession(o=>o.Cookie.Name=".MuhasebeTakip2.Session");
    if(persistent)b.AddPersistentSessionKeys();
    else b.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(legacy));
    b.WebHost.UseUrls("http://127.0.0.1:0");
    var app=b.Build();app.UseAccountingPublicAssets();app.UseSession();
    app.MapGet("/set",async c=>{c.Session.SetString("fixture","synthetic");await c.Response.WriteAsync("OK");});
    app.MapGet("/get",async c=>await c.Response.WriteAsync(c.Session.GetString("fixture")??"EMPTY"));
    return app;
}
string Url(WebApplication a)=>a.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
using var client=new HttpClient(new HttpClientHandler{UseCookies=false});
await using var old=Build(false);await old.StartAsync();
var oldProtector=old.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("fixture-purpose");
var oldToken=oldProtector.Protect("synthetic-value");
var response=await client.GetAsync(Url(old)+"/set");
var cookie=response.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
_ = new PrivateKeyRepository(target);
foreach(var path in Directory.GetFiles(legacy,"*.xml"))
{
    var destination=Path.Combine(target,Path.GetFileName(path));
    using var input=File.OpenRead(path);
    using var output=new FileStream(destination,new FileStreamOptions{Mode=FileMode.CreateNew,Access=FileAccess.Write,UnixCreateMode=UnixFileMode.UserRead|UnixFileMode.UserWrite});
    input.CopyTo(output);output.Flush(true);
}
var oldFiles=Directory.GetFiles(legacy,"*.xml");
Check(oldFiles.Length>0 && oldFiles.All(p=>File.ReadAllBytes(p).SequenceEqual(File.ReadAllBytes(Path.Combine(target,Path.GetFileName(p))))),"synthetic legacy key copy unchanged");
await old.StopAsync();
await using var first=Build(true);await first.StartAsync();
var protector=first.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("fixture-purpose");
Check(protector.Unprotect(oldToken)=="synthetic-value","old protected payload survives migration with unchanged discriminator");
using(var req=new HttpRequestMessage(HttpMethod.Get,Url(first)+"/get")){req.Headers.Add("Cookie",cookie);using var r=await client.SendAsync(req);Check(await r.Content.ReadAsStringAsync()=="synthetic","real old session cookie accepted when cache survives");}
first.Services.GetRequiredService<IKeyManager>().CreateNewKey(DateTimeOffset.UtcNow,DateTimeOffset.UtcNow.AddDays(90));
var newToken=protector.Protect("after-migration");
var legacyReader=DataProtectionProvider.Create(new DirectoryInfo(target),o=>o.SetApplicationName(first.Services.GetRequiredService<IOptions<DataProtectionOptions>>().Value.ApplicationDiscriminator!));
Check(legacyReader.CreateProtector("fixture-purpose").Unprotect(newToken)=="after-migration","legacy filesystem reader can read rotated persistent key ring");
await first.StopAsync();
File.WriteAllText("/work/restart-root",root);
File.WriteAllText("/work/restart-token",newToken);
File.WriteAllText("/work/restart-discriminator",first.Services.GetRequiredService<IOptions<DataProtectionOptions>>().Value.ApplicationDiscriminator);
await using var second=Build(true);await second.StartAsync();
Check(second.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("fixture-purpose").Unprotect(newToken)=="after-migration","new protected payload survives restart");
using(var created=await client.GetAsync(Url(second)+"/set"))
{
    var freshCookie=created.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
    using var request=new HttpRequestMessage(HttpMethod.Get,Url(second)+"/get");request.Headers.Add("Cookie",freshCookie);
    using var read=await client.SendAsync(request);Check(await read.Content.ReadAsStringAsync()=="synthetic","new HTTP session cookie works after migration");
}
var unmounted=WebApplication.CreateBuilder(new WebApplicationOptions{ContentRootPath=root,WebRootPath="wwwroot",EnvironmentName="Production"});
unmounted.Configuration["DataProtection:PersistKeys"]="true";
try{unmounted.AddPersistentSessionKeys();throw new Exception("Unmounted production disk accepted");}catch(InvalidOperationException e)when(e.Message.StartsWith("Persistent session key disk")){Check(true,"production refuses unmounted key storage");}
await using var parallel=Build(true);await parallel.StartAsync();
Check(parallel.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("fixture-purpose").Unprotect(newToken)=="after-migration","second instance on same key ring can decrypt");
Check(File.GetUnixFileMode(target)==(UnixFileMode)448,"key directory mode 700");
bool OwnerOnlyFile(string p) => OperatingSystem.IsLinux() && File.GetUnixFileMode(p)==(UnixFileMode)384;
Check(Directory.GetFiles(target,"*.xml").All(OwnerOnlyFile),"all key files mode 600");
var name=Path.GetFileName(Directory.GetFiles(target,"*.xml")[0]);
foreach(var path in new[]{"/uploads/.firmova-private/data-protection/"+name,"/css/../uploads/.firmova-private/data-protection/"+name})
{using var r=await client.GetAsync(Url(second)+path);Check(r.StatusCode==HttpStatusCode.NotFound,"HTTP cannot serve key path");}
var bad=Path.Combine(root,"bad");Directory.CreateDirectory(bad);File.SetUnixFileMode(bad,(UnixFileMode)511);
try{_=new PrivateKeyRepository(bad);throw new Exception("Unsafe mode accepted");}catch(IOException){Check(true,"unsafe directory mode rejected");}
var link=Path.Combine(root,"linked");Directory.CreateSymbolicLink(link,target);
try{_=new PrivateKeyRepository(link);throw new Exception("Symlink accepted");}catch(IOException){Check(true,"symlink directory rejected");}
var repo=new PrivateKeyRepository(target);var rogue=Path.Combine(target,"rogue.xml");File.CreateSymbolicLink(rogue,oldFiles[0]);
try{repo.GetAllElements();throw new Exception("Symlink file accepted");}catch(IOException){Check(true,"symlink file rejected");}finally{File.Delete(rogue);}
second.Services.GetRequiredService<IKeyManager>().RevokeKey(Guid.NewGuid(),"synthetic unused key");
Check(repo.GetAllElements().Any(x=>x.Name=="revocation"),"framework-generated revocation records persisted");
var parallelPath=Path.Combine(root,"parallel-writes");var writer=new PrivateKeyRepository(parallelPath);
Parallel.For(0,8,i=>writer.StoreElement(new XElement("fixture",i),"same-name"));
Check(writer.GetAllElements().Count==8,"concurrent writers cannot overwrite each other");
try{new PrivateKeyRepository(parallelPath,requireExistingKeys:true).GetAllElements();throw new Exception("Missing key ring accepted");}catch(IOException){Check(true,"missing key ring rejected instead of silently replaced");}
await parallel.StopAsync();await second.StopAsync();
Check(oldFiles.All(File.Exists),"legacy keys retained");
Console.WriteLine($"PASS {checks} isolated persistent session key checks");
