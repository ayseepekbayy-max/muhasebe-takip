using System.ComponentModel.DataAnnotations;

namespace MuhasebeTakip2.App.Models;

public sealed class PrivatePresence
{
    public int Id { get; set; }
    [Range(1, 2)]
    public int PersonNumber { get; set; }
    public DateTime LastSeenAtUtc { get; set; }
    public bool ShowLastSeen { get; set; } = true;
}