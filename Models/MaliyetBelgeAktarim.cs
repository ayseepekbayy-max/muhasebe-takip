namespace MuhasebeTakip2.App.Models;

public class MaliyetBelgeAktarim
{
    public string UretimAdi { get; set; } = "";

    public string OkunanMetin { get; set; } = "";

    public List<MaliyetBelgeAktarimKalemi> Kalemler { get; set; } = new();
}

public class MaliyetBelgeAktarimKalemi
{
    public string Aciklama { get; set; } = "";

    public decimal Tutar { get; set; }
}
