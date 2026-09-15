-- =============================================================================
-- Crush Royale - initial schema
--
-- Security model
--   * The .NET API (role crush_api) is the ONLY writer. Economy, trophies and
--     match results are server-authoritative (replays are re-simulated).
--   * Clients never read or write tables through the Data API. The single
--     exception is guild chat, read through Realtime (Postgres Changes) under RLS.
--   * Every table has RLS enabled. anon/authenticated get no grants except
--     SELECT on guild_messages (members of the guild only).
--   * Financial audit trail (ledger_entries, purchase_log) is append-only:
--     updates/deletes are rejected, except the automatic anonymisation
--     (player_id -> null) when a player account is erased (GDPR).
--
-- Hand-authored (Supabase CLI not installed on the authoring machine). Apply with
-- `supabase db push` or paste into the SQL editor, then run the advisors.
-- =============================================================================

create schema if not exists private;

-- -----------------------------------------------------------------------------
-- API role (least privilege). Enable login once, outside of migrations:
--   alter role crush_api with login password '<strong password>';
-- -----------------------------------------------------------------------------
do $$
begin
  if not exists (select 1 from pg_roles where rolname = 'crush_api') then
    create role crush_api nologin noinherit;
  end if;
end
$$;

-- -----------------------------------------------------------------------------
-- Helpers
-- -----------------------------------------------------------------------------
create or replace function private.set_updated_at()
returns trigger
language plpgsql
security invoker
set search_path = ''
as $$
begin
  new.updated_at := now();
  return new;
end;
$$;

-- -----------------------------------------------------------------------------
-- Guilds (created before players because players.guild_id references it)
-- -----------------------------------------------------------------------------
create table public.guilds (
  id bigint generated always as identity primary key,
  name text not null check (char_length(name) between 3 and 20),
  level integer not null default 1 check (level between 1 and 20),
  member_count integer not null default 0 check (member_count between 0 and 20),
  total_trophies bigint not null default 0 check (total_trophies >= 0),
  is_open boolean not null default true,
  min_trophies integer not null default 0 check (min_trophies >= 0),
  state jsonb not null,
  version bigint not null default 1,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);

create unique index guilds_name_unique_idx on public.guilds (lower(name));
create index guilds_total_trophies_idx on public.guilds (total_trophies desc, id);
create index guilds_name_prefix_idx on public.guilds (lower(name) text_pattern_ops);

create trigger guilds_set_updated_at
  before update on public.guilds
  for each row execute function private.set_updated_at();

-- -----------------------------------------------------------------------------
-- Players: queryable columns + the full game state document (jsonb).
-- Optimistic concurrency through `version`.
-- -----------------------------------------------------------------------------
create table public.players (
  id uuid primary key references auth.users (id) on delete cascade,
  display_name text not null check (char_length(display_name) between 3 and 20),
  trophies integer not null default 0 check (trophies >= 0),
  league smallint not null default 0 check (league between 0 and 5),
  highest_stage integer not null default 1 check (highest_stage >= 1),
  vip_tier smallint not null default 0 check (vip_tier between 0 and 10),
  lifetime_spend_cents bigint not null default 0 check (lifetime_spend_cents >= 0),
  guild_id bigint references public.guilds (id) on delete set null,
  region text not null default '',
  suspended_until timestamptz,
  permanently_banned boolean not null default false,
  state jsonb not null,
  version bigint not null default 1,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);

create index players_trophies_idx on public.players (trophies desc, id);
create index players_league_trophies_idx on public.players (league, trophies desc, id);
create index players_guild_id_idx on public.players (guild_id) where guild_id is not null;
create index players_display_name_prefix_idx on public.players (lower(display_name) text_pattern_ops);

create trigger players_set_updated_at
  before update on public.players
  for each row execute function private.set_updated_at();

