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
                // Short, soft "tick" with a hint of a second harmonic for body, not a bare beep.
                case Sfx.UiClick:
                    return Tone("sfx_ui", 0.06f, t =>
                        (Sine(t, 1000f) + Sine(t, 2000f) * 0.25f) * Env(t, 0.06f, 0.002f, 18f, 0.012f) * 0.45f);

                // Rising "boop": pitch sweeps up, gentle decay — reads as upward motion.
                case Sfx.Jump:
                    return Tone("sfx_jump", 0.18f, t =>
                        Sine(t, Mathf.Lerp(300f, 720f, Mathf.Clamp01(t / 0.18f))) * Env(t, 0.18f, 0.004f, 6.5f, 0.03f) * 0.6f);

                // Thud: low body tone + a touch of filtered-feeling noise, fast decay.
                case Sfx.Land:
                    return Tone("sfx_land", 0.14f, t =>
                        (Sine(t, 150f) * 0.8f + Noise() * 0.35f) * Env(t, 0.14f, 0.001f, 20f, 0.02f) * 0.7f);

                // Whoosh: noise shaped by a bell so it swells then fades — no hard edges.
                case Sfx.Push:
                    return Tone("sfx_push", 0.18f, t => Noise() * Bell(t, 0.18f) * 0.5f);

                // Impact: noise crack over a low sine, quick decay for punch without harshness.
                case Sfx.Hit:
                    return Tone("sfx_hit", 0.2f, t =>
                        (Noise() * 0.5f + Sine(t, 95f) * 0.9f) * Env(t, 0.2f, 0.0008f, 14f, 0.025f) * 0.8f);

                // Falling pitch slide — "down and out". Smooth attack/release keeps it musical.
                case Sfx.Eliminate:
                    return Tone("sfx_elim", 0.38f, t =>
                        Sine(t, Mathf.Lerp(560f, 130f, Mathf.Clamp01(t / 0.38f))) * Env(t, 0.38f, 0.005f, 4.5f, 0.05f) * 0.6f);

                // Rising major arpeggio (C-E-G) with a soft per-note envelope, then a tail fade.
                case Sfx.Win:
                    return Tone("sfx_win", 0.55f, t => WinArp(t) * Exp(t, 2.2f));

                // Pleasant confirm chime: a perfect-fifth dyad under a bell envelope.
                case Sfx.Start:
                    return Tone("sfx_start", 0.2f, t =>
                        (Sine(t, 523f) + Sine(t, 784f) * 0.6f) * Bell(t, 0.2f) * 0.45f);

                default:
                    return Tone("sfx_blip", 0.06f, t => Sine(t, 440f) * Env(t, 0.06f, 0.002f, 20f, 0.012f));
            }
        }

        private static AudioClip Tone(string name, float dur, Func<float, float> fn)
        {
            int n = Mathf.Max(1, (int)(dur * SampleRate));
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                data[i] = SoftClip(fn(t));
            }
            var clip = AudioClip.Create(name, n, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static float Sine(float t, float hz) => Mathf.Sin(2f * Mathf.PI * hz * t);
        private static float Exp(float t, float k) => Mathf.Exp(-k * t);
        private static float Bell(float t, float dur) => Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / dur));
        private static float Noise() => UnityEngine.Random.value * 2f - 1f;

        /// Smooth amplitude envelope: a short linear attack ramp (kills the start-of-clip click),
        /// an exponential body decay, and a linear release fade to zero (kills the end-of-clip pop).
        /// <param name="t">Time in seconds since the clip started.</param>
        /// <param name="dur">Total clip duration in seconds.</param>
        /// <param name="attack">Fade-in time in seconds.</param>
        /// <param name="decay">Exponential decay rate (higher = snappier).</param>
        /// <param name="release">Fade-out time in seconds at the tail of the clip.</param>
        private static float Env(float t, float dur, float attack, float decay, float release)
        {
            float a = attack > 0f ? Mathf.Clamp01(t / attack) : 1f;
            float r = release > 0f ? Mathf.Clamp01((dur - t) / release) : 1f;
            return a * r * Exp(t, decay);
        }

        /// Gentle tanh-style saturation: tames peaks without the harsh edges of a hard clamp,
        /// so loud SFX overshoot rolls off smoothly instead of square-waving.
        private static float SoftClip(float x)
        {
            const float drive = 1.5f;
            float y = x * drive;
            // Rational approximation of tanh — cheap and smooth across the audible range.
            float s = y / (1f + Mathf.Abs(y));
            return Mathf.Clamp(s, -1f, 1f);
        }

        private static float WinArp(float t)
        {
            float[] notes = { 523f, 659f, 784f }; // C5-E5-G5
            const float step = 0.16f;
            int idx = Mathf.Clamp((int)(t / step), 0, notes.Length - 1);
            // Per-note bell so each step swells and fades instead of snapping on a hard boundary.
            float local = t - idx * step;
            return Sine(t, notes[idx]) * Bell(local, step) * 0.6f;
        }
    }
}
