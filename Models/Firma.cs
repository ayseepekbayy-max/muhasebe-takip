namespace MuhasebeTakip2.App.Models
{
    public class Firma
    {
        public int Id { get; set; }
        public string FirmaAdi { get; set; } = "";
        public bool AktifMi { get; set; } = true;

        public string Adres { get; set; } = "";
        public string Telefon { get; set; } = "";
        public string Email { get; set; } = "";
        public string VergiDairesi { get; set; } = "";
        public string VergiNo { get; set; } = "";
        public string LogoYolu { get; set; } = "";

        public bool MenuCariKartlar { get; set; } = true;
        public bool MenuKasa { get; set; } = true;
        public bool MenuRaporlar { get; set; } = true;
        public bool MenuCalisanlar { get; set; } = true;
        public bool MenuMusteriler { get; set; } = true;
        public bool MenuStoklar { get; set; } = true;
        public bool MenuMaliyet { get; set; } = true;
        public bool MenuCekler { get; set; } = true;
    }
}