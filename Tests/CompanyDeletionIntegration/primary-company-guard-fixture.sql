\set ON_ERROR_STOP on
BEGIN;
SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '30s';

-- Set main_admin_user_id in psql to the verified AdminPortal__MainAdminUserId.
CREATE TABLE public.firmova_company_deletion_owner (
    singleton boolean PRIMARY KEY DEFAULT true CHECK (singleton),
    user_id integer NOT NULL REFERENCES public."Kullanicilar" ("Id")
        ON DELETE RESTRICT ON UPDATE RESTRICT
);
-- A missing or ineligible account fails the NOT NULL constraint and rolls back installation.
INSERT INTO public.firmova_company_deletion_owner (user_id)
VALUES ((
    SELECT u."Id"
    FROM public."Kullanicilar" u JOIN public."Firmalar" f ON f."Id" = u."FirmaId"
    WHERE u."Id" = :'main_admin_user_id'::integer AND u."Rol" = 'SuperAdmin' AND f."AktifMi"
      AND EXISTS (SELECT 1 FROM public."Firmalar" WHERE "Id" = 1)
    FOR SHARE OF u, f
));
REVOKE ALL ON public.firmova_company_deletion_owner FROM PUBLIC;

CREATE FUNCTION public.firmova_guard_primary_company() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, pg_temp AS $$
DECLARE
    owner_company_id integer;
BEGIN
    IF TG_OP = 'TRUNCATE' THEN
        RAISE EXCEPTION 'Firma ID=1 korunuyor' USING ERRCODE = '23514';
    END IF;
    SELECT u."FirmaId" INTO STRICT owner_company_id
        FROM public.firmova_company_deletion_owner o
        JOIN public."Kullanicilar" u ON u."Id" = o.user_id
        WHERE o.singleton FOR SHARE OF u;
    IF OLD."Id" IN (1, owner_company_id) THEN
        IF TG_OP = 'DELETE' THEN
            RAISE EXCEPTION 'Ana firma korunuyor' USING ERRCODE = '23514';
        ELSIF NEW."Id" IS DISTINCT FROM OLD."Id" THEN
            RAISE EXCEPTION 'Ana firma korunuyor' USING ERRCODE = '23514';
        END IF;
    END IF;
    IF TG_OP = 'DELETE' THEN
        RETURN OLD;
    END IF;
    RETURN NEW;
END;
$$;

CREATE TRIGGER firmova_guard_primary_company_row
BEFORE DELETE OR UPDATE OF "Id" ON public."Firmalar"
FOR EACH ROW EXECUTE FUNCTION public.firmova_guard_primary_company();

CREATE TRIGGER firmova_guard_primary_company_truncate
BEFORE TRUNCATE ON public."Firmalar"
FOR EACH STATEMENT EXECUTE FUNCTION public.firmova_guard_primary_company();

REVOKE ALL ON FUNCTION public.firmova_guard_primary_company() FROM PUBLIC;
COMMIT;
