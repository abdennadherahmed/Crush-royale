-- =====================================================================================
-- Repare le rattachement a la guilde du compte de test, et donne de quoi diagnostiquer l'achat.
--
-- Mon script precedent n'a rempli que la colonne players.guild_id. Or le serveur ne la lit jamais :
-- il lit state.guildId, a l'interieur du JSON de l'etat du joueur. Le compte se retrouvait donc dans
-- la liste des membres de la guilde, mais sans guilde de son point de vue, d'ou l'ecran vide et le
-- message contradictoire.
--
-- A lancer dans l'editeur SQL de Supabase.
-- =====================================================================================

begin;

-- 1. Le champ que le serveur lit vraiment.
update public.players
   set state = jsonb_set(state, '{guildId}', '1'::jsonb),
       guild_id = 1,
       version = version + 1,
       updated_at = now()
 where id = 'a8fab080-e0a5-4037-a8d2-e89edaa58409';

-- 2. Verification : les deux doivent dire 1.
select display_name,
       state ->> 'guildId' as guilde_dans_le_json,
       guild_id as guilde_colonne,
       state -> 'wallet' ->> 'orbes' as orbes
  from public.players
 where id = 'a8fab080-e0a5-4037-a8d2-e89edaa58409';

commit;

-- =====================================================================================
-- Diagnostic de l'achat, en lecture seule. A lancer apres le commit ci-dessus.
-- =====================================================================================

-- Un achat valide par le serveur laisse une ligne ici. Zero ligne = le serveur a refuse le recu.
select id, player_id, sku, price_cents, status, created_at
  from public.iap_purchases
 order by created_at desc
 limit 5;

-- Et si le serveur a refuse, il a signale le compte avec la raison exacte.
select created_at, reason, severity, details
  from public.cheat_flags
 where player_id = 'a8fab080-e0a5-4037-a8d2-e89edaa58409'
 order by created_at desc
 limit 5;
