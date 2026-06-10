namespace FirmovaAI.Models
{
    public class ChatLog
    {
        public int Id { get; set; }

        public string Soru { get; set; } = "";
        public string Cevap { get; set; } = "";

        public DateTime Tarih { get; set; } = DateTime.Now;
    }
}