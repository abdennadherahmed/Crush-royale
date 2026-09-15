using System;
using System.Collections.Generic;
using System.Text;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;

namespace CrushRoyale.Core.Social
{
    public sealed class ChatModerationResult
    {
        public bool Accepted { get; internal set; }

        public ErrorCode Error { get; internal set; }

        /// <summary>Text to store/broadcast (profanity masked).</summary>
        public string Text { get; internal set; }

        public bool Masked { get; internal set; }

        public string Reason { get; internal set; }
    }

    /// <summary>
    /// Guild chat moderation: length limit, per-player rate limit, repeated-message spam, links (phishing) and
    /// profanity masking. Matching works on whole words (and words starting with a banned root of 4+ letters)
    /// after leetspeak normalization, so innocent words that merely contain a banned substring are not masked.
    /// Admins extend the list through remote config.
    /// </summary>
    public sealed class ChatModerator
    {
        private const int SpamRepeatLimit = 3;

        private static readonly string[] DefaultBannedWords =
        {
            "fuck", "shit", "bitch", "asshole", "bastard", "cunt", "dick", "pussy", "whore", "slut", "retard",
            "merde", "putain", "connard", "connasse", "salope", "encule", "pute", "batard",
            "mierda", "puta", "cabron", "pendejo", "gilipollas",
            "scheisse", "arschloch", "hurensohn", "fotze",
            "cazzo", "stronzo", "vaffanculo",
            "porra", "caralho", "foda",
            "blyat", "suka", "pizdec"
        };

        private static readonly string[] LinkMarkers = { "http://", "https://", "www.", ".com", ".net", ".org", ".io", "discord.gg", "t.me/", "bit.ly" };

        private readonly GameBalance _balance;
        private readonly IClock _clock;
        private readonly List<string> _banned;
        private readonly Dictionary<string, long> _lastMessageAt = new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<string>> _recent = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        private readonly object _lock = new object();

        public ChatModerator(GameBalance balance, IClock clock, IEnumerable<string> extraBannedWords = null)
        {
            _balance = balance ?? throw new ArgumentNullException(nameof(balance));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _banned = new List<string>(DefaultBannedWords);
            if (extraBannedWords != null)
            {
                foreach (string w in extraBannedWords)
                {
                    if (!string.IsNullOrWhiteSpace(w))
                    {
                        _banned.Add(Normalize(w.Trim()));
                    }
                }
            }
        }

        public ChatModerationResult Moderate(string playerId, string text, bool muted = false)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                throw new ArgumentException("Player id required.", nameof(playerId));
            }
            if (muted)
            {
                return Reject(ErrorCode.Banned, "Player is muted.");
            }

            string trimmed = (text ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                return Reject(ErrorCode.InvalidArgument, "Empty message.");
            }
            if (trimmed.Length > _balance.Guild.ChatMaxLength)
            {
                return Reject(ErrorCode.MessageRejected, "Message too long.");
            }

            string lower = trimmed.ToLowerInvariant();
            foreach (string marker in LinkMarkers)
            {
                if (lower.Contains(marker))
                {
                    return Reject(ErrorCode.MessageRejected, "Links are not allowed.");
                }
            }

            long now = TimeUtil.ToUnixMs(_clock.UtcNow);
            string normalized = Normalize(trimmed);
            lock (_lock)
            {
                if (_lastMessageAt.TryGetValue(playerId, out long last) && now - last < _balance.Guild.ChatMinIntervalMs)
                {
                    return Reject(ErrorCode.CooldownActive, "Slow down.");
                }

                if (!_recent.TryGetValue(playerId, out List<string> recent))
                {
                    recent = new List<string>();
                    _recent[playerId] = recent;
                }
                int repeats = 0;
                foreach (string r in recent)
                {
                    if (r == normalized)
                    {
                        repeats++;
                    }
                }
                if (repeats >= SpamRepeatLimit - 1)
                {
                    return Reject(ErrorCode.MessageRejected, "Repeated message.");
                }

                _lastMessageAt[playerId] = now;
                recent.Add(normalized);
                if (recent.Count > 5)
                {
                    recent.RemoveAt(0);
                }
            }

            string output = Mask(trimmed, _banned, out bool masked);
            return new ChatModerationResult { Accepted = true, Error = ErrorCode.None, Text = output, Masked = masked };
        }

        public static bool ContainsProfanity(string text)
        {
            Mask(text ?? string.Empty, DefaultBannedWords, out bool masked);
            return masked;
        }

        public static string MaskProfanity(string text, out bool masked) => Mask(text ?? string.Empty, DefaultBannedWords, out masked);

        private static string Mask(string text, IList<string> banned, out bool masked)
        {
            masked = false;
            var output = new StringBuilder(text.Length);
            int i = 0;
            while (i < text.Length)
            {
                if (!char.IsLetterOrDigit(text[i]) && text[i] != '@' && text[i] != '$')
                {
                    output.Append(text[i]);
                    i++;
                    continue;
                }

                int start = i;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '@' || text[i] == '$'))
                {
                    i++;
                }
                string token = text.Substring(start, i - start);
                if (IsBanned(Normalize(token), banned))
                {
                    output.Append('*', token.Length);
                    masked = true;
                }
                else
                {
                    output.Append(token);
                }
            }
            return output.ToString();
        }

        private static bool IsBanned(string normalizedToken, IList<string> banned)
        {
            foreach (string word in banned)
            {
                if (normalizedToken == word || (word.Length >= 4 && normalizedToken.StartsWith(word, StringComparison.Ordinal)))
                {
                    return true;
                }
            }
            return false;
        }

        private static string Normalize(string value)
        {
            var sb = new StringBuilder(value.Length);
            foreach (char raw in value.ToLowerInvariant())
            {
                switch (raw)
                {
                    case '0': sb.Append('o'); break;
                    case '1': sb.Append('i'); break;
                    case '3': sb.Append('e'); break;
                    case '4': sb.Append('a'); break;
                    case '5': sb.Append('s'); break;
                    case '7': sb.Append('t'); break;
                    case '@': sb.Append('a'); break;
                    case '$': sb.Append('s'); break;
                    case 'é': case 'è': case 'ê': sb.Append('e'); break;
                    case 'à': case 'â': sb.Append('a'); break;
                    case 'ç': sb.Append('c'); break;
                    default: sb.Append(raw); break;
                }
            }
            return sb.ToString();
        }

        private static ChatModerationResult Reject(ErrorCode error, string reason) =>
            new ChatModerationResult { Accepted = false, Error = error, Reason = reason };
    }
}
