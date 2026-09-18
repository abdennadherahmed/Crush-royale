-- Guilds now level up to 50 (the old 1..20 check made every donation past level 20 fail with a server error).
alter table public.guilds drop constraint if exists guilds_level_check;
alter table public.guilds add constraint guilds_level_check check (level between 1 and 50);

-- In-game updater: public, read-only bucket holding the latest APK (split in parts under the free 50 MB limit)
-- and android/latest.json. Only the CI (service role, which bypasses RLS) writes to it: no insert/update policy.
insert into storage.buckets (id, name, public, file_size_limit, allowed_mime_types)
values ('releases', 'releases', true, 52428800, null)
on conflict (id) do update set public = true, file_size_limit = 52428800;
