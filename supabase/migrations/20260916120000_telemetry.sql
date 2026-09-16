-- Home-made analytics and crash reports (no third-party SDK, no extra account):
-- the game batches events and exceptions to POST /v1/telemetry, the API stores them here.
-- Rows keep no personal data beyond the player id, which is nulled when the account is deleted.

create table public.telemetry_events (
  id bigint generated always as identity primary key,
  player_id uuid references public.players (id) on delete set null,
  session_id text not null default '' check (char_length(session_id) <= 64),
  name text not null check (char_length(name) between 1 and 64),
  props jsonb not null default '{}'::jsonb check (pg_column_size(props) <= 4096),
  app_version text not null default '' check (char_length(app_version) <= 32),
  device text not null default '' check (char_length(device) <= 96),
  client_at timestamptz,
  created_at timestamptz not null default now()
);

create index telemetry_events_name_created_idx on public.telemetry_events (name, created_at desc);
create index telemetry_events_player_created_idx on public.telemetry_events (player_id, created_at desc);

create table public.client_errors (
  id bigint generated always as identity primary key,
  player_id uuid references public.players (id) on delete set null,
  fingerprint text not null check (char_length(fingerprint) <= 64),
  message text not null check (char_length(message) <= 2000),
  stack text not null default '' check (char_length(stack) <= 8000),
  app_version text not null default '' check (char_length(app_version) <= 32),
  device text not null default '' check (char_length(device) <= 96),
  os text not null default '' check (char_length(os) <= 96),
  occurrences integer not null default 1 check (occurrences > 0),
  first_seen timestamptz not null default now(),
  last_seen timestamptz not null default now(),
  unique (fingerprint, app_version)
);

alter table public.telemetry_events enable row level security;
alter table public.client_errors enable row level security;
-- Supabase default privileges grant new public tables to the API roles: this data is server-only.
revoke all on public.telemetry_events, public.client_errors from anon, authenticated;

grant select, insert on public.telemetry_events to crush_api;
grant select, insert, update on public.client_errors to crush_api;
grant usage, select on all sequences in schema public to crush_api;
create policy telemetry_events_api_all on public.telemetry_events for all to crush_api using (true) with check (true);
create policy client_errors_api_all on public.client_errors for all to crush_api using (true) with check (true);

-- -----------------------------------------------------------------------------
-- Dashboard views (read them from the Supabase SQL editor). Not exposed to the Data API.
-- -----------------------------------------------------------------------------
create schema if not exists analytics;
revoke all on schema analytics from public, anon, authenticated;

create or replace view analytics.daily_active with (security_invoker = true) as
select date_trunc('day', created_at)::date as day,
       count(distinct player_id) as players,
       count(distinct session_id) as sessions,
       count(*) filter (where name = 'stage_end') as stages_played,
       count(*) filter (where name = 'pvp_end') as duels_played
from public.telemetry_events
group by 1
order by 1 desc;

-- D1 / D7 retention by install day (first event of each player).
create or replace view analytics.retention with (security_invoker = true) as
with first_seen as (
  select player_id, min(created_at)::date as install_day
  from public.telemetry_events where player_id is not null group by player_id
), activity as (
  select distinct player_id, created_at::date as day from public.telemetry_events where player_id is not null
)
select f.install_day,
       count(*) as installs,
       round(100.0 * count(*) filter (where exists (select 1 from activity a where a.player_id = f.player_id and a.day = f.install_day + 1)) / count(*), 1) as d1_percent,
       round(100.0 * count(*) filter (where exists (select 1 from activity a where a.player_id = f.player_id and a.day = f.install_day + 7)) / count(*), 1) as d7_percent
from first_seen f
group by f.install_day
order by f.install_day desc;

-- First-session funnel: how many players reach each onboarding step.
create or replace view analytics.onboarding_funnel with (security_invoker = true) as
select step, players from (
  select 1 as ord, 'app_open' as step, count(distinct player_id) as players from public.telemetry_events where name = 'app_open'
  union all select 2, 'hero_created', count(distinct player_id) from public.telemetry_events where name = 'hero_created'
  union all select 3, 'tutorial_done', count(distinct player_id) from public.telemetry_events where name = 'tutorial_done'
  union all select 4, 'stage_1_won', count(distinct player_id) from public.telemetry_events where name = 'stage_end' and props->>'stage' = '1' and props->>'result' = 'Won'
  union all select 5, 'stage_5_won', count(distinct player_id) from public.telemetry_events where name = 'stage_end' and props->>'stage' = '5' and props->>'result' = 'Won'
  union all select 6, 'first_pvp', count(distinct player_id) from public.telemetry_events where name = 'pvp_end'
  union all select 7, 'first_summon', count(distinct player_id) from public.telemetry_events where name = 'pet_summon'
) s order by ord;

-- Stages where players give up: attempts, win rate and average score.
create or replace view analytics.stage_difficulty with (security_invoker = true) as
select (props->>'stage')::int as stage,
       count(*) as attempts,
       round(100.0 * count(*) filter (where props->>'result' = 'Won') / count(*), 1) as win_percent,
       round(avg((props->>'score')::bigint)) as avg_score
from public.telemetry_events
where name = 'stage_end' and props ? 'stage'
group by 1
order by 1;

create or replace view analytics.top_errors with (security_invoker = true) as
select app_version, occurrences, last_seen, left(message, 200) as message, device, os
from public.client_errors
order by last_seen desc, occurrences desc;
