using CrushRoyale.Core.AntiCheat;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Economy;
using CrushRoyale.Core.Pvp;
using CrushRoyale.Server.Infrastructure;

namespace CrushRoyale.Server.Persistence;

/// <summary>
/// Process-local store for development and integration tests. Transactions are serialized by a gate and
/// rolled back through an undo log; documents are deep-copied on every read/write so callers can never
/// mutate stored state by accident. Not for production (no durability, single instance).
/// </summary>
public sealed class InMemoryGameStore : IGameStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _lock = new();
    private readonly Func<GameBalance> _balance;

    private readonly Dictionary<Guid, StoredDoc> _players = new();
    private readonly Dictionary<long, StoredDoc> _guilds = new();
    private readonly List<GuildMessageRow> _messages = new();
    private readonly Dictionary<long, ReplayRow> _replays = new();
    private readonly Dictionary<string, string> _matches = new(StringComparer.Ordinal);
    private readonly List<(Guid PlayerId, LedgerEntry Entry)> _ledger = new();
    private readonly List<(Guid PlayerId, TransactionRecord Record)> _purchases = new();
    private readonly Dictionary<string, IapRow> _iap = new(StringComparer.Ordinal);
    private readonly List<FlagRow> _flags = new();
    private readonly Dictionary<string, HashSet<Guid>> _devices = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _archives = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _config = new(StringComparer.Ordinal);
    private long _sequence;

    public InMemoryGameStore(Func<GameBalance> balance)
    {
        _balance = balance ?? throw new ArgumentNullException(nameof(balance));
    }

    /// <summary>Test hook: full ledger.</summary>
    public IReadOnlyList<(Guid PlayerId, LedgerEntry Entry)> LedgerSnapshot()
    {
        lock (_lock)
        {
            return _ledger.ToList();
        }
    }

    /// <summary>Test hook: purchase log.</summary>
    public IReadOnlyList<(Guid PlayerId, TransactionRecord Record)> PurchaseSnapshot()
    {
        lock (_lock)
        {
            return _purchases.ToList();
        }
    }

    public async Task<T> TransactAsync<T>(Func<IStoreTransaction, Task<T>> work, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var tx = new Tx(this);
        try
        {
            return await work(tx).ConfigureAwait(false);
        }
        catch
        {
            tx.Rollback();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<IReadOnlyList<LeaderboardRow>> TopPlayersAsync(League? league, int limit, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            IReadOnlyList<LeaderboardRow> rows = RankedPlayers(league)
                .Take(limit)
                .Select((p, i) => new LeaderboardRow(i + 1, p.Id, p.State.DisplayName, p.State.Pvp.Trophies, LeagueOf(p.State.Pvp.Trophies)))
                .ToList();
            return Task.FromResult(rows);
        }
    }

    public Task<int?> PlayerRankAsync(Guid playerId, League? league, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            int index = RankedPlayers(league).FindIndex(p => p.Id == playerId);
            return Task.FromResult(index < 0 ? (int?)null : index + 1);
        }
    }

    public Task<IReadOnlyList<GuildRankingRow>> TopGuildsAsync(int limit, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            IReadOnlyList<GuildRankingRow> rows = _guilds.Values
                .Select(d => Json.Deserialize<GuildRecord>(d.Json))
                .Where(g => g.Guild.Members.Count > 0)
                .OrderByDescending(g => g.Guild.TotalTrophies)
                .ThenBy(g => g.Id)
                .Take(limit)
                .Select((g, i) => new GuildRankingRow(i + 1, g.Id, g.Guild.Name, g.Guild.TotalTrophies, g.Guild.Members.Count, g.Guild.Level))
                .ToList();
            return Task.FromResult(rows);
        }
    }

    public Task<IReadOnlyList<GhostRow>> FindGhostsAsync(int minTrophies, int maxTrophies, DateTime recordedAfter, int limit, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            IReadOnlyList<GhostRow> rows = _replays.Values
                .Where(r => r.IsGhost && r.Trophies >= minTrophies && r.Trophies <= maxTrophies && r.CreatedAt >= recordedAfter)
                .OrderByDescending(r => r.CreatedAt)
                .Take(limit)
                .Select(r => new GhostRow(r.Id, r.PlayerId, r.Trophies, r.CreatedAt, r.Region, r.Seed))
                .ToList();
            return Task.FromResult(rows);
        }
    }

    public Task<IReadOnlyList<PlayerSummary>> SearchPlayersAsync(string namePrefix, int limit, CancellationToken cancellationToken)
    {
        string prefix = (namePrefix ?? string.Empty).Trim().ToLowerInvariant();
        lock (_lock)
        {
            IReadOnlyList<PlayerSummary> rows = AllPlayers()
                .Where(p => p.State.DisplayName.ToLowerInvariant().StartsWith(prefix, StringComparison.Ordinal))
                .OrderBy(p => p.State.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Take(limit)
                .Select(Summary)
                .ToList();
            return Task.FromResult(rows);
        }
    }

    public Task<IReadOnlyList<PlayerSummary>> GetPlayerSummariesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            IReadOnlyList<PlayerSummary> rows = ids
                .Where(_players.ContainsKey)
                .Select(id => Summary(ToPlayer(id, _players[id])))
                .ToList();
            return Task.FromResult(rows);
        }
    }

    public Task<IReadOnlyList<GuildSummary>> SearchGuildsAsync(string namePrefix, int limit, CancellationToken cancellationToken)
    {
        string prefix = (namePrefix ?? string.Empty).Trim().ToLowerInvariant();
        lock (_lock)
        {
            IReadOnlyList<GuildSummary> rows = _guilds.Values
                .Select(d => Json.Deserialize<GuildRecord>(d.Json))
                .Where(g => g.Guild.Name.ToLowerInvariant().StartsWith(prefix, StringComparison.Ordinal))
                .OrderByDescending(g => g.Guild.TotalTrophies)
                .Take(limit)
                .Select(g => new GuildSummary(g.Id, g.Guild.Name, g.Guild.Level, g.Guild.Members.Count, g.Guild.TotalTrophies, g.Guild.IsOpen, g.Guild.MinTrophies))
                .ToList();
            return Task.FromResult(rows);
        }
    }

    public Task<IReadOnlyList<GuildMessageRow>> GetGuildMessagesAsync(long guildId, long? beforeId, int limit, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            IReadOnlyList<GuildMessageRow> rows = _messages
                .Where(m => m.GuildId == guildId && (beforeId == null || m.Id < beforeId))
                .OrderByDescending(m => m.Id)
                .Take(limit)
                .Select(Json.Clone)
                .ToList();
            return Task.FromResult(rows);
        }
    }

    public Task<IReadOnlyList<FlagRow>> PendingFlagsAsync(int limit, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            IReadOnlyList<FlagRow> rows = _flags.Where(f => !f.Reviewed)
                .OrderByDescending(f => f.Severity)
                .ThenBy(f => f.CreatedAt)
                .Take(limit)
                .Select(Json.Clone)
                .ToList();
            return Task.FromResult(rows);
        }
    }

    public Task<string?> GetConfigAsync(string key, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return Task.FromResult(_config.TryGetValue(key, out string? v) ? v : null);
        }
    }

    public Task<bool> PingAsync(CancellationToken cancellationToken) => Task.FromResult(true);

    // ---------------------------------------------------------------- helpers

    private League LeagueOf(int trophies) => LeagueTable.GetLeague(trophies, _balance().Trophies);

    private List<PlayerRecord> AllPlayers() => _players.Select(kv => ToPlayer(kv.Key, kv.Value)).ToList();

    private List<PlayerRecord> RankedPlayers(League? league) => AllPlayers()
        .Where(p => !p.State.Integrity.PermanentlyBanned && (league == null || LeagueOf(p.State.Pvp.Trophies) == league))
        .OrderByDescending(p => p.State.Pvp.Trophies)
        .ThenBy(p => p.Id)
        .ToList();

    private PlayerSummary Summary(PlayerRecord p) => new(
        p.Id,
        p.State.DisplayName,
        p.State.Pvp.Trophies,
        LeagueOf(p.State.Pvp.Trophies),
        p.State.Story.HighestUnlockedStage,
        p.State.GuildId,
        p.State.Inventory.EquippedFrame,
        p.State.Inventory.EquippedTitle);

    private static PlayerRecord ToPlayer(Guid id, StoredDoc doc) => new()
    {
        Id = id,
        Version = doc.Version,
        CreatedAt = doc.CreatedAt,
        State = Json.Deserialize<PlayerState>(doc.Json)
    };

    private sealed record StoredDoc(long Version, DateTime CreatedAt, string Json, string Key);

    private sealed class Tx : IStoreTransaction
    {
        private readonly InMemoryGameStore _s;
        private readonly List<Action> _undo = new();

        public Tx(InMemoryGameStore store)
        {
            _s = store;
        }

        public void Rollback()
        {
            lock (_s._lock)
            {
                for (int i = _undo.Count - 1; i >= 0; i--)
                {
                    _undo[i]();
                }
            }
        }

        public Task<PlayerRecord?> GetPlayerAsync(Guid id, bool forUpdate = true)
        {
            lock (_s._lock)
            {
                return Task.FromResult(_s._players.TryGetValue(id, out StoredDoc? d) ? ToPlayer(id, d) : null);
            }
        }

        public Task InsertPlayerAsync(PlayerRecord player)
        {
            lock (_s._lock)
            {
                if (_s._players.ContainsKey(player.Id))
                {
                    throw new ConcurrencyException("Player already exists.");
                }
                player.Version = 1;
                _s._players[player.Id] = new StoredDoc(1, player.CreatedAt, Json.Serialize(player.State), string.Empty);
                _undo.Add(() => _s._players.Remove(player.Id));
            }
            return Task.CompletedTask;
        }

        public Task UpdatePlayerAsync(PlayerRecord player)
        {
            lock (_s._lock)
            {
                if (!_s._players.TryGetValue(player.Id, out StoredDoc? old) || old.Version != player.Version)
                {
                    throw new ConcurrencyException("Player " + player.Id + " was modified concurrently.");
                }
                _s._players[player.Id] = old with { Version = old.Version + 1, Json = Json.Serialize(player.State) };
                player.Version++;
                _undo.Add(() => _s._players[player.Id] = old);
            }
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<PlayerRecord>> GetPlayersPageAsync(Guid? afterId, int limit)
        {
            lock (_s._lock)
            {
                IReadOnlyList<PlayerRecord> rows = _s._players
                    .Where(kv => afterId == null || kv.Key.CompareTo(afterId.Value) > 0)
                    .OrderBy(kv => kv.Key)
                    .Take(limit)
                    .Select(kv => ToPlayer(kv.Key, kv.Value))
                    .ToList();
                return Task.FromResult(rows);
            }
        }

        public Task AppendLedgerAsync(Guid playerId, IReadOnlyList<LedgerEntry> entries)
        {
            lock (_s._lock)
            {
                foreach (LedgerEntry e in entries)
                {
                    if (e.IdempotencyKey != null && _s._ledger.Any(l => l.PlayerId == playerId && l.Entry.IdempotencyKey == e.IdempotencyKey))
                    {
                        throw new InvalidOperationException("Duplicate idempotency key " + e.IdempotencyKey);
                    }
                    var item = (playerId, Json.Clone(e));
                    _s._ledger.Add(item);
                    _undo.Add(() => _s._ledger.Remove(item));
                }
            }
            return Task.CompletedTask;
        }

        public Task AppendPurchaseLogAsync(Guid playerId, TransactionRecord record)
        {
            lock (_s._lock)
            {
                var item = (playerId, Json.Clone(record));
                _s._purchases.Add(item);
                _undo.Add(() => _s._purchases.Remove(item));
            }
            return Task.CompletedTask;
        }

        public Task<bool> TryInsertIapAsync(IapRow row)
        {
            lock (_s._lock)
            {
                if (_s._iap.ContainsKey(row.StoreTransactionId))
                {
                    return Task.FromResult(false);
                }
                _s._iap[row.StoreTransactionId] = Json.Clone(row);
                _undo.Add(() => _s._iap.Remove(row.StoreTransactionId));
                return Task.FromResult(true);
            }
        }

        public Task<long> InsertReplayAsync(ReplayRow row)
        {
            lock (_s._lock)
            {
                long id = ++_s._sequence;
                var copy = Json.Clone(row);
                copy.Id = id;
                copy.Data = row.Data.ToArray();
                _s._replays[id] = copy;
                _undo.Add(() => _s._replays.Remove(id));
                row.Id = id;
                return Task.FromResult(id);
            }
        }

        public Task<ReplayRow?> GetReplayAsync(long id)
        {
            lock (_s._lock)
            {
                return Task.FromResult(_s._replays.TryGetValue(id, out ReplayRow? r) ? CloneReplay(r) : null);
            }
        }

        public Task<ReplayRow?> LatestGhostOfPlayerAsync(Guid playerId)
        {
            lock (_s._lock)
            {
                ReplayRow? r = _s._replays.Values.Where(x => x.PlayerId == playerId && x.IsGhost).OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).FirstOrDefault();
                return Task.FromResult(r == null ? null : CloneReplay(r));
            }
        }

        public Task<MatchRow?> GetMatchAsync(string id, bool forUpdate = true)
        {
            lock (_s._lock)
            {
                return Task.FromResult(_s._matches.TryGetValue(id, out string? json) ? Json.Deserialize<MatchRow>(json) : null);
            }
        }

        public Task InsertMatchAsync(MatchRow match)
        {
            lock (_s._lock)
            {
                if (_s._matches.ContainsKey(match.Id))
                {
                    throw new InvalidOperationException("Duplicate match id.");
                }
                _s._matches[match.Id] = Json.Serialize(match);
                _undo.Add(() => _s._matches.Remove(match.Id));
            }
            return Task.CompletedTask;
        }

        public Task UpdateMatchAsync(MatchRow match)
        {
            lock (_s._lock)
            {
                string old = _s._matches[match.Id];
                _s._matches[match.Id] = Json.Serialize(match);
                _undo.Add(() => _s._matches[match.Id] = old);
            }
            return Task.CompletedTask;
        }

        public Task<int> ExpireOpenMatchesAsync(DateTime startedBefore)
        {
            lock (_s._lock)
            {
                int count = 0;
                foreach (string id in _s._matches.Keys.ToList())
                {
                    MatchRow m = Json.Deserialize<MatchRow>(_s._matches[id]);
                    if (m.Status == MatchStatus.Started && m.StartedAt < startedBefore)
                    {
                        string old = _s._matches[id];
                        m.Status = MatchStatus.Expired;
                        m.FinishedAt = DateTime.UtcNow;
                        _s._matches[id] = Json.Serialize(m);
                        _undo.Add(() => _s._matches[id] = old);
                        count++;
                    }
                }
                return Task.FromResult(count);
            }
        }

        public Task<GuildRecord?> GetGuildAsync(long id, bool forUpdate = true)
        {
            lock (_s._lock)
            {
                if (!_s._guilds.TryGetValue(id, out StoredDoc? d))
                {
                    return Task.FromResult<GuildRecord?>(null);
                }
                GuildRecord g = Json.Deserialize<GuildRecord>(d.Json);
                g.Version = d.Version;
                return Task.FromResult<GuildRecord?>(g);
            }
        }

        public Task<long?> InsertGuildAsync(GuildRecord guild)
        {
            lock (_s._lock)
            {
                string key = guild.Guild.Name.Trim().ToLowerInvariant();
                if (_s._guilds.Values.Any(g => g.Key == key))
                {
                    return Task.FromResult<long?>(null);
                }
                long id = ++_s._sequence;
                guild.Id = id;
                guild.Version = 1;
                guild.Guild.Id = id.ToString(System.Globalization.CultureInfo.InvariantCulture);
                _s._guilds[id] = new StoredDoc(1, guild.CreatedAt, Json.Serialize(guild), key);
                _undo.Add(() => _s._guilds.Remove(id));
                return Task.FromResult<long?>(id);
            }
        }

        public Task UpdateGuildAsync(GuildRecord guild)
        {
            lock (_s._lock)
            {
                if (!_s._guilds.TryGetValue(guild.Id, out StoredDoc? old) || old.Version != guild.Version)
                {
                    throw new ConcurrencyException("Guild " + guild.Id + " was modified concurrently.");
                }
                _s._guilds[guild.Id] = old with { Version = old.Version + 1, Json = Json.Serialize(guild), Key = guild.Guild.Name.Trim().ToLowerInvariant() };
                guild.Version++;
                _undo.Add(() => _s._guilds[guild.Id] = old);
            }
            return Task.CompletedTask;
        }

        public Task DeleteGuildAsync(long id)
        {
            lock (_s._lock)
            {
                if (_s._guilds.TryGetValue(id, out StoredDoc? old))
                {
                    _s._guilds.Remove(id);
                    var messages = _s._messages.Where(m => m.GuildId == id).ToList();
                    _s._messages.RemoveAll(m => m.GuildId == id);
                    _undo.Add(() =>
                    {
                        _s._guilds[id] = old;
                        _s._messages.AddRange(messages);
                    });
                }
            }
            return Task.CompletedTask;
        }

        public Task DeletePlayerAccountAsync(Guid id)
        {
            lock (_s._lock)
            {
                if (_s._players.TryGetValue(id, out StoredDoc? old))
                {
                    _s._players.Remove(id);
                    var devices = _s._devices.Where(kv => kv.Value.Remove(id)).Select(kv => kv.Key).ToList();
                    _undo.Add(() =>
                    {
                        _s._players[id] = old;
                        foreach (string device in devices)
                        {
                            _s._devices[device].Add(id);
                        }
                    });
                }
            }
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<GuildRecord>> GetAllGuildsAsync()
        {
            lock (_s._lock)
            {
                IReadOnlyList<GuildRecord> rows = _s._guilds.Values.Select(d =>
                {
                    GuildRecord g = Json.Deserialize<GuildRecord>(d.Json);
                    g.Version = d.Version;
                    return g;
                }).ToList();
                return Task.FromResult(rows);
            }
        }

        public Task<long> InsertGuildMessageAsync(GuildMessageRow message)
        {
            lock (_s._lock)
            {
                long id = ++_s._sequence;
                var copy = Json.Clone(message);
                copy.Id = id;
                _s._messages.Add(copy);
                _undo.Add(() => _s._messages.Remove(copy));
                message.Id = id;
                return Task.FromResult(id);
            }
        }

        public Task<long> InsertFlagAsync(CheatFlag flag)
        {
            lock (_s._lock)
            {
                long id = ++_s._sequence;
                var row = new FlagRow
                {
                    Id = id,
                    PlayerId = Guid.Parse(flag.PlayerId),
                    Reason = flag.Reason,
                    Severity = flag.Severity,
                    Details = flag.Details,
                    MatchId = flag.MatchId,
                    CreatedAt = Core.Common.TimeUtil.FromUnixMs(flag.AtUnixMs)
                };
                _s._flags.Add(row);
                _undo.Add(() => _s._flags.Remove(row));
                return Task.FromResult(id);
            }
        }

        public Task<FlagRow?> GetFlagAsync(long id)
        {
            lock (_s._lock)
            {
                FlagRow? f = _s._flags.FirstOrDefault(x => x.Id == id);
                return Task.FromResult(f == null ? null : Json.Clone(f));
            }
        }

        public Task UpdateFlagAsync(FlagRow flag)
        {
            lock (_s._lock)
            {
                int index = _s._flags.FindIndex(x => x.Id == flag.Id);
                FlagRow old = _s._flags[index];
                _s._flags[index] = Json.Clone(flag);
                _undo.Add(() => _s._flags[index] = old);
            }
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Guid>> RegisterDeviceAsync(string deviceHash, Guid playerId)
        {
            lock (_s._lock)
            {
                if (!_s._devices.TryGetValue(deviceHash, out HashSet<Guid>? accounts))
                {
                    accounts = new HashSet<Guid>();
                    _s._devices[deviceHash] = accounts;
                }
                if (accounts.Add(playerId))
                {
                    _undo.Add(() => accounts.Remove(playerId));
                }
                IReadOnlyList<Guid> list = accounts.ToList();
                return Task.FromResult(list);
            }
        }

        public Task ArchiveSeasonAsync(int week, League league, string entriesJson)
        {
            SetKeyed(_s._archives, "season:" + week + ":" + (int)league, entriesJson);
            return Task.CompletedTask;
        }

        public Task ArchiveGuildSeasonAsync(int week, string entriesJson)
        {
            SetKeyed(_s._archives, "guilds:" + week, entriesJson);
            return Task.CompletedTask;
        }

        public Task<string?> GetConfigAsync(string key)
        {
            lock (_s._lock)
            {
                return Task.FromResult(_s._config.TryGetValue(key, out string? v) ? v : null);
            }
        }

        public Task SetConfigAsync(string key, string json, Guid? updatedBy)
        {
            SetKeyed(_s._config, key, json);
            return Task.CompletedTask;
        }

        public Task<bool> TryAdvisoryLockAsync(long key) => Task.FromResult(true);

        private void SetKeyed(Dictionary<string, string> map, string key, string value)
        {
            lock (_s._lock)
            {
                bool had = map.TryGetValue(key, out string? old);
                map[key] = value;
                _undo.Add(() =>
                {
                    if (had)
                    {
                        map[key] = old!;
                    }
                    else
                    {
                        map.Remove(key);
                    }
                });
            }
        }

        private static ReplayRow CloneReplay(ReplayRow r)
        {
            var copy = Json.Clone(r);
            copy.Data = r.Data.ToArray();
            return copy;
        }
    }
}
