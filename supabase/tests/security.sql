begin;

select plan(32);

select ok(
  (select relrowsecurity from pg_catalog.pg_class where oid = 'public.support_tickets'::regclass),
  'support tickets have RLS enabled'
);
select ok(
  not has_table_privilege('anon', 'public.support_tickets', 'select'),
  'anonymous users cannot read support tickets'
);
select ok(
  not has_table_privilege('authenticated', 'public.support_tickets', 'update'),
  'signed-in users cannot modify support tickets'
);
select ok(
  (select not is_public from storage.buckets where id = 'support-attachments'),
  'support attachments are in a private bucket'
);
select ok(
  exists (select 1 from pg_catalog.pg_policies
    where schemaname = 'storage' and tablename = 'objects'
      and policyname = 'donut_support_attachments_client_block'
      and permissive = 'RESTRICTIVE'),
  'client roles are explicitly blocked from support attachment storage'
);
select ok(
  not has_function_privilege('anon', 'public.claim_donut_boost_code_v2(text,text)', 'execute'),
  'anonymous callers cannot execute the mutating license claim RPC'
);
select ok(
  not has_function_privilege('anon', 'public.claim_donut_boost_code(text,text,integer)', 'execute')
    and not has_function_privilege('anon', 'public.claim_donut_boost_code(text,text,integer,timestamp with time zone,timestamp with time zone)', 'execute'),
  'anonymous callers cannot probe legacy license claims'
);
select ok(
  has_function_privilege('service_role', 'public.claim_donut_boost_code_v2(text,text)', 'execute'),
  'the server role can execute the mutating license claim RPC'
);
select ok(
  not has_function_privilege('anon', 'public.claim_donut_boost_code_v3(text,text,boolean)', 'execute')
    and not has_function_privilege('authenticated', 'public.claim_donut_boost_code_v3(text,text,boolean)', 'execute'),
  'browser roles cannot execute the single-use license claim RPC'
);
select ok(
  has_function_privilege('service_role', 'public.claim_donut_boost_code_v3(text,text,boolean)', 'execute'),
  'the trusted server role can execute the single-use license claim RPC'
);
select ok(
  not has_table_privilege('anon', 'private.donut_boost_issued_codes', 'select'),
  'anonymous callers cannot read issued license code hashes'
);
select ok(
  not has_table_privilege('authenticated', 'private.donut_boost_code_claims', 'select'),
  'signed-in users cannot read license claims'
);
select ok(
  has_table_privilege('service_role', 'public.support_tickets', 'insert'),
  'the support function service role can save tickets'
);
select ok(
  not has_table_privilege('anon', 'public.support_tickets', 'insert')
    and not has_table_privilege('authenticated', 'public.support_tickets', 'select'),
  'browser clients cannot create or read support tickets directly'
);
select ok(
  not has_function_privilege('anon', 'public.issue_donut_boost_code(text,integer)', 'execute')
    and not has_function_privilege('anon', 'public.revoke_donut_boost_code(text)', 'execute'),
  'anonymous callers cannot issue or revoke licenses'
);
select is(
  (select pg_catalog.count(*) from pg_catalog.generate_series(1, 3)
    where public.consume_support_submission_rate_limit('security-test@example.invalid', '192.0.2.17'::inet)),
  3::bigint,
  'support submission rate limit allows the configured first three requests'
);
select is(
  public.consume_support_submission_rate_limit('security-test@example.invalid', '192.0.2.17'::inet),
  false,
  'support submission rate limit blocks the next request'
);
select is(
  (select pg_catalog.count(*) from pg_catalog.generate_series(1, 40)
    where public.consume_donut_boost_redemption_attempt('192.0.2.18'::inet)),
  40::bigint,
  'license redemption rate limit allows the configured first forty attempts'
);
select is(
  public.consume_donut_boost_redemption_attempt('192.0.2.18'::inet),
  false,
  'license redemption rate limit blocks the next attempt'
);

insert into private.donut_boost_issued_codes (code_hash, tier)
values ('AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA', 1)
on conflict (code_hash) do nothing;

select is(
  public.claim_donut_boost_code_v2(repeat('B', 64), 'security-test-device'),
  '{"status":"REJECTED"}'::jsonb,
  'unissued code hashes are rejected'
);
select is(
  public.claim_donut_boost_code_v2(repeat('A', 64), 'security-test-device')->>'status',
  'SUCCESS',
  'issued activation codes can be claimed'
);
select is(
  public.claim_donut_boost_code_v2(repeat('A', 64), 'different-security-test-device')->>'status',
  'ALREADY_CLAIMED',
  'one code cannot be claimed by another device'
);
select is(
  public.claim_donut_boost_code_v2(repeat('A', 64), 'security-test-device')->>'status',
  'RESTORE_SAME_DEVICE',
  'the original device can restore its existing claim'
);
select is(
  public.claim_donut_boost_code_v3(repeat('A', 64), 'security-test-device', false)->>'status',
  'ALREADY_CLAIMED_THIS_DEVICE',
  'a redeemed code cannot be redeemed again on the original device'
);
select is(
  public.claim_donut_boost_code_v3(repeat('A', 64), 'security-test-device', true)->>'status',
  'RESTORE_SAME_DEVICE',
  'saved activation restoration returns the original fixed entitlement'
);
select is(
  public.claim_donut_boost_code_v3(repeat('A', 64), 'different-security-test-device', true)->>'status',
  'ALREADY_CLAIMED',
  'restore mode cannot move a code to another device'
);
update private.donut_boost_code_claims
  set expires_at = pg_catalog.now() - interval '1 second'
  where code_hash = repeat('A', 64);
select is(
  public.claim_donut_boost_code_v2(repeat('A', 64), 'security-test-device')->>'status',
  'EXPIRED',
  'expired claims cannot be restored as active'
);
select is(
  public.claim_donut_boost_code_v3(repeat('A', 64), 'security-test-device', true)->>'status',
  'EXPIRED',
  'expired licenses remain expired through the restore path'
);
select is(
  public.claim_donut_boost_trial('security-trial-device')->>'status',
  'SUCCESS',
  'a device can receive the 24-hour trial once'
);
select is(
  public.claim_donut_boost_trial('security-trial-device')->>'status',
  'RESTORE_SAME_DEVICE',
  'a device can restore the original trial without restarting it'
);
select is(
  public.revoke_donut_boost_code(repeat('A', 64)),
  true,
  'an issued code can be revoked by the trusted server role'
);
select is(
  public.claim_donut_boost_code_v2(repeat('A', 64), 'security-test-device')->>'status',
  'REJECTED',
  'revoked activation codes are rejected'
);

select * from finish();
rollback;
