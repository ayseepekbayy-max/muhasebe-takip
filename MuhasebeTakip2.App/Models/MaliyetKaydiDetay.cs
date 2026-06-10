namespace MuhasebeTakip2.App.Models;

public class MaliyetKaydiDetay
{
    public List<MaliyetDetaySatiri> Plakalar { get; set; } = new();

    public List<MaliyetDetaySatiri> Bantlamalar { get; set; } = new();

    public List<MaliyetDetaySatiri> Arkaliklar { get; set; } = new();

    public List<MaliyetDetaySatiri> Malzemeler { get; set; } = new();

    public List<MaliyetDetaySatiri> BelgeKalemleri { get; set; } = new();
}

public class MaliyetDetaySatiri
{
    public string Aciklama { get; set; } = "";

    public string Olcu { get; set; } = "";

    public decimal Adet { get; set; }

    public decimal BirimFiyat { get; set; }

    public decimal Toplam { get; set; }

    public string Not { get; set; } = "";
}
