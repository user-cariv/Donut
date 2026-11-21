create table if not exists public.support_tickets (
  id uuid primary key,
  name text not null check (char_length(name) between 1 and 120),
  email text not null check (char_length(email) <= 254),
  issue text not null check (char_length(issue) between 1 and 10000),
  status text not null default 'open' check (status in ('open', 'in_progress', 'closed')),
  attachments jsonb not null default '[]'::jsonb,
  notification_status text not null default 'pending' check (notification_status in ('pending', 'sent', 'failed', 'not_configured')),
  created_at timestamptz not null default now()
);

create index if not exists support_tickets_email_created_at_idx
  on public.support_tickets (email, created_at desc);

alter table public.support_tickets enable row level security;
revoke all on public.support_tickets from anon, authenticated;

insert into storage.buckets (id, name, public, file_size_limit, allowed_mime_types)
values (
  'support-attachments',
  'support-attachments',
  false,
  8388608,
  array['image/jpeg', 'image/png', 'image/webp', 'image/gif', 'application/pdf']
)
on conflict (id) do update set
  public = false,
  file_size_limit = excluded.file_size_limit,
  allowed_mime_types = excluded.allowed_mime_types;

-- No anon/authenticated Storage policies are created. Only the Edge Function's
-- service role can access this private bucket; never expose that key to the site.
