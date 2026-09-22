using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using CrushRoyale.Client;
using CrushRoyale.Game.Screens;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CrushRoyale.Game.UI
{
    /// <summary>Base class of every full-screen page. Content is built in code in <see cref="Build"/>.</summary>
    public abstract class UIScreen : MonoBehaviour
    {
        protected GameRoot Game => GameRoot.Instance;

        protected UIRoot UI { get; private set; }

        protected Localization Loc => Game.Loc;

        protected RectTransform Root { get; private set; }

        /// <summary>Screen that should appear when the player presses back (null = previous in history).</summary>
        public virtual Type BackTarget => null;

        internal object Args { get; private set; }

        internal void Setup(UIRoot ui, object args)
        {
            UI = ui;
            Args = args;
            Root = (RectTransform)transform;
            Image background = gameObject.AddComponent<Image>();
            background.color = Theme.Background;
            Build();
        }

        protected abstract void Build();

        /// <summary>Called after the screen is visible (load data here).</summary>
        public virtual Task OnShownAsync() => Task.CompletedTask;

        public virtual void OnHidden()
        {
        }

        /// <summary>Return true if the back press was consumed (closing a sub-panel...).</summary>
        public virtual bool HandleBack() => false;

        /// <summary>Rebuilds the whole screen (language change, data refresh).</summary>
        public void Rebuild()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Destroy(transform.GetChild(i).gameObject);
            }
            Build();
        }

        /// <summary>Kingdom illustrated behind framed screens (darkened); override to match the screen's context.</summary>
        protected virtual CrushRoyale.Core.Story.Kingdom BackdropKingdom => CrushRoyale.Core.Story.Kingdom.Central;

        /// <summary>
        /// Scene id of a dedicated background ("shop", "vip", "settings"...). When the image is not there yet the
        /// kingdom art above is used, so every screen already stops sharing the very same castle.
        /// </summary>
        protected virtual string BackdropScene => null;

        /// <summary>Safe-area content container with a title bar and optional back button.</summary>
        protected RectTransform Frame(string titleKey, bool backButton = true)
        {
            if (string.IsNullOrEmpty(BackdropScene))
            {
                Widgets.Backdrop(Root, BackdropKingdom, 0.45f);
            }
            else
            {
                Widgets.Backdrop(Root, BackdropScene, 0.45f);
            }
            RectTransform safe = UIFactory.Stretch(UIFactory.Rect("Safe", Root));
            safe.gameObject.AddComponent<SafeArea>();

            RectTransform bar = UIFactory.Anchor(UIFactory.Rect("TitleBar", safe), 0, 0.92f, 1, 1);
            Widgets.Fade(Root, true, 0.16f, 0.85f);
            RectTransform ribbon = UIFactory.Anchor(UIFactory.Rect("Ribbon", bar), 0.13f, -0.08f, 0.87f, 1.02f);
            if (UiKit.Ribbon(ribbon) != null)
            {
                Text ribbonTitle = UIFactory.Label(ribbon, Loc.T(titleKey), Theme.HeaderSize, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Anchor(ribbonTitle.rectTransform, 0.2f, 0.36f, 0.8f, 0.9f);
                Widgets.TitleOutline(ribbonTitle);
            }
            else
            {
                Text title = UIFactory.Label(bar, Loc.T(titleKey), Theme.HeaderSize, Theme.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Stretch(title.rectTransform, 160, 160, 0, 0);
            }
            if (backButton)
            {
                Button back = UIFactory.Button(bar, "<", () => UI.Back(), Theme.Panel, Theme.HeaderSize, Theme.Text);
                UIFactory.Anchor(back.GetComponent<RectTransform>(), 0, 0, 0, 1, 12).sizeDelta = new Vector2(120, -24);
                back.GetComponent<RectTransform>().anchoredPosition = new Vector2(84, 0);
            }
            return UIFactory.Anchor(UIFactory.Rect("Body", safe), 0, 0, 1, 0.92f);
        }

        /// <summary>Runs an API call with a loading overlay and localized error handling.</summary>
        protected async Task<T> Api<T>(Func<CrushApi, Task<T>> call, bool loading = true) where T : class
        {
            if (!Game.Backend.IsOnline)
            {
                UI.Toast(Loc.T("error.offline"));
                return null;
            }
            IDisposable overlay = loading ? UI.Loading() : null;
            try
            {
                return await call(Game.Backend.Client.Api);
            }
            catch (CrushApiException ex)
            {
                UI.ShowError(ex);
                return null;
            }
            finally
            {
                overlay?.Dispose();
            }
        }
    }

    /// <summary>
    /// Task 15 UI controller: canvas, screen stack with fade transitions, dialogs, toasts, loading overlay,
    /// Android back button, rebuild on language change.
    /// </summary>
    public sealed class UIRoot : MonoBehaviour
    {
        private readonly Stack<(Type Type, object Args)> _history = new Stack<(Type, object)>();
        private GameRoot _game;
        private RectTransform _screens;
        private RectTransform _dialogs;
        private RectTransform _toasts;
        private RectTransform _loading;
        private int _loadingCount;
        private bool _transitioning;

        /// <summary>Set by Back() and consumed by the next transition, which then plays in the other direction.</summary>
        private bool _back;

        public UIScreen Current { get; private set; }

        public Canvas Canvas { get; private set; }

        public static UIRoot Create(Transform parent, GameRoot game)
        {
            var go = new GameObject("UI", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.layer = 5;
            UIRoot root = go.AddComponent<UIRoot>();
            root._game = game;

            root.Canvas = go.AddComponent<Canvas>();
            root.Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            root.Canvas.sortingOrder = 10;
            CanvasScaler scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();

            if (FindFirstObjectByType<EventSystem>() == null)
            {
                var events = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                events.transform.SetParent(parent, false);
            }

            // One canvas for the whole interface meant that a single ticking chest timer, or one pulsing button,
            // dirtied every widget on screen and forced Unity to rebuild the lot each frame. Each layer gets a nested
            // canvas of its own, so a toast redraws a toast and nothing else.
            root._screens = Layer(go.transform, "Screens", 0);
            root._dialogs = Layer(go.transform, "Dialogs", 10);
            root._toasts = Layer(go.transform, "Toasts", 20);
            root._loading = Layer(go.transform, "Loading", 30);
            root.BuildLoading();

            game.Loc.LanguageChanged += () => root.Current?.Rebuild();
            return root;
        }

        /// <summary>
        /// A full-screen layer with its own canvas. A nested canvas keeps its rebuilds to itself; it also needs its
        /// own raycaster, because a canvas stops the one above it from reaching the widgets inside.
        /// </summary>
        private static RectTransform Layer(Transform parent, string name, int order)
        {
            RectTransform rect = UIFactory.Stretch(UIFactory.Rect(name, parent));
            Canvas canvas = rect.gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = order;
            rect.gameObject.AddComponent<GraphicRaycaster>();
            return rect;
        }

        public void Boot() => Show<SplashScreen>(addToHistory: false);

        public T Show<T>(object args = null, bool addToHistory = true) where T : UIScreen => (T)Show(typeof(T), args, addToHistory);

        public UIScreen Show(Type type, object args = null, bool addToHistory = true)
        {
            if (Current != null && addToHistory)
            {
                _history.Push((Current.GetType(), Current.Args));
            }

            UIScreen previous = Current;
            _game.Telemetry?.Track("screen_view", ("screen", type.Name));
            RectTransform rect = UIFactory.Stretch(UIFactory.Rect(type.Name, _screens));
            CanvasGroup group = rect.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            var screen = (UIScreen)rect.gameObject.AddComponent(type);
            screen.Setup(this, args);
            Current = screen;

            StartCoroutine(Transition(previous, group, screen));
            return screen;
        }

        /// <summary>Clears history (e.g. after login, return to the main menu).</summary>
        public T ShowRoot<T>(object args = null) where T : UIScreen
        {
            _history.Clear();
            return Show<T>(args, addToHistory: false);
        }

        public void Back()
        {
            if (Current != null && Current.HandleBack())
            {
                return;
            }
            if (Current?.BackTarget != null)
            {
                _back = true;
                Show(Current.BackTarget, null, addToHistory: false);
                return;
            }
            if (_history.Count > 0)
            {
                (Type type, object args) = _history.Pop();
                _back = true;
                Show(type, args, addToHistory: false);
            }
        }

        public IDisposable Loading()
        {
            _loadingCount++;
            _loading.gameObject.SetActive(true);
            return new LoadingHandle(this);
        }

        public void Toast(string message, float seconds = 2.2f) => StartCoroutine(ToastRoutine(message, seconds));

        public void ShowError(CrushApiException ex)
        {
            string key = "error." + ex.Code;
            string message = _game.Loc.Has(key) ? _game.Loc.T(key) : _game.Loc.T("error.generic", ex.Message);
            if (ex.Code == "Banned" || ex.Code == "VersionMismatch")
            {
                _ = Alert(_game.Loc.T("error.title"), message);
                return;
            }
            Toast(message, 3f);
            _game.Audio.PlaySFX(Audio.SoundIds.Invalid);
        }

        /// <summary>Layer above every screen for dialogs, popups and full-screen reveals.</summary>
        public Transform DialogLayer => _dialogs;

        public Task Alert(string title, string message) => Dialog(title, message, _game.Loc.T("common.ok"), null);

        public async Task<bool> Confirm(string title, string message, string ok = null, string cancel = null) =>
            await Dialog(title, message, ok ?? _game.Loc.T("common.ok"), cancel ?? _game.Loc.T("common.cancel"));

        /// <summary>Modal dialog. Returns true for the primary button.</summary>
        public Task<bool> Dialog(string title, string message, string primary, string secondary)
        {
            var tcs = new TaskCompletionSource<bool>();
            // Fading dim with a vignette, and the page behind pushed back: a modal arrives, it does not just appear.
            Image shade = ModalShade.Create(_dialogs, _screens, Theme.Overlay);

            Image box = UIFactory.Panel("Box", shade.transform, Theme.Panel);
            UIFactory.Anchor(box.rectTransform, 0.08f, 0.32f, 0.92f, 0.68f);
            bool framed = UiKit.FramePanel(box);
            box.gameObject.AddComponent<PopIn>();

            if (framed)
            {
                // Title on a ribbon straddling the top edge of the ornate frame.
                RectTransform ribbon = UIFactory.Anchor(UIFactory.Rect("Ribbon", box.transform), -0.04f, 0.83f, 1.04f, 1.1f);
                UiKit.Ribbon(ribbon);
                Text ribbonTitle = UIFactory.Label(ribbon, title, Theme.HeaderSize, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Anchor(ribbonTitle.rectTransform, 0.2f, 0.36f, 0.8f, 0.9f);
                Widgets.TitleOutline(ribbonTitle);
            }
            else
            {
                Text titleText = UIFactory.Label(box.transform, title, Theme.HeaderSize, Theme.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Anchor(titleText.rectTransform, 0.05f, 0.75f, 0.95f, 0.95f);
            }
            Text body = UIFactory.Label(box.transform, message, Theme.BodySize);
            UIFactory.Anchor(body.rectTransform, 0.08f, 0.3f, 0.92f, framed ? 0.8f : 0.75f);

            void Close(bool result)
            {
                Destroy(shade.gameObject);
                tcs.TrySetResult(result);
            }

            if (secondary != null)
            {
                Button no = UIFactory.Button(box.transform, secondary, () => Close(false), Theme.PanelLight);
                UIFactory.Anchor(no.GetComponent<RectTransform>(), 0.06f, 0.06f, 0.47f, 0.25f);
                Button yes = UIFactory.Button(box.transform, primary, () => Close(true));
                UIFactory.Anchor(yes.GetComponent<RectTransform>(), 0.53f, 0.06f, 0.94f, 0.25f);
            }
            else
            {
                Button ok = UIFactory.Button(box.transform, primary, () => Close(true));
                UIFactory.Anchor(ok.GetComponent<RectTransform>(), 0.25f, 0.06f, 0.75f, 0.25f);
            }
            return tcs.Task;
        }

        /// <summary>Overlay panel inside the dialog layer (reward popups, pickers). Destroy the returned object to close.</summary>
        public RectTransform Popup(float minY = 0.2f, float maxY = 0.8f)
        {
            Image shade = ModalShade.Create(_dialogs, _screens, Theme.Overlay);
            Image box = UIFactory.Panel("Box", shade.transform, Theme.Panel);
            UIFactory.Anchor(box.rectTransform, 0.05f, minY, 0.95f, maxY);
            UiKit.FramePanel(box);
            box.gameObject.AddComponent<PopIn>();
            return box.rectTransform;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape) && _loadingCount == 0)
            {
                if (_dialogs.childCount > 0)
                {
                    return;
                }
                Back();
            }
        }

        private IEnumerator Transition(UIScreen previous, CanvasGroup incoming, UIScreen screen)
        {
            while (_transitioning)
            {
                yield return null;
            }
            _transitioning = true;
            CanvasGroup outgoing = previous != null ? previous.GetComponent<CanvasGroup>() : null;
            if (outgoing != null)
            {
                outgoing.interactable = false;
            }

            // Directional slide plus fade and a slight zoom; back is quicker and comes from the other side.
            bool back = _back;
            _back = false;
            yield return Transitions.Page(incoming, outgoing, back);
            if (previous != null)
            {
                previous.OnHidden();
                Destroy(previous.gameObject);
            }
            _transitioning = false;

            Task shown = screen != null ? screen.OnShownAsync() : Task.CompletedTask;
            while (!shown.IsCompleted)
            {
                yield return null;
            }
            if (shown.IsFaulted)
            {
                Debug.LogException(shown.Exception);
            }
        }

        private IEnumerator ToastRoutine(string message, float seconds)
        {
            Image toast = UIFactory.Panel("Toast", _toasts, new Color(0.08f, 0.06f, 0.16f, 0.95f));
            UIFactory.Anchor(toast.rectTransform, 0.08f, 0.1f, 0.92f, 0.17f);
            Text label = UIFactory.Label(toast.transform, message, Theme.SmallSize + 4);
            UIFactory.Stretch(label.rectTransform, 24, 24, 8, 8);
            CanvasGroup group = toast.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;

            for (float t = 0; t < 0.2f; t += Time.unscaledDeltaTime)
            {
                group.alpha = t / 0.2f;
                yield return null;
            }
            yield return new WaitForSecondsRealtime(seconds);
            for (float t = 0; t < 0.3f; t += Time.unscaledDeltaTime)
            {
                group.alpha = 1 - t / 0.3f;
                yield return null;
            }
            Destroy(toast.gameObject);
        }

        private void BuildLoading()
        {
            Image shade = _loading.gameObject.AddComponent<Image>();
            shade.color = new Color(0, 0, 0, 0.55f);
            Image spinner = UIFactory.Icon(_loading, ProceduralSprites.Ring(), Theme.Gold, 160);
            spinner.gameObject.AddComponent<Spinner>();
            _loading.gameObject.SetActive(false);
        }

        private void EndLoading()
        {
            _loadingCount = Math.Max(0, _loadingCount - 1);
            if (_loadingCount == 0 && _loading != null)
            {
                _loading.gameObject.SetActive(false);
            }
        }

        private sealed class LoadingHandle : IDisposable
        {
            private UIRoot _root;

            public LoadingHandle(UIRoot root)
            {
                _root = root;
            }

            public void Dispose()
            {
                _root?.EndLoading();
                _root = null;
            }
        }
    }
}
