using System.Collections.Generic;
using System.Threading.Tasks;
using CrushRoyale.Contracts;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Gameplay;
using CrushRoyale.Core.Story;
using CrushRoyale.Game.Audio;
using CrushRoyale.Game.Gameplay;
using CrushRoyale.Game.UI;
using UnityEngine;
using UnityEngine.UI;

namespace CrushRoyale.Game.Screens
{
    public sealed class StoryResultArgs
    {
        public int StageId;
        public StageResult Local;
        public StageCompleteResponse Server;
        public bool Offline;
        public bool Queued;
    }

    /// <summary>Win/lose screen with stars, rewards, unlocks, after-stage dialogues and branching choices.</summary>
    public sealed class StoryResultScreen : UIScreen
    {
        private StoryResultArgs _args;

        public override System.Type BackTarget => typeof(WorldMapScreen);

        protected override void Build()
        {
            _args = (StoryResultArgs)Args;
            RectTransform body = Frame(_args.Local.Won ? "result.win" : "result.lose", backButton: false);
            RectTransform list = UIFactory.ScrollList(body, 24, 48);
            UIFactory.Stretch((RectTransform)list.parent.parent, 0, 0, 0, 260);

            bool accepted = _args.Offline || _args.Queued || (_args.Server != null && _args.Server.Accepted);
            if (_args.Server != null && !_args.Server.Accepted)
            {
                UIFactory.Height(UIFactory.Label(list, Loc.T("result.rejected", Loc.T("error." + _args.Server.Error)), Theme.BodySize, Theme.Danger), 140);
            }

            int stars = _args.Server?.Stars ?? _args.Local.Stars;
            Text starText = UIFactory.Label(list, new string('*', stars) + new string('-', 3 - Mathf.Clamp(stars, 0, 3)), 110, Theme.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Height(starText, 160);

            UIFactory.Height(UIFactory.Label(list, Loc.T("result.score", Loc.Number(_args.Local.FinalScore)), Theme.HeaderSize, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold), 90);
            if (_args.Local.BonusPoints > 0)
            {
                UIFactory.Height(UIFactory.Label(list, Loc.T("result.bonus", Loc.Number(_args.Local.BonusPoints)), Theme.BodySize, Theme.TextMuted), 60);
            }
            UIFactory.Height(UIFactory.Label(list, Loc.T("result.stats", _args.Local.TotalCascades, _args.Local.MaxCascadeLevel, _args.Local.SpecialsCreated), Theme.SmallSize, Theme.TextMuted), 60);

            if (_args.Offline)
            {
                UIFactory.Height(UIFactory.Label(list, Loc.T("result.practice"), Theme.BodySize, Theme.Warning), 110);
            }
            else if (_args.Queued)
            {
                UIFactory.Height(UIFactory.Label(list, Loc.T("result.queued"), Theme.BodySize, Theme.Warning), 110);
            }
            else if (_args.Server != null && _args.Server.Accepted)
            {
                StageCompleteResponse s = _args.Server;
                if (s.FirstWin)
                {
                    UIFactory.Height(UIFactory.Label(list, Loc.T("result.firstWin"), Theme.BodySize, Theme.Success, TextAnchor.MiddleCenter, FontStyle.Bold), 70);
                }
                if (s.CoinsEarned > 0 || s.OrbesEarned > 0)
                {
                    UIFactory.Height(UIFactory.Label(list, Loc.T("result.rewards", Loc.Number(s.CoinsEarned), Loc.Number(s.OrbesEarned)), Theme.HeaderSize, Theme.Gold), 80);
                    Game.Audio.PlaySFX(SoundIds.Coins);
                }
                foreach (string feature in s.FeaturesUnlocked)
                {
                    UIFactory.Height(UIFactory.Label(list, Loc.T("result.unlocked", Loc.T("feature." + feature)), Theme.BodySize, Theme.Crystal, TextAnchor.MiddleCenter, FontStyle.Bold), 70);
                }
                foreach (string character in s.CharactersJoined)
                {
                    UIFactory.Height(UIFactory.Label(list, Loc.T("result.joined", Loc.T("char." + character)), Theme.BodySize, Theme.Crystal), 70);
                }
                foreach (string achievement in s.AchievementsUnlocked)
                {
                    UIFactory.Height(UIFactory.Label(list, Loc.T("result.achievement", Loc.T("ach." + achievement + ".title")), Theme.BodySize, Theme.Gold), 70);
                }
            }

            RectTransform buttons = UIFactory.Anchor(UIFactory.Rect("Buttons", body), 0.05f, 0.02f, 0.95f, 0.12f);
            HorizontalLayoutGroup row = buttons.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 24;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = true;

            UIFactory.Button(buttons, Loc.T("result.map"), () => UI.ShowRoot<WorldMapScreen>(), Theme.PanelLight);
            UIFactory.Button(buttons, Loc.T("result.retry"), () => UI.Show<StagePreviewScreen>(_args.StageId, addToHistory: false), Theme.PanelLight);
            if (_args.Local.Won && accepted)
            {
                int next = _args.Server?.NextStage ?? _args.StageId + 1;
                UIFactory.Button(buttons, Loc.T("result.next"), () => UI.Show<StagePreviewScreen>(next, addToHistory: false));
            }
        }

        public override async Task OnShownAsync()
        {
            Game.Audio.PlayMusic(SoundIds.MenuMusic);
            if (_args.Server == null || !_args.Server.Accepted)
            {
                return;
            }

            foreach (StoryEventDto e in _args.Server.Events)
            {
                await DialogueOverlay.PlayAsync(UI, Loc, e.DialogueId);
                if (!string.IsNullOrEmpty(e.ChoiceId))
                {
                    await ChoicePopup.AskAsync(UI, Loc, e.ChoiceId);
                }
                _ = Game.Backend.Client.Api.MarkEventSeenAsync(e.Id);
            }
        }
    }

