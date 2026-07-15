namespace MuhasebeTakip2.App.Services;

public class EmailSendResult
{
    public bool BasariliMi { get; set; }
    public string? HataMesaji { get; set; }

    public static EmailSendResult Basarili() => new() { BasariliMi = true };
    public static EmailSendResult Hata(string mesaj) => new() { BasariliMi = false, HataMesaji = mesaj };
}
