-- A typed activation is one-use even on the same device. Saved installations
-- use the explicit restore path, which returns the original fixed expiry.
-- Existing claims and issued-code records are preserved.

create or replace function public.claim_donut_boost_code_v3(
  p_code_hash text,
  p_hwid text,
  p_allow_restore boolean default false
)
returns jsonb
language plpgsql
security definer
set search_path = ''
as $$
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
     or p_hwid is null or pg_catalog.char_length(pg_catalog.btrim(p_hwid)) not between 1 and 128
     or p_allow_restore is null then
    return pg_catalog.jsonb_build_object('status', 'REJECTED');
  end if;

  -- Serialize every redemption of a code, including requests from different devices.
  select i.tier, i.revoked_at into v_tier, v_revoked_at
    from private.donut_boost_issued_codes i
    where i.code_hash = pg_catalog.upper(p_code_hash)
    for update;
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

  if v_hwid is distinct from pg_catalog.btrim(p_hwid) or v_claim_tier is distinct from v_tier then
    return pg_catalog.jsonb_build_object('status', 'ALREADY_CLAIMED');
  end if;
  if not p_allow_restore then
    return pg_catalog.jsonb_build_object('status', 'ALREADY_CLAIMED_THIS_DEVICE');
  end if;
  if v_expires_at <= v_now then
    return pg_catalog.jsonb_build_object('status', 'EXPIRED');
  end if;
  return pg_catalog.jsonb_build_object('status', 'RESTORE_SAME_DEVICE', 'tier', v_tier,
    'activated_at', v_activated_at, 'expires_at', v_expires_at);
end;
$$;

revoke all on function public.claim_donut_boost_code_v3(text, text, boolean) from public, anon, authenticated;
grant execute on function public.claim_donut_boost_code_v3(text, text, boolean) to service_role;

notify pgrst, 'reload schema';
