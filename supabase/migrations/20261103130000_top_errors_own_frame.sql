-- -----------------------------------------------------------------------------
-- La premiere ligne de pile qui soit du code du jeu.
--
-- La toute premiere ligne est celle du framework : ThrowHelper, DebugLogHandler, List.get_Item,
-- String.Substring. Elle dit ce qui a leve l'exception, jamais qui l'a appele, donc elle ne mene a aucun
-- fichier. La premiere ligne qui nomme CrushRoyale, elle, donne la methode et le numero de ligne.
--
-- Le repli reste la premiere ligne non vide : une pile sans code du jeu vaut mieux que pas de pile.
-- -----------------------------------------------------------------------------

create or replace view analytics.top_errors with (security_invoker = true) as
select app_version,
       occurrences,
       last_seen,
       left(message, 200) as message,
       device,
       os,
       coalesce(
         (select trim(line) from unnest(string_to_array(stack, E'\n')) as line
          where line like '%CrushRoyale%' limit 1),
         (select trim(line) from unnest(string_to_array(stack, E'\n')) as line
          where trim(line) <> '' limit 1)) as first_frame
from public.client_errors
order by last_seen desc, occurrences desc;
