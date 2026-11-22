# Donut support setup

The site is static, so support requests need a hosted form endpoint. This setup
stores tickets and attachments in a private Supabase project and can send email
through Resend. The website config contains only the public URL and publishable
key. The support backend uses server-side secrets.

## 1. Create a Supabase project

Create a project on Supabase's Free plan, then copy the project URL and the
publishable key into Website/support-config.js. Those two values are public
client configuration. Never put a secret API key or Resend API key in any
Website/ file.

## 2. Deploy the database and function

Install the Supabase CLI, authenticate, and link this repository to the project:

```sh
supabase login
supabase link --project-ref YOUR_PROJECT_REF
supabase db push
supabase functions deploy submit-support --no-verify-jwt
```

The migrations create a private ticket table, a private attachment bucket, and
an internal rate-limit table. Only the Edge Function uses the service-role key
to write or read those records. The function bounds request bodies, checks file
signatures and allowed types, limits uploads to 5 files (8 MB each, 20 MB total),
limits submissions to 3 per email and 5 per IP per hour, and checks an origin
allow-list. The browser's public key and CORS origin check are not treated as
authentication.

## 3. Configure function secrets

Set these in Supabase Edge Function secrets (not in the website):

```sh
supabase secrets set ALLOWED_ORIGINS=https://user-cariv.github.io,http://127.0.0.1:4173,http://localhost:4173
supabase secrets set SUPPORT_INBOX=YOUR_SUPPORT_EMAIL
supabase secrets set "SUPPORT_FROM=Donut Support <support@YOUR_VERIFIED_DOMAIN>"
supabase secrets set RESEND_API_KEY=YOUR_RESEND_API_KEY
```

Supabase provides SUPABASE_URL, SUPABASE_ANON_KEY, and
SUPABASE_SERVICE_ROLE_KEY to Edge Functions. Confirm these are available in
the deployed function's environment; never manually expose the service-role key.

Email delivery requires a Resend account and a verified sender domain. If the
email secrets are omitted, submissions are still saved, and the form reports
that email notifications are not configured.

## 4. Check the setup

Submit a request from the deployed site with a small JPG or PDF attached. Confirm
that the ticket appears in Supabase, the object is in the private bucket, and
the inbox receives the notification and confirmation. Do not use real customer
information during the first check.

Free plans have quotas and can change. Keep attachments small, review storage
usage, and configure retention/deletion as ticket volume grows. Public forms can
also attract spam; before a broad launch, add a CAPTCHA such as Cloudflare
Turnstile and stronger abuse monitoring.

## Donut Boost activation-code binding

Before releasing a Boost build with cloud activation checks, run
`migrations/20261122000000_donut_boost_code_claims.sql` once in the Supabase SQL
Editor for this project. It creates an atomic, private claim table and the RPC
used by Boost. Each code hash is claimed once; a different hardware ID is
rejected. Re-entry on the same device restores the original expiry and cannot
restart the pass. The migration keeps the old three-argument RPC signature so
already-installed builds receive the same server-side one-device decision.

Do not publish the updated Boost installer before this migration has run. New
builds fail closed when the licensing endpoint is unavailable.
