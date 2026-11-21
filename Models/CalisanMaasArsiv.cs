namespace MuhasebeTakip2.App.Models
{
    public class CalisanMaasArsiv
    {
        public int Id { get; set; }

        public int FirmaId { get; set; }
        // Original historical reference. Never infer a person from their name or rewrite this ID.
        public int CalisanId { get; set; }
        // A separately verified, same-company employee. Null means unresolved, never a name match.
        public int? DogrulanmisCalisanId { get; set; }

        public DateTime DonemBaslangic { get; set; }
        public DateTime DonemBitis { get; set; }

        public decimal ToplamMaas { get; set; }
        public decimal ToplamAvans { get; set; }
        public decimal KalanMaas { get; set; }

        public DateTime OdemeTarihi { get; set; } = DateTime.Now;

        public string? Aciklama { get; set; }
    }
}
