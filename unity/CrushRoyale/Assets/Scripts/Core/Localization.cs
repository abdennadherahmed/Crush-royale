using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using UnityEngine;

namespace CrushRoyale.Game
{
    /// <summary>
    /// Flat key/value string tables in Resources/Localization/{code}.json (FR, AR, EN, ES, DE, IT, PT, RU, JA, KO, ZH),
    /// English fallback, {0} placeholders, right-to-left shaping for Arabic.
    /// </summary>
    public sealed class Localization
    {
        public static readonly string[] Supported = { "en", "fr", "ar", "es", "de", "it", "pt", "ru", "ja", "ko", "zh" };

        private Dictionary<string, string> _strings = new Dictionary<string, string>();
        private Dictionary<string, string> _fallback = new Dictionary<string, string>();

        public Localization(string preferred)
        {
            _fallback = LoadTable("en");
            SetLanguage(string.IsNullOrEmpty(preferred) ? DetectSystemLanguage() : preferred);
        }

        public event Action LanguageChanged;

        public string Language { get; private set; }

        public bool IsRightToLeft => Language == "ar";

        public static string DetectSystemLanguage()
        {
            switch (Application.systemLanguage)
            {
                case SystemLanguage.French: return "fr";
                case SystemLanguage.Arabic: return "ar";
                case SystemLanguage.Spanish: return "es";
                case SystemLanguage.German: return "de";
                case SystemLanguage.Italian: return "it";
                case SystemLanguage.Portuguese: return "pt";
                case SystemLanguage.Russian: return "ru";
                case SystemLanguage.Japanese: return "ja";
                case SystemLanguage.Korean: return "ko";
                case SystemLanguage.Chinese:
                case SystemLanguage.ChineseSimplified:
                case SystemLanguage.ChineseTraditional: return "zh";
                default: return "en";
            }
        }

        public void SetLanguage(string code)
        {
            Language = Array.IndexOf(Supported, code) >= 0 ? code : "en";
            _strings = Language == "en" ? _fallback : LoadTable(Language);
            LanguageChanged?.Invoke();
        }

        public bool Has(string key) => key != null && (_strings.ContainsKey(key) || _fallback.ContainsKey(key));

        /// <summary>Translated text; the key itself if missing everywhere (visible in QA, never crashes).</summary>
        public string T(string key, params object[] args)
        {
            if (string.IsNullOrEmpty(key))
            {
                return string.Empty;
            }
            if (!_strings.TryGetValue(key, out string value) && !_fallback.TryGetValue(key, out value))
            {
                value = key;
            }
            if (args != null && args.Length > 0)
            {
                try
                {
                    value = string.Format(CultureInfo.InvariantCulture, value, args);
                }
                catch (FormatException)
                {
                    // Broken translation placeholder: show the raw text.
                }
            }
            return IsRightToLeft ? ArabicShaper.Shape(value) : value;
        }

        public string Number(long value) => value.ToString("N0", CultureFor(Language));

        private static CultureInfo CultureFor(string code)
        {
            try
            {
                return CultureInfo.GetCultureInfo(code);
            }
            catch (CultureNotFoundException)
            {
                return CultureInfo.InvariantCulture;
            }
        }

        private static Dictionary<string, string> LoadTable(string code)
        {
            TextAsset asset = Resources.Load<TextAsset>("Localization/" + code);
            if (asset == null)
            {
                Debug.LogWarning("Missing localization table " + code);
                return new Dictionary<string, string>();
            }
            return JsonConvert.DeserializeObject<Dictionary<string, string>>(asset.text) ?? new Dictionary<string, string>();
        }
    }

