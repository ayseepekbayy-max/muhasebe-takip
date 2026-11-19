-- Included inside the approved installer's transaction. Never grants runtime EXECUTE.
-- Persistent conflict rows also invalidate stale REPEATABLE READ/SERIALIZABLE writers.
CREATE TABLE public.firmova_document_file_gates (file_id uuid PRIMARY KEY);
REVOKE ALL ON public.firmova_document_file_gates FROM PUBLIC;
CREATE FUNCTION public.firmova_document_file_id(value text) RETURNS uuid
LANGUAGE plpgsql IMMUTABLE SET search_path = pg_catalog, pg_temp AS $$
DECLARE s text; bytes bytea := ''::bytea; i integer := 1; m text[];
BEGIN
    IF value IS NULL OR value = '' OR lower(left(value,5) COLLATE "C") = 'data:' THEN RETURN NULL; END IF;
    IF length(value)>8192 THEN RAISE EXCEPTION 'Document reference path too long' USING ERRCODE='23514'; END IF;
    s := regexp_replace(value, '[?#].*$', '');
    WHILE i <= length(s) LOOP
        IF substr(s,i,1) = '%' AND substr(s,i+1,2) ~ '^[0-9a-fA-F]{2}$' THEN
            bytes := bytes || decode(substr(s,i+1,2),'hex'); i := i+3;
        ELSE bytes := bytes || convert_to(substr(s,i,1),'UTF8'); i := i+1;
        END IF;
    END LOOP;
    s := lower(replace(regexp_replace(convert_from(bytes,'UTF8'), '[?#].*$', ''), chr(92), '/') COLLATE "C");
    IF octet_length(s)<>length(s) AND s ~ '[0-9a-f]{8}-?(?:[0-9a-f]{4}-?){3}[0-9a-f]{12}' THEN
        RAISE EXCEPTION 'Ambiguous non-ASCII document identity path' USING ERRCODE='23514';
    END IF;
    m := regexp_match(s, '^/uploads/\.firmova-private/objects/(?:cekler|faturalar|cariler)/(?:fatura-[0-9]+-|cari-[0-9]+-)?([0-9a-f]{32}|[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12})\.(?:pdf|png|jpg|jpeg|webp|xlsx|xls|docx)$');
    IF m IS NULL THEN m := regexp_match(s, '^/companydocument/([0-9a-f]{32})\.[a-z0-9]+$'); END IF;
    IF m IS NOT NULL THEN RETURN m[1]::uuid; END IF;
    -- Existing recovery restores use fresh directory/file GUIDs, not tracked objects.
    -- Keep those links writable/readable; they remain unowned and BLOCK company deletion.
    IF s ~ '^/uploads/\.firmova-private/recovery/[0-9a-f]{32}/[0-9a-f]{32}\.(?:pdf|png|jpg|jpeg|webp|xlsx|xls|docx)$' THEN RETURN NULL; END IF;
    IF s LIKE '%/.firmova-private/%' OR s LIKE '%/companydocument/%' THEN
        RAISE EXCEPTION 'Ambiguous private document path' USING ERRCODE='23514';
    END IF;
    RETURN NULL;
END; $$;
REVOKE ALL ON FUNCTION public.firmova_document_file_id(text) FROM PUBLIC;

