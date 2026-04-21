using FirmovaAI.Services.Ai;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddSession();

builder.Services.AddScoped<QueryInterpreter>();
builder.Services.AddScoped<QueryExecutor>();

builder.Services.AddHttpClient<MuhasebeApiClient>(client =>
{
    var baseUrl = builder.Configuration["ApiSettings:MuhasebeApiBaseUrl"];
    client.BaseAddress = new Uri(baseUrl!);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseSession();
app.UseAuthorization();

app.MapRazorPages();

app.Run();