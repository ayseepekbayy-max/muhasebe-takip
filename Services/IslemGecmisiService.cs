using System.Text.Json;
using System.Text.Json.Serialization;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;

namespace MuhasebeTakip2.App.Services;

public class IslemGecmisiService : IIslemGecmisiService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly AppDbContext _db;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public IslemGecmisiService(
        AppDbContext db,
        IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _httpContextAccessor = httpContextAccessor;
    }

    public Task KaydetAsync(
        string modul,
        string islemTuru,
        string aciklama,
        object? eskiDeger = null,
        object? yeniDeger = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var session = _httpContextAccessor.HttpContext?.Session
            ?? throw new InvalidOperationException("İşlem geçmişi için aktif oturum bulunamadı.");

        var firmaId = session.GetInt32("FirmaId")
            ?? throw new InvalidOperationException("İşlem geçmişi için firma bilgisi bulunamadı.");
        var httpContext = _httpContextAccessor.HttpContext;

        var forwardedFor = httpContext?.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        var ipAdresi = forwardedFor?
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault()
            ?? httpContext?.Connection.RemoteIpAddress?.ToString()
            ?? "Bilinmiyor";
        var tarayici = httpContext?.Request.Headers.UserAgent.ToString();

        _db.IslemGecmisleri.Add(new IslemGecmisi
        {
            FirmaId = firmaId,
            KullaniciId = session.GetInt32("KullaniciId"),
            KullaniciAdi = Limit(session.GetString("KullaniciAdi") ?? "Bilinmeyen kullanıcı", 100),
            Modul = Limit(modul.Trim(), 80),
            IslemTuru = Limit(islemTuru.Trim(), 30),
            Aciklama = Limit(aciklama.Trim(), 500),
            EskiDeger = Serialize(eskiDeger),
            YeniDeger = Serialize(yeniDeger),
            IpAdresi = Limit(ipAdresi, 80),
            TarayiciBilgisi = Limit(
                string.IsNullOrWhiteSpace(tarayici) ? "Bilinmiyor" : tarayici,
                300),
            Tarih = DateTime.UtcNow
        });

        return Task.CompletedTask;
    }

    private static string? Serialize(object? value)
    {
        return value == null ? null : JsonSerializer.Serialize(value, JsonOptions);
    }

    private static string Limit(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
