using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MuhasebeTakip2.App.Migrations
{
    /// <inheritdoc />
    public partial class AddPostgresPrivateMedia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PrivateMediaContents",
                columns: table => new
                {
                    MessageId = table.Column<int>(type: "integer", nullable: false),
                    MediaKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Payload = table.Column<byte[]>(type: "bytea", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrivateMediaContents", x => x.MessageId);
                    table.CheckConstraint("CK_PrivateMediaContent_Expiry", "\"ExpiresAtUtc\" - \"CreatedAtUtc\" = interval '24 hours'");
                    table.CheckConstraint("CK_PrivateMediaContent_Key", "\"MediaKey\" ~ '^[0-9a-f]{32}$'");
                    table.CheckConstraint("CK_PrivateMediaContent_Size", "octet_length(\"Payload\") BETWEEN 1 AND 16777216");
                    table.ForeignKey(
                        name: "FK_PrivateMediaContents_PrivateMessages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "PrivateMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PrivateMediaQuotas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UsedBytes = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrivateMediaQuotas", x => x.Id);
                    table.CheckConstraint("CK_PrivateMediaQuota_Limit", "\"Id\" = 1 AND \"UsedBytes\" BETWEEN 0 AND 104857600");
                });

            migrationBuilder.InsertData(
                table: "PrivateMediaQuotas",
                columns: new[] { "Id", "UsedBytes" },
                values: new object[] { 1, 0L });

            migrationBuilder.CreateIndex(
                name: "IX_PrivateMediaContents_ExpiresAtUtc",
                table: "PrivateMediaContents",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PrivateMediaContents_MediaKey",
                table: "PrivateMediaContents",
                column: "MediaKey",
                unique: true);
            migrationBuilder.Sql("""
                ALTER TABLE public."PrivateMediaContents" SET (
                    autovacuum_vacuum_scale_factor = 0.05,
                    autovacuum_vacuum_threshold = 1,
                    toast.autovacuum_vacuum_scale_factor = 0.05,
                    toast.autovacuum_vacuum_threshold = 1
                );
                CREATE FUNCTION public.firmova_private_media_quota() RETURNS trigger
                LANGUAGE plpgsql SET search_path = pg_catalog, public AS $body$
                DECLARE delta_bytes bigint; media_kind integer;
                BEGIN
                    IF TG_OP = 'UPDATE' THEN
                        RAISE EXCEPTION USING ERRCODE = 'P0001', MESSAGE = 'PRIVATE_MEDIA_IMMUTABLE';
                    END IF;
                    IF TG_OP = 'INSERT' THEN
                        SELECT "Kind" INTO media_kind FROM public."PrivateMessages"
                        WHERE "Id" = NEW."MessageId" AND "MediaKey" = NEW."MediaKey"
                          AND "CreatedAtUtc" = NEW."CreatedAtUtc" AND "Kind" IN (1, 2);
                        IF NOT FOUND THEN
                            RAISE EXCEPTION USING ERRCODE = 'P0001', MESSAGE = 'PRIVATE_MEDIA_METADATA';
                        END IF;
                        IF media_kind = 2 AND octet_length(NEW."Payload") > 8388608 THEN
                            RAISE EXCEPTION USING ERRCODE = 'P0001', MESSAGE = 'PRIVATE_MEDIA_SIZE';
                        END IF;
                        delta_bytes := octet_length(NEW."Payload");
                    ELSE
                        delta_bytes := -octet_length(OLD."Payload");
                    END IF;
                    -- UPDATE obtains the singleton row lock and rechecks the predicate after waiting.
                    -- This also runs on FK cascades and on writes made outside EF Core.
                    UPDATE public."PrivateMediaQuotas" SET "UsedBytes" = "UsedBytes" + delta_bytes
                    WHERE "Id" = 1 AND "UsedBytes" + delta_bytes BETWEEN 0 AND 104857600;
                    IF NOT FOUND THEN
                        RAISE EXCEPTION USING ERRCODE = 'P0001', MESSAGE = 'PRIVATE_MEDIA_QUOTA';
                    END IF;
                    IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
                    RETURN NEW;
                END;
                $body$;
                CREATE TRIGGER firmova_private_media_quota_trigger
                BEFORE INSERT OR UPDATE OR DELETE ON public."PrivateMediaContents"
                FOR EACH ROW EXECUTE FUNCTION public.firmova_private_media_quota();
                CREATE FUNCTION public.firmova_private_media_no_truncate() RETURNS trigger
                LANGUAGE plpgsql AS $body$
                BEGIN
                    RAISE EXCEPTION USING ERRCODE = 'P0001', MESSAGE = 'PRIVATE_MEDIA_TRUNCATE_FORBIDDEN';
                END;
                $body$;
                CREATE TRIGGER firmova_private_media_no_truncate_trigger
                BEFORE TRUNCATE ON public."PrivateMediaContents"
                FOR EACH STATEMENT EXECUTE FUNCTION public.firmova_private_media_no_truncate();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER firmova_private_media_quota_trigger ON public."PrivateMediaContents";
                DROP TRIGGER firmova_private_media_no_truncate_trigger ON public."PrivateMediaContents";
                DROP FUNCTION public.firmova_private_media_quota();
                DROP FUNCTION public.firmova_private_media_no_truncate();
                """);

            migrationBuilder.DropTable(
                name: "PrivateMediaContents");

            migrationBuilder.DropTable(
                name: "PrivateMediaQuotas");
        }
    }
}
