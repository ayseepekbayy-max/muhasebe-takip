namespace FirmovaAI.Services.Ai;

public class NovaReplyService
{
    private static readonly Random Random = new();

    private readonly string[] _wakeReplies =
    {
        "Duyuyorum, nasıl yardımcı olabilirim?",
        "Buradayım, seni dinliyorum.",
        "Evet, buradayım. Ne yapmak istersin?",
        "Hazırım, sana nasıl yardımcı olayım?",
        "Seni dinliyorum.",
        "Evet, buyur.",
        "Buradayım, devam edebilirsin.",
        "Hazırım, neye bakmamı istersin?"
    };

    private readonly string[] _introReplies =
    {
        "Hey, ben Nova. Senin akıllı asistanınım. Herkese merhabalar, size nasıl yardımcı olabilirim?",
        "Merhaba, ben Nova. Akıllı asistanınız olarak buradayım. Herkese selamlar, size nasıl yardımcı olabilirim?",
        "Hey, ben Nova. Senin akıllı iş asistanınım. Herkese merhaba, bugün size nasıl yardımcı olabilirim?",
        "Merhabalar, ben Nova. İşlerinizi takip eden akıllı asistanınızım. Size nasıl yardımcı olabilirim?"
    };

    public string GetReply(string text)
    {
        text = (text ?? "").Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(text))
            return GetRandomWakeReply();

        if (text.Contains("herkese selam ver") ||
            text.Contains("selam ver") ||
            text.Contains("kendini tanıt") ||
            text.Contains("tanıt kendini"))
        {
            return GetRandomIntroReply();
        }

        if (text == "hey nova" ||
            text == "nova" ||
            text.Contains("hey nova"))
        {
            return GetRandomWakeReply();
        }

        return "";
    }

    private string GetRandomWakeReply()
    {
        return _wakeReplies[Random.Next(_wakeReplies.Length)];
    }

    private string GetRandomIntroReply()
    {
        return _introReplies[Random.Next(_introReplies.Length)];
    }
}