    public sealed class PvpResultArgs
    {
        public MatchLaunch Launch;
        public StageResult Local;
        public long OpponentScore;
        public PvpResultDto Server;
        public bool Offline;
        public bool Queued;
    }

    /// <summary>PvP / friendly challenge result, polling the server while a live opponent is still playing.</summary>
    public sealed class PvpResultScreen : UIScreen
    {
        private PvpResultArgs _args;
        private RectTransform _list;

        public override System.Type BackTarget => typeof(MainMenuScreen);

        protected override void Build()
        {
            _args = (PvpResultArgs)Args;
            RectTransform body = Frame("pvp.result", backButton: false);
            _list = UIFactory.ScrollList(body, 24, 48);
            UIFactory.Stretch((RectTransform)_list.parent.parent, 0, 0, 0, 260);
            Fill();

            RectTransform buttons = UIFactory.Anchor(UIFactory.Rect("Buttons", body), 0.05f, 0.02f, 0.95f, 0.12f);
            HorizontalLayoutGroup row = buttons.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 24;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = true;
            UIFactory.Button(buttons, Loc.T("common.menu"), () => UI.ShowRoot<MainMenuScreen>(), Theme.PanelLight);
            UIFactory.Button(buttons, Loc.T("pvp.again"), () => UI.Show<PvpScreen>(null, addToHistory: false));
        }

        public override async Task OnShownAsync()
        {
            Game.Audio.PlayMusic(SoundIds.MenuMusic);
            for (int attempt = 0; attempt < 20 && _args.Server != null && _args.Server.Outcome == "Pending" && this != null; attempt++)
            {
                await Task.Delay(3000);
                if (this == null)
                {
                    return;
                }
                MatchInfoResponse info = await Api(api => api.GetMatchAsync(_args.Launch.Ticket.MatchId), loading: false);
                if (info?.Result != null)
                {
                    _args.Server = info.Result;
                    UIFactory.Clear(_list);
                    Fill();
                }
            }
        }

