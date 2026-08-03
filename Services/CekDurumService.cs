using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;

namespace MuhasebeTakip2.App.Services;

public class CekDurumService : ICekDurumService
{
    private readonly AppDbContext _db;
    private readonly IIslemGecmisiService _islemGecmisi;

    public CekDurumService(AppDbContext db, IIslemGecmisiService islemGecmisi)
    {
        _db = db;
        _islemGecmisi = islemGecmisi;
    }

    public async Task<CekDurumSonucu> DurumDegistirAsync(int firmaId, int cekId, bool odendiMi, CancellationToken cancellationToken = default)
    {
        var cek = await _db.Cekler.FirstOrDefaultAsync(x => x.Id == cekId && x.FirmaId == firmaId, cancellationToken);
        if (cek == null)
            return new(false, "Çek bulunamadı veya bu işlem için yetkiniz yok.");

        var fiil = cek.Tip == CekTipi.Alinacak ? "tahsil" : "öden";
        if (cek.OdendiMi == odendiMi)
            return new(true, odendiMi ? $"Çek zaten {fiil}miş olarak işaretli." : $"Çek zaten {fiil}memiş olarak işaretli.");

        var eskiDeger = new { cek.OdendiMi, cek.OdemeTarihi };
        cek.OdendiMi = odendiMi;
        cek.OdemeTarihi = odendiMi ? DateTime.UtcNow : null;

        var tipMetni = cek.Tip == CekTipi.Alinacak ? "alınan" : "verilen";
        var durumMetni = cek.Tip == CekTipi.Alinacak
            ? (odendiMi ? "tahsil edildi" : "tahsil edilmedi")
            : (odendiMi ? "ödendi" : "ödenmedi");

        await _islemGecmisi.KaydetAsync(
            "Çekler",
            "Güncelleme",
            $"{cek.No} numaralı {tipMetni} çek {durumMetni} olarak işaretlendi.",
            eskiDeger,
            new { cek.OdendiMi, cek.OdemeTarihi },
            cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        return new(true, $"{cek.No} numaralı çek {durumMetni} olarak işaretlendi.");
    }
}
