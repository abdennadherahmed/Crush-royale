-- Keep the free Render API instance awake: it sleeps after 15 min without traffic and then needs 25-50 s to answer,
-- longer than the game's 8 s login budget. One GET /health every 10 minutes (fits Render's 750 free hours per month).
create extension if not exists pg_cron;
create extension if not exists pg_net;

select cron.schedule(
  'keep-crushroyale-api-awake',
  '*/10 * * * *',
  $$select net.http_get(url := 'https://crushroyale-api.onrender.com/health', timeout_milliseconds := 60000)$$
);
