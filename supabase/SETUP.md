# Supabase backend security deployment

This repository contains a static GitHub Pages site, Supabase Edge Functions,
and a Windows client. The browser publishable key is public by design; all
authorization must be enforced by PostgreSQL policies or server-side functions.
Never place a Supabase secret/service-role key, mail key, issuer token, or signing
private key in `Website/`, a desktop client, Git, or a Pages artifact.

## Back up and deploy database changes

Before production deployment, take a Supabase backup and inspect the resulting
schema diff in an isolated project or branch. Do not reset the production
database. Then deploy the forward-only migrations:

```powershell
supabase login
supabase link --project-ref qigzqfntaokxnsfcejiy
supabase db push --dry-run
supabase db push
```

The hardening migration preserves current support rows and copies every
currently claimed activation hash into the protected issuance ledger. Any valid
codes sold but not yet claimed that are tracked outside that database need to be
reissued/imported through a reviewed admin process before enabling the new
licensing endpoint; the old desktop HMAC key is intentionally not retained in
the client to validate arbitrary codes. Do not delete or rewrite the old claims
table. If a migration fails, stop and inspect the transaction/error before
retrying; these migrations are forward-only.

### Repair the current support save error by itself

If support form submissions still say they could not be saved, open the
Supabase SQL Editor for the Donut Support project and run this non-destructive
permission repair. It grants access only to the server's `service_role`; browser
roles remain revoked and blocked by RLS:

```sql
grant usage on schema public to service_role;
grant select, insert, update, delete on table public.support_tickets to service_role;
```

Then submit a test request again. Do not use the full `db push` just to apply
this support-only repair when the other pending licensing changes and secrets
have not been reviewed.

Run database tests in the local Supabase stack, never against production:

```powershell
supabase start
supabase test db
```

## Configure Edge Function secrets

In Supabase Dashboard → Edge Functions → Secrets, set:

- `ALLOWED_ORIGINS=https://user-cariv.github.io,http://127.0.0.1:4173,http://localhost:4173`
- `SUPPORT_INBOX` to the private inbox where tickets should arrive
- `SUPPORT_FROM` to the sender identity verified with the selected mail provider
- Either `RESEND_API_KEY` (Resend; requires a verified domain) or `SENDGRID_API_KEY` (SendGrid)
- `DONUT_LICENSE_ISSUER_TOKEN` to a randomly generated, private admin token
- `DONUT_LICENSE_SIGNING_PRIVATE_KEY` to the PEM from the local ignored file `.tools/boost-license-private.pem`
- `DONUT_LICENSE_KEY_ID=2026-11`

Keep `.tools/boost-license-private.pem` backed up in a password manager or
secrets vault and never commit or send it in chat. The matching public key is
embedded in the Windows client. Supabase provides its URL and service-role
secret to deployed Edge Functions; do not copy either into the website.

For a temporary no-domain setup, SendGrid can verify one sender by sending a
verification email to that address. Set `SUPPORT_FROM` to that exact verified
address and use `SENDGRID_API_KEY`; set `SUPPORT_INBOX` to the mailbox receiving
requests. Large inbox providers such as Gmail may reject or spam messages sent
through a third-party service because the sender domain's SPF/DKIM alignment
does not match. SendGrid describes single-sender verification as testing only;
use a domain you control for reliable production delivery. If both provider keys
are configured, the function prefers SendGrid.

Deploy functions after secrets and migrations are ready:

```powershell
supabase functions deploy submit-support --no-verify-jwt
supabase functions deploy redeem-boost-license --no-verify-jwt
supabase functions deploy issue-boost-license --no-verify-jwt
```

`issue-boost-license` is an admin-only endpoint protected by the issuer token.
Use `scripts/boost-license-admin.ps1` to issue a code or revoke a code. Keep the
one-time code private and deliver it only after payment confirmation. Issued
hashes, tiers, activation timestamps, expiry and revocation are decided by
PostgreSQL; the client cannot select those values.

## Support attachments and rate limits

The bucket is private. The browser can submit a form but cannot read tickets,
edit tickets, or download attachments. The Edge Function validates file types,
signatures, counts, sizes, randomized paths, and rate limits before it creates
short-lived links for notification emails. Email delivery is a side effect: a
mail outage does not discard a saved ticket.

The form currently limits submissions by email and the client IP supplied by
Supabase's gateway. A public form can still receive distributed spam; configure
CAPTCHA/WAF protection at the edge before substantial public traffic and review
retention/storage use. Malware scanning is not included; treat attachments as
untrusted and scan them before opening.

## License key rotation and recovery

To rotate the signing key, first ship a Windows build that trusts both the old
and new public key IDs. Then add the new private key and key ID to Supabase,
switch the Edge Function signer, deploy it, and verify a test entitlement.
Keep the old public key in clients while valid entitlements signed by it remain
in use. Revoke an activation through the admin script; revocation is enforced
when the app performs its online verification. Offline use cannot learn about a
new server-side revocation until it reconnects.

The server allows one claim per code and binds it to the client-reported Windows
hardware identifier. This prevents ordinary reuse but is not hardware
attestation: a modified client can spoof identifiers. Keep activation codes
private and use account-assisted recovery for legitimate hardware changes.

## Rollback and recovery

Do not roll back by deleting new tables or restoring an old production snapshot
over live data. For a bad function deployment, redeploy the previous function
version while leaving the additive tables in place. For a bad policy or
migration, use a reviewed forward migration to correct it. Restore from backup
only with an explicit recovery plan that accounts for support tickets,
attachments, issued codes, redemptions, and revocations created since backup.
