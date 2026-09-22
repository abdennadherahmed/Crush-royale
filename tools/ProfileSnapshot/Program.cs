using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using CrushRoyale.Client;
using CrushRoyale.Contracts;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Story;
using CrushRoyale.Server.Persistence;
using CrushRoyale.Server.Services;

namespace CrushRoyale.Tools.ProfileSnapshot
{
    /// <summary>
    /// Writes one real player's profile to a file the screenshot harness can load.
    ///
    /// Every capture until now was taken against an invented demo player, and the defects that only a real account
    /// produces were therefore invisible: the bag's Convert button broke precisely because its owner had collected
    /// every pet, a state the demo profile never reaches. What Supabase stores is the server-side state, which is not
    /// the shape the screens read, so this runs that state through the same mapper the live server uses and writes
    /// the result in the client's own JSON dialect.
    ///
    ///     dotnet run --project tools/ProfileSnapshot -- Escobaros [output.json]
    ///
    /// Needs SUPABASE_URL and SUPABASE_SERVICE_ROLE_KEY. The file it writes holds a real person's name and progress,
    /// so it is never committed: the build generates it and throws it away.
    /// </summary>
    public static class Program
    {
        private const string DefaultOutput = "unity/CrushRoyale/Assets/Editor/Fixtures/profile.json";

        public static async Task<int> Main(string[] args)
        {
            string name = args.Length > 0 ? args[0] : Environment.GetEnvironmentVariable("SNAPSHOT_PLAYER");
            string output = args.Length > 1 ? args[1] : DefaultOutput;
            if (string.IsNullOrWhiteSpace(name))
            {
                Console.Error.WriteLine("usage: ProfileSnapshot <display name> [output.json]");
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

            string json;
            try
            {
                json = await FetchAsync(url, key, name).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Could not read the player: " + ex.Message);
                return 0;
            }

            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.GetArrayLength() == 0)
            {
                Console.Error.WriteLine("No player called \"" + name + "\".");
                return 0;
            }
            JsonElement row = document.RootElement[0];

            var record = new PlayerRecord
            {
                Id = Guid.TryParse(row.GetProperty("id").GetString(), out Guid id) ? id : Guid.NewGuid(),
                Version = row.TryGetProperty("version", out JsonElement v) && v.TryGetInt64(out long version) ? version : 1,
                CreatedAt = DateTime.UtcNow,
                State = CrushRoyale.Server.Infrastructure.Json.Deserialize<PlayerState>(row.GetProperty("state").GetRawText())
            };

            var balance = new GameBalance();
            var workspace = new PlayerWorkspace(record, balance, new StageCatalog(balance), new SystemClock(), null);
            ProfileDto profile = Mappers.Profile(workspace);

            string path = Path.GetFullPath(output);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonSettings.Serialize(profile));
            Console.WriteLine("Snapshot of " + profile.DisplayName + " (stage " + profile.Story?.HighestUnlockedStage
                + ", " + profile.Pvp?.Trophies + " trophies) written to " + output);
            return 0;
        }

        private static async Task<string> FetchAsync(string url, string key, string name)
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("apikey", key);
            // Legacy service_role keys are JWTs and want a bearer header too; sb_secret_ keys do not.
            if (key.StartsWith("eyJ", StringComparison.Ordinal))
            {
                http.DefaultRequestHeaders.Add("Authorization", "Bearer " + key);
            }
            string query = url + "/rest/v1/players?display_name=ilike." + Uri.EscapeDataString(name)
                + "&select=id,version,state&limit=1";
            using HttpResponseMessage response = await http.GetAsync(query).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        }
    }
}