        private void Fill()
        {
            PvpResultDto s = _args.Server;
            string outcome;
            long myScore = _args.Local.FinalScore;
            long theirScore = _args.OpponentScore;

            if (s != null && s.Accepted && s.Outcome != "Pending")
            {
                outcome = s.Outcome;
                myScore = s.Score;
                theirScore = s.OpponentScore;
            }
            else if (s != null && !s.Accepted)
            {
                outcome = "Rejected";
            }
            else if (s != null)
            {
                outcome = "Pending";
            }
            else
            {
                outcome = myScore > theirScore ? "Win" : myScore < theirScore ? "Loss" : "Draw";
            }

            Color color = outcome == "Win" ? Theme.Success : outcome == "Loss" || outcome == "Rejected" ? Theme.Danger : Theme.Gold;
            UIFactory.Height(UIFactory.Label(_list, Loc.T("pvp." + outcome.ToLowerInvariant()), 90, color, TextAnchor.MiddleCenter, FontStyle.Bold), 150);
            UIFactory.Height(UIFactory.Label(_list, Loc.T("pvp.scores", Loc.Number(myScore), Loc.Number(theirScore)), Theme.HeaderSize), 90);

            if (s != null && (s.FrozenPoints > 0 || s.OpponentFrozenPoints > 0))
            {
                UIFactory.Height(UIFactory.Label(_list, Loc.T("pvp.frozen", Loc.Number(s.FrozenPoints), Loc.Number(s.OpponentFrozenPoints)), Theme.SmallSize, Theme.Crystal), 70);
            }

            if (s != null && s.Accepted && s.Outcome != "Pending")
            {
                if (s.Ranked)
                {
                    string delta = (s.TrophyDelta >= 0 ? "+" : string.Empty) + s.TrophyDelta;
                    UIFactory.Height(UIFactory.Label(_list, Loc.T("pvp.trophies", delta, s.Trophies, Loc.T("league." + s.League)), Theme.HeaderSize, Theme.League(s.League)), 90);
                    Game.Audio.PlaySFX(s.TrophyDelta >= 0 ? SoundIds.TrophyGain : SoundIds.TrophyLoss);
                    if (s.Promoted)
                    {
                        UIFactory.Height(UIFactory.Label(_list, Loc.T("pvp.promoted", Loc.T("league." + s.League)), Theme.HeaderSize, Theme.Gold, TextAnchor.MiddleCenter, FontStyle.Bold), 90);
                    }
                    if (s.FirstReachOrbes > 0)
                    {
                        UIFactory.Height(UIFactory.Label(_list, Loc.T("pvp.rankup", s.FirstReachOrbes), Theme.BodySize, Theme.Orbe), 70);
                    }
                }
                else
                {
                    UIFactory.Height(UIFactory.Label(_list, Loc.T("pvp.friendly"), Theme.BodySize, Theme.TextMuted), 70);
                }
                UIFactory.Height(UIFactory.Label(_list, Loc.T("result.rewards", Loc.Number(s.CoinsEarned), 0), Theme.BodySize, Theme.Gold), 70);
                foreach (string achievement in s.AchievementsUnlocked)
                {
                    UIFactory.Height(UIFactory.Label(_list, Loc.T("result.achievement", Loc.T("ach." + achievement + ".title")), Theme.BodySize, Theme.Gold), 70);
                }
            }
            else if (outcome == "Pending")
            {
                UIFactory.Height(UIFactory.Label(_list, Loc.T("pvp.waiting"), Theme.BodySize, Theme.TextMuted), 90);
            }
            else if (outcome == "Rejected")
            {
                UIFactory.Height(UIFactory.Label(_list, Loc.T("result.rejected", Loc.T("error." + s.Error)), Theme.BodySize, Theme.Danger), 110);
            }
            else if (_args.Offline)
            {
                UIFactory.Height(UIFactory.Label(_list, Loc.T("result.practice"), Theme.BodySize, Theme.Warning), 110);
            }
            else if (_args.Queued)
            {
                UIFactory.Height(UIFactory.Label(_list, Loc.T("result.queued"), Theme.BodySize, Theme.Warning), 110);
            }
        }
    }

