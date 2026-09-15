-- =====================================================================================
-- Account deletion (Google Play requirement, GDPR right to erasure)
--
-- The API role (crush_api) has no access to auth.users, so account deletion goes through a
-- SECURITY DEFINER function kept in the non-exposed private schema and callable only by crush_api.
-- The server passes the id taken from the caller's verified JWT, never a client-supplied id.
--
-- Effects of deleting the auth user:
--   * players row deleted -> replays, matches, cheat_flags, device_accounts cascade;
--   * ledger_entries, purchase_log, iap_purchases keep their rows with player_id = null
--     (anonymised financial audit trail, allowed by the append-only triggers);
--   * guild_messages keep the text but lose the author (player_id = null, display name removed).
-- =====================================================================================

create or replace function private.delete_player_account(p_player_id uuid)
returns void
language plpgsql
security definer
set search_path = ''
as $$
begin
  if p_player_id is null then
    raise exception 'player id required';
  end if;

  update public.guild_messages
     set display_name = '-'
   where player_id = p_player_id;

  delete from auth.users where id = p_player_id;
  -- Players without an auth user (should not happen in production) are removed as well.
  delete from public.players where id = p_player_id;
end;
$$;

revoke all on function private.delete_player_account(uuid) from public, anon, authenticated;
grant execute on function private.delete_player_account(uuid) to crush_api;
