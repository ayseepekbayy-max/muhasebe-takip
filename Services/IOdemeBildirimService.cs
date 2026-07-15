namespace MuhasebeTakip2.App.Services;

public interface IOdemeBildirimService
{
    Task<List<OdemeBildirimSatiri>> AktifBildirimleriGetirAsync(
        int firmaId,
        int? kullaniciId,
        DateTime bugun,
        CancellationToken cancellationToken = default);

    Task<int> AktifBildirimSayisiAsync(
        int firmaId,
        int? kullaniciId,
        DateTime bugun,
        CancellationToken cancellationToken = default);

    Task BugunGizleAsync(
        int firmaId,
        int kullaniciId,
        string? kullaniciAdi,
        int odemePlaniId,
        DateTime bugun,
        CancellationToken cancellationToken = default);
}
