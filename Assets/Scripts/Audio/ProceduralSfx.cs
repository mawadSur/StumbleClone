using System;
using UnityEngine;

namespace StumbleClone.Audio
{
    /// Synthesizes tiny placeholder SFX in code (sine + noise envelopes) so the game ships with
    /// sound and no audio files. Clips are built once and cached. Replace with imported clips
    /// later behind the same Sfx enum — call sites don't change.
    public static class ProceduralSfx
    {
        private const int SampleRate = 44100;
        private static readonly AudioClip[] _cache = new AudioClip[Enum.GetValues(typeof(Sfx)).Length];

        public static AudioClip Get(Sfx sfx)
        {
            int i = (int)sfx;
            if (_cache[i] == null) _cache[i] = Build(sfx);
            return _cache[i];
        }

        private static AudioClip Build(Sfx sfx)
        {
            switch (sfx)
            {
                case Sfx.UiClick:
                    return Tone("sfx_ui", 0.05f, t => Sine(t, 880f) * Exp(t, 40f) * 0.5f);
                case Sfx.Jump:
                    return Tone("sfx_jump", 0.16f, t => Sine(t, Mathf.Lerp(320f, 660f, t / 0.16f)) * Exp(t, 7f) * 0.7f);
                case Sfx.Land:
                    return Tone("sfx_land", 0.12f, t => (Noise() * 0.5f + Sine(t, 140f)) * Exp(t, 22f) * 0.8f);
                case Sfx.Push:
                    return Tone("sfx_push", 0.16f, t => Noise() * Bell(t, 0.16f) * 0.6f);
                case Sfx.Hit:
                    return Tone("sfx_hit", 0.18f, t => (Noise() * 0.6f + Sine(t, 90f)) * Exp(t, 16f) * 0.9f);
                case Sfx.Eliminate:
                    return Tone("sfx_elim", 0.35f, t => Sine(t, Mathf.Lerp(520f, 120f, t / 0.35f)) * Exp(t, 5f) * 0.7f);
                case Sfx.Win:
                    return Tone("sfx_win", 0.5f, t => WinArp(t) * Exp(t, 2.5f));
                case Sfx.Start:
                    return Tone("sfx_start", 0.18f, t => Sine(t, 520f) * Bell(t, 0.18f) * 0.6f);
                default:
                    return Tone("sfx_blip", 0.05f, t => Sine(t, 440f) * Exp(t, 30f));
            }
        }

        private static AudioClip Tone(string name, float dur, Func<float, float> fn)
        {
            int n = Mathf.Max(1, (int)(dur * SampleRate));
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                data[i] = Mathf.Clamp(fn(t), -1f, 1f);
            }
            var clip = AudioClip.Create(name, n, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static float Sine(float t, float hz) => Mathf.Sin(2f * Mathf.PI * hz * t);
        private static float Exp(float t, float k) => Mathf.Exp(-k * t);
        private static float Bell(float t, float dur) => Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / dur));
        private static float Noise() => UnityEngine.Random.value * 2f - 1f;

        private static float WinArp(float t)
        {
            float[] notes = { 523f, 659f, 784f }; // C5-E5-G5
            int idx = Mathf.Clamp((int)(t / 0.16f), 0, notes.Length - 1);
            return Sine(t, notes[idx]) * 0.6f;
        }
    }
}
