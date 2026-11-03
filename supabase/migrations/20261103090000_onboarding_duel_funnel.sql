-- -----------------------------------------------------------------------------
-- Le duel d'accueil dans l'entonnoir de premiere session.
--
-- L'entonnoir commencait a « hero_created », donc la marche la plus raide, celle qui va de l'ouverture de
-- l'application au premier duel, etait invisible. Ces trois etapes disent combien de joueurs voient
-- l'affiche, combien lancent le duel et combien le terminent, avant tout le reste.
-- -----------------------------------------------------------------------------

create or replace view analytics.onboarding_funnel with (security_invoker = true) as
select step, players from (
  select 1 as ord, 'app_open' as step, count(distinct player_id) as players from public.telemetry_events where name = 'app_open'
  union all select 2, 'first_duel_shown', count(distinct player_id) from public.telemetry_events where name = 'first_duel_shown'
  union all select 3, 'first_duel_start', count(distinct player_id) from public.telemetry_events where name = 'first_duel_start'
  union all select 4, 'first_duel_end', count(distinct player_id) from public.telemetry_events where name = 'first_duel_end'
  union all select 5, 'hero_created', count(distinct player_id) from public.telemetry_events where name = 'hero_created'
  union all select 6, 'tutorial_done', count(distinct player_id) from public.telemetry_events where name = 'tutorial_done'
  union all select 7, 'stage_1_won', count(distinct player_id) from public.telemetry_events where name = 'stage_end' and props->>'stage' = '1' and props->>'result' = 'Won'
  union all select 8, 'stage_5_won', count(distinct player_id) from public.telemetry_events where name = 'stage_end' and props->>'stage' = '5' and props->>'result' = 'Won'
  union all select 9, 'first_pvp', count(distinct player_id) from public.telemetry_events where name = 'pvp_end'
  union all select 10, 'first_summon', count(distinct player_id) from public.telemetry_events where name = 'pet_summon'
) s order by ord;

-- Le duel d'accueil est-il gagnable ? Il est cale pour l'etre : si le taux de victoire descend sous les
-- trois quarts, la selection de graine ne suffit plus et c'est le moment de le revoir.
create or replace view analytics.first_duel with (security_invoker = true) as
select created_at::date as day,
       count(*) as duels,
       round(100.0 * count(*) filter (where props->>'result' = 'Won') / count(*), 1) as won_percent,
       round(avg((props->>'score')::bigint)) as avg_player_score,
       round(avg((props->>'opponent')::bigint)) as avg_bot_score
from public.telemetry_events
where name = 'first_duel_end' and props ? 'result'
group by 1
order by 1 desc;
