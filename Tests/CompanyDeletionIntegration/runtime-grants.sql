-- Synthetic fixture baseline only. Mirrors reviewed runtime/document grants; not a production installer.
GRANT USAGE ON SCHEMA public TO :"runtime_role";
GRANT SELECT, INSERT, DELETE ON public."Kullanicilar" TO :"runtime_role";
GRANT UPDATE ("KullaniciAdi", "Email", "OdemeEmailBildirimiAktifMi", "EmailDogrulandiMi",
    "Sifre", "SifreSifirlamaKodu", "SifreSifirlamaKodGecerlilik", "Rol")
    ON public."Kullanicilar" TO :"runtime_role";
GRANT SELECT, INSERT ON public."Firmalar" TO :"runtime_role";
GRANT UPDATE ("FirmaAdi", "AktifMi", "Adres", "Telefon", "Email", "VergiDairesi", "VergiNo", "LogoYolu",
    "MenuCariKartlar", "MenuKasa", "MenuRaporlar", "MenuOdemeler", "MenuCalisanlar", "MenuMusteriler",
    "MenuStoklar", "MenuMaliyet", "MenuCekler") ON public."Firmalar" TO :"runtime_role";

GRANT SELECT, INSERT, UPDATE, DELETE ON
    public."CalisanArsivleri", public."CalisanAvanslari", public."CalisanMaasArsivleri",
    public."CalisanPuantajlari", public."Calisanlar", public."CariKartlar", public."Cekler",
    public."EkDosyalar", public."FaturaKalemleri", public."FaturaNumaraAyarlari", public."Faturalar",
    public."IslemGecmisleri", public."KasaHareketleri", public."KasaHesaplar", public."KasaTransferler",
    public."MaliyetKayitlari", public."MusteriIsler", public."MusteriMasraflar", public."Musteriler",
    public."OdemeBildirimGecmisleri", public."OdemeBildirimGizlemeleri", public."OdemeHareketleri",
    public."OdemePlanlari", public."PrivateMessageHidden", public."PrivateMessages", public."PrivatePresence",
    public."StokHareketleri", public."StokUrunler", public."YoneticiNotlari"
    TO :"runtime_role";
GRANT SELECT, INSERT, DELETE ON public."PrivateMediaContents" TO :"runtime_role";
GRANT SELECT ("Id", "UsedBytes"), UPDATE ("UsedBytes") ON public."PrivateMediaQuotas" TO :"runtime_role";

SELECT to_regclass('public."CompanyStoredFiles"') IS NOT NULL AS tracked_documents
\gset
\if :tracked_documents
GRANT SELECT, INSERT ON public."CompanyStoredFiles", public."CompanyStoredFileReferences",
    public."CompanyFileCleanupJobs" TO :"runtime_role";
REVOKE DELETE ON public."CompanyStoredFileReferences" FROM :"runtime_role";
GRANT UPDATE ("RetiredAtUtc") ON public."CompanyStoredFiles" TO :"runtime_role";
GRANT UPDATE ("CompletedAtUtc") ON public."CompanyFileCleanupJobs" TO :"runtime_role";
\endif

-- Grant sequence usage only when its owning table was explicitly opened for INSERT.
SELECT format('GRANT USAGE ON SEQUENCE %I.%I TO %I', n.nspname, s.relname, :'runtime_role')
FROM pg_catalog.pg_class s
JOIN pg_catalog.pg_namespace n ON n.oid = s.relnamespace
JOIN pg_catalog.pg_depend d ON d.classid = 'pg_catalog.pg_class'::regclass AND d.objid = s.oid
    AND d.refclassid = 'pg_catalog.pg_class'::regclass AND d.deptype IN ('a', 'i')
WHERE n.nspname = 'public' AND s.relkind = 'S'
  AND has_table_privilege(:'runtime_role', d.refobjid, 'INSERT')
\gexec
