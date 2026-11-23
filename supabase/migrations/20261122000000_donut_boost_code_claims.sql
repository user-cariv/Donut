create schema if not exists private;
revoke all on schema private from public, anon, authenticated;

create table if not exists private.donut_boost_code_claims (
  code_hash text primary key check (code_hash ~ '^[A-Fa-f0-9]{64}$'),
  hardware_id text not null check (char_length(btrim(hardware_id)) between 1 and 128),
  tier integer not null check (tier in (1, 2, 3, 4)),
  activated_at timestamptz not null,
  expires_at timestamptz not null
);

alter table private.donut_boost_code_claims enable row level security;
revoke all on table private.donut_boost_code_claims from public, anon, authenticated;

drop function if exists public.claim_donut_boost_code(text, text, integer);

create or replace function public.claim_donut_boost_code(
  p_code_hash text,
  p_hwid text,
  p_tier integer,
  p_activated_at timestamptz,
  p_expires_at timestamptz
)
returns jsonb
language plpgsql
security definer
set search_path = ''
as $$
declare
  v_now timestamptz := pg_catalog.now();
  v_activated_at timestamptz;
  v_expires_at timestamptz;
  v_hardware_id text;
  v_tier integer;
  v_inserted boolean := false;
begin
  if p_code_hash is null or p_code_hash !~ '^[A-Fa-f0-9]{64}$'
     or p_hwid is null or pg_catalog.char_length(pg_catalog.btrim(p_hwid)) not between 1 and 128
     or p_tier is null or p_tier not in (1, 2, 3, 4) then
    return pg_catalog.jsonb_build_object('status', 'REJECTED', 'message', 'Invalid activation claim.');
  end if;

  if (p_activated_at is null) <> (p_expires_at is null) then
    return pg_catalog.jsonb_build_object('status', 'REJECTED', 'message', 'Invalid activation period.');
  end if;

  if p_activated_at is null then
    v_activated_at := v_now;
    v_expires_at := case p_tier
      when 1 then v_now + interval '1 day'
      when 2 then v_now + interval '7 days'
      when 3 then v_now + interval '30 days'
      when 4 then v_now + interval '100 years'
    end;
  else
    if p_activated_at > v_now + interval '5 minutes'
       or p_expires_at <= p_activated_at
       or p_expires_at > p_activated_at + (case p_tier
         when 1 then interval '1 day 5 minutes'
         when 2 then interval '7 days 5 minutes'
         when 3 then interval '30 days 5 minutes'
         when 4 then interval '100 years 5 minutes'
       end) then
      return pg_catalog.jsonb_build_object('status', 'REJECTED', 'message', 'Invalid activation period.');
    end if;
    v_activated_at := p_activated_at;
    v_expires_at := p_expires_at;
  end if;

  insert into private.donut_boost_code_claims (code_hash, hardware_id, tier, activated_at, expires_at)
  values (pg_catalog.upper(p_code_hash), p_hwid, p_tier, v_activated_at, v_expires_at)
  on conflict (code_hash) do nothing
  returning true into v_inserted;

  if v_inserted then
    return pg_catalog.jsonb_build_object(
      'status', 'SUCCESS',
      'message', 'Activation code claimed for this device.',
      'activated_at', v_activated_at,
      'expires_at', v_expires_at
    );
  end if;

  select c.hardware_id, c.tier, c.activated_at, c.expires_at
    into v_hardware_id, v_tier, v_activated_at, v_expires_at
  from private.donut_boost_code_claims as c
  where c.code_hash = pg_catalog.upper(p_code_hash);

  if v_hardware_id = p_hwid and v_tier = p_tier then
    return pg_catalog.jsonb_build_object(
      'status', 'RESTORE_SAME_DEVICE',
      'message', 'This code is already bound to this device. Its original activation period was restored.',
      'activated_at', v_activated_at,
      'expires_at', v_expires_at
    );
  end if;

  if v_hardware_id = p_hwid then
    return pg_catalog.jsonb_build_object(
      'status', 'ALREADY_CLAIMED_THIS_DEVICE',
      'message', 'This code was already used on this device with a different timeline.'
    );
  end if;

  return pg_catalog.jsonb_build_object(
    'status', 'ALREADY_CLAIMED',
    'message', 'This activation code has already been activated on another device.'
  );
end;
$$;

-- Keep the original endpoint signature for already-installed clients. Those builds
-- accept SUCCESS and ALREADY_CLAIMED, so they receive the same one-device decision.
create or replace function public.claim_donut_boost_code(
  p_code_hash text,
  p_hwid text,
  p_tier integer
)
returns jsonb
language plpgsql
security definer
set search_path = ''
as $$
begin
  return public.claim_donut_boost_code(p_code_hash, p_hwid, p_tier, null, null);
end;
$$;

revoke all on function public.claim_donut_boost_code(text, text, integer, timestamptz, timestamptz) from public, authenticated;
grant execute on function public.claim_donut_boost_code(text, text, integer, timestamptz, timestamptz) to anon;
revoke all on function public.claim_donut_boost_code(text, text, integer) from public, authenticated;
grant execute on function public.claim_donut_boost_code(text, text, integer) to anon;

notify pgrst, 'reload schema';
