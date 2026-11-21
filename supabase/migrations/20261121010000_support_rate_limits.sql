create schema if not exists private;
revoke all on schema private from public, anon, authenticated;

create table if not exists private.support_submission_rate_limits (
  rate_key text not null,
  created_at timestamptz not null default now()
);

create index if not exists support_submission_rate_limits_key_created_idx
  on private.support_submission_rate_limits (rate_key, created_at desc);
create index if not exists support_submission_rate_limits_created_idx
  on private.support_submission_rate_limits (created_at);

alter table private.support_submission_rate_limits enable row level security;
revoke all on private.support_submission_rate_limits from public, anon, authenticated;

create or replace function public.consume_support_submission_rate_limit(p_email text, p_ip inet)
returns boolean
language plpgsql
security definer
set search_path = ''
as $$
declare
  v_now timestamptz := pg_catalog.now();
  v_email_key text := 'support-email:' || pg_catalog.lower(pg_catalog.btrim(p_email));
  v_ip_key text := 'support-ip:' || p_ip::text;
  v_email_count integer;
  v_ip_count integer;
begin
  if p_email is null or pg_catalog.btrim(p_email) = '' or p_ip is null then
    return false;
  end if;

  perform pg_catalog.pg_advisory_xact_lock(pg_catalog.hashtextextended(v_email_key, 0));
  perform pg_catalog.pg_advisory_xact_lock(pg_catalog.hashtextextended(v_ip_key, 0));

  delete from private.support_submission_rate_limits
  where created_at < v_now - interval '1 day';

  select pg_catalog.count(*) into v_email_count
  from private.support_submission_rate_limits
  where rate_key = v_email_key and created_at >= v_now - interval '1 hour';

  select pg_catalog.count(*) into v_ip_count
  from private.support_submission_rate_limits
  where rate_key = v_ip_key and created_at >= v_now - interval '1 hour';

  if v_email_count >= 3 or v_ip_count >= 5 then
    return false;
  end if;

  insert into private.support_submission_rate_limits (rate_key, created_at)
  values (v_email_key, v_now), (v_ip_key, v_now);
  return true;
end;
$$;

revoke all on function public.consume_support_submission_rate_limit(text, inet) from public, anon, authenticated;
grant execute on function public.consume_support_submission_rate_limit(text, inet) to service_role;
