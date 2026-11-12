using System.ComponentModel.DataAnnotations;

namespace MuhasebeTakip2.App.Models;

public enum AdminCompanyTransitionStatus { Pending, Completed, Failed, Uncertain }

public sealed class AdminCompanyTransition
{
    public Guid Id { get; set; }
    public int AdminUserId { get; set; }
    [MaxLength(32)] public string LoginEpoch { get; set; } = "";
    public int SourceCompanyId { get; set; }
    public int TargetCompanyId { get; set; }
    public Guid SourceVersion { get; set; }
    public bool ReturnHome { get; set; }
    public AdminCompanyTransitionStatus Status { get; set; }
    public DateTime RequestedAtUtc { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
}
