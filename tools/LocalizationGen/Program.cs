using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using CrushRoyale.Core.Board;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Economy;
using CrushRoyale.Core.Progression;
using CrushRoyale.Core.Social;
using CrushRoyale.Core.Story;

namespace CrushRoyale.Tools.LocalizationGen
{
    /// <summary>
    /// Builds unity/CrushRoyale/Assets/Resources/Localization/{lang}.json from tools/LocalizationGen/Source:
    /// merges ui.{lang}.json and story.{lang}.json, expands "gen.*" templates (boss names, achievement descriptions,
    /// generated cosmetics) and validates everything the game can ask for (keys used in the Unity scripts, enum-driven
    /// families, story events). English and French must be complete; other languages fall back to English at runtime.
    /// Usage: dotnet run --project tools/LocalizationGen [repoRoot]
    /// </summary>
    internal static class Program
    {
        private static readonly string[] Languages = { "en", "fr", "ar", "es", "de", "it", "pt", "ru", "ja", "ko", "zh" };
        private static readonly HashSet<string> CompleteLanguages = new HashSet<string> { "en", "fr" };
        private static readonly string[] ClientErrorCodes = { "Network", "Timeout", "AuthFailed", "InvalidConfig", "StoreUnavailable", "PurchaseFailed", "Cancelled", "Unauthorized", "RateLimited", "Conflict", "Internal" };
        private static readonly string[] BattlePassParts = { "frame", "board", "pieces" };
        private const int GeneratedSeasons = 12;

        private static readonly Regex CodeKey = new Regex("\"((?:chapter|hud|pause|continue|boss|splash|privacy|hero|menu|login|settings|map|stage|result|pvp|lives|shop|guild|friends|leaderboard|achievements|battlepass|quests|common|error|choice|notif|currency|pets|pet|chest|challenge|vip|get|reveal|profile|kingdom|season|streak|wheel|update|mechanic|bag|guildboss)\\.[A-Za-z0-9_.]*[A-Za-z0-9_])\"");
        private static readonly Regex Placeholder = new Regex("\\{(\\d+)(?:[,:][^}]*)?\\}");
        private static readonly Regex DialogueLine = new Regex("^dlg\\..+\\.\\d+$");

