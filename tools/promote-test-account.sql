-- =====================================================================================
-- Prepare le compte de test d'Ahmed : etape 250 debloquee, et chef de la guilde Winners.
--
-- Ahmed a reinstalle le jeu depuis le Play Store, ce qui a cree un compte neuf. L'ancien, qui porte le
-- meme nom et detient toute la progression, reste inaccessible : son authentification etait anonyme et
-- liee a l'ancienne installation. Le nouveau compte reprend donc sa place a la tete de la guilde, et
-- l'ancien y redevient simple membre plutot que d'etre efface, pour que rien ne soit perdu avant qu'il
-- ait verifie que tout va bien.
--
-- La bourse n'est volontairement pas touchee. Le but de ce compte est d'essayer un vrai achat : lui
-- donner un million d'orbes lui enleverait toute raison d'en acheter.
--
-- Le membre ajoute est construit a partir d'un membre existant, pour qu'il ait exactement les memes
-- champs que ceux que le serveur ecrit lui-meme. Seuls l'identifiant, le role, le nom et les compteurs
-- de boss de la semaine sont remis a leur valeur de depart.
--
-- A lancer dans l'editeur SQL de Supabase.
-- =====================================================================================

begin;

-- 1. Le compte : etape 250 debloquee et rattachement a la guilde.
--    La version sert au controle de concurrence : la laisser en arriere ferait rejeter la prochaine
--    ecriture du serveur sur ce joueur.
update public.players
   set state = jsonb_set(state, '{story,highestUnlockedStage}', '250'::jsonb),
       guild_id = 1,
       version = version + 1,
       updated_at = now()
 where id = 'a8fab080-e0a5-4037-a8d2-e89edaa58409';

-- 2. La guilde : tout le monde repasse membre, puis le nouveau compte entre comme chef.
update public.guilds g
   set state = jsonb_set(
         g.state,
         '{guild,members}',
         coalesce((
           select jsonb_agg(jsonb_set(m, '{role}', '"Member"'))
             from jsonb_array_elements(g.state -> 'guild' -> 'members') m
            where m ->> 'playerId' <> 'a8fab080-e0a5-4037-a8d2-e89edaa58409'
         ), '[]'::jsonb)
         || jsonb_build_array((
              select jsonb_set(jsonb_set(jsonb_set(jsonb_set(jsonb_set(jsonb_set(
                       m,
                       '{playerId}', '"a8fab080-e0a5-4037-a8d2-e89edaa58409"'),
                       '{role}', '"Leader"'),
                       '{displayName}', '"Escobaros"'),
                       '{bossAttacksThisWeek}', '0'::jsonb),
                       '{bossAttacksToday}', '0'::jsonb),
                       '{bossDamageThisWeek}', '0'::jsonb)
                from jsonb_array_elements(g.state -> 'guild' -> 'members') m
               limit 1))
       ),
       version = version + 1,
       updated_at = now()
 where g.id = 1;

-- 3. Le decompte suit la liste, qui vient d'etre reecrite.
update public.guilds g
   set member_count = (select count(*) from jsonb_array_elements(g.state -> 'guild' -> 'members'))
 where g.id = 1;

-- 4. De quoi verifier avant de valider : deux membres, le nouveau chef en tete.
select m ->> 'displayName' as membre, m ->> 'playerId' as id, m ->> 'role' as role, g.member_count
  from public.guilds g, jsonb_array_elements(g.state -> 'guild' -> 'members') m
 where g.id = 1;

select display_name, state -> 'story' ->> 'highestUnlockedStage' as etape, guild_id
  from public.players
 where id = 'a8fab080-e0a5-4037-a8d2-e89edaa58409';

-- Relire les deux resultats ci-dessus, puis :
commit;
-- En cas de doute, remplacer la ligne precedente par : rollback;
