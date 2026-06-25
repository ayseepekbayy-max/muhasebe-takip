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
        var session = _httpContextAccessor.HttpContext?.Session
            ?? throw new InvalidOperationException("İşlem geçmişi için aktif oturum bulunamadı.");

        var firmaId = session.GetInt32("FirmaId")
            ?? throw new InvalidOperationException("İşlem geçmişi için firma bilgisi bulunamadı.");
        var httpContext = _httpContextAccessor.HttpContext;

        var ipAdresi = httpContext?.Connection.RemoteIpAddress?.ToString();
        var tarayici = httpContext?.Request.Headers.UserAgent.ToString();

        _db.IslemGecmisleri.Add(new IslemGecmisi
        {
            FirmaId = firmaId,
            KullaniciId = session.GetInt32("KullaniciId"),
            KullaniciAdi = session.GetString("KullaniciAdi") ?? "Bilinmeyen kullanıcı",
            Modul = modul.Trim(),
            IslemTuru = islemTuru.Trim(),
            Aciklama = aciklama.Trim(),
            EskiDeger = Serialize(eskiDeger),
            YeniDeger = Serialize(yeniDeger),
            IpAdresi = ipAdresi,
            TarayiciBilgisi = tarayici,
            Tarih = DateTime.UtcNow
        });

        return Task.CompletedTask;
    }

    private static string? Serialize(object? value)
    {
        return value == null ? null : JsonSerializer.Serialize(value, JsonOptions);
    }
}
