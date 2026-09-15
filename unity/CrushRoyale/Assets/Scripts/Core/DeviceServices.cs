using System.Diagnostics;
using CrushRoyale.Core.Gameplay;
using UnityEngine;

namespace CrushRoyale.Game
{
    /// <summary>Vibration feedback (Android VibrationEffect with amplitude when available).</summary>
    public sealed class Haptics
    {
        private readonly LocalSave _save;
#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject _vibrator;
        private int _sdk;
#endif

        public Haptics(LocalSave save)
        {
            _save = save;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                {
                    _sdk = version.GetStatic<int>("SDK_INT");
                }
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    _vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
                }
            }
            catch (AndroidJavaException)
            {
                _vibrator = null;
            }
#endif
        }

        public void Light() => Vibrate(15, 60);

        public void Medium() => Vibrate(30, 140);

        public void Heavy() => Vibrate(60, 255);

        public void Vibrate(long milliseconds, int amplitude)
        {
            if (!_save.Settings.HapticsEnabled)
            {
                return;
            }
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_vibrator == null)
            {
                return;
            }
            try
            {
                if (_sdk >= 26)
                {
                    using (var effectClass = new AndroidJavaClass("android.os.VibrationEffect"))
                    using (AndroidJavaObject effect = effectClass.CallStatic<AndroidJavaObject>("createOneShot", milliseconds, Mathf.Clamp(amplitude, 1, 255)))
                    {
                        _vibrator.Call("vibrate", effect);
                    }
                }
                else
                {
                    _vibrator.Call("vibrate", milliseconds);
                }
            }
            catch (AndroidJavaException)
            {
                // Device without vibrator permission/hardware.
            }
#elif UNITY_IOS && !UNITY_EDITOR
            Handheld.Vibrate();
#endif
        }
    }

    /// <summary>
    /// Session clock for a match: real time since start, optionally paused (story pause menu only; PvP never pauses).
    /// Never uses Time.deltaTime: the simulation only sees millisecond timestamps.
    /// </summary>
    public sealed class MatchClock : IGameTimeSource
    {
        private readonly Stopwatch _watch = new Stopwatch();

        public int NowMs => (int)_watch.ElapsedMilliseconds;

        public bool IsPaused => !_watch.IsRunning;

        public void Start() => _watch.Restart();

        public void Pause() => _watch.Stop();

        public void Resume() => _watch.Start();

        public void Stop() => _watch.Stop();
    }
}
