-- Explicitly allow the trusted submit-support Edge Function role to store
-- tickets. Client roles remain blocked by RLS and revoked table privileges.
begin;

grant usage on schema public to service_role;
grant select, insert, update, delete on table public.support_tickets to service_role;

commit;
