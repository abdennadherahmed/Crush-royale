using System;
using System.Collections.Generic;
using UnityEngine;

namespace CrushRoyale.Game.Audio
{
    /// <summary>
    /// Procedural sound effects and music loops, so the game has complete audio without shipping assets.
    /// Real recordings placed in Resources/Audio/{id} automatically replace them (see AudioManager).
    /// </summary>
    public static class SynthClips
    {
        public const int SampleRate = 44100;

        private static readonly Dictionary<string, AudioClip> Cache = new Dictionary<string, AudioClip>();

        public static AudioClip Get(string id)
        {
            if (Cache.TryGetValue(id, out AudioClip clip))
            {
                return clip;
            }
            float[] data = Generate(id);
            if (data == null)
            {
                return null;
            }
            clip = AudioClip.Create("synth_" + id, data.Length, 1, SampleRate, false);
            clip.SetData(data, 0);
            Cache[id] = clip;
            return clip;
        }

        private static float[] Generate(string id)
        {
            switch (id)
            {
                case SoundIds.Match3: return Pluck(660, 0.18f, 0.5f);
                case SoundIds.Match4: return Sequence(0.09f, 660, 880);
                case SoundIds.Match5: return Sequence(0.07f, 660, 830, 990, 1320);
                case SoundIds.Cascade: return Sweep(500, 1100, 0.22f, 0.4f);
                case SoundIds.CascadeComplete: return Chord(new[] { 523.25, 659.25, 783.99, 1046.5 }, 0.6f, 0.35f);
                case SoundIds.MegaCascade: return Mix(Chord(new[] { 392.0, 493.88, 587.33, 783.99 }, 0.9f, 0.35f), Sweep(300, 1600, 0.9f, 0.2f));
                case SoundIds.Click: return Pluck(1200, 0.05f, 0.35f);
                case SoundIds.Invalid: return Sequence(0.08f, 300, 220);
                case SoundIds.PowerUp: return Mix(Noise(0.35f, 0.25f), Sweep(200, 900, 0.35f, 0.4f));
                case SoundIds.Explosion: return Mix(Noise(0.6f, 0.6f), Sweep(180, 40, 0.6f, 0.5f));
                case SoundIds.RedSurge: return Sequence(0.1f, 440, 554, 659, 880, 1108);
                case SoundIds.TrophyGain: return Sequence(0.11f, 784, 988, 1175);
                case SoundIds.TrophyLoss: return Sequence(0.14f, 587, 494, 392);
                case SoundIds.Chat: return Sequence(0.06f, 988, 1319);
                case SoundIds.Coins: return Sequence(0.05f, 1568, 2093, 1568, 2093);
                case SoundIds.WinFanfare: return Sequence(0.16f, 523, 659, 784, 1046, 784, 1046);
                case SoundIds.LoseJingle: return Sequence(0.28f, 440, 415, 392, 330);
                case SoundIds.MenuMusic: return Music(96, new[] { 0, 5, 3, 4 }, 57, false);
                case SoundIds.PvpMusic: return Music(140, new[] { 0, 0, 5, 4 }, 50, true);
                case SoundIds.StoryAct1: return Music(84, new[] { 0, 5, 3, 4 }, 52, false);
                case SoundIds.StoryAct2: return Music(104, new[] { 0, 3, 4, 3 }, 54, true);
                case SoundIds.StoryAct3: return Music(90, new[] { 0, 4, 5, 3 }, 55, false);
                case SoundIds.StoryAct4: return Music(76, new[] { 0, 3, 5, 4 }, 50, false);
                case SoundIds.StoryAct5: return Music(118, new[] { 0, 5, 4, 5 }, 53, true);
                default: return null;
            }
        }

