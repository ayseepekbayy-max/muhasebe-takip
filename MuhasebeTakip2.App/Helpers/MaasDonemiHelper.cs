namespace MuhasebeTakip2.App.Helpers
{
    public static class MaasDonemiHelper
    {
        public static (DateTime Baslangic, DateTime Bitis) GetDonem(DateTime tarih)
        {
            tarih = tarih.Date;

            if (tarih.Day >= 6)
            {
                var baslangic = new DateTime(tarih.Year, tarih.Month, 6);
                var sonrakiAy = baslangic.AddMonths(1);
                var bitis = new DateTime(sonrakiAy.Year, sonrakiAy.Month, 5);
                return (baslangic, bitis);
            }
            else
            {
                var oncekiAy = tarih.AddMonths(-1);
                var baslangic = new DateTime(oncekiAy.Year, oncekiAy.Month, 6);
                var bitis = new DateTime(tarih.Year, tarih.Month, 5);
                return (baslangic, bitis);
            }
        }
    }
}