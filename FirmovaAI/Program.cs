using FirmovaAI.Services.Ai;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddSession();

builder.Services.AddScoped<QueryInterpreter>();
builder.Services.AddScoped<QueryExecutor>();

builder.Services.AddHttpClient<MuhasebeApiClient>(client =>
{
    var baseUrl = builder.Configuration["ApiSettings:MuhasebeApiBaseUrl"];

    if (string.IsNullOrWhiteSpace(baseUrl))
        throw new Exception("ApiSettings:MuhasebeApiBaseUrl bulunamadı.");

    client.BaseAddress = new Uri(baseUrl);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// Localde https sorunu yaşamamak için kapalı tutuyoruz.
// app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();

app.UseSession();
app.UseAuthorization();

app.MapRazorPages();

app.Run();