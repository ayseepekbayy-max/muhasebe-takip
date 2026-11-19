\set ON_ERROR_STOP on
CREATE ROLE firmova_app_runtime LOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS NOREPLICATION;
\set runtime_role firmova_app_runtime
\i /tests/runtime-grants.sql