        private static float[] Pluck(double frequency, float seconds, float volume)
        {
            int n = (int)(seconds * SampleRate);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)SampleRate;
                double env = Math.Exp(-t * 18);
                data[i] = (float)(volume * env * (Math.Sin(2 * Math.PI * frequency * t) + 0.3 * Math.Sin(4 * Math.PI * frequency * t)));
            }
            return Fade(data);
        }

        private static float[] Sequence(float noteSeconds, params double[] notes)
        {
            var parts = new List<float[]>();
            foreach (double f in notes)
            {
                parts.Add(Pluck(f, noteSeconds * 1.6f, 0.45f));
            }
            int step = (int)(noteSeconds * SampleRate);
            int length = step * (notes.Length - 1) + parts[parts.Count - 1].Length;
            var data = new float[length];
            for (int p = 0; p < parts.Count; p++)
            {
                for (int i = 0; i < parts[p].Length && p * step + i < length; i++)
                {
                    data[p * step + i] += parts[p][i];
                }
            }
            return Normalize(data, 0.8f);
        }

        private static float[] Sweep(double from, double to, float seconds, float volume)
        {
            int n = (int)(seconds * SampleRate);
            var data = new float[n];
            double phase = 0;
            for (int i = 0; i < n; i++)
            {
                double k = i / (double)n;
                double f = from + (to - from) * k;
                phase += 2 * Math.PI * f / SampleRate;
                data[i] = (float)(volume * Math.Sin(phase) * (1 - k));
            }
            return Fade(data);
        }

        private static float[] Chord(double[] frequencies, float seconds, float volume)
        {
            int n = (int)(seconds * SampleRate);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)SampleRate;
                double env = Math.Min(1, t * 40) * Math.Exp(-t * 3.5);
                double s = 0;
                foreach (double f in frequencies)
                {
                    s += Math.Sin(2 * Math.PI * f * t);
                }
                data[i] = (float)(volume * env * s / frequencies.Length);
            }
            return Fade(data);
        }

        private static float[] Noise(float seconds, float volume)
        {
            int n = (int)(seconds * SampleRate);
            var data = new float[n];
            var rng = new System.Random(1234);
            float low = 0;
            for (int i = 0; i < n; i++)
            {
                float white = (float)(rng.NextDouble() * 2 - 1);
                low += (white - low) * 0.08f;
                data[i] = volume * low * 4 * (1 - i / (float)n);
            }
            return Fade(data);
        }

        /// <summary>4-bar chord loop (pad + bass + optional arpeggio). degrees are scale steps in a minor key rooted at MIDI rootNote.</summary>
        private static float[] Music(int bpm, int[] degrees, int rootNote, bool arpeggio)
        {
            int[] minorScale = { 0, 2, 3, 5, 7, 8, 10 };
            double beat = 60.0 / bpm;
            int barSamples = (int)(beat * 4 * SampleRate);
            int bars = degrees.Length * 2;
            var data = new float[barSamples * bars];

            for (int bar = 0; bar < bars; bar++)
            {
                int degree = degrees[bar % degrees.Length];
                int root = rootNote + minorScale[degree % 7];
                double[] chord = { Midi(root), Midi(root + (IsMinorDegree(degree) ? 3 : 4)), Midi(root + 7) };
                int offset = bar * barSamples;

                for (int i = 0; i < barSamples; i++)
                {
                    double t = i / (double)SampleRate;
                    double pad = 0;
                    foreach (double f in chord)
                    {
                        pad += Math.Sin(2 * Math.PI * f * t) + 0.25 * Math.Sin(2 * Math.PI * f * 2.003 * t);
                    }
                    double padEnv = Math.Min(1, t * 4) * Math.Min(1, (barSamples - i) / (double)SampleRate * 6);
                    double bassT = (t % beat) / beat;
                    double bass = Math.Sin(2 * Math.PI * Midi(root - 12) * t) * Math.Exp(-bassT * 4);
                    double sample = 0.09 * pad * padEnv / chord.Length + 0.22 * bass;

                    if (arpeggio)
                    {
                        double sixteenth = beat / 4;
                        int step = (int)(t / sixteenth);
                        double local = t - step * sixteenth;
                        double note = chord[step % chord.Length] * 2;
                        sample += 0.08 * Math.Sin(2 * Math.PI * note * local) * Math.Exp(-local * 22);
                    }
                    data[offset + i] = (float)sample;
                }
            }
            return Normalize(data, 0.6f);
        }

        private static bool IsMinorDegree(int degree) => degree == 0 || degree == 3 || degree == 4;

        private static double Midi(int note) => 440.0 * Math.Pow(2, (note - 69) / 12.0);

        private static float[] Mix(float[] a, float[] b)
        {
            var data = new float[Math.Max(a.Length, b.Length)];
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = (i < a.Length ? a[i] : 0) + (i < b.Length ? b[i] : 0);
            }
            return Normalize(data, 0.85f);
        }

        private static float[] Normalize(float[] data, float peak)
        {
            float max = 0.0001f;
            foreach (float s in data)
            {
                max = Math.Max(max, Math.Abs(s));
            }
            float gain = Math.Min(1f, peak / max);
            for (int i = 0; i < data.Length; i++)
            {
                data[i] *= gain;
            }
            return data;
        }

        private static float[] Fade(float[] data)
        {
            int fade = Math.Min(220, data.Length / 4);
            for (int i = 0; i < fade; i++)
            {
                float k = i / (float)fade;
                data[i] *= k;
                data[data.Length - 1 - i] *= k;
            }
            return data;
        }
    }

    public static class SoundIds
    {
        public const string Match3 = "match3";
        public const string Match4 = "match4";
        public const string Match5 = "match5";
        public const string Cascade = "cascade";
        public const string CascadeComplete = "cascade_complete";
        public const string MegaCascade = "mega_cascade";
        public const string Click = "click";
        public const string Invalid = "invalid";
        public const string PowerUp = "powerup";
        public const string Explosion = "explosion";
        public const string RedSurge = "red_surge";
        public const string TrophyGain = "trophy_gain";
        public const string TrophyLoss = "trophy_loss";
        public const string Chat = "chat";
        public const string Coins = "coins";
        public const string WinFanfare = "win";
        public const string LoseJingle = "lose";
        public const string MenuMusic = "music_menu";
        public const string PvpMusic = "music_pvp";
        public const string StoryAct1 = "music_act1";
        public const string StoryAct2 = "music_act2";
        public const string StoryAct3 = "music_act3";
        public const string StoryAct4 = "music_act4";
        public const string StoryAct5 = "music_act5";

        public static string StoryMusicForAct(int act)
        {
            switch (act)
            {
                case 2: return StoryAct2;
                case 3: return StoryAct3;
                case 4: return StoryAct4;
                case 5: return StoryAct5;
                default: return StoryAct1;
            }
        }
    }
}
