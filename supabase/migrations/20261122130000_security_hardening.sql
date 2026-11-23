-- Forward-only security hardening. Existing support tickets and claimed license
-- records are preserved. Apply to a backup or isolated branch before production.

create schema if not exists private;
revoke all on schema private from public, anon, authenticated;

alter table public.support_tickets enable row level security;
revoke all on table public.support_tickets from public, anon, authenticated;
grant select, insert, update, delete on table public.support_tickets to service_role;
alter table private.support_submission_rate_limits enable row level security;
revoke all on table private.support_submission_rate_limits from public, anon, authenticated;
grant execute on function public.consume_support_submission_rate_limit(text, inet) to service_role;

update storage.buckets set public = false where id = 'support-attachments';
drop policy if exists donut_support_attachments_client_block on storage.objects;
create policy donut_support_attachments_client_block
  on storage.objects as restrictive for all to anon, authenticated
  using (bucket_id <> 'support-attachments')
  with check (bucket_id <> 'support-attachments');
revoke all on table private.donut_boost_code_claims from public, anon, authenticated;

-- The server-side issuance ledger prevents a client from inventing a code hash
-- and selecting its own tier or expiry. Import already claimed codes verbatim.
create table if not exists private.donut_boost_issued_codes (
  code_hash text primary key check (code_hash ~ '^[A-F0-9]{64}$'),
  tier integer not null check (tier in (1, 2, 3, 4)),
  issued_at timestamptz not null default pg_catalog.now(),
  revoked_at timestamptz,
  check (revoked_at is null or revoked_at >= issued_at)
);
alter table private.donut_boost_issued_codes enable row level security;
revoke all on table private.donut_boost_issued_codes from public, anon, authenticated;
insert into private.donut_boost_issued_codes (code_hash, tier, issued_at)
select pg_catalog.upper(code_hash), tier, activated_at
from private.donut_boost_code_claims
on conflict (code_hash) do nothing;

create table if not exists private.donut_boost_redemption_attempts (
  client_ip inet not null,
  attempted_at timestamptz not null default pg_catalog.now()
);
create index if not exists donut_boost_redemption_attempts_at_idx
  on private.donut_boost_redemption_attempts (client_ip, attempted_at desc);
create index if not exists donut_boost_redemption_attempts_cleanup_idx
  on private.donut_boost_redemption_attempts (attempted_at);
alter table private.donut_boost_redemption_attempts enable row level security;
revoke all on table private.donut_boost_redemption_attempts from public, anon, authenticated;

create or replace function public.consume_donut_boost_redemption_attempt(p_client_ip inet)
returns boolean language plpgsql security definer set search_path = '' as $$
declare
  v_now timestamptz := pg_catalog.now();
  v_count integer;
begin
  if p_client_ip is null then return false; end if;
  perform pg_catalog.pg_advisory_xact_lock(pg_catalog.hashtextextended(p_client_ip::text, 801743));
  delete from private.donut_boost_redemption_attempts
    where attempted_at < v_now - interval '1 day';
  select pg_catalog.count(*) into v_count
    from private.donut_boost_redemption_attempts
    where client_ip = p_client_ip and attempted_at >= v_now - interval '1 hour';
  if v_count >= 40 then return false; end if;
  insert into private.donut_boost_redemption_attempts (client_ip, attempted_at) values (p_client_ip, v_now);
  return true;
end;
$$;
revoke all on function public.consume_donut_boost_redemption_attempt(inet) from public, anon, authenticated;
grant execute on function public.consume_donut_boost_redemption_attempt(inet) to service_role;

create table if not exists private.donut_boost_trial_claims (
  hardware_id text primary key check (char_length(btrim(hardware_id)) between 1 and 128),
  activated_at timestamptz not null,
  expires_at timestamptz not null check (expires_at > activated_at)
);
alter table private.donut_boost_trial_claims enable row level security;
revoke all on table private.donut_boost_trial_claims from public, anon, authenticated;

create or replace function public.claim_donut_boost_trial(p_hwid text)
returns jsonb language plpgsql security definer set search_path = '' as $$
declare
  v_now timestamptz := pg_catalog.now();
  v_activated_at timestamptz;
  v_expires_at timestamptz;
