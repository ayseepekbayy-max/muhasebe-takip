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

    public PrivateMessageKind Kind { get; set; }
    [MaxLength(32)] public string? MediaKey { get; set; }
    [MaxLength(64)] public string? MediaContentType { get; set; }
    public int? DurationSeconds { get; set; }
    public DateTime? ViewedAtUtc { get; set; }
    public DateTime? ExpiredAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
public enum PrivateMessageKind { Text = 0, Audio = 1, ViewOncePhoto = 2 }