CREATE FUNCTION public.firmova_guard_document_file() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, pg_temp AS $$
DECLARE old_id uuid; new_id uuid; id uuid; old_row jsonb; new_row jsonb; column_name text; raw_reference boolean := false;
BEGIN
    IF TG_TABLE_SCHEMA <> 'public' OR TG_NARGS <> 0 OR TG_LEVEL <> 'ROW' THEN
        RAISE EXCEPTION 'Unexpected document trigger context';
    END IF;
    column_name := CASE TG_TABLE_NAME
        WHEN 'Cekler' THEN 'ResimYolu' WHEN 'EkDosyalar' THEN 'DosyaYolu'
        WHEN 'Firmalar' THEN 'LogoYolu' WHEN 'CompanyStoredFiles' THEN 'Id'
        WHEN 'CompanyStoredFileReferences' THEN 'FileId' WHEN 'CompanyFileCleanupJobs' THEN 'FileId'
        ELSE NULL END;
    IF column_name IS NULL THEN RAISE EXCEPTION 'Unexpected document trigger table'; END IF;
    raw_reference := TG_TABLE_NAME IN ('Cekler','EkDosyalar','Firmalar');
    IF TG_OP <> 'INSERT' THEN
        old_row := to_jsonb(OLD);
        old_id := CASE WHEN raw_reference THEN public.firmova_document_file_id(old_row->>column_name)
            ELSE (old_row->>column_name)::uuid END;
    END IF;
    IF TG_OP <> 'DELETE' THEN
        new_row := to_jsonb(NEW);
        new_id := CASE WHEN raw_reference THEN public.firmova_document_file_id(new_row->>column_name)
            ELSE (new_row->>column_name)::uuid END;
    END IF;
    FOR id IN SELECT DISTINCT v FROM unnest(ARRAY[old_id,new_id]) v WHERE v IS NOT NULL ORDER BY v LOOP
        -- Transaction-scoped, identical to the application's UUID gate. Hash collision can
        -- only add waiting, never omit mutual exclusion. No table-wide lock is taken.
        PERFORM pg_advisory_xact_lock(hashtextextended(id::text, 471006));
        INSERT INTO public.firmova_document_file_gates(file_id) VALUES(id)
        ON CONFLICT(file_id) DO UPDATE SET file_id=excluded.file_id;
    END LOOP;
    IF TG_TABLE_NAME='CompanyStoredFiles' AND TG_OP='UPDATE' AND
       old_row->>'RetiredAtUtc' IS NOT NULL AND new_row->>'RetiredAtUtc' IS NULL THEN
        RAISE EXCEPTION 'Retired document cannot be reactivated' USING ERRCODE='23514';
    END IF;
    IF new_id IS NOT NULL AND (raw_reference OR TG_TABLE_NAME='CompanyStoredFileReferences') AND
       (TG_OP='INSERT' OR old_row->>column_name IS DISTINCT FROM new_row->>column_name OR
        (TG_TABLE_NAME='CompanyStoredFileReferences' AND old_row IS DISTINCT FROM new_row)) AND
       EXISTS (SELECT FROM public."CompanyStoredFiles" f WHERE f."Id"=new_id AND f."RetiredAtUtc" IS NOT NULL) THEN
        RAISE EXCEPTION 'Retired document cannot acquire a reference' USING ERRCODE='23514';
    END IF;
    IF TG_OP='DELETE' THEN RETURN OLD; ELSE RETURN NEW; END IF;
END; $$;
REVOKE ALL ON FUNCTION public.firmova_guard_document_file() FROM PUBLIC;

CREATE TRIGGER firmova_document_file_gate BEFORE INSERT OR UPDATE OR DELETE ON public."Cekler"
FOR EACH ROW EXECUTE FUNCTION public.firmova_guard_document_file();
CREATE TRIGGER firmova_document_file_gate BEFORE INSERT OR UPDATE OR DELETE ON public."EkDosyalar"
FOR EACH ROW EXECUTE FUNCTION public.firmova_guard_document_file();
CREATE TRIGGER firmova_document_file_gate BEFORE INSERT OR UPDATE OR DELETE ON public."Firmalar"
FOR EACH ROW EXECUTE FUNCTION public.firmova_guard_document_file();
CREATE TRIGGER firmova_document_file_gate BEFORE INSERT OR UPDATE OR DELETE ON public."CompanyStoredFiles"
FOR EACH ROW EXECUTE FUNCTION public.firmova_guard_document_file();
CREATE TRIGGER firmova_document_file_gate BEFORE INSERT OR UPDATE OR DELETE ON public."CompanyStoredFileReferences"
FOR EACH ROW EXECUTE FUNCTION public.firmova_guard_document_file();
CREATE TRIGGER firmova_document_file_gate BEFORE INSERT OR UPDATE OR DELETE ON public."CompanyFileCleanupJobs"
FOR EACH ROW EXECUTE FUNCTION public.firmova_guard_document_file();
