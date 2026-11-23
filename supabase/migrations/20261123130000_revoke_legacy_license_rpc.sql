-- The hardened licensing endpoint is the only client entry point for claims.
-- The compatibility RPC now returns only restore status, but still reveals
-- whether a code hash is registered and which hardware id claimed it.
-- Existing locally activated installations remain valid; clients using the
-- retired anon RPC must upgrade before attempting online license restoration.
begin;

revoke all on function public.claim_donut_boost_code(text, text, integer, timestamptz, timestamptz)
  from public, anon, authenticated;
revoke all on function public.claim_donut_boost_code(text, text, integer)
  from public, anon, authenticated;

commit;

notify pgrst, 'reload schema';
