using System;
using System.Threading.Tasks;
using UnityEngine;
#if CRUSH_ADS
using Unity.Services.LevelPlay;
#endif

namespace CrushRoyale.Game.Networking
{
    /// <summary>
    /// Ads through Unity LevelPlay, the mediation that makes several networks bid for the same impression.
    ///
    /// Two formats, and they are not worth the same. The rewarded video is the one the player chooses to watch,
    /// for a life or a handful of moves, and it pays several times what an interstitial pays. The interstitial is
    /// the one nobody asked for, so the server decides when it may appear and this class never shows one on its
    /// own initiative.
    ///
    /// A loaded ad is consumed when it is shown, so each format reloads itself as soon as it closes. An ad that
    /// failed to load is not an error worth surfacing: the player sees a button that is simply not there.
    /// </summary>
    public sealed class LevelPlayAdsProvider : IAdsProvider
    {
#if CRUSH_ADS
        private readonly string _rewardedUnitId;
        private readonly string _interstitialUnitId;

        private LevelPlayRewardedAd _rewarded;
        private LevelPlayInterstitialAd _interstitial;

        private TaskCompletionSource<string> _rewardedShow;
        private TaskCompletionSource<bool> _interstitialShow;

        public LevelPlayAdsProvider(string appKey, string rewardedUnitId, string interstitialUnitId, bool personalised)
        {
            _rewardedUnitId = rewardedUnitId;
            _interstitialUnitId = interstitialUnitId;

            LevelPlay.OnInitSuccess += OnInitSuccess;
            LevelPlay.OnInitFailed += error => Debug.LogWarning("LevelPlay init failed: " + error);
            // Before the first request, or the first ad is served under whatever the SDK assumed.
            LevelPlay.SetConsent(personalised);
            LevelPlay.Init(appKey);
        }

        /// <summary>
        /// Personalised ads, or not. LevelPlay wants this before the first ad request, so it is set from the
        /// constructor with whatever the player answered last time and again the moment they change their mind.
        /// </summary>
        public static void SetConsent(bool personalised) => LevelPlay.SetConsent(personalised);

        public bool IsRewardedReady => _rewarded != null && _rewarded.IsAdReady();

        public bool IsInterstitialReady => _interstitial != null && _interstitial.IsAdReady();

        private void OnInitSuccess(LevelPlayConfiguration configuration)
        {
            // The ad objects cannot exist before the SDK is up, so everything is built from this callback.
            _rewarded = new LevelPlayRewardedAd(_rewardedUnitId);
            _rewarded.OnAdRewarded += (info, reward) => _rewardedShow?.TrySetResult(info?.AdId ?? "levelplay");
            _rewarded.OnAdClosed += info =>
            {
                // Closed without the reward callback: the player skipped, and nothing is granted.
                _rewardedShow?.TrySetResult(null);
                _rewarded.LoadAd();
            };
            _rewarded.OnAdDisplayFailed += (info, error) =>
            {
                Debug.LogWarning("Rewarded display failed: " + error);
                _rewardedShow?.TrySetResult(null);
                _rewarded.LoadAd();
            };
            _rewarded.OnAdLoadFailed += error => Debug.Log("Rewarded unavailable: " + error);
            _rewarded.LoadAd();

            _interstitial = new LevelPlayInterstitialAd(_interstitialUnitId);
            _interstitial.OnAdClosed += info =>
            {
                _interstitialShow?.TrySetResult(true);
                _interstitial.LoadAd();
            };
            _interstitial.OnAdDisplayFailed += (info, error) =>
            {
                _interstitialShow?.TrySetResult(false);
                _interstitial.LoadAd();
            };
            _interstitial.OnAdLoadFailed += error => Debug.Log("Interstitial unavailable: " + error);
            _interstitial.LoadAd();
        }

        /// <summary>
        /// Shows the rewarded video and waits for the player to finish it. Answers null when they skipped, so the
        /// caller grants nothing.
        /// </summary>
        public Task<string> ShowRewardedAsync(string placement)
        {
            if (!IsRewardedReady)
            {
                return Task.FromResult<string>(null);
            }
            _rewardedShow = new TaskCompletionSource<string>();
            _rewarded.ShowAd(placement);
            return _rewardedShow.Task;
        }

        public Task ShowInterstitialAsync()
        {
            if (!IsInterstitialReady)
            {
                return Task.CompletedTask;
            }
            _interstitialShow = new TaskCompletionSource<bool>();
            _interstitial.ShowAd();
            return _interstitialShow.Task;
        }
#else
        public LevelPlayAdsProvider(string appKey, string rewardedUnitId, string interstitialUnitId, bool personalised)
        {
            Debug.Log("LevelPlay package not installed: ads disabled.");
        }

        public static void SetConsent(bool personalised)
        {
        }

        public bool IsRewardedReady => false;

        public bool IsInterstitialReady => false;

        public Task<string> ShowRewardedAsync(string placement) => Task.FromResult<string>(null);

        public Task ShowInterstitialAsync() => Task.CompletedTask;
#endif
    }
}
