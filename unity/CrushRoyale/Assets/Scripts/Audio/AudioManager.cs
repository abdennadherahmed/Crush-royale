using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace CrushRoyale.Game.Audio
{
    /// <summary>
    /// Task 16. SFX pool + music with crossfade. Uses the AudioMixer at Resources/Audio/CrushMixer when present
    /// (exposed parameters "MasterVolume", "MusicVolume", "SfxVolume" in dB), otherwise per-source volumes.
    /// Clips come from Resources/Audio/{id} if provided, else from <see cref="SynthClips"/>.
    /// </summary>
    public sealed class AudioManager : MonoBehaviour
    {
        private const int SfxVoices = 10;
        private const float CrossfadeSeconds = 0.8f;

        private readonly List<AudioSource> _sfx = new List<AudioSource>();
        private readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        private AudioSource _musicA;
        private AudioSource _musicB;
        private AudioMixer _mixer;
        private AudioMixerGroup _musicGroup;
        private AudioMixerGroup _sfxGroup;
        private LocalSave _save;
        private string _currentMusic;
        private Coroutine _fade;

        public void Initialize(LocalSave save)
        {
            _save = save;
            _mixer = Resources.Load<AudioMixer>("Audio/CrushMixer");
            if (_mixer != null)
            {
                AudioMixerGroup[] music = _mixer.FindMatchingGroups("Music");
                AudioMixerGroup[] sfx = _mixer.FindMatchingGroups("SFX");
                _musicGroup = music.Length > 0 ? music[0] : null;
                _sfxGroup = sfx.Length > 0 ? sfx[0] : null;
            }

            for (int i = 0; i < SfxVoices; i++)
            {
                AudioSource source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.outputAudioMixerGroup = _sfxGroup;
                _sfx.Add(source);
            }
            _musicA = CreateMusicSource();
            _musicB = CreateMusicSource();
            ApplyVolumes();
        }

        public void PlaySFX(string soundId, float pitch = 1f)
        {
            if (_save == null || _save.Settings.Muted)
            {
                return;
            }
            AudioClip clip = Clip(soundId);
            if (clip == null)
            {
                return;
            }

            AudioSource voice = _sfx[0];
            foreach (AudioSource s in _sfx)
            {
                if (!s.isPlaying)
                {
                    voice = s;
                    break;
                }
            }
            voice.pitch = pitch;
            voice.volume = _mixer != null ? 1f : _save.Settings.MasterVolume * _save.Settings.SfxVolume;
            voice.PlayOneShot(clip);
        }

        public void PlayMusic(string musicId, bool loop = true)
        {
            if (_save == null || musicId == _currentMusic)
            {
                return;
            }
            AudioClip clip = Clip(musicId);
            if (clip == null)
            {
                return;
            }

            _currentMusic = musicId;
            AudioSource from = _musicA.isPlaying ? _musicA : _musicB;
            AudioSource to = from == _musicA ? _musicB : _musicA;
            to.clip = clip;
            to.loop = loop;
            to.volume = 0f;
            to.Play();

            if (_fade != null)
            {
                StopCoroutine(_fade);
            }
            _fade = StartCoroutine(Crossfade(from, to));
        }

        public void StopMusic()
        {
            _currentMusic = null;
            _musicA.Stop();
            _musicB.Stop();
        }

        /// <summary>Prompt API: volumes 0-1, persisted.</summary>
        public void SetVolume(float masterVolume, float sfxVolume, float musicVolume)
        {
            _save.Settings.MasterVolume = Mathf.Clamp01(masterVolume);
            _save.Settings.SfxVolume = Mathf.Clamp01(sfxVolume);
            _save.Settings.MusicVolume = Mathf.Clamp01(musicVolume);
            _save.SaveSettings();
            ApplyVolumes();
        }

        public void SetMuted(bool muted)
        {
            _save.Settings.Muted = muted;
            _save.SaveSettings();
            ApplyVolumes();
        }

        private void ApplyVolumes()
        {
            PlayerSettings s = _save.Settings;
            float master = s.Muted ? 0f : s.MasterVolume;
            if (_mixer != null)
            {
                _mixer.SetFloat("MasterVolume", ToDecibels(master));
                _mixer.SetFloat("MusicVolume", ToDecibels(s.MusicVolume));
                _mixer.SetFloat("SfxVolume", ToDecibels(s.SfxVolume));
            }
            float musicVolume = TargetMusicVolume();
            if (_musicA.isPlaying && (_fade == null))
            {
                _musicA.volume = _musicA.clip != null ? musicVolume : 0f;
            }
            if (_musicB.isPlaying && (_fade == null))
            {
                _musicB.volume = _musicB.clip != null ? musicVolume : 0f;
            }
        }

        private float TargetMusicVolume()
        {
            PlayerSettings s = _save.Settings;
            if (_mixer != null)
            {
                return 1f;
            }
            return s.Muted ? 0f : s.MasterVolume * s.MusicVolume;
        }

        private IEnumerator Crossfade(AudioSource from, AudioSource to)
        {
            float start = from.volume;
            float target = TargetMusicVolume();
            for (float t = 0; t < CrossfadeSeconds; t += Time.unscaledDeltaTime)
            {
                float k = t / CrossfadeSeconds;
                from.volume = Mathf.Lerp(start, 0f, k);
                to.volume = Mathf.Lerp(0f, target, k);
                yield return null;
            }
            from.Stop();
            to.volume = target;
            _fade = null;
        }

        private AudioClip Clip(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }
            if (!_clips.TryGetValue(id, out AudioClip clip))
            {
                clip = Resources.Load<AudioClip>("Audio/" + id) ?? SynthClips.Get(id);
                _clips[id] = clip;
            }
            return clip;
        }

        private AudioSource CreateMusicSource()
        {
            AudioSource source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.outputAudioMixerGroup = _musicGroup;
            return source;
        }

        private static float ToDecibels(float linear) => linear <= 0.0001f ? -80f : Mathf.Log10(linear) * 20f;
    }
}
