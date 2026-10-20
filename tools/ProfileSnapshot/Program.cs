using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CrushRoyale.Client;
using CrushRoyale.Contracts;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Social;
using CrushRoyale.Server.Infrastructure;
using CrushRoyale.Server.Persistence;
using CrushRoyale.Server.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrushRoyale.Tools.ProfileSnapshot
{
    /// <summary>
    /// Writes a real account to the files the screenshot harness loads, so the captures show a real player.
    ///
    /// Every capture was taken against an invented demo player, and the defects only a real account produces were
    /// invisible: the bag's Convert button broke precisely because its owner had collected every pet, a state the
    /// demo profile never reaches. Worse, every screen whose data comes from the server (guild, friends, profile,
    /// battle pass, quests, achievements, leaderboard) stayed empty in the editor, because the editor never signs
    /// in. Those screens could therefore only be reviewed by a player, on a phone, after a fifty-minute build.
    ///
    ///     dotnet run --project tools/ProfileSnapshot -- Escobaros
    ///
    /// Needs SUPABASE_URL and SUPABASE_SERVICE_ROLE_KEY. The way it works is deliberately dull: it copies the rows
    /// into the in-memory store the tests already use, then calls the server's own services on them. Nothing is
    /// reimplemented here, so a snapshot cannot drift from what the live server would answer.
    ///
    /// The files it writes hold real people's names and progress, so they are never committed.
    /// </summary>
    public static class Program
    {
        private const string DefaultOutput = "unity/CrushRoyale/Assets/Editor/Fixtures";

        public static async Task<int> Main(string[] args)
        {
            string name = args.Length > 0 ? args[0] : Environment.GetEnvironmentVariable("SNAPSHOT_PLAYER");
            string output = args.Length > 1 ? args[1] : DefaultOutput;
            if (string.IsNullOrWhiteSpace(name))
            {
                Console.Error.WriteLine("usage: ProfileSnapshot <display name> [output folder]");
                return 2;
            }

            string url = (Environment.GetEnvironmentVariable("SUPABASE_URL") ?? string.Empty).TrimEnd('/');
            string key = (Environment.GetEnvironmentVariable("SUPABASE_SERVICE_ROLE_KEY") ?? string.Empty).Trim();
            if (url.Length == 0 || key.Length == 0)
            {
                // Not an error: a build without the secret simply keeps the demo profile.
                Console.WriteLine("SUPABASE_URL or SUPABASE_SERVICE_ROLE_KEY missing: no snapshot taken.");
                return 0;
            }

            // The whole table, not just one row: the guild screen needs the other members, the friends screen needs
            // the friends, and the leaderboard needs everyone. The game has few enough players for this to be cheap.
            string playersJson, guildsJson;
            try
            {
                playersJson = await FetchAsync(url, key, "players?select=id,version,state&limit=1000").ConfigureAwait(false);
                guildsJson = await FetchAsync(url, key, "guilds?select=id,version,state&limit=500").ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Could not read the database: " + ex.Message);
                return 0;
            }

            var balance = GameBalance.CreateDefault();
            var clock = new SystemClock();
            var store = new InMemoryGameStore(() => balance);
            var players = new List<(Guid Id, string Name)>();

            await store.TransactAsync<bool>(async tx =>
            {
                using (JsonDocument document = JsonDocument.Parse(playersJson))
                {
                    foreach (JsonElement row in document.RootElement.EnumerateArray())
                    {
                        PlayerRecord record = ReadPlayer(row);
                        await tx.InsertPlayerAsync(record).ConfigureAwait(false);
                        players.Add((record.Id, record.State?.DisplayName ?? string.Empty));
                    }
                }
                using (JsonDocument document = JsonDocument.Parse(guildsJson))
                {
                    foreach (JsonElement row in document.RootElement.EnumerateArray())
                    {
                        await tx.InsertGuildAsync(ReadGuild(row)).ConfigureAwait(false);
                    }
                }
                return true;
            }, CancellationToken.None).ConfigureAwait(false);

            (Guid Id, string Name) me = players.FirstOrDefault(
                p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (me.Id == Guid.Empty)
            {
                Console.Error.WriteLine("No player called \"" + name + "\" among " + players.Count + " players.");
                return 0;
            }

            var ops = new PlayerOperations(store, new FixedBalance(balance), clock);
            var guilds = new GuildService(ops, new ChatModerator(balance, clock));
            var profiles = new ProfileService(ops, guilds, new AdsOptions(), NullLogger<ProfileService>.Instance);
            var social = new SocialService(ops);
            var progression = new ProgressionService(ops);

            string folder = Path.GetFullPath(output);
            Directory.CreateDirectory(folder);
            var written = new List<string>();

            // Named after the response type, because that is how the harness hands a fixture to a screen: the screen
            // asks for a BattlePassResponse, it is given the BattlePassResponse of this account.
            await WriteAsync(folder, written, "profile", () => profiles.GetMeAsync(me.Id, false, CancellationToken.None));
            await WriteAsync(folder, written, "PlayerStatsDto", () => profiles.GetStatsAsync(me.Id, CancellationToken.None));
            await WriteAsync(folder, written, "FriendsResponse", () => social.GetFriendsAsync(me.Id, CancellationToken.None));
            await WriteAsync(folder, written, "BattlePassResponse", () => progression.GetBattlePassAsync(me.Id, CancellationToken.None));
            await WriteAsync(folder, written, "QuestsResponse", () => progression.GetQuestsAsync(me.Id, CancellationToken.None));
            await WriteAsync(folder, written, "AchievementsResponse", () => progression.GetAchievementsAsync(me.Id, CancellationToken.None));
            await WriteAsync(folder, written, "GuildSearchResponse", () => guilds.SearchAsync(null, CancellationToken.None));
            // A player without a guild is a normal state, and the screen has to show it: no file, no fixture.
            await WriteAsync(folder, written, "GuildDto", () => guilds.GetMineAsync(me.Id, CancellationToken.None));

            Console.WriteLine("Snapshot of " + me.Name + " written to " + output + ": " + string.Join(", ", written));
            return 0;
        }

        /// <summary>Runs one service call and keeps its answer, or says why that screen has no data.</summary>
        private static async Task WriteAsync<T>(string folder, List<string> written, string name, Func<Task<T>> call)
            where T : class
        {
            T value;
            try
            {
                value = await call().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine("  " + name + ": " + ex.Message);
                return;
            }
            if (value == null)
            {
                Console.WriteLine("  " + name + ": nothing (this account has none).");
                return;
            }
            File.WriteAllText(Path.Combine(folder, name + ".json"), JsonSettings.Serialize(value));
            written.Add(name);
        }

        private static PlayerRecord ReadPlayer(JsonElement row) => new PlayerRecord
        {
            Id = Guid.TryParse(row.GetProperty("id").GetString(), out Guid id) ? id : Guid.NewGuid(),
            Version = Version(row),
            CreatedAt = DateTime.UtcNow,
            State = Json.Deserialize<PlayerState>(row.GetProperty("state").GetRawText())
        };

        /// <summary>Read exactly as the live store reads it: the state column holds the whole record.</summary>
        private static GuildRecord ReadGuild(JsonElement row)
        {
            GuildRecord guild = Json.Deserialize<GuildRecord>(row.GetProperty("state").GetRawText());
            guild.Id = row.GetProperty("id").TryGetInt64(out long id) ? id : 0;
            guild.Version = Version(row);
            guild.CreatedAt = DateTime.UtcNow;
            return guild;
        }

        private static long Version(JsonElement row) =>
            row.TryGetProperty("version", out JsonElement v) && v.TryGetInt64(out long version) ? version : 1;

        private static async Task<string> FetchAsync(string url, string key, string query)
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("apikey", key);
            // Legacy service_role keys are JWTs and want a bearer header too; sb_secret_ keys do not.
            if (key.StartsWith("eyJ", StringComparison.Ordinal))
            {
                http.DefaultRequestHeaders.Add("Authorization", "Bearer " + key);
            }
            using HttpResponseMessage response = await http.GetAsync(url + "/rest/v1/" + query).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        }

        /// <summary>The live server can reload its balance; a snapshot is taken at one instant, so this one cannot.</summary>
        private sealed class FixedBalance : IBalanceProvider
        {
            private readonly GameBalance _balance;
            private readonly StageCatalog _catalog;

            public FixedBalance(GameBalance balance)
            {
                _balance = balance;
                _catalog = new StageCatalog(balance);
            }

            public GameBalance Current => _balance;

            public StageCatalog Catalog => _catalog;

            public ulong Hash => 0;

            public string HashHex => "0";

            public string Json => string.Empty;

            public void Apply(GameBalance balance)
            {
            }
        }
    }
}
