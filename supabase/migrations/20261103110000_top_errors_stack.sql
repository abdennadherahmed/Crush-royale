-- -----------------------------------------------------------------------------
-- La premiere ligne de pile dans les plantages.
--
-- La vue ne montrait que le message. « NullReferenceException: Object reference not set to an instance of
-- an object », vue vingt-huit fois, ne designe aucun fichier et ne se corrige pas. La premiere ligne de la
-- pile nomme la methode et le numero de ligne, et c'est tout ce qu'il faut pour aller voir.
--
-- La pile est deja nettoyee par le client : jetons, cles et adresses e-mail n'y arrivent jamais.
-- -----------------------------------------------------------------------------

create or replace view analytics.top_errors with (security_invoker = true) as
select app_version,
       occurrences,
       last_seen,
       left(message, 200) as message,
       -- Unity prefixe souvent la pile d'un saut de ligne : on prend la premiere ligne non vide.
       (select trim(line) from unnest(string_to_array(stack, E'\n')) as line
        where trim(line) <> '' limit 1) as first_frame,
       device,
       os
from public.client_errors
order by last_seen desc, occurrences desc;
