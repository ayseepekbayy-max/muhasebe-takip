using System.ComponentModel.DataAnnotations;

namespace MuhasebeTakip2.App.Models;

public sealed class PrivateMessageHidden
{
    public int Id { get; set; }
    [Range(1, 2)]
    public int PersonNumber { get; set; }
    public int PrivateMessageId { get; set; }
    public DateTime HiddenAtUtc { get; set; }
}