namespace FirmovaAI.Models.Ai;

public class QueryIntent
{
    public string Intent { get; set; } = "";
    public string RawText { get; set; } = "";

    public string? CalisanAdi { get; set; }
    public string? CariAdi { get; set; }
    public string? MusteriAdi { get; set; }
    public string? StokAdi { get; set; }

    public string? DateRange { get; set; }
    public string? RequestType { get; set; }

    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
}