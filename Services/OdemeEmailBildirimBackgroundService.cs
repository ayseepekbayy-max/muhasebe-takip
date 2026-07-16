using System.Globalization;
using System.Text.Encodings.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;

namespace MuhasebeTakip2.App.Services;

public class OdemeEmailBildirimBackgroundService : BackgroundService
{
    private static readonly TimeSpan IlkBekleme = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan KontrolAraligi = TimeSpan.FromHours(6);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OdemeEmailBildirimBackgroundService> _logger;
    private readonly HtmlEncoder _htmlEncoder = HtmlEncoder.Default;

    public OdemeEmailBildirimBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<OdemeEmailBildirimBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(IlkBekleme, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await KontrolEtAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ödeme e-posta bildirim kontrolü sırasında hata oluştu.");
            }

            try
            {
                await Task.Delay(KontrolAraligi, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task KontrolEtAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
        var emailSettings = scope.ServiceProvider.GetRequiredService<IOptions<EmailSettings>>().Value;

        var bugun = DateTime.UtcNow.Date;
        var planlar = await db.OdemePlanlari
            .AsNoTracking()
            .Include(x => x.Firma)
            .Where(x => x.AktifMi &&
                        !x.TamamlandiMi &&
                        x.BildirimAktifMi &&
                        x.KalanTaksitSayisi > 0 &&
                        x.SonrakiOdemeTarihi != null &&
                        x.SonrakiOdemeTarihi.Value.Date <= bugun.AddDays(x.BildirimGunu))
            .OrderBy(x => x.SonrakiOdemeTarihi)
            .Take(100)
            .ToListAsync(cancellationToken);

        foreach (var plan in planlar)
        {
            try
            {
                await PlanIcinBildirimGonderAsync(db, emailService, emailSettings, plan, bugun, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ödeme planı e-posta bildirimi işlenemedi. OdemePlaniId: {OdemePlaniId}", plan.Id);
            }
        }
    }

    private async Task PlanIcinBildirimGonderAsync(
        AppDbContext db,
        IEmailService emailService,
        EmailSettings emailSettings,
        OdemePlani plan,
        DateTime bugun,
        CancellationToken cancellationToken)
    {
        var kullanici = await BildirimKullanicisiAsync(db, plan, cancellationToken);
        if (kullanici == null || !EmailService.IsValidEmail(kullanici.Email))
            return;

        var donem = plan.SonrakiOdemeTarihi!.Value.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var dahaOnceBasarili = await db.OdemeBildirimGecmisleri.AnyAsync(x =>
            x.FirmaId == plan.FirmaId &&
            x.KullaniciId == kullanici.Id &&
            x.OdemePlaniId == plan.Id &&
            x.BildirimTuru == "Email" &&
            x.OdemeDonemi == donem &&
            x.BasariliMi,
            cancellationToken);

        if (dahaOnceBasarili)
            return;

        var gecikmis = plan.SonrakiOdemeTarihi!.Value.Date < bugun;
        var konu = EmailService.SanitizeSubject($"{(gecikmis ? "Gecikmiş Ödeme" : "Yaklaşan Ödeme")}: {plan.OdemeAdi}");
        var html = MailHtml(plan, kullanici, emailSettings, bugun, gecikmis);
        var sonuc = await emailService.SendAsync(kullanici.Email, konu, html, cancellationToken);

        db.OdemeBildirimGecmisleri.Add(new OdemeBildirimGecmisi
        {
            FirmaId = plan.FirmaId,
            KullaniciId = kullanici.Id,
            OdemePlaniId = plan.Id,
            BildirimTuru = "Email",
            OdemeDonemi = donem,
            HedefEmail = kullanici.Email,
            BasariliMi = sonuc.BasariliMi,
            HataMesaji = sonuc.BasariliMi ? null : GuvenliHata(sonuc.HataMesaji),
            BildirimTarihi = DateTime.UtcNow,
            OlusturmaTarihi = DateTime.UtcNow
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task<Kullanici?> BildirimKullanicisiAsync(AppDbContext db, OdemePlani plan, CancellationToken cancellationToken)
    {
        if (plan.OlusturanKullaniciId.HasValue)
        {
            var olusturan = await db.Kullanicilar.AsNoTracking().FirstOrDefaultAsync(x =>
                x.Id == plan.OlusturanKullaniciId.Value &&
                x.FirmaId == plan.FirmaId &&
                x.OdemeEmailBildirimiAktifMi &&
                x.Email != "",
                cancellationToken);

            if (olusturan != null && EmailService.IsValidEmail(olusturan.Email))
                return olusturan;
        }

        var adaylar = await db.Kullanicilar.AsNoTracking()
            .Where(x => x.FirmaId == plan.FirmaId &&
                        x.OdemeEmailBildirimiAktifMi &&
                        x.Email != "")
            .OrderByDescending(x => x.Rol == "Admin" || x.Rol == "SuperAdmin")
            .ThenBy(x => x.Id)
            .Take(3)
            .ToListAsync(cancellationToken);

        return adaylar.FirstOrDefault(x => EmailService.IsValidEmail(x.Email));
    }

    private string MailHtml(OdemePlani plan, Kullanici kullanici, EmailSettings settings, DateTime bugun, bool gecikmis)
    {
        var tr = CultureInfo.GetCultureInfo("tr-TR");
        var kalanGun = (plan.SonrakiOdemeTarihi!.Value.Date - bugun).Days;
        var baseUrl = string.IsNullOrWhiteSpace(settings.AppBaseUrl) ? "" : settings.AppBaseUrl.TrimEnd('/');
        var odemelerUrl = string.IsNullOrWhiteSpace(baseUrl) ? "/Odemeler" : $"{baseUrl}/Odemeler";
        var firmaAdi = plan.Firma?.FirmaAdi ?? "Firma";

        string E(string? value) => _htmlEncoder.Encode(value ?? "");

        return $"""
<!doctype html>
<html lang="tr">
<body style="margin:0;background:#f8fafc;font-family:Segoe UI,Arial,sans-serif;color:#0f172a;">
  <div style="max-width:640px;margin:0 auto;padding:20px;">
    <div style="background:#fff;border:1px solid #e5e7eb;border-radius:8px;padding:20px;">
      <h2 style="margin:0 0 8px;color:#1e293b;">{(gecikmis ? "Gecikmiş Ödeme" : "Yaklaşan Ödeme")}</h2>
      <p>Merhaba {E(kullanici.KullaniciAdi)},</p>
      <p><strong>{E(firmaAdi)}</strong> firmasındaki ödeme planınız için hatırlatma:</p>
      <table style="width:100%;border-collapse:collapse;font-size:14px;">
        <tr><td style="padding:8px;border-bottom:1px solid #e5e7eb;">Ödeme adı</td><td style="padding:8px;border-bottom:1px solid #e5e7eb;"><strong>{E(plan.OdemeAdi)}</strong></td></tr>
        <tr><td style="padding:8px;border-bottom:1px solid #e5e7eb;">Tür</td><td style="padding:8px;border-bottom:1px solid #e5e7eb;">{E(plan.OdemeTuru.Metin())}</td></tr>
        <tr><td style="padding:8px;border-bottom:1px solid #e5e7eb;">Aylık tutar</td><td style="padding:8px;border-bottom:1px solid #e5e7eb;">{plan.AylikOdemeTutari.ToString("C2", tr)}</td></tr>
        <tr><td style="padding:8px;border-bottom:1px solid #e5e7eb;">Sonraki ödeme tarihi</td><td style="padding:8px;border-bottom:1px solid #e5e7eb;">{plan.SonrakiOdemeTarihi!.Value:dd.MM.yyyy}</td></tr>
        <tr><td style="padding:8px;border-bottom:1px solid #e5e7eb;">Ödemeye kalan gün</td><td style="padding:8px;border-bottom:1px solid #e5e7eb;">{kalanGun}</td></tr>
        <tr><td style="padding:8px;border-bottom:1px solid #e5e7eb;">Kalan taksit</td><td style="padding:8px;border-bottom:1px solid #e5e7eb;">{plan.KalanTaksitSayisi}</td></tr>
        <tr><td style="padding:8px;border-bottom:1px solid #e5e7eb;">Kalan toplam</td><td style="padding:8px;border-bottom:1px solid #e5e7eb;">{plan.KalanToplamTutar.ToString("C2", tr)}</td></tr>
        <tr><td style="padding:8px;">Durum</td><td style="padding:8px;">{(gecikmis ? "Gecikmiş" : "Yaklaşıyor")}</td></tr>
      </table>
      <p style="margin-top:20px;"><a href="{E(odemelerUrl)}" style="background:#6366f1;color:#fff;text-decoration:none;padding:10px 14px;border-radius:8px;display:inline-block;">Ödemeler sayfasını aç</a></p>
    </div>
  </div>
</body>
</html>
""";
    }

    private static string? GuvenliHata(string? hata)
    {
        if (string.IsNullOrWhiteSpace(hata))
            return null;
        return hata.Length <= 500 ? hata : hata[..500];
    }
}
