namespace MuhasebeTakip2.App.Models
{
    public class Kullanici
    {
        public int Id { get; set; }
        public string KullaniciAdi { get; set; } = "";
        public string Sifre { get; set; } = "";

        public int FirmaId { get; set; }
        public Firma? Firma { get; set; }

        public string Rol { get; set; } = "Kullanici";
    }
}