begin
  if p_hwid is null or pg_catalog.char_length(pg_catalog.btrim(p_hwid)) not between 1 and 128 then
    return pg_catalog.jsonb_build_object('status', 'REJECTED');
  end if;
  insert into private.donut_boost_trial_claims (hardware_id, activated_at, expires_at)
  values (pg_catalog.btrim(p_hwid), v_now, v_now + interval '24 hours')
  on conflict (hardware_id) do nothing
  returning activated_at, expires_at into v_activated_at, v_expires_at;
  if v_activated_at is null then
    select t.activated_at, t.expires_at into v_activated_at, v_expires_at
      from private.donut_boost_trial_claims t
      where t.hardware_id = pg_catalog.btrim(p_hwid);
    if v_activated_at is null then
      return pg_catalog.jsonb_build_object('status', 'ALREADY_USED');
    end if;
    return pg_catalog.jsonb_build_object('status', 'RESTORE_SAME_DEVICE', 'tier', 5,
      'activated_at', v_activated_at, 'expires_at', v_expires_at);
  end if;
  return pg_catalog.jsonb_build_object('status', 'SUCCESS', 'tier', 5,
    'activated_at', v_activated_at, 'expires_at', v_expires_at);
end;
$$;
revoke all on function public.claim_donut_boost_trial(text) from public, anon, authenticated;
grant execute on function public.claim_donut_boost_trial(text) to service_role;

create or replace function public.issue_donut_boost_code(p_code_hash text, p_tier integer)
returns boolean language plpgsql security definer set search_path = '' as $$
begin
  if p_code_hash is null or p_code_hash !~ '^[A-Fa-f0-9]{64}$' or p_tier is null or p_tier not in (1, 2, 3, 4) then
    return false;
  end if;
  insert into private.donut_boost_issued_codes (code_hash, tier)
  values (pg_catalog.upper(p_code_hash), p_tier)
  on conflict (code_hash) do nothing;
  return found;
end;
$$;
revoke all on function public.issue_donut_boost_code(text, integer) from public, anon, authenticated;
grant execute on function public.issue_donut_boost_code(text, integer) to service_role;

create or replace function public.revoke_donut_boost_code(p_code_hash text)
returns boolean language plpgsql security definer set search_path = '' as $$
begin
  if p_code_hash is null or p_code_hash !~ '^[A-Fa-f0-9]{64}$' then return false; end if;
  update private.donut_boost_issued_codes
    set revoked_at = coalesce(revoked_at, pg_catalog.now())
    where code_hash = pg_catalog.upper(p_code_hash);
  return found;
end;
$$;
revoke all on function public.revoke_donut_boost_code(text) from public, anon, authenticated;
grant execute on function public.revoke_donut_boost_code(text) to service_role;

-- Only Edge Functions running with the server secret can create a claim.
create or replace function public.claim_donut_boost_code_v2(p_code_hash text, p_hwid text)
returns jsonb language plpgsql security definer set search_path = '' as $$
declare
  v_now timestamptz := pg_catalog.now();
  v_tier integer;
  v_revoked_at timestamptz;
  v_activated_at timestamptz;
  v_expires_at timestamptz;
  v_hwid text;
  v_claim_tier integer;
  v_inserted boolean := false;
