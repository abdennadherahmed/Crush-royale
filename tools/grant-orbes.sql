-- One-off grant, to run in the Supabase SQL editor.
--
-- Gives 9000 orbes to three named players. The wallet lives inside players.state, so the update has to go through
-- jsonb_set; "version" is bumped so a client holding an older snapshot reloads instead of overwriting this.
--
-- Check first who is about to be changed:
--     select display_name, state->'wallet'->>'orbes' as orbes from public.players
--     where display_name in ('escobaros', '7kou', 'majors_blue');
--
-- Then run the update. Adjust the names to the exact display names the query above printed (they are
-- case-sensitive), and keep the list short: every name in it receives the orbes.

update public.players
set state = jsonb_set(
        state,
        '{wallet,orbes}',
        to_jsonb(coalesce((state->'wallet'->>'orbes')::bigint, 0) + 9000)
    ),
    version = version + 1,
    updated_at = now()
where display_name in ('escobaros', '7kou', 'majors_blue');

-- Confirm:
--     select display_name, state->'wallet'->>'orbes' as orbes from public.players
--     where display_name in ('escobaros', '7kou', 'majors_blue');
