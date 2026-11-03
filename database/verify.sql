select tablename, rowsecurity from pg_tables
where schemaname = 'public' and tablename like 'mp_%' order by tablename;

select tablename, policyname, roles, qual, with_check from pg_policies
where schemaname = 'public' and tablename like 'mp_%' order by tablename;

select table_name, grantee, privilege_type from information_schema.role_table_grants
where table_schema = 'public' and table_name like 'mp_%'
and grantee in ('anon','authenticated') order by table_name, grantee, privilege_type;

