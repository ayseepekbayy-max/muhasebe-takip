using System.Data;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;

namespace MuhasebeTakip2.App.Services;

// Read-only preview. The EF model inventory is not a claim about deployed schema or file ownership.
public sealed class CompanyDeletionInventory(AppDbContext db)
{
    public sealed record Table(string Name, int Rows, bool HasExternalReferences);
    public sealed record Result(IReadOnlyList<Table> Tables, int Attachments, int CheckImages,
        bool UnverifiedLogo, int SharedFilePaths, int UnrecognizedFilePaths);

    private static readonly Regex UploadPath = new(
        @"\A/uploads/(?:cekler|cariler|faturalar)/[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}\.[a-z0-9]+\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public async Task<Result> InspectAsync(int companyId, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(companyId, 2);
        await using var transaction = await db.Database.BeginTransactionAsync(
            db.Database.IsNpgsql() ? IsolationLevel.RepeatableRead : IsolationLevel.Serializable, ct);

        var model = db.Model.GetEntityTypes().ToArray();
        var owned = model.Where(x => x.FindProperty("FirmaId") is not null ||
            x.ClrType == typeof(Firma) || x.ClrType == typeof(FaturaKalem)).ToArray();
        var tables = new List<Table>();
        foreach (var entity in owned.OrderBy(x => x.GetTableName(), StringComparer.Ordinal))
        {
            var table = TableName(entity);
            var owner = Ownership(entity, "r");
            var countSql = $"SELECT CAST(COUNT(*) AS integer) AS \"Value\" FROM {table} r WHERE {owner} = {{0}}";
            var rows = await db.Database.SqlQueryRaw<int>(countSql, companyId).SingleAsync(ct);
            var external = false;
            foreach (var link in IncomingLinks(entity, model))
            {
                var sourceOwner = link.Source.ClrType == typeof(CompanyStoredFileReference)
                    ? """s."CompanyId" """ : Ownership(link.Source, "s");
                var targetId = Column(entity, "Id");
                var sourceId = Column(link.Source, link.Property);
                var sql = $$"""
                    SELECT EXISTS (
                        SELECT 1 FROM {{TableName(link.Source)}} s
                        JOIN {{table}} r ON s.{{sourceId}} = r.{{targetId}}
                        WHERE {{owner}} = {0} AND ({{sourceOwner}} IS NULL OR {{sourceOwner}} <> {0})
                    ) AS "Value"
                    """;
                if (await db.Database.SqlQueryRaw<bool>(sql, companyId).SingleAsync(ct))
                {
                    external = true;
                    break;
                }
            }
            if (entity.ClrType == typeof(IslemGecmisi))
                external |= await CompanyAuditActorPolicy.HasUnresolvedAsync(db, companyId, ct);
            tables.Add(new(entity.GetTableName()!, rows, external));
        }

        // This production legacy table is intentionally outside the EF model. Keep the
        // preview in the same snapshot as the modeled rows and use the deletion scope's
        // exact FirmaId filter; do not infer ownership from the historical employee ID.
        if (db.Database.IsNpgsql())
        {
            var archiveCount = await db.Database.SqlQueryRaw<int>("""
                SELECT CAST(COUNT(*) AS integer) AS "Value"
                FROM public."CalisanArsivleri" WHERE "FirmaId" = {0}
                """, companyId).SingleAsync(ct);
            var archiveExternal = await db.Database.SqlQueryRaw<bool>("""
                SELECT EXISTS (
                    SELECT 1 FROM public."CalisanArsivleri" a
                    JOIN public."Calisanlar" c ON c."Id" = a."EskiCalisanId"
                    WHERE a."FirmaId" = {0} AND c."FirmaId" IS DISTINCT FROM a."FirmaId"
                ) AS "Value"
                """, companyId).SingleAsync(ct);
            var employeeExternal = await db.Database.SqlQueryRaw<bool>("""
                SELECT EXISTS (
                    SELECT 1 FROM public."Calisanlar" c
                    JOIN public."CalisanArsivleri" a ON a."EskiCalisanId" = c."Id"
                    WHERE c."FirmaId" = {0} AND a."FirmaId" IS DISTINCT FROM c."FirmaId"
                ) AS "Value"
                """, companyId).SingleAsync(ct);
            tables.Add(new("CalisanArsivleri", archiveCount, archiveExternal));
            if (employeeExternal)
            {
                var index = tables.FindIndex(x => x.Name == "Calisanlar");
                tables[index] = tables[index] with { HasExternalReferences = true };
            }
            tables.Sort((left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
        }

        var attachments = db.EkDosyalar.Select(x => new { x.FirmaId, Path = (string?)x.DosyaYolu });
        var checkImages = db.Cekler.Where(x => x.ResimYolu != null && x.ResimYolu != "")
            .Select(x => new { x.FirmaId, Path = x.ResimYolu });
        var logos = db.Firmalar.Where(x => x.LogoYolu != "" && !x.LogoYolu.StartsWith("data:"))
            .Select(x => new { FirmaId = x.Id, Path = (string?)x.LogoYolu });
        var fileReferences = attachments.Concat(checkImages).Concat(logos);
        var targetPaths = (await fileReferences.Where(x => x.FirmaId == companyId)
            .Select(x => x.Path).ToListAsync(ct)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var recognized = targetPaths.OfType<string>().Where(path => UploadPath.IsMatch(path))
            .Select(x => x.ToLowerInvariant()).ToArray();
        var shared = await fileReferences.Where(x =>
                x.FirmaId != companyId && x.Path != null && recognized.Contains(x.Path.ToLower()))
            .Select(x => x.Path!.ToLower()).Distinct().CountAsync(ct);
        var attachmentCount = await attachments.CountAsync(x => x.FirmaId == companyId, ct);
        var imageCount = await checkImages.CountAsync(x => x.FirmaId == companyId, ct);
        var logo = await db.Firmalar.Where(x => x.Id == companyId).Select(x => x.LogoYolu).SingleOrDefaultAsync(ct);
        var unverifiedLogo = !string.IsNullOrEmpty(logo) &&
            !(logo.StartsWith("data:", StringComparison.Ordinal) && logo.Contains(";base64,", StringComparison.Ordinal));
        await transaction.CommitAsync(ct);
        return new(tables, attachmentCount, imageCount, unverifiedLogo, shared, targetPaths.Length - recognized.Length);
    }

    internal static IEnumerable<(IEntityType Source, string Property)> IncomingLinks(
        IEntityType target, IEntityType[] model)
    {
        foreach (var fk in target.GetReferencingForeignKeys())
        {
            if (fk.DeclaringEntityType.ClrType == typeof(CompanyStoredFileReference))
            {
                yield return (fk.DeclaringEntityType, fk.Properties[0].Name);
                continue;
            }
            var idIndex = fk.PrincipalKey.Properties.Select((p, i) => (p, i)).Where(x => x.p.Name == "Id").Select(x => x.i).DefaultIfEmpty(-1).Single();
            if (idIndex < 0 || fk.PrincipalKey.Properties.Any(p => p.Name is not ("Id" or "FirmaId")))
                throw new NotSupportedException("Beklenmeyen firma silme ilişkisi.");
            yield return (fk.DeclaringEntityType, fk.Properties[idIndex].Name);
        }

        // Some historical references are scalar fields without a database FK.
        var additional = target.ClrType == typeof(Kullanici)
            ? new[] { (typeof(IslemGecmisi), "KullaniciId"), (typeof(OdemePlani), "OlusturanKullaniciId"),
                (typeof(OdemeHareketi), "OlusturanKullaniciId") }
            : target.ClrType == typeof(Calisan) ? new[] { (typeof(CalisanMaasArsiv), "CalisanId") } : [];
        foreach (var (type, property) in additional)
            yield return (model.Single(x => x.ClrType == type), property);
    }

    private static string Ownership(IEntityType entity, string alias)
    {
        if (entity.ClrType == typeof(FaturaKalem))
            return $"""(SELECT f."FirmaId" FROM "Faturalar" f WHERE f."Id" = {alias}."FaturaId")""";
        if (entity.ClrType == typeof(Firma))
            return $"{alias}.{Column(entity, "Id")}";
        return entity.FindProperty("FirmaId") is null ? "NULL" : $"{alias}.{Column(entity, "FirmaId")}";
    }

    private static string TableName(IEntityType entity) => Quote(entity.GetTableName()!);
    private static string Column(IEntityType entity, string name) =>
        Quote(entity.FindProperty(name)!.GetColumnName(StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()))!);
    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";
}
