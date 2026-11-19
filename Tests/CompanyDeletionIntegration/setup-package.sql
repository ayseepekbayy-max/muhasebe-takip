-- Synthetic fixture only. Not a production installer.
\set ON_ERROR_STOP on
DO $$ BEGIN
 IF current_database()<>'firmova_stage10_synthetic' THEN RAISE EXCEPTION 'Synthetic database required'; END IF;
END; $$;
\set ON_ERROR_STOP on
\set expected_database firmova_stage10_synthetic
\set main_admin_user_id 101
CREATE ROLE firmova_app_runtime LOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS NOREPLICATION;
CREATE ROLE synthetic_observer NOLOGIN;
\set runtime_role firmova_app_runtime
\i /tests/runtime-grants.sql
INSERT INTO public."Firmalar" ("Id","FirmaAdi","AktifMi","Adres","Telefon","Email","VergiDairesi","VergiNo","LogoYolu",
 "MenuCariKartlar","MenuKasa","MenuRaporlar","MenuOdemeler","MenuCalisanlar","MenuMusteriler","MenuStoklar","MenuMaliyet","MenuCekler")
SELECT id, 'Synthetic', true,'','','','','','',true,true,true,true,true,true,true,true,true FROM unnest(ARRAY[1,72,73,74]) id;
INSERT INTO public."Kullanicilar" ("Id","FirmaId","KullaniciAdi","Email","OdemeEmailBildirimiAktifMi","EmailDogrulandiMi","Sifre","Rol")
VALUES (101,72,'Synthetic','','false','false','','SuperAdmin');
\i /tests/primary-company-guard-fixture.sql