    /// <summary>Visual-novel style dialogue. Lines are localization keys "{dialogueId}.{n}" with the format "speaker|text".</summary>
    public static class DialogueOverlay
    {
        public static async Task PlayAsync(UIRoot ui, Localization loc, string dialogueId)
        {
            if (string.IsNullOrEmpty(dialogueId) || !loc.Has(dialogueId + ".1"))
            {
                return;
            }

            RectTransform box = ui.Popup(0.04f, 0.36f);
            Image portrait = UIFactory.Icon(box, ProceduralSprites.Circle(), Theme.Crystal, 180);
            portrait.rectTransform.anchorMin = portrait.rectTransform.anchorMax = new Vector2(0.12f, 0.78f);
            Text initial = UIFactory.Label(portrait.transform, string.Empty, 72, Theme.Background, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Stretch(initial.rectTransform);
            Text speaker = UIFactory.Label(box, string.Empty, Theme.HeaderSize, Theme.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Anchor(speaker.rectTransform, 0.24f, 0.7f, 0.95f, 0.92f);
            Text text = UIFactory.Label(box, string.Empty, Theme.BodySize, Theme.Text, TextAnchor.UpperLeft);
            UIFactory.Anchor(text.rectTransform, 0.05f, 0.08f, 0.95f, 0.66f);

            var advance = new TaskCompletionSource<bool>();
            Button tap = box.parent.gameObject.AddComponent<Button>();
            tap.transition = Selectable.Transition.None;
            tap.onClick.AddListener(() => advance.TrySetResult(true));

            for (int i = 1; loc.Has(dialogueId + "." + i); i++)
            {
                string raw = loc.T(dialogueId + "." + i);
                int bar = raw.IndexOf('|');
                string who = bar > 0 ? raw.Substring(0, bar).Trim() : "narrator";
                string line = bar > 0 ? raw.Substring(bar + 1).Trim() : raw;

                speaker.text = loc.T("char." + who);
                initial.text = speaker.text.Length > 0 ? speaker.text.Substring(0, 1) : "?";
                portrait.color = CharacterColor(who);

                // Typewriter effect; a tap completes the line, the next tap advances.
                advance = new TaskCompletionSource<bool>();
                for (int c = 1; c <= line.Length && !advance.Task.IsCompleted; c += 2)
                {
                    text.text = line.Substring(0, c);
                    await Task.Delay(16);
                    if (box == null)
                    {
                        return;
                    }
                }
                text.text = line;
                if (advance.Task.IsCompleted)
                {
                    advance = new TaskCompletionSource<bool>();
                }
                await advance.Task;
            }

            if (box != null)
            {
                Object.Destroy(box.parent.gameObject);
            }
        }

        private static Color CharacterColor(string id)
        {
            switch (id)
            {
                case "hero": return Theme.Gold;
                case "lyra": return Theme.Crystal;
                case "kael": return Theme.Hex("5B8CFF");
                case "mira": return Theme.Hex("FF8FB1");
                case "thorin": return Theme.Hex("F2E6B8");
                case "zara": return Theme.Hex("7ED957");
                case "elian": return Theme.Hex("3DDC84");
                case "mark": return Theme.Hex("8A8A9E");
                case "soren": return Theme.Hex("C77DFF");
                case "valdorax": return Theme.Danger;
                default: return Theme.TextMuted;
            }
        }
    }

    /// <summary>Branching story choice (server-validated: options can require earlier decisions).</summary>
    public static class ChoicePopup
    {
        public static async Task AskAsync(UIRoot ui, Localization loc, string choiceId)
        {
            StoryChoice choice = StoryDatabase.GetChoice(choiceId);
            GameRoot game = GameRoot.Instance;
            if (choice == null || !game.Backend.IsOnline)
            {
                return;
            }

            var flags = new HashSet<string>(game.Backend.Profile?.Story?.Flags ?? new List<string>());
            var picked = new TaskCompletionSource<string>();
            RectTransform box = ui.Popup(0.2f, 0.8f);
            Text prompt = UIFactory.Label(box, loc.T(choice.PromptKey), Theme.HeaderSize, Theme.Gold);
            UIFactory.Anchor(prompt.rectTransform, 0.05f, 0.72f, 0.95f, 0.95f);

            float top = 0.66f;
            foreach (StoryChoiceOption option in choice.Options)
            {
                bool available = StoryDatabase.IsOptionAvailable(option, flags);
                string label = loc.T(option.TextKey) + (available ? string.Empty : " (" + loc.T("choice.locked") + ")");
                Button button = UIFactory.Button(box, label, () => picked.TrySetResult(option.Id), available ? Theme.PanelLight : Theme.BackgroundLight, Theme.BodySize, Theme.Text);
                button.interactable = available;
                UIFactory.Anchor(button.GetComponent<RectTransform>(), 0.06f, top - 0.18f, 0.94f, top);
                top -= 0.21f;
            }

            string optionId = await picked.Task;
            Object.Destroy(box.parent.gameObject);

            try
            {
                ChoiceResponse response = await game.Backend.Client.Api.MakeChoiceAsync(choiceId, optionId);
                game.Backend.Profile?.Story?.Flags.Add(choice.Options.Find(o => o.Id == optionId)?.SetsFlag);
                if (!string.IsNullOrEmpty(response.Ending))
                {
                    await DialogueOverlay.PlayAsync(ui, loc, "dlg.ending." + response.Ending.ToLowerInvariant());
                }
            }
            catch (CrushRoyale.Client.CrushApiException ex)
            {
                ui.ShowError(ex);
            }
        }
    }
}