    /// <summary>
    /// Minimal Arabic contextual shaping + visual reordering for uGUI (which renders glyphs left to right without shaping).
    /// Handles the 28 letters, hamza variants, lam-alef ligatures and keeps Latin digits/words in logical order.
    /// </summary>
    public static class ArabicShaper
    {
        // isolated, final, initial, medial
        private static readonly Dictionary<char, char[]> Forms = new Dictionary<char, char[]>
        {
            ['ء'] = new[] { 'ﺀ', 'ﺀ', 'ﺀ', 'ﺀ' },
            ['آ'] = new[] { 'ﺁ', 'ﺂ', 'ﺁ', 'ﺂ' },
            ['أ'] = new[] { 'ﺃ', 'ﺄ', 'ﺃ', 'ﺄ' },
            ['ؤ'] = new[] { 'ﺅ', 'ﺆ', 'ﺅ', 'ﺆ' },
            ['إ'] = new[] { 'ﺇ', 'ﺈ', 'ﺇ', 'ﺈ' },
            ['ئ'] = new[] { 'ﺉ', 'ﺊ', 'ﺋ', 'ﺌ' },
            ['ا'] = new[] { 'ﺍ', 'ﺎ', 'ﺍ', 'ﺎ' },
            ['ب'] = new[] { 'ﺏ', 'ﺐ', 'ﺑ', 'ﺒ' },
            ['ة'] = new[] { 'ﺓ', 'ﺔ', 'ﺓ', 'ﺔ' },
            ['ت'] = new[] { 'ﺕ', 'ﺖ', 'ﺗ', 'ﺘ' },
            ['ث'] = new[] { 'ﺙ', 'ﺚ', 'ﺛ', 'ﺜ' },
            ['ج'] = new[] { 'ﺝ', 'ﺞ', 'ﺟ', 'ﺠ' },
            ['ح'] = new[] { 'ﺡ', 'ﺢ', 'ﺣ', 'ﺤ' },
            ['خ'] = new[] { 'ﺥ', 'ﺦ', 'ﺧ', 'ﺨ' },
            ['د'] = new[] { 'ﺩ', 'ﺪ', 'ﺩ', 'ﺪ' },
            ['ذ'] = new[] { 'ﺫ', 'ﺬ', 'ﺫ', 'ﺬ' },
            ['ر'] = new[] { 'ﺭ', 'ﺮ', 'ﺭ', 'ﺮ' },
            ['ز'] = new[] { 'ﺯ', 'ﺰ', 'ﺯ', 'ﺰ' },
            ['س'] = new[] { 'ﺱ', 'ﺲ', 'ﺳ', 'ﺴ' },
            ['ش'] = new[] { 'ﺵ', 'ﺶ', 'ﺷ', 'ﺸ' },
            ['ص'] = new[] { 'ﺹ', 'ﺺ', 'ﺻ', 'ﺼ' },
            ['ض'] = new[] { 'ﺽ', 'ﺾ', 'ﺿ', 'ﻀ' },
            ['ط'] = new[] { 'ﻁ', 'ﻂ', 'ﻃ', 'ﻄ' },
            ['ظ'] = new[] { 'ﻅ', 'ﻆ', 'ﻇ', 'ﻈ' },
            ['ع'] = new[] { 'ﻉ', 'ﻊ', 'ﻋ', 'ﻌ' },
            ['غ'] = new[] { 'ﻍ', 'ﻎ', 'ﻏ', 'ﻐ' },
            ['ف'] = new[] { 'ﻑ', 'ﻒ', 'ﻓ', 'ﻔ' },
            ['ق'] = new[] { 'ﻕ', 'ﻖ', 'ﻗ', 'ﻘ' },
            ['ك'] = new[] { 'ﻙ', 'ﻚ', 'ﻛ', 'ﻜ' },
            ['ل'] = new[] { 'ﻝ', 'ﻞ', 'ﻟ', 'ﻠ' },
            ['م'] = new[] { 'ﻡ', 'ﻢ', 'ﻣ', 'ﻤ' },
            ['ن'] = new[] { 'ﻥ', 'ﻦ', 'ﻧ', 'ﻨ' },
            ['ه'] = new[] { 'ﻩ', 'ﻪ', 'ﻫ', 'ﻬ' },
            ['و'] = new[] { 'ﻭ', 'ﻮ', 'ﻭ', 'ﻮ' },
            ['ى'] = new[] { 'ﻯ', 'ﻰ', 'ﻯ', 'ﻰ' },
            ['ي'] = new[] { 'ﻱ', 'ﻲ', 'ﻳ', 'ﻴ' }
        };

        /// <summary>Letters that never connect to the following letter.</summary>
        private static readonly HashSet<char> RightJoinOnly = new HashSet<char> { 'ء', 'آ', 'أ', 'ؤ', 'إ', 'ا', 'ة', 'د', 'ذ', 'ر', 'ز', 'و', 'ى' };

        public static string Shape(string text)
        {
            if (string.IsNullOrEmpty(text) || !ContainsArabic(text))
            {
                return text;
            }

            var shaped = new System.Text.StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (!Forms.ContainsKey(c))
                {
                    shaped.Append(c);
                    continue;
                }

                // Lam + Alef ligatures.
                if (c == 'ل' && i + 1 < text.Length && (text[i + 1] == 'ا' || text[i + 1] == 'آ' || text[i + 1] == 'أ' || text[i + 1] == 'إ'))
                {
                    bool joinPrev = i > 0 && JoinsForward(text[i - 1]);
                    char alef = text[i + 1];
                    char lig = alef == 'آ' ? 'ﻵ' : alef == 'أ' ? 'ﻷ' : alef == 'إ' ? 'ﻹ' : 'ﻻ';
                    shaped.Append((char)(lig + (joinPrev ? 1 : 0)));
                    i++;
                    continue;
                }

                bool prev = i > 0 && JoinsForward(text[i - 1]);
                bool next = i + 1 < text.Length && Forms.ContainsKey(text[i + 1]) && JoinsForward(c);
                int form = prev && next ? 3 : prev ? 1 : next ? 2 : 0;
                shaped.Append(Forms[c][form]);
            }
            return Reorder(shaped.ToString());
        }

        private static bool JoinsForward(char c) => Forms.ContainsKey(c) && !RightJoinOnly.Contains(c);

        private static bool ContainsArabic(string text)
        {
            foreach (char c in text)
            {
                if (c >= '؀' && c <= 'ۿ')
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Reverses the line for display while keeping runs of Latin letters/digits readable.</summary>
        private static string Reorder(string logical)
        {
            var result = new System.Text.StringBuilder(logical.Length);
            string[] lines = logical.Split('\n');
            for (int l = 0; l < lines.Length; l++)
            {
                string line = lines[l];
                var runs = new List<string>();
                int i = 0;
                while (i < line.Length)
                {
                    bool ltr = IsLtr(line[i]);
                    int start = i;
                    while (i < line.Length && IsLtr(line[i]) == ltr && (ltr || true))
                    {
                        i++;
                        if (!ltr)
                        {
                            break;
                        }
                    }
                    runs.Add(line.Substring(start, i - start));
                }
                runs.Reverse();
                foreach (string run in runs)
                {
                    result.Append(run.Length == 1 ? Mirror(run[0]).ToString() : run);
                }
                if (l < lines.Length - 1)
                {
                    result.Append('\n');
                }
            }
            return result.ToString();
        }

        private static bool IsLtr(char c) => (c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || c == '.' || c == ':' || c == '%';

        private static char Mirror(char c)
        {
            switch (c)
            {
                case '(': return ')';
                case ')': return '(';
                case '[': return ']';
                case ']': return '[';
                case '<': return '>';
                case '>': return '<';
                default: return c;
            }
        }
    }
}
