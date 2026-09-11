using System.ComponentModel.DataAnnotations;

namespace MuhasebeTakip2.App.Models;

public sealed class PrivateMessage
{
    public const int MaxContentLength = 4000;
    public const int MaxSenderNameLength = 200;

    public int Id { get; set; }

    [Range(1, 2)]
    public int SenderPerson { get; set; }

    [Required, MaxLength(MaxSenderNameLength)]
    public string SenderName { get; set; } = "";

    [Required, MaxLength(MaxContentLength)]
    public string Content { get; set; } = "";

    public DateTime CreatedAtUtc { get; set; }
}