        private static int Main(string[] args)
        {
            string root = args.Length > 0 ? Path.GetFullPath(args[0]) : FindRepoRoot();
            if (root == null)
            {
                Console.Error.WriteLine("CrushRoyale.sln not found: run from the repository or pass its path.");
                return 2;
            }

            string sourceDir = Path.Combine(root, "tools", "LocalizationGen", "Source");
            string scriptsDir = Path.Combine(root, "unity", "CrushRoyale", "Assets", "Scripts");
            string outputDir = Path.Combine(root, "unity", "CrushRoyale", "Assets", "Resources", "Localization");

            StoryBalance story = GameBalance.CreateDefault().Story;
            SortedSet<string> uiKeys = RequiredUiKeys(scriptsDir, story);
            SortedSet<string> storyKeys = RequiredStoryKeys(story);
            HashSet<string> speakers = new HashSet<string>(StoryDatabase.Characters.Select(c => c.Id)) { "narrator" };

            var errors = new List<string>();
            var warnings = new List<string>();
            var tables = new Dictionary<string, SortedDictionary<string, string>>();
            foreach (string lang in Languages)
            {
                tables[lang] = Load(sourceDir, lang, errors, warnings);
                Expand(lang, tables[lang], story, errors);
            }

            SortedDictionary<string, string> en = tables["en"];
            foreach (string lang in Languages)
            {
                SortedDictionary<string, string> table = tables[lang];
                IEnumerable<string> required = CompleteLanguages.Contains(lang) ? uiKeys.Concat(storyKeys) : uiKeys;
                List<string> missing = required.Where(k => !table.ContainsKey(k)).ToList();
                if (missing.Count > 0)
                {
                    string message = lang + ": " + missing.Count + " missing key(s): " + string.Join(", ", missing.Take(25)) + (missing.Count > 25 ? " ..." : string.Empty);
                    (CompleteLanguages.Contains(lang) ? errors : warnings).Add(message);
                }

                foreach (KeyValuePair<string, string> pair in table)
                {
                    if (IsTemplate(pair.Key))
                    {
                        continue;
                    }
                    if (lang != "en")
                    {
                        if (!en.TryGetValue(pair.Key, out string reference))
                        {
                            warnings.Add(lang + ": key not in English: " + pair.Key);
                        }
                        else if (Placeholders(reference) != Placeholders(pair.Value))
                        {
                            errors.Add(lang + ": placeholders of '" + pair.Key + "' differ from English (" + Placeholders(reference) + " vs " + Placeholders(pair.Value) + ")");
                        }
                    }
                    if (DialogueLine.IsMatch(pair.Key))
                    {
                        int bar = pair.Value.IndexOf('|');
                        if (bar <= 0 || !speakers.Contains(pair.Value.Substring(0, bar).Trim()))
                        {
                            errors.Add(lang + ": dialogue line '" + pair.Key + "' must start with a known speaker id and '|'");
                        }
                    }
                }
            }

            foreach (string key in en.Keys.Where(k => !IsTemplate(k) && !uiKeys.Contains(k) && !storyKeys.Contains(k) && !DialogueLine.IsMatch(k)
                && !k.StartsWith("cosmetic." + CosmeticCatalog.BattlePassPrefix, StringComparison.Ordinal)))
            {
                warnings.Add("en: unused key " + key);
            }

            Directory.CreateDirectory(outputDir);
            var json = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
            foreach (string lang in Languages)
            {
                var output = new SortedDictionary<string, string>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, string> pair in tables[lang].Where(p => !IsTemplate(p.Key)))
                {
                    output[pair.Key] = pair.Value;
                }
                File.WriteAllText(Path.Combine(outputDir, lang + ".json"), JsonSerializer.Serialize(output, json) + "\n", new UTF8Encoding(false));
                int coverage = uiKeys.Count == 0 ? 100 : uiKeys.Count(output.ContainsKey) * 100 / uiKeys.Count;
                Console.WriteLine(lang + ": " + output.Count + " strings, UI coverage " + coverage + "%");
            }

            foreach (string warning in warnings)
            {
                Console.WriteLine("warning: " + warning);
            }
            foreach (string error in errors)
            {
                Console.Error.WriteLine("error: " + error);
            }
            Console.WriteLine(errors.Count == 0 ? "Localization OK (" + uiKeys.Count + " UI keys, " + storyKeys.Count + " story keys)." : errors.Count + " error(s).");
            return errors.Count == 0 ? 0 : 1;
        }

        private static SortedDictionary<string, string> Load(string sourceDir, string lang, List<string> errors, List<string> warnings)
        {
            var table = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (string part in new[] { "ui", "story" })
            {
                string path = Path.Combine(sourceDir, part + "." + lang + ".json");
                if (!File.Exists(path))
                {
                    if (part == "ui")
                    {
                        (lang == "en" ? errors : warnings).Add("missing source file " + path);
                    }
                    continue;
                }
                Dictionary<string, string> values;
                try
                {
                    values = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
                }
                catch (JsonException ex)
                {
                    errors.Add(path + ": " + ex.Message);
                    continue;
                }
                foreach (KeyValuePair<string, string> pair in values)
                {
                    if (table.ContainsKey(pair.Key))
                    {
                        errors.Add(lang + ": duplicate key " + pair.Key + " (ui and story)");
                    }
                    table[pair.Key] = pair.Value;
                }
            }
            return table;
        }

