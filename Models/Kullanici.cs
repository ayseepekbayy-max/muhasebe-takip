namespace MuhasebeTakip2.App.Models
{
    public class Kullanici
    {
        public int Id { get; set; }

        public string KullaniciAdi { get; set; } = "";

        public string Email { get; set; } = "";

        public bool OdemeEmailBildirimiAktifMi { get; set; } = true;

        public bool EmailDogrulandiMi { get; set; } = false;

        public string Sifre { get; set; } = "";

        public string? SifreSifirlamaKodu { get; set; }

        public DateTime? SifreSifirlamaKodGecerlilik { get; set; }

        public int FirmaId { get; set; }
        public Firma? Firma { get; set; }

        public string Rol { get; set; } = "Kullanici";
    }
}