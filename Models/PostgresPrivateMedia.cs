using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
namespace MuhasebeTakip2.App.Models;

public sealed class PrivateMediaContent
{
    public int MessageId { get; set; }
    [MaxLength(32)] public string MediaKey { get; set; } = "";
    [JsonIgnore] public byte[] Payload { get; set; } = [];
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
}
public sealed class PrivateMediaQuota
{
    public int Id { get; set; } = 1;
    public long UsedBytes { get; set; }
}