        /// <summary>Expands templates of this language only (a language without a template falls back to English at runtime).</summary>
        private static void Expand(string lang, SortedDictionary<string, string> t, StoryBalance story, List<string> errors)
        {
            CultureInfo culture = CultureInfo.GetCultureInfo(lang);
            string Get(string key) => t.TryGetValue(key, out string value) ? value : null;
            void Put(string key, string value)
            {
                if (value != null && !t.ContainsKey(key))
                {
                    t[key] = value;
                }
            }

            string mini = Get("gen.boss.MiniBoss");
            string chapterBoss = Get("gen.boss.ChapterBoss");
            int chapters = story.Acts * story.ChaptersPerAct;
            for (int chapter = 1; chapter <= chapters; chapter++)
            {
                int act = (chapter - 1) / story.ChaptersPerAct + 1;
                string creatures = Get("gen.boss.creatures." + (Kingdom)(act - 1));
                if (creatures == null || mini == null || chapterBoss == null)
                {
                    continue;
                }
                string[] names = creatures.Split('|');
                int indexInAct = (chapter - 1) % story.ChaptersPerAct;
                if (names.Length != story.ChaptersPerAct)
                {
                    errors.Add(lang + ": gen.boss.creatures." + (Kingdom)(act - 1) + " needs " + story.ChaptersPerAct + " names separated by '|'");
                    continue;
                }
                string creature = names[indexInAct].Trim();
                Put(BossKey(act, chapter, BossKind.MiniBoss), Format(mini, creature));
                if (chapter == chapters)
                {
                    continue;
                }
                Put(BossKey(act, chapter, chapter % story.ChaptersPerAct == 0 ? BossKind.ActBoss : BossKind.ChapterBoss),
                    chapter % story.ChaptersPerAct == 0 ? Get("gen.boss.act." + act) : Format(chapterBoss, creature));
            }

            foreach (AchievementData a in AchievementCatalog.All)
            {
                string template = (a.Target == 1 ? Get("gen.stat." + a.Stat + ".one") : null) ?? Get("gen.stat." + a.Stat);
                if (template == null)
                {
                    continue;
                }
                string value = a.Stat == StatKey.HighestStage ? (a.Target - 1).ToString("N0", culture)
                    : a.Stat == StatKey.HighestLeague ? Get("league." + (League)a.Target)
                    : a.Target.ToString("N0", culture);
                Put(a.DescriptionKey, Format(template, value));
            }

            string pageFrame = Get("gen.cosmetic.pageFrame");
            for (int page = 1; page <= AchievementCatalog.PageCount && pageFrame != null; page++)
            {
                Put("cosmetic." + CosmeticCatalog.PageFrame(page), Format(pageFrame, page));
            }
            foreach (string part in BattlePassParts)
            {
                string template = Get("gen.cosmetic.bp." + part);
                for (int season = 1; season <= GeneratedSeasons && template != null; season++)
                {
                    Put("cosmetic." + CosmeticCatalog.BattlePassPrefix + season + "." + part, Format(template, season));
                }
            }
        }

        private static SortedSet<string> RequiredUiKeys(string scriptsDir, StoryBalance story)
        {
            var keys = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string file in Directory.GetFiles(scriptsDir, "*.cs", SearchOption.AllDirectories))
            {
                foreach (Match match in CodeKey.Matches(File.ReadAllText(file)))
                {
                    if (!match.Groups[1].Value.EndsWith(".json", StringComparison.Ordinal))
                    {
                        keys.Add(match.Groups[1].Value);
                    }
                }
            }

