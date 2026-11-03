-- -----------------------------------------------------------------------------
-- Le tableau de bord du jeu, en une seule requete.
--
-- Les vues du schema analytics existaient deja, mais il fallait savoir qu'elles existaient, les ouvrir une
-- par une, et l'editeur SQL de Supabase n'affiche de toute facon que le dernier resultat. Ce fichier les
-- ramene toutes dans un seul tableau de trois colonnes : la section, la ligne, la valeur.
--
--   Supabase > SQL Editor > coller ce fichier > Run
--
-- Rien n'est modifie : que des lectures.
-- -----------------------------------------------------------------------------

with
-- Sept derniers jours d'activite.
activite(ord, section, ligne, valeur, rn) as (
  select 10 as ord, 'Activite' as section,
         to_char(day, 'DD/MM') as ligne,
         players || ' joueurs, ' || sessions || ' sessions, ' || stages_played || ' etapes, ' || duels_played || ' duels' as valeur,
         row_number() over (order by day desc) as rn
  from analytics.daily_active
),
-- Retention par jour d'installation. C'est le seul chiffre qui dit si le jeu tient.
retention(ord, section, ligne, valeur, rn) as (
  select 20, 'Retention',
         to_char(install_day, 'DD/MM') || ' (' || installs || ' install.)',
         'J1 ' || coalesce(d1_percent, 0) || ' %, J7 ' || coalesce(d7_percent, 0) || ' %',
         row_number() over (order by install_day desc)
  from analytics.retention
),
-- Entonnoir de premiere session, avec la part de ceux qui ouvrent l'application.
ouvertures as (
  select max(players) as total from analytics.onboarding_funnel
),
entonnoir(ord, section, ligne, valeur, rn) as (
  select 30, 'Premiere session',
         f.step,
         f.players || ' joueurs' ||
           case when o.total > 0 then ' (' || round(100.0 * f.players / o.total, 1) || ' %)' else '' end,
         row_number() over (order by f.players desc)
  from analytics.onboarding_funnel f cross join ouvertures o
),
-- Le duel d'accueil doit se gagner. Sous 75 % de victoires, la selection de graine ne suffit plus.
duel(ord, section, ligne, valeur, rn) as (
  select 40, 'Duel d''accueil',
         to_char(day, 'DD/MM'),
         duels || ' duels, ' || won_percent || ' % gagnes, ' || avg_player_score || ' contre ' || avg_bot_score,
         row_number() over (order by day desc)
  from analytics.first_duel
),
-- Les dix etapes ou l'on abandonne le plus : au moins vingt tentatives, du pire taux de victoire au moins pire.
murs(ord, section, ligne, valeur, rn) as (
  select 50, 'Murs de difficulte',
         'Etape ' || stage,
         win_percent || ' % gagnees sur ' || attempts || ' tentatives',
         row_number() over (order by win_percent asc, attempts desc)
  from analytics.stage_difficulty
  where attempts >= 20
),
-- Les plantages les plus recents, qui comptent plus que les plus nombreux : ils sont dans la version en ligne.
plantages(ord, section, ligne, valeur, rn) as (
  select 60, 'Plantages',
         app_version || ' x' || occurrences,
         message,
         row_number() over (order by last_seen desc, occurrences desc)
  from analytics.top_errors
)
select section, ligne, valeur from (
  select * from activite where rn <= 7
  union all select * from retention where rn <= 7
  union all select * from entonnoir
  union all select * from duel where rn <= 7
  union all select * from murs where rn <= 10
  union all select * from plantages where rn <= 10
) tout
order by ord, rn;
