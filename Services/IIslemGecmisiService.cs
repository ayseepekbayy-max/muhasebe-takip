namespace MuhasebeTakip2.App.Services;

public interface IIslemGecmisiService
{
    Task KaydetAsync(
        string modul,
        string islemTuru,
        string aciklama,
        object? eskiDeger = null,
        object? yeniDeger = null,
        CancellationToken cancellationToken = default);
}
