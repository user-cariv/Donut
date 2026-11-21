# Donut support setup

The site is static, so support requests need a hosted form endpoint. This setup
stores tickets and attachments in a private Supabase project and can send email
through Resend. The website config now points to the Donut Support project;
the backend still needs its migration and function deployed.

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

The migration creates a private ticket table and a private attachment bucket.
Only the Edge Function uses the service-role key to write or read those records.
The function enforces allowed file types, up to 5 files, 8 MB per file, 20 MB
total, a per-email submission limit, and an origin allow-list.

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
