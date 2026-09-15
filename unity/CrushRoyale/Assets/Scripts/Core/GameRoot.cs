using CrushRoyale.Game.Audio;
using CrushRoyale.Game.Networking;
using CrushRoyale.Game.UI;
using UnityEngine;

namespace CrushRoyale.Game
{
    /// <summary>Creates the persistent game root after the first scene loads: no scene setup is required.</summary>
    public static class GameBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (GameRoot.Instance != null)
            {
                return;
            }
            var go = new GameObject("CrushRoyale");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<GameRoot>();
        }
    }

    /// <summary>Service hub (GameManager): owns every long-lived service of the client.</summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class GameRoot : MonoBehaviour
    {
        public static GameRoot Instance { get; private set; }

        public ClientConfig Config { get; private set; }

        public LocalSave Save { get; private set; }

        public Localization Loc { get; private set; }

        public AudioManager Audio { get; private set; }

        public Haptics Haptics { get; private set; }

        public BackendManager Backend { get; private set; }

        public IapService Iap { get; private set; }

        public AdsService Ads { get; private set; }

        public NotificationService Notifications { get; private set; }

        public GoogleSignIn Google { get; private set; }

        public UIRoot UI { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Screen.orientation = ScreenOrientation.Portrait;

            MainThread.EnsureExists(transform);
            Config = ClientConfig.Load();
            Save = new LocalSave();
            Loc = new Localization(Save.Settings.Language);
            Haptics = new Haptics(Save);

            Audio = gameObject.AddComponent<AudioManager>();
            Audio.Initialize(Save);

            Backend = new BackendManager(Config, Save);
            Google = GoogleSignIn.Create(transform);
            Iap = new IapService(Backend, Config);
            Ads = new AdsService(Config);
            Notifications = new NotificationService(Loc, Save);

            UI = UIRoot.Create(transform, this);
        }

        private void Start()
        {
            UI.Boot();
        }

        private void OnApplicationPause(bool paused)
        {
            if (Backend == null)
            {
                return;
            }
            if (paused)
            {
                Notifications.ScheduleReminders(Backend.Profile);
            }
            else
            {
                Notifications.CancelAll();
                Backend.OnResume();
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Backend?.Dispose();
                Instance = null;
            }
        }
    }
}
