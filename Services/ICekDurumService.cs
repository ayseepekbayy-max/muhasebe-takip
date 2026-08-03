namespace MuhasebeTakip2.App.Services;

public interface ICekDurumService
{
    Task<CekDurumSonucu> DurumDegistirAsync(int firmaId, int cekId, bool odendiMi, CancellationToken cancellationToken = default);
}

public record CekDurumSonucu(bool Basarili, string Mesaj);