            foreach (PowerUpType type in Enum.GetValues(typeof(PowerUpType)))
            {
                keys.Add("powerup." + type);
                keys.Add("powerup." + type + ".desc");
            }
            AddEnum<ObjectiveType>(keys, "objective.");
            AddEnum<League>(keys, "league.");
            AddEnum<Feature>(keys, "feature.");
            AddEnum<GuildRole>(keys, "guild.role.");
            AddEnum<QuestType>(keys, "quest.");
            foreach (PieceColor color in Enum.GetValues(typeof(PieceColor)))
            {
                if (color != PieceColor.None)
                {
                    keys.Add("color." + color);
                }
            }
            foreach (Kingdom kingdom in Enum.GetValues(typeof(Kingdom)))
            {
                keys.Add("kingdom." + kingdom);
                keys.Add("kingdom." + kingdom + ".short");
            }
            foreach (BossKind kind in Enum.GetValues(typeof(BossKind)))
            {
                if (kind != BossKind.None)
                {
                    keys.Add("boss." + kind);
                }
            }
            foreach (GuildTech tech in Enum.GetValues(typeof(GuildTech)))
            {
                keys.Add("guild.tech." + tech);
                keys.Add("guild.tech." + tech + ".desc");
            }
            foreach (ShopItemKind kind in new[] { ShopItemKind.RemoveAds, ShopItemKind.BattlePass, ShopItemKind.RarePerk })
            {
                keys.Add("shop.item." + kind);
                keys.Add("shop.item." + kind + ".desc");
            }
            foreach (ErrorCode code in Enum.GetValues(typeof(ErrorCode)))
            {
                if (code != ErrorCode.None)
                {
                    keys.Add("error." + code);
                }
            }
            foreach (string code in ClientErrorCodes)
            {
                keys.Add("error." + code);
            }
            foreach (string lang in Languages)
            {
                keys.Add("language." + lang);
            }
            foreach (CharacterDefinition c in StoryDatabase.Characters)
            {
                keys.Add(c.NameKey);
            }
            keys.Add("char.narrator");
            keys.Add("hero.male");
            keys.Add("hero.female");
            foreach (string outcome in new[] { "win", "loss", "draw", "pending" })
            {
                keys.Add("pvp." + outcome);
            }
            foreach (CosmeticDefinition c in CosmeticCatalog.All)
            {
                keys.Add("cosmetic." + c.Id);
            }
            // Season display names come from the catalog, not from a literal in the scripts.
            foreach (SeasonDefinition season in SeasonCatalog.All)
            {
                keys.Add(season.NameKey);
            }
            keys.Add("season.encore");
            foreach (AchievementData a in AchievementCatalog.All)
            {
                keys.Add(a.TitleKey);
                keys.Add(a.DescriptionKey);
            }

            int chapters = story.Acts * story.ChaptersPerAct;
            for (int chapter = 1; chapter <= chapters; chapter++)
            {
                int act = (chapter - 1) / story.ChaptersPerAct + 1;
                keys.Add(BossKey(act, chapter, BossKind.MiniBoss));
                keys.Add(chapter == chapters ? "boss.valdorax.name" : BossKey(act, chapter, chapter % story.ChaptersPerAct == 0 ? BossKind.ActBoss : BossKind.ChapterBoss));
            }
            return keys;
        }

        private static SortedSet<string> RequiredStoryKeys(StoryBalance story)
        {
            var keys = new SortedSet<string>(StringComparer.Ordinal) { "dlg.prologue.1" };
            for (int chapter = 1; chapter <= story.Acts * story.ChaptersPerAct; chapter++)
            {
                keys.Add("chapter." + chapter + ".name");
            }
            foreach (StoryEvent e in StoryDatabase.GetEvents(story))
            {
                keys.Add(e.DialogueId + ".1");
            }
            foreach (StoryEnding ending in Enum.GetValues(typeof(StoryEnding)))
            {
                keys.Add("dlg.ending." + ending.ToString().ToLowerInvariant() + ".1");
            }
            foreach (StoryChoice choice in StoryDatabase.Choices)
            {
                keys.Add(choice.PromptKey);
                foreach (StoryChoiceOption option in choice.Options)
                {
                    keys.Add(option.TextKey);
                }
            }
            return keys;
        }

        private static void AddEnum<T>(SortedSet<string> keys, string prefix) where T : Enum
        {
            foreach (T value in Enum.GetValues(typeof(T)))
            {
                keys.Add(prefix + value);
            }
        }

        private static string BossKey(int act, int chapter, BossKind kind) => "boss.a" + act + ".c" + chapter + "." + kind.ToString().ToLowerInvariant() + ".name";

        private static bool IsTemplate(string key) => key.StartsWith("gen.", StringComparison.Ordinal);

        private static string Format(string template, object value) => string.Format(CultureInfo.InvariantCulture, template, value);

        private static string Placeholders(string text) =>
            string.Join(",", Placeholder.Matches(text).Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).Distinct().OrderBy(i => i));

        private static string FindRepoRoot()
        {
            foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                for (DirectoryInfo dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
                {
                    if (File.Exists(Path.Combine(dir.FullName, "CrushRoyale.sln")))
                    {
                        return dir.FullName;
                    }
                }
            }
            return null;
        }
    }
}