-- -----------------------------------------------------------------------------
-- Guild chat (read by clients through Realtime)
-- -----------------------------------------------------------------------------
create table public.guild_messages (
  id bigint generated always as identity primary key,
  guild_id bigint not null references public.guilds (id) on delete cascade,
  player_id uuid references public.players (id) on delete set null,
  display_name text not null,
  body text not null check (char_length(body) between 1 and 200),
  masked boolean not null default false,
  created_at timestamptz not null default now()
);

create index guild_messages_guild_created_idx on public.guild_messages (guild_id, id desc);
create index guild_messages_player_id_idx on public.guild_messages (player_id) where player_id is not null;

-- -----------------------------------------------------------------------------
-- Replays (ghosts) and matches
-- Seeds are unsigned 64-bit on the client; stored as bigint (two's complement).
-- -----------------------------------------------------------------------------
create table public.replays (
  id bigint generated always as identity primary key,
  player_id uuid not null references public.players (id) on delete cascade,
  mode smallint not null check (mode between 0 and 3),
  seed bigint not null,
  stage_id integer not null default 0,
  trophies integer not null default 0,
  final_score bigint not null check (final_score >= 0),
  region text not null default '',
  data bytea not null check (octet_length(data) between 16 and 65536),
  is_ghost boolean not null default false,
  created_at timestamptz not null default now()
);

create index replays_ghost_pool_idx on public.replays (trophies, created_at desc) where is_ghost;
create index replays_player_created_idx on public.replays (player_id, created_at desc);

create table public.matches (
  id text primary key,
  player_id uuid not null references public.players (id) on delete cascade,
  mode smallint not null check (mode between 0 and 3),
  seed bigint not null,
  stage_id integer not null default 0,
  config jsonb not null,
  status smallint not null default 0 check (status between 0 and 3),
  opponent_id uuid references public.players (id) on delete set null,
  ghost_replay_id bigint references public.replays (id) on delete set null,
  replay_id bigint references public.replays (id) on delete set null,
  result jsonb,
  started_at timestamptz not null default now(),
  finished_at timestamptz
);

create index matches_player_started_idx on public.matches (player_id, started_at desc);
create index matches_opponent_id_idx on public.matches (opponent_id) where opponent_id is not null;
create index matches_ghost_replay_id_idx on public.matches (ghost_replay_id) where ghost_replay_id is not null;
create index matches_replay_id_idx on public.matches (replay_id) where replay_id is not null;
create index matches_open_started_idx on public.matches (started_at) where status = 0;

-- -----------------------------------------------------------------------------
-- Financial audit trail (append-only)
-- -----------------------------------------------------------------------------
create table public.ledger_entries (
  id bigint generated always as identity primary key,
  player_id uuid references public.players (id) on delete set null,
  client_entry_id text not null,
  currency smallint not null check (currency in (0, 1)),
  amount bigint not null check (amount <> 0),
  balance_after bigint not null check (balance_after >= 0),
  reason smallint not null,
  reference text,
  idempotency_key text,
  created_at timestamptz not null default now()
);

create index ledger_entries_player_created_idx on public.ledger_entries (player_id, created_at desc);
create index ledger_entries_reason_created_idx on public.ledger_entries (reason, created_at);
create unique index ledger_entries_idempotency_idx on public.ledger_entries (player_id, idempotency_key)
  where idempotency_key is not null;

create table public.purchase_log (
  id bigint generated always as identity primary key,
  player_id uuid references public.players (id) on delete set null,
  transaction_id text not null,
  item_id text not null,
  kind smallint not null,
  method smallint not null check (method between 0 and 2),
  coins_spent bigint not null default 0 check (coins_spent >= 0),
  orbes_spent bigint not null default 0 check (orbes_spent >= 0),
  cents_charged integer not null default 0 check (cents_charged >= 0),
  created_at timestamptz not null default now()
);

create index purchase_log_player_created_idx on public.purchase_log (player_id, created_at desc);
create index purchase_log_created_idx on public.purchase_log (created_at);

create table public.iap_purchases (
  store_transaction_id text primary key,
  player_id uuid references public.players (id) on delete set null,
  sku text not null,
  price_cents integer not null check (price_cents > 0),
  purchase_token_hash text not null,
  status smallint not null default 0 check (status between 0 and 2), -- 0 granted, 1 refunded, 2 chargeback
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);

create index iap_purchases_player_created_idx on public.iap_purchases (player_id, created_at desc);

create trigger iap_purchases_set_updated_at
  before update on public.iap_purchases
  for each row execute function private.set_updated_at();

create or replace function private.ledger_entries_append_only()
returns trigger
language plpgsql
security invoker
set search_path = ''
as $$
begin
  if tg_op = 'UPDATE'
     and old.player_id is not null and new.player_id is null
     and (new.id, new.client_entry_id, new.currency, new.amount, new.balance_after, new.reason, new.reference, new.idempotency_key, new.created_at)
         is not distinct from
         (old.id, old.client_entry_id, old.currency, old.amount, old.balance_after, old.reason, old.reference, old.idempotency_key, old.created_at)
  then
    return new; -- GDPR anonymisation only
  end if;
  raise exception 'ledger_entries is append-only';
end;
$$;

create trigger ledger_entries_append_only
  before update or delete on public.ledger_entries
  for each row execute function private.ledger_entries_append_only();

create or replace function private.purchase_log_append_only()
returns trigger
language plpgsql
security invoker
set search_path = ''
as $$
begin
  if tg_op = 'UPDATE'
     and old.player_id is not null and new.player_id is null
     and (new.id, new.transaction_id, new.item_id, new.kind, new.method, new.coins_spent, new.orbes_spent, new.cents_charged, new.created_at)
         is not distinct from
         (old.id, old.transaction_id, old.item_id, old.kind, old.method, old.coins_spent, old.orbes_spent, old.cents_charged, old.created_at)
  then
    return new;
  end if;
  raise exception 'purchase_log is append-only';
end;
$$;

create trigger purchase_log_append_only
  before update or delete on public.purchase_log
  for each row execute function private.purchase_log_append_only();

-- -----------------------------------------------------------------------------
-- Anti-cheat
-- -----------------------------------------------------------------------------
create table public.cheat_flags (
  id bigint generated always as identity primary key,
  player_id uuid not null references public.players (id) on delete cascade,
  reason smallint not null,
  severity smallint not null check (severity between 0 and 3),
  details text,
  match_id text,
  reviewed boolean not null default false,
  review_outcome text,
  reviewed_by uuid,
  created_at timestamptz not null default now(),
  reviewed_at timestamptz
);

create index cheat_flags_player_created_idx on public.cheat_flags (player_id, created_at desc);
create index cheat_flags_pending_idx on public.cheat_flags (created_at) where not reviewed;

create table public.device_accounts (
  device_hash text not null check (char_length(device_hash) between 16 and 128),
  player_id uuid not null references public.players (id) on delete cascade,
  first_seen_at timestamptz not null default now(),
  last_seen_at timestamptz not null default now(),
  primary key (device_hash, player_id)
);

create index device_accounts_player_id_idx on public.device_accounts (player_id);

-- -----------------------------------------------------------------------------
-- Seasons and live configuration
-- -----------------------------------------------------------------------------
create table public.season_archives (
  week integer not null,
  league smallint not null check (league between 0 and 5),
  entries jsonb not null,
  created_at timestamptz not null default now(),
  primary key (week, league)
);

create table public.guild_season_archives (
  week integer primary key,
  entries jsonb not null,
  created_at timestamptz not null default now()
);

create table public.remote_config (
  key text primary key,
  value jsonb not null,
  updated_by uuid,
  updated_at timestamptz not null default now()
);

create trigger remote_config_set_updated_at
  before update on public.remote_config
  for each row execute function private.set_updated_at();

-- -----------------------------------------------------------------------------
-- Row Level Security: enabled everywhere
-- -----------------------------------------------------------------------------
alter table public.guilds enable row level security;
alter table public.players enable row level security;
alter table public.guild_messages enable row level security;
alter table public.replays enable row level security;
alter table public.matches enable row level security;
alter table public.ledger_entries enable row level security;
alter table public.purchase_log enable row level security;
alter table public.iap_purchases enable row level security;
alter table public.cheat_flags enable row level security;
alter table public.device_accounts enable row level security;
alter table public.season_archives enable row level security;
alter table public.guild_season_archives enable row level security;
alter table public.remote_config enable row level security;

-- Nothing is exposed to client roles by default.
revoke all on all tables in schema public from anon, authenticated;
revoke all on all sequences in schema public from anon, authenticated;
revoke all on all functions in schema private from public;

-- -----------------------------------------------------------------------------
-- Guild chat read access for members (Realtime Postgres Changes)
-- SECURITY DEFINER lookup kept in the non-exposed private schema, keyed on auth.uid().
-- -----------------------------------------------------------------------------
create or replace function private.current_guild_id()
returns bigint
language sql
stable
security definer
set search_path = ''
as $$
  select p.guild_id
  from public.players p
  where p.id = (select auth.uid());
$$;

revoke all on function private.current_guild_id() from public, anon;
grant usage on schema private to authenticated;
grant execute on function private.current_guild_id() to authenticated;

grant select on public.guild_messages to authenticated;

create policy guild_messages_select_members
  on public.guild_messages
  for select
  to authenticated
  using (guild_id = (select private.current_guild_id()));

alter publication supabase_realtime add table public.guild_messages;

-- -----------------------------------------------------------------------------
-- API server role: explicit grants + permissive policies scoped to crush_api
-- -----------------------------------------------------------------------------
grant usage on schema public to crush_api;
grant usage on schema private to crush_api;
grant execute on function private.set_updated_at() to crush_api;
grant execute on function private.ledger_entries_append_only() to crush_api;
grant execute on function private.purchase_log_append_only() to crush_api;

grant select, insert, update, delete on
  public.guilds, public.players, public.guild_messages, public.replays, public.matches,
  public.cheat_flags, public.device_accounts, public.season_archives,
  public.guild_season_archives, public.remote_config
to crush_api;
grant select, insert on public.ledger_entries, public.purchase_log to crush_api;
grant select, insert, update on public.iap_purchases to crush_api;
grant usage, select on all sequences in schema public to crush_api;

create policy guilds_api_all on public.guilds for all to crush_api using (true) with check (true);
create policy players_api_all on public.players for all to crush_api using (true) with check (true);
create policy guild_messages_api_all on public.guild_messages for all to crush_api using (true) with check (true);
create policy replays_api_all on public.replays for all to crush_api using (true) with check (true);
create policy matches_api_all on public.matches for all to crush_api using (true) with check (true);
create policy ledger_entries_api_read on public.ledger_entries for select to crush_api using (true);
create policy ledger_entries_api_insert on public.ledger_entries for insert to crush_api with check (true);
create policy purchase_log_api_read on public.purchase_log for select to crush_api using (true);
create policy purchase_log_api_insert on public.purchase_log for insert to crush_api with check (true);
create policy iap_purchases_api_all on public.iap_purchases for all to crush_api using (true) with check (true);
create policy cheat_flags_api_all on public.cheat_flags for all to crush_api using (true) with check (true);
create policy device_accounts_api_all on public.device_accounts for all to crush_api using (true) with check (true);
create policy season_archives_api_all on public.season_archives for all to crush_api using (true) with check (true);
create policy guild_season_archives_api_all on public.guild_season_archives for all to crush_api using (true) with check (true);
create policy remote_config_api_all on public.remote_config for all to crush_api using (true) with check (true);