begin
  if p_code_hash is null or p_code_hash !~ '^[A-Fa-f0-9]{64}$'
     or p_hwid is null or pg_catalog.char_length(pg_catalog.btrim(p_hwid)) not between 1 and 128 then
    return pg_catalog.jsonb_build_object('status', 'REJECTED');
  end if;
  select i.tier, i.revoked_at into v_tier, v_revoked_at
    from private.donut_boost_issued_codes i
    where i.code_hash = pg_catalog.upper(p_code_hash) for update;
  if not found or v_revoked_at is not null then
    return pg_catalog.jsonb_build_object('status', 'REJECTED');
  end if;
  insert into private.donut_boost_code_claims (code_hash, hardware_id, tier, activated_at, expires_at)
  values (
    pg_catalog.upper(p_code_hash), pg_catalog.btrim(p_hwid), v_tier, v_now,
    case v_tier when 1 then v_now + interval '1 day'
      when 2 then v_now + interval '7 days'
      when 3 then v_now + interval '30 days'
      when 4 then v_now + interval '100 years' end
  )
  on conflict (code_hash) do nothing
  returning true, activated_at, expires_at into v_inserted, v_activated_at, v_expires_at;
  if v_inserted then
    return pg_catalog.jsonb_build_object('status', 'SUCCESS', 'tier', v_tier,
      'activated_at', v_activated_at, 'expires_at', v_expires_at);
  end if;
  select c.hardware_id, c.tier, c.activated_at, c.expires_at
    into v_hwid, v_claim_tier, v_activated_at, v_expires_at
    from private.donut_boost_code_claims c
    where c.code_hash = pg_catalog.upper(p_code_hash);
  if v_hwid = pg_catalog.btrim(p_hwid) and v_claim_tier = v_tier then
    if v_expires_at <= v_now then
      return pg_catalog.jsonb_build_object('status', 'EXPIRED');
    end if;
    return pg_catalog.jsonb_build_object('status', 'RESTORE_SAME_DEVICE', 'tier', v_tier,
      'activated_at', v_activated_at, 'expires_at', v_expires_at);
  end if;
  return pg_catalog.jsonb_build_object('status', 'ALREADY_CLAIMED');
end;
$$;
revoke all on function public.claim_donut_boost_code_v2(text, text) from public, anon, authenticated;
grant execute on function public.claim_donut_boost_code_v2(text, text) to service_role;

-- Historical APIs are retained for installed clients but made read-only: they
-- can restore an existing claim for its bound device, never create or extend one.
create or replace function public.claim_donut_boost_code(
  p_code_hash text, p_hwid text, p_tier integer,
  p_activated_at timestamptz, p_expires_at timestamptz
)
returns jsonb language plpgsql security definer set search_path = '' as $$
declare
  v_hwid text; v_tier integer; v_activated_at timestamptz; v_expires_at timestamptz;
begin
  if p_code_hash is null or p_code_hash !~ '^[A-Fa-f0-9]{64}$' or p_hwid is null then
    return pg_catalog.jsonb_build_object('status', 'REJECTED', 'message', 'Activation could not be verified.');
  end if;
  select c.hardware_id, c.tier, c.activated_at, c.expires_at
    into v_hwid, v_tier, v_activated_at, v_expires_at
    from private.donut_boost_code_claims c
    where c.code_hash = pg_catalog.upper(p_code_hash);
  if not found or v_hwid <> pg_catalog.btrim(p_hwid) or v_tier <> p_tier
     or (p_activated_at is not null and p_activated_at <> v_activated_at)
     or (p_expires_at is not null and p_expires_at <> v_expires_at)
     or exists (select 1 from private.donut_boost_issued_codes i
       where i.code_hash = pg_catalog.upper(p_code_hash) and i.revoked_at is not null) then
    return pg_catalog.jsonb_build_object('status', 'REJECTED', 'message', 'Activation could not be verified.');
  end if;
  return pg_catalog.jsonb_build_object('status', 'RESTORE_SAME_DEVICE',
    'message', 'Existing activation restored.', 'activated_at', v_activated_at, 'expires_at', v_expires_at);
end;
$$;

create or replace function public.claim_donut_boost_code(p_code_hash text, p_hwid text, p_tier integer)
returns jsonb language plpgsql security definer set search_path = '' as $$
declare
  v_result jsonb;
begin
  v_result := public.claim_donut_boost_code(p_code_hash, p_hwid, p_tier, null, null);
  if v_result->>'status' = 'RESTORE_SAME_DEVICE' then
    return pg_catalog.jsonb_set(v_result, '{status}', '"SUCCESS"'::jsonb);
  end if;
  return v_result;
end;
$$;
revoke all on function public.claim_donut_boost_code(text, text, integer, timestamptz, timestamptz) from public, authenticated;
grant execute on function public.claim_donut_boost_code(text, text, integer, timestamptz, timestamptz) to anon;
revoke all on function public.claim_donut_boost_code(text, text, integer) from public, authenticated;
grant execute on function public.claim_donut_boost_code(text, text, integer) to anon;

notify pgrst, 'reload schema';
