using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CrushRoyale.Client;
using CrushRoyale.Contracts;
using CrushRoyale.Game.Gameplay;
using CrushRoyale.Game.UI;
using UnityEngine;
using UnityEngine.UI;

namespace CrushRoyale.Game.Screens
{
    /// <summary>
    /// "Challenge me" links: share the latest duel as a link (WhatsApp, Instagram...), and accept a challenge from a
    /// copied code (the web page copies it before opening the game) or a typed one.
    /// </summary>
    public static class ChallengeFlow
    {
        private static readonly Regex CodePattern = new Regex("^CR-[0-9A-Z]{1,12}-[0-9A-F]{5}$");

        public static bool IsCode(string text) => !string.IsNullOrEmpty(text) && CodePattern.IsMatch(text.Trim().ToUpperInvariant());

        public static async Task ShareAsync(UIRoot ui, GameRoot game)
        {
            if (!game.Backend.IsOnline)
            {
                ui.Toast(game.Loc.T("error.offline"));
                return;
            }
            ChallengeCreateResponse link;
            using (ui.Loading())
            {
                try
                {
                    link = await game.Backend.Client.Api.CreateChallengeAsync();
                }
                catch (CrushApiException ex)
                {
                    ui.ShowError(ex);
                    return;
                }
            }
            string text = game.Loc.T("challenge.shareText", game.Loc.Number(link.Score), link.Url);
            game.Telemetry.Track("challenge_share");
            if (!NativeShare.ShareText(game.Loc.T("challenge.shareSubject"), text))
            {
                GUIUtility.systemCopyBuffer = link.Url;
                ui.Toast(game.Loc.T("challenge.copied"), 3f);
            }
        }

        /// <summary>Home screen: a challenge code in the clipboard (copied by the web page) is offered once.</summary>
        public static async Task CheckClipboardAsync(UIRoot ui, GameRoot game)
        {
            if (!game.Backend.IsOnline)
            {
                return;
            }
            string clip;
            try
            {
                clip = (GUIUtility.systemCopyBuffer ?? string.Empty).Trim().ToUpperInvariant();
            }
            catch (System.Exception)
            {
                return;
            }
            if (!IsCode(clip) || clip == game.Save.Settings.LastChallengeCode)
            {
                return;
            }
            game.Save.Settings.LastChallengeCode = clip;
            game.Save.SaveSettings();
            if (await ui.Confirm(game.Loc.T("challenge.foundTitle"), game.Loc.T("challenge.foundBody"), game.Loc.T("challenge.play")))
            {
                await AcceptAsync(ui, game, clip);
            }
        }

        /// <summary>PvP screen: type a code received by message.</summary>
        public static async Task PromptCodeAsync(UIRoot ui, GameRoot game)
        {
            RectTransform box = ui.Popup(0.36f, 0.64f);
            Text title = UIFactory.Label(box, game.Loc.T("challenge.enterCode"), Theme.HeaderSize, Theme.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(title.rectTransform, 0.05f, 0.72f, 0.95f, 0.95f);
            InputField input = Widgets.Input(box, "CR-XXXX-XXXXX", 20);
            UIFactory.Anchor(input.GetComponent<RectTransform>(), 0.08f, 0.42f, 0.92f, 0.66f);
            var done = new TaskCompletionSource<string>();
            Button cancel = UIFactory.Button(box, game.Loc.T("common.cancel"), () => done.TrySetResult(null), Theme.PanelLight);
            UIFactory.Anchor(cancel.GetComponent<RectTransform>(), 0.08f, 0.08f, 0.46f, 0.32f);
            Button ok = UIFactory.Button(box, game.Loc.T("challenge.play"), () => done.TrySetResult(input.text));
            UIFactory.Anchor(ok.GetComponent<RectTransform>(), 0.54f, 0.08f, 0.92f, 0.32f);
            string code = await done.Task;
            if (box != null)
            {
                Object.Destroy(box.parent.gameObject);
            }
            if (code == null)
            {
                return;
            }
            code = code.Trim().ToUpperInvariant();
            if (!IsCode(code))
            {
                ui.Toast(game.Loc.T("challenge.invalid"));
                return;
            }
            await AcceptAsync(ui, game, code);
        }

        public static async Task AcceptAsync(UIRoot ui, GameRoot game, string code)
        {
            MatchStartResponse start;
            using (ui.Loading())
            {
                try
                {
                    start = await game.Backend.Client.Api.AcceptChallengeAsync(code, null);
                }
                catch (CrushApiException ex)
                {
                    ui.ShowError(ex);
                    return;
                }
            }
            game.Telemetry.Track("challenge_accept");
            if (start.Ghost != null)
            {
                ui.Toast(game.Loc.T("challenge.versus", start.Ghost.OpponentName), 2.5f);
            }
            ui.Show<GameplayScreen>(MatchLaunch.Online(start), addToHistory: false);
        }
    }

    /// <summary>Android share sheet for plain text (false when unavailable, e.g. in the editor).</summary>
    public static class NativeShare
    {
        public static bool ShareText(string subject, string text)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var intentClass = new AndroidJavaClass("android.content.Intent"))
                using (var intent = new AndroidJavaObject("android.content.Intent"))
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    intent.Call<AndroidJavaObject>("setAction", intentClass.GetStatic<string>("ACTION_SEND"));
                    intent.Call<AndroidJavaObject>("setType", "text/plain");
                    intent.Call<AndroidJavaObject>("putExtra", intentClass.GetStatic<string>("EXTRA_SUBJECT"), subject);
                    intent.Call<AndroidJavaObject>("putExtra", intentClass.GetStatic<string>("EXTRA_TEXT"), text);
                    using (AndroidJavaObject chooser = intentClass.CallStatic<AndroidJavaObject>("createChooser", intent, subject))
                    {
                        activity.Call("startActivity", chooser);
                    }
                }
                return true;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("Share failed: " + ex.Message);
                return false;
            }
#else
            return false;
#endif
        }
    }
}
