namespace MuhasebeTakip2.App.Models;

public sealed class CompanyStoredFile
{
    public Guid Id { get; set; }
    public int OwnerCompanyId { get; set; }
    public string Sha256 { get; set; } = "";
    public long Length { get; set; }
    public DateTime? RetiredAtUtc { get; set; }
}

public sealed class CompanyStoredFileReference
{
    public Guid FileId { get; set; }
    public int CompanyId { get; set; }
    public int? AttachmentId { get; set; }
    public int? CheckId { get; set; }
}

public sealed class CompanyFileCleanupJob
{
    public Guid FileId { get; set; }
    public Guid OperationId { get; set; }
    public int CompanyId { get; set; }
    public DateTime EnqueuedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}
