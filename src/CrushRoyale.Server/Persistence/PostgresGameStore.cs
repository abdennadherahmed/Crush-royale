using System.Data;
using CrushRoyale.Core.AntiCheat;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Economy;
using CrushRoyale.Core.Pvp;
using CrushRoyale.Server.Infrastructure;
using Npgsql;
using NpgsqlTypes;

namespace CrushRoyale.Server.Persistence;

/// <summary>
/// Supabase Postgres implementation (Npgsql). Connect with the least-privilege <c>crush_api</c> role over the
/// direct connection (IPv6) or the Supavisor session pooler (IPv4); see docs/BACKEND.md.
/// Every statement is parameterized. Player and guild documents use optimistic concurrency plus row locks.
/// </summary>
public sealed class PostgresGameStore : IGameStore
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly Func<GameBalance> _balance;

    public PostgresGameStore(NpgsqlDataSource dataSource, Func<GameBalance> balance)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _balance = balance ?? throw new ArgumentNullException(nameof(balance));
    }

    public async Task<T> TransactAsync<T>(Func<IStoreTransaction, Task<T>> work, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
        try
        {
            T result = await work(new Tx(connection, transaction, _balance, cancellationToken)).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (NpgsqlException)
            {
                // Connection already broken: the server rolls back on its own.
            }
            throw;
        }
    }

    public async Task<IReadOnlyList<LeaderboardRow>> TopPlayersAsync(League? league, int limit, CancellationToken cancellationToken)
    {
        const string sql = @"
            select id, display_name, trophies, league
            from public.players
            where not permanently_banned and (@league is null or league = @league)
            order by trophies desc, id
            limit @limit";

        await using NpgsqlCommand cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.Add(Param("league", league.HasValue ? (short)league.Value : null, NpgsqlDbType.Smallint));
        cmd.Parameters.AddWithValue("limit", limit);

        var rows = new List<LeaderboardRow>();
        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new LeaderboardRow(rows.Count + 1, reader.GetGuid(0), reader.GetString(1), reader.GetInt32(2), (League)reader.GetInt16(3)));
        }
        return rows;
    }

    public async Task<int?> PlayerRankAsync(Guid playerId, League? league, CancellationToken cancellationToken)
    {
        const string sql = @"
            select count(*) + 1
            from public.players p
            join public.players me on me.id = @id
            where not p.permanently_banned
              and (@league is null or p.league = @league)
              and (p.trophies > me.trophies or (p.trophies = me.trophies and p.id < me.id))";

        await using NpgsqlCommand exists = _dataSource.CreateCommand("select 1 from public.players where id = @id");
        exists.Parameters.AddWithValue("id", playerId);
        if (await exists.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) == null)
        {
            return null;
        }

        await using NpgsqlCommand cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("id", playerId);
        cmd.Parameters.Add(Param("league", league.HasValue ? (short)league.Value : null, NpgsqlDbType.Smallint));
        object? value = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    public async Task<IReadOnlyList<GuildRankingRow>> TopGuildsAsync(int limit, CancellationToken cancellationToken)
    {
        const string sql = @"
            select id, name, total_trophies, member_count, level
            from public.guilds
            where member_count > 0
            order by total_trophies desc, id
            limit @limit";

        await using NpgsqlCommand cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("limit", limit);
        var rows = new List<GuildRankingRow>();
        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new GuildRankingRow(rows.Count + 1, reader.GetInt64(0), reader.GetString(1), reader.GetInt64(2), reader.GetInt32(3), reader.GetInt32(4)));
        }
        return rows;
    }

    public async Task<IReadOnlyList<GhostRow>> FindGhostsAsync(int minTrophies, int maxTrophies, DateTime recordedAfter, int limit, CancellationToken cancellationToken)
    {
        const string sql = @"
            select id, player_id, trophies, created_at, region, seed
            from public.replays
            where is_ghost and trophies between @min and @max and created_at >= @after
            order by created_at desc
            limit @limit";

        await using NpgsqlCommand cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("min", minTrophies);
        cmd.Parameters.AddWithValue("max", maxTrophies);
        cmd.Parameters.AddWithValue("after", DateTime.SpecifyKind(recordedAfter, DateTimeKind.Utc));
        cmd.Parameters.AddWithValue("limit", limit);

        var rows = new List<GhostRow>();
        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new GhostRow(reader.GetInt64(0), reader.GetGuid(1), reader.GetInt32(2), reader.GetDateTime(3), reader.GetString(4), unchecked((ulong)reader.GetInt64(5))));
        }
        return rows;
    }

    public async Task<IReadOnlyList<PlayerSummary>> SearchPlayersAsync(string namePrefix, int limit, CancellationToken cancellationToken)
    {
        const string sql = SummarySelect + @"
            where lower(display_name) like @prefix escape '\'
            order by lower(display_name)
            limit @limit";

        await using NpgsqlCommand cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("prefix", LikePrefix(namePrefix));
        cmd.Parameters.AddWithValue("limit", limit);
        return await ReadSummaries(cmd, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PlayerSummary>> GetPlayerSummariesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return Array.Empty<PlayerSummary>();
        }
        await using NpgsqlCommand cmd = _dataSource.CreateCommand(SummarySelect + " where id = any(@ids)");
        cmd.Parameters.AddWithValue("ids", ids.ToArray());
        return await ReadSummaries(cmd, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<GuildSummary>> SearchGuildsAsync(string namePrefix, int limit, CancellationToken cancellationToken)
    {
        const string sql = @"
            select id, name, level, member_count, total_trophies, is_open, min_trophies
            from public.guilds
            where lower(name) like @prefix escape '\'
            order by total_trophies desc, id
            limit @limit";

        await using NpgsqlCommand cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("prefix", LikePrefix(namePrefix));
        cmd.Parameters.AddWithValue("limit", limit);
        var rows = new List<GuildSummary>();
        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new GuildSummary(reader.GetInt64(0), reader.GetString(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt64(4), reader.GetBoolean(5), reader.GetInt32(6)));
        }
        return rows;
    }

    public async Task<IReadOnlyList<GuildMessageRow>> GetGuildMessagesAsync(long guildId, long? beforeId, int limit, CancellationToken cancellationToken)
    {
        const string sql = @"
            select id, guild_id, player_id, display_name, body, masked, created_at
            from public.guild_messages
            where guild_id = @guild and (@before is null or id < @before)
            order by id desc
            limit @limit";

        await using NpgsqlCommand cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("guild", guildId);
        cmd.Parameters.Add(Param("before", beforeId, NpgsqlDbType.Bigint));
        cmd.Parameters.AddWithValue("limit", limit);
        var rows = new List<GuildMessageRow>();
        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new GuildMessageRow
            {
                Id = reader.GetInt64(0),
                GuildId = reader.GetInt64(1),
                PlayerId = reader.IsDBNull(2) ? null : reader.GetGuid(2),
                DisplayName = reader.GetString(3),
                Body = reader.GetString(4),
                Masked = reader.GetBoolean(5),
                CreatedAt = reader.GetDateTime(6)
            });
        }
        return rows;
    }

    public async Task<IReadOnlyList<FlagRow>> PendingFlagsAsync(int limit, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand cmd = _dataSource.CreateCommand(FlagSelect + " where not reviewed order by severity desc, created_at limit @limit");
        cmd.Parameters.AddWithValue("limit", limit);
        var rows = new List<FlagRow>();
        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(ReadFlag(reader));
        }
        return rows;
    }

    public async Task<string?> GetConfigAsync(string key, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand cmd = _dataSource.CreateCommand("select value::text from public.remote_config where key = @key");
        cmd.Parameters.AddWithValue("key", key);
        return await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
    }

    public async Task<bool> PingAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using NpgsqlCommand cmd = _dataSource.CreateCommand("select 1");
            return await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) != null;
        }
        catch (NpgsqlException)
        {
            return false;
        }
    }

    // ------------------------------------------------------------------ shared SQL helpers

    private const string SummarySelect = @"
        select id, display_name, trophies, league, highest_stage, guild_id,
               state->'inventory'->>'equippedFrame', state->'inventory'->>'equippedTitle'
        from public.players";

    private const string FlagSelect = @"
        select id, player_id, reason, severity, details, match_id, reviewed, review_outcome, reviewed_by, created_at
        from public.cheat_flags";

    private static async Task<IReadOnlyList<PlayerSummary>> ReadSummaries(NpgsqlCommand cmd, CancellationToken ct)
    {
        var rows = new List<PlayerSummary>();
        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new PlayerSummary(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetInt32(2),
                (League)reader.GetInt16(3),
                reader.GetInt32(4),
                reader.IsDBNull(5) ? null : reader.GetInt64(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7)));
        }
        return rows;
    }

    private static FlagRow ReadFlag(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        PlayerId = reader.GetGuid(1),
        Reason = (FlagReason)reader.GetInt16(2),
        Severity = (CheatSeverity)reader.GetInt16(3),
        Details = reader.IsDBNull(4) ? null : reader.GetString(4),
        MatchId = reader.IsDBNull(5) ? null : reader.GetString(5),
        Reviewed = reader.GetBoolean(6),
        ReviewOutcome = reader.IsDBNull(7) ? null : reader.GetString(7),
        ReviewedBy = reader.IsDBNull(8) ? null : reader.GetGuid(8),
        CreatedAt = reader.GetDateTime(9)
    };

    private static NpgsqlParameter Param(string name, object? value, NpgsqlDbType type) =>
        new(name, type) { Value = value ?? DBNull.Value };

    /// <summary>Lower-cased LIKE prefix with %, _ and \ escaped.</summary>
    private static string LikePrefix(string input)
    {
        string trimmed = (input ?? string.Empty).Trim().ToLowerInvariant();
        return trimmed.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
    }

    private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private sealed class Tx : IStoreTransaction
    {
        private readonly NpgsqlConnection _connection;
        private readonly NpgsqlTransaction _transaction;
        private readonly Func<GameBalance> _balance;
        private readonly CancellationToken _ct;

        public Tx(NpgsqlConnection connection, NpgsqlTransaction transaction, Func<GameBalance> balance, CancellationToken ct)
        {
            _connection = connection;
            _transaction = transaction;
            _balance = balance;
            _ct = ct;
        }

        private NpgsqlCommand Cmd(string sql) => new(sql, _connection, _transaction);

        public async Task<PlayerRecord?> GetPlayerAsync(Guid id, bool forUpdate = true)
        {
            await using NpgsqlCommand cmd = Cmd("select id, version, created_at, state::text from public.players where id = @id" + (forUpdate ? " for update" : string.Empty));
            cmd.Parameters.AddWithValue("id", id);
            await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(_ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(_ct).ConfigureAwait(false))
            {
                return null;
            }
            return ReadPlayer(reader);
        }

        public async Task InsertPlayerAsync(PlayerRecord player)
        {
            const string sql = @"
                insert into public.players
                  (id, display_name, trophies, league, highest_stage, vip_tier, lifetime_spend_cents, guild_id, region,
                   suspended_until, permanently_banned, state, version, created_at)
                values
                  (@id, @name, @trophies, @league, @stage, @vip, @spend, @guild, @region, @suspended, @banned, @state, 1, @created)
                on conflict (id) do nothing";

            await using NpgsqlCommand cmd = Cmd(sql);
            AddPlayerColumns(cmd, player);
            cmd.Parameters.AddWithValue("created", Utc(player.CreatedAt));
            if (await cmd.ExecuteNonQueryAsync(_ct).ConfigureAwait(false) != 1)
            {
                throw new ConcurrencyException("Player " + player.Id + " already exists.");
            }
            player.Version = 1;
        }

        public async Task UpdatePlayerAsync(PlayerRecord player)
        {
            const string sql = @"
                update public.players set
                  display_name = @name, trophies = @trophies, league = @league, highest_stage = @stage, vip_tier = @vip,
                  lifetime_spend_cents = @spend, guild_id = @guild, region = @region, suspended_until = @suspended,
                  permanently_banned = @banned, state = @state, version = version + 1
                where id = @id and version = @version";

            await using NpgsqlCommand cmd = Cmd(sql);
            AddPlayerColumns(cmd, player);
            cmd.Parameters.AddWithValue("version", player.Version);
            if (await cmd.ExecuteNonQueryAsync(_ct).ConfigureAwait(false) != 1)
            {
                throw new ConcurrencyException("Player " + player.Id + " was modified concurrently.");
            }
            player.Version++;
        }

        public async Task<IReadOnlyList<PlayerRecord>> GetPlayersPageAsync(Guid? afterId, int limit)
        {
            await using NpgsqlCommand cmd = Cmd("select id, version, created_at, state::text from public.players where (@after is null or id > @after) order by id limit @limit for update");
            cmd.Parameters.Add(Param("after", afterId, NpgsqlDbType.Uuid));
            cmd.Parameters.AddWithValue("limit", limit);
            var rows = new List<PlayerRecord>();
            await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(_ct).ConfigureAwait(false);
            while (await reader.ReadAsync(_ct).ConfigureAwait(false))
            {
                rows.Add(ReadPlayer(reader));
            }
            return rows;
        }

        public async Task AppendLedgerAsync(Guid playerId, IReadOnlyList<LedgerEntry> entries)
        {
            if (entries.Count == 0)
            {
                return;
            }

            const string sql = @"
                insert into public.ledger_entries
                  (player_id, client_entry_id, currency, amount, balance_after, reason, reference, idempotency_key, created_at)
                select @player, e.id, e.currency, e.amount, e.balance_after, e.reason, e.reference, e.idem, e.created_at
                from unnest(@ids, @currencies, @amounts, @balances, @reasons, @references, @keys, @created)
                  as e(id, currency, amount, balance_after, reason, reference, idem, created_at)";

            await using NpgsqlCommand cmd = Cmd(sql);
            cmd.Parameters.AddWithValue("player", playerId);
            cmd.Parameters.Add(new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = entries.Select(e => e.Id).ToArray() });
            cmd.Parameters.Add(new NpgsqlParameter("currencies", NpgsqlDbType.Array | NpgsqlDbType.Smallint) { Value = entries.Select(e => (short)e.Currency).ToArray() });
            cmd.Parameters.Add(new NpgsqlParameter("amounts", NpgsqlDbType.Array | NpgsqlDbType.Bigint) { Value = entries.Select(e => e.Amount).ToArray() });
            cmd.Parameters.Add(new NpgsqlParameter("balances", NpgsqlDbType.Array | NpgsqlDbType.Bigint) { Value = entries.Select(e => e.BalanceAfter).ToArray() });
            cmd.Parameters.Add(new NpgsqlParameter("reasons", NpgsqlDbType.Array | NpgsqlDbType.Smallint) { Value = entries.Select(e => (short)e.Reason).ToArray() });
            cmd.Parameters.Add(new NpgsqlParameter("references", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = entries.Select(e => (object?)e.Reference ?? DBNull.Value).ToArray() });
            cmd.Parameters.Add(new NpgsqlParameter("keys", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = entries.Select(e => (object?)e.IdempotencyKey ?? DBNull.Value).ToArray() });
            cmd.Parameters.Add(new NpgsqlParameter("created", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz) { Value = entries.Select(e => TimeUtil.FromUnixMs(e.TimestampUnixMs)).ToArray() });
            await cmd.ExecuteNonQueryAsync(_ct).ConfigureAwait(false);
        }

        public async Task AppendPurchaseLogAsync(Guid playerId, TransactionRecord record)
        {
            const string sql = @"
                insert into public.purchase_log (player_id, transaction_id, item_id, kind, method, coins_spent, orbes_spent, cents_charged, created_at)
                values (@player, @tx, @item, @kind, @method, @coins, @orbes, @cents, @created)";

            await using NpgsqlCommand cmd = Cmd(sql);
            cmd.Parameters.AddWithValue("player", playerId);
            cmd.Parameters.AddWithValue("tx", record.TransactionId);
            cmd.Parameters.AddWithValue("item", record.ItemId);
            cmd.Parameters.AddWithValue("kind", (short)record.Kind);
            cmd.Parameters.AddWithValue("method", (short)record.Method);
            cmd.Parameters.AddWithValue("coins", record.CoinsSpent);
            cmd.Parameters.AddWithValue("orbes", record.OrbesSpent);
            cmd.Parameters.AddWithValue("cents", record.CentsCharged);
            cmd.Parameters.AddWithValue("created", TimeUtil.FromUnixMs(record.TimestampUnixMs));
            await cmd.ExecuteNonQueryAsync(_ct).ConfigureAwait(false);
        }

        public async Task<bool> TryInsertIapAsync(IapRow row)
        {
            const string sql = @"
                insert into public.iap_purchases (store_transaction_id, player_id, sku, price_cents, purchase_token_hash, status)
                values (@id, @player, @sku, @cents, @token, @status)
                on conflict (store_transaction_id) do nothing";

            await using NpgsqlCommand cmd = Cmd(sql);
            cmd.Parameters.AddWithValue("id", row.StoreTransactionId);
            cmd.Parameters.AddWithValue("player", row.PlayerId);
            cmd.Parameters.AddWithValue("sku", row.Sku);
            cmd.Parameters.AddWithValue("cents", row.PriceCents);
            cmd.Parameters.AddWithValue("token", row.PurchaseTokenHash);
            cmd.Parameters.AddWithValue("status", (short)row.Status);
            return await cmd.ExecuteNonQueryAsync(_ct).ConfigureAwait(false) == 1;
        }

        public async Task<long> InsertReplayAsync(ReplayRow row)
        {
            const string sql = @"
                insert into public.replays (player_id, mode, seed, stage_id, trophies, final_score, region, data, is_ghost)
                values (@player, @mode, @seed, @stage, @trophies, @score, @region, @data, @ghost)
                returning id";

            await using NpgsqlCommand cmd = Cmd(sql);
            cmd.Parameters.AddWithValue("player", row.PlayerId);
            cmd.Parameters.AddWithValue("mode", (short)row.Mode);
            cmd.Parameters.AddWithValue("seed", unchecked((long)row.Seed));
            cmd.Parameters.AddWithValue("stage", row.StageId);
            cmd.Parameters.AddWithValue("trophies", row.Trophies);
            cmd.Parameters.AddWithValue("score", row.FinalScore);
            cmd.Parameters.AddWithValue("region", row.Region);
            cmd.Parameters.AddWithValue("data", row.Data);
            cmd.Parameters.AddWithValue("ghost", row.IsGhost);
            row.Id = (long)(await cmd.ExecuteScalarAsync(_ct).ConfigureAwait(false))!;
            return row.Id;
        }

        public async Task<ReplayRow?> GetReplayAsync(long id)
        {
            await using NpgsqlCommand cmd = Cmd(ReplaySelect + " where id = @id");
            cmd.Parameters.AddWithValue("id", id);
            return await ReadReplay(cmd).ConfigureAwait(false);
        }

        public async Task<ReplayRow?> LatestGhostOfPlayerAsync(Guid playerId)
        {
            await using NpgsqlCommand cmd = Cmd(ReplaySelect + " where player_id = @player and is_ghost order by created_at desc, id desc limit 1");
            cmd.Parameters.AddWithValue("player", playerId);
            return await ReadReplay(cmd).ConfigureAwait(false);
        }

        public async Task<MatchRow?> GetMatchAsync(string id, bool forUpdate = true)
        {
            string sql = @"
                select id, player_id, mode, seed, stage_id, config::text, status, opponent_id, ghost_replay_id, replay_id,
                       result::text, started_at, finished_at
                from public.matches where id = @id" + (forUpdate ? " for update" : string.Empty);

            await using NpgsqlCommand cmd = Cmd(sql);
            cmd.Parameters.AddWithValue("id", id);
            await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(_ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(_ct).ConfigureAwait(false))
            {
                return null;
            }
            return new MatchRow
            {
                Id = reader.GetString(0),
                PlayerId = reader.GetGuid(1),
                Mode = (GameMode)reader.GetInt16(2),
                Seed = unchecked((ulong)reader.GetInt64(3)),
                StageId = reader.GetInt32(4),
                Config = Json.Deserialize<MatchConfigSnapshot>(reader.GetString(5)),
                Status = (MatchStatus)reader.GetInt16(6),
                OpponentId = reader.IsDBNull(7) ? null : reader.GetGuid(7),
                GhostReplayId = reader.IsDBNull(8) ? null : reader.GetInt64(8),
                ReplayId = reader.IsDBNull(9) ? null : reader.GetInt64(9),
                ResultJson = reader.IsDBNull(10) ? null : reader.GetString(10),
                StartedAt = reader.GetDateTime(11),
                FinishedAt = reader.IsDBNull(12) ? null : reader.GetDateTime(12)
            };
        }

        public async Task InsertMatchAsync(MatchRow match)
        {
            const string sql = @"
                insert into public.matches (id, player_id, mode, seed, stage_id, config, status, opponent_id, ghost_replay_id, started_at)
                values (@id, @player, @mode, @seed, @stage, @config, @status, @opponent, @ghost, @started)";

            await using NpgsqlCommand cmd = Cmd(sql);
            cmd.Parameters.AddWithValue("id", match.Id);
            cmd.Parameters.AddWithValue("player", match.PlayerId);
            cmd.Parameters.AddWithValue("mode", (short)match.Mode);
            cmd.Parameters.AddWithValue("seed", unchecked((long)match.Seed));
            cmd.Parameters.AddWithValue("stage", match.StageId);
            cmd.Parameters.Add(new NpgsqlParameter("config", NpgsqlDbType.Jsonb) { Value = Json.Serialize(match.Config) });
            cmd.Parameters.AddWithValue("status", (short)match.Status);
            cmd.Parameters.Add(Param("opponent", match.OpponentId, NpgsqlDbType.Uuid));
            cmd.Parameters.Add(Param("ghost", match.GhostReplayId, NpgsqlDbType.Bigint));
            cmd.Parameters.AddWithValue("started", Utc(match.StartedAt));
            await cmd.ExecuteNonQueryAsync(_ct).ConfigureAwait(false);
        }

        public async Task UpdateMatchAsync(MatchRow match)
        {
            const string sql = @"
                update public.matches set
                  config = @config, status = @status, opponent_id = @opponent, ghost_replay_id = @ghost,
                  replay_id = @replay, result = @result, finished_at = @finished
                where id = @id";

            await using NpgsqlCommand cmd = Cmd(sql);
            cmd.Parameters.AddWithValue("id", match.Id);
            cmd.Parameters.Add(new NpgsqlParameter("config", NpgsqlDbType.Jsonb) { Value = Json.Serialize(match.Config) });
            cmd.Parameters.AddWithValue("status", (short)match.Status);
            cmd.Parameters.Add(Param("opponent", match.OpponentId, NpgsqlDbType.Uuid));
            cmd.Parameters.Add(Param("ghost", match.GhostReplayId, NpgsqlDbType.Bigint));
            cmd.Parameters.Add(Param("replay", match.ReplayId, NpgsqlDbType.Bigint));
            cmd.Parameters.Add(Param("result", match.ResultJson, NpgsqlDbType.Jsonb));
            cmd.Parameters.Add(Param("finished", match.FinishedAt.HasValue ? Utc(match.FinishedAt.Value) : null, NpgsqlDbType.TimestampTz));
            await cmd.ExecuteNonQueryAsync(_ct).ConfigureAwait(false);
        }

        public async Task<int> ExpireOpenMatchesAsync(DateTime startedBefore)
        {
            await using NpgsqlCommand cmd = Cmd("update public.matches set status = 3, finished_at = now() where status = 0 and started_at < @before");
            cmd.Parameters.AddWithValue("before", Utc(startedBefore));
            return await cmd.ExecuteNonQueryAsync(_ct).ConfigureAwait(false);
        }

        public async Task<GuildRecord?> GetGuildAsync(long id, bool forUpdate = true)
        {
            await using NpgsqlCommand cmd = Cmd("select id, version, created_at, state::text from public.guilds where id = @id" + (forUpdate ? " for update" : string.Empty));
            cmd.Parameters.AddWithValue("id", id);
            await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(_ct).ConfigureAwait(false);
            return await reader.ReadAsync(_ct).ConfigureAwait(false) ? ReadGuild(reader) : null;
        }

        public async Task<long?> InsertGuildAsync(GuildRecord guild)
        {
            const string insert = @"
                insert into public.guilds (name, level, member_count, total_trophies, is_open, min_trophies, state)
                values (@name, @level, @members, @trophies, @open, @min, @state)
                on conflict do nothing
                returning id, created_at";

            await using (NpgsqlCommand cmd = Cmd(insert))
            {
                AddGuildColumns(cmd, guild);
                await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(_ct).ConfigureAwait(false);
                if (!await reader.ReadAsync(_ct).ConfigureAwait(false))
                {
                    return null;
                }
                guild.Id = reader.GetInt64(0);
                guild.CreatedAt = reader.GetDateTime(1);
            }

            guild.Version = 1;
            guild.Guild.Id = guild.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            await using (NpgsqlCommand fix = Cmd("update public.guilds set state = @state where id = @id"))
            {
                fix.Parameters.AddWithValue("id", guild.Id);
                fix.Parameters.Add(new NpgsqlParameter("state", NpgsqlDbType.Jsonb) { Value = Json.Serialize(guild) });
                await fix.ExecuteNonQueryAsync(_ct).ConfigureAwait(false);
            }
            return guild.Id;
        }

        public async Task UpdateGuildAsync(GuildRecord guild)
        {
            const string sql = @"
                update public.guilds set
                  name = @name, level = @level, member_count = @members, total_trophies = @trophies, is_open = @open,
                  min_trophies = @min, state = @state, version = version + 1
                where id = @id and version = @version";

            await using NpgsqlCommand cmd = Cmd(sql);
            AddGuildColumns(cmd, guild);
            cmd.Parameters.AddWithValue("id", guild.Id);
            cmd.Parameters.AddWithValue("version", guild.Version);
            if (await cmd.ExecuteNonQueryAsync(_ct).ConfigureAwait(false) != 1)
            {
                throw new ConcurrencyException("Guild " + guild.Id + " was modified concurrently.");
            }
            guild.Version++;
        }

        public async Task DeleteGuildAsync(long id)
        {
            await using NpgsqlCommand cmd = Cmd("delete from public.guilds where id = @id");
            cmd.Parameters.AddWithValue("id", id);
            await cmd.ExecuteNonQueryAsync(_ct).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<GuildRecord>> GetAllGuildsAsync()
        {
            await using NpgsqlCommand cmd = Cmd("select id, version, created_at, state::text from public.guilds where member_count > 0 order by id for update");
            var rows = new List<GuildRecord>();
            await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(_ct).ConfigureAwait(false);
            while (await reader.ReadAsync(_ct).ConfigureAwait(false))
            {
                rows.Add(ReadGuild(reader));
            }
            return rows;
        }

        public async Task<long> InsertGuildMessageAsync(GuildMessageRow message)
        {
            const string sql = @"
                insert into public.guild_messages (guild_id, player_id, display_name, body, masked)
                values (@guild, @player, @name, @body, @masked)
                returning id, created_at";

            await using NpgsqlCommand cmd = Cmd(sql);
            cmd.Parameters.AddWithValue("guild", message.GuildId);
            cmd.Parameters.Add(Param("player", message.PlayerId, NpgsqlDbType.Uuid));
            cmd.Parameters.AddWithValue("name", message.DisplayName);
            cmd.Parameters.AddWithValue("body", message.Body);
            cmd.Parameters.AddWithValue("masked", message.Masked);
            await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(_ct).ConfigureAwait(false);
            await reader.ReadAsync(_ct).ConfigureAwait(false);
            message.Id = reader.GetInt64(0);
            message.CreatedAt = reader.GetDateTime(1);
            return message.Id;
        }

        public async Task<long> InsertFlagAsync(CheatFlag flag)
        {
            const string sql = @"
                insert into public.cheat_flags (player_id, reason, severity, details, match_id, created_at)
                values (@player, @reason, @severity, @details, @match, @created)
                returning id";

            await using NpgsqlCommand cmd = Cmd(sql);
            cmd.Parameters.AddWithValue("player", Guid.Parse(flag.PlayerId));
            cmd.Parameters.AddWithValue("reason", (short)flag.Reason);
            cmd.Parameters.AddWithValue("severity", (short)flag.Severity);
            cmd.Parameters.Add(Param("details", flag.Details, NpgsqlDbType.Text));
            cmd.Parameters.Add(Param("match", flag.MatchId, NpgsqlDbType.Text));
            cmd.Parameters.AddWithValue("created", TimeUtil.FromUnixMs(flag.AtUnixMs));
            return (long)(await cmd.ExecuteScalarAsync(_ct).ConfigureAwait(false))!;
        }

        public async Task<FlagRow?> GetFlagAsync(long id)
        {
            await using NpgsqlCommand cmd = Cmd(FlagSelect + " where id = @id for update");
            cmd.Parameters.AddWithValue("id", id);
            await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(_ct).ConfigureAwait(false);
            return await reader.ReadAsync(_ct).ConfigureAwait(false) ? ReadFlag(reader) : null;
        }

        public async Task UpdateFlagAsync(FlagRow flag)
        {
            const string sql = @"
                update public.cheat_flags
                set reviewed = @reviewed, review_outcome = @outcome, reviewed_by = @by, reviewed_at = case when @reviewed then now() else null end
                where id = @id";

            await using NpgsqlCommand cmd = Cmd(sql);
            cmd.Parameters.AddWithValue("id", flag.Id);
            cmd.Parameters.AddWithValue("reviewed", flag.Reviewed);
            cmd.Parameters.Add(Param("outcome", flag.ReviewOutcome, NpgsqlDbType.Text));
            cmd.Parameters.Add(Param("by", flag.ReviewedBy, NpgsqlDbType.Uuid));
            await cmd.ExecuteNonQueryAsync(_ct).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<Guid>> RegisterDeviceAsync(string deviceHash, Guid playerId)
        {
            await using (NpgsqlCommand upsert = Cmd(@"
                insert into public.device_accounts (device_hash, player_id) values (@hash, @player)
                on conflict (device_hash, player_id) do update set last_seen_at = now()"))
            {
                upsert.Parameters.AddWithValue("hash", deviceHash);
                upsert.Parameters.AddWithValue("player", playerId);
                await upsert.ExecuteNonQueryAsync(_ct).ConfigureAwait(false);
            }

            await using NpgsqlCommand cmd = Cmd("select player_id from public.device_accounts where device_hash = @hash");
            cmd.Parameters.AddWithValue("hash", deviceHash);
            var accounts = new List<Guid>();
            await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(_ct).ConfigureAwait(false);
            while (await reader.ReadAsync(_ct).ConfigureAwait(false))
            {
                accounts.Add(reader.GetGuid(0));
            }
            return accounts;
        }

        public async Task ArchiveSeasonAsync(int week, League league, string entriesJson)
        {
            await using NpgsqlCommand cmd = Cmd(@"
                insert into public.season_archives (week, league, entries) values (@week, @league, @entries)
                on conflict (week, league) do update set entries = excluded.entries");
            cmd.Parameters.AddWithValue("week", week);
            cmd.Parameters.AddWithValue("league", (short)league);
            cmd.Parameters.Add(new NpgsqlParameter("entries", NpgsqlDbType.Jsonb) { Value = entriesJson });
            await cmd.ExecuteNonQueryAsync(_ct).ConfigureAwait(false);
        }

        public async Task ArchiveGuildSeasonAsync(int week, string entriesJson)
        {
            await using NpgsqlCommand cmd = Cmd(@"
                insert into public.guild_season_archives (week, entries) values (@week, @entries)
                on conflict (week) do update set entries = excluded.entries");
            cmd.Parameters.AddWithValue("week", week);
            cmd.Parameters.Add(new NpgsqlParameter("entries", NpgsqlDbType.Jsonb) { Value = entriesJson });
            await cmd.ExecuteNonQueryAsync(_ct).ConfigureAwait(false);
        }

        public async Task<string?> GetConfigAsync(string key)
        {
            await using NpgsqlCommand cmd = Cmd("select value::text from public.remote_config where key = @key");
            cmd.Parameters.AddWithValue("key", key);
            return await cmd.ExecuteScalarAsync(_ct).ConfigureAwait(false) as string;
        }

        public async Task SetConfigAsync(string key, string json, Guid? updatedBy)
        {
            await using NpgsqlCommand cmd = Cmd(@"
                insert into public.remote_config (key, value, updated_by) values (@key, @value, @by)
                on conflict (key) do update set value = excluded.value, updated_by = excluded.updated_by");
            cmd.Parameters.AddWithValue("key", key);
            cmd.Parameters.Add(new NpgsqlParameter("value", NpgsqlDbType.Jsonb) { Value = json });
            cmd.Parameters.Add(Param("by", updatedBy, NpgsqlDbType.Uuid));
            await cmd.ExecuteNonQueryAsync(_ct).ConfigureAwait(false);
        }

        public async Task<bool> TryAdvisoryLockAsync(long key)
        {
            await using NpgsqlCommand cmd = Cmd("select pg_try_advisory_xact_lock(@key)");
            cmd.Parameters.AddWithValue("key", key);
            return (bool)(await cmd.ExecuteScalarAsync(_ct).ConfigureAwait(false))!;
        }

        // -------------------------------------------------------------- mapping

        private const string ReplaySelect = "select id, player_id, mode, seed, stage_id, trophies, final_score, region, data, is_ghost, created_at from public.replays";

        private async Task<ReplayRow?> ReadReplay(NpgsqlCommand cmd)
        {
            await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(_ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(_ct).ConfigureAwait(false))
            {
                return null;
            }
            return new ReplayRow
            {
                Id = reader.GetInt64(0),
                PlayerId = reader.GetGuid(1),
                Mode = (GameMode)reader.GetInt16(2),
                Seed = unchecked((ulong)reader.GetInt64(3)),
                StageId = reader.GetInt32(4),
                Trophies = reader.GetInt32(5),
                FinalScore = reader.GetInt64(6),
                Region = reader.GetString(7),
                Data = reader.GetFieldValue<byte[]>(8),
                IsGhost = reader.GetBoolean(9),
                CreatedAt = reader.GetDateTime(10)
            };
        }

        private static PlayerRecord ReadPlayer(NpgsqlDataReader reader) => new()
        {
            Id = reader.GetGuid(0),
            Version = reader.GetInt64(1),
            CreatedAt = reader.GetDateTime(2),
            State = Json.Deserialize<PlayerState>(reader.GetString(3))
        };

        private static GuildRecord ReadGuild(NpgsqlDataReader reader)
        {
            GuildRecord guild = Json.Deserialize<GuildRecord>(reader.GetString(3));
            guild.Id = reader.GetInt64(0);
            guild.Version = reader.GetInt64(1);
            guild.CreatedAt = reader.GetDateTime(2);
            return guild;
        }

        private void AddPlayerColumns(NpgsqlCommand cmd, PlayerRecord player)
        {
            PlayerState s = player.State;
            int trophies = s.Pvp.Trophies;
            cmd.Parameters.AddWithValue("id", player.Id);
            cmd.Parameters.AddWithValue("name", s.DisplayName);
            cmd.Parameters.AddWithValue("trophies", trophies);
            cmd.Parameters.AddWithValue("league", (short)LeagueTable.GetLeague(trophies, _balance().Trophies));
            cmd.Parameters.AddWithValue("stage", s.Story.HighestUnlockedStage);
            cmd.Parameters.AddWithValue("vip", (short)s.Vip.Tier);
            cmd.Parameters.AddWithValue("spend", s.Vip.LifetimeSpendCents);
            cmd.Parameters.Add(Param("guild", s.GuildId, NpgsqlDbType.Bigint));
            cmd.Parameters.AddWithValue("region", s.Region ?? string.Empty);
            cmd.Parameters.Add(Param("suspended", s.Integrity.SuspendedUntilUnixMs > 0 ? TimeUtil.FromUnixMs(s.Integrity.SuspendedUntilUnixMs) : null, NpgsqlDbType.TimestampTz));
            cmd.Parameters.AddWithValue("banned", s.Integrity.PermanentlyBanned);
            cmd.Parameters.Add(new NpgsqlParameter("state", NpgsqlDbType.Jsonb) { Value = Json.Serialize(s) });
        }

        private static void AddGuildColumns(NpgsqlCommand cmd, GuildRecord guild)
        {
            cmd.Parameters.AddWithValue("name", guild.Guild.Name);
            cmd.Parameters.AddWithValue("level", guild.Guild.Level);
            cmd.Parameters.AddWithValue("members", guild.Guild.Members.Count);
            cmd.Parameters.AddWithValue("trophies", guild.Guild.TotalTrophies);
            cmd.Parameters.AddWithValue("open", guild.Guild.IsOpen);
            cmd.Parameters.AddWithValue("min", guild.Guild.MinTrophies);
            cmd.Parameters.Add(new NpgsqlParameter("state", NpgsqlDbType.Jsonb) { Value = Json.Serialize(guild) });
        }
    }
}
