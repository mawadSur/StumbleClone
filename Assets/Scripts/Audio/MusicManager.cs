using StumbleClone.Game;
using UnityEngine;

namespace StumbleClone.Audio
{
    /// Self-bootstrapping background-music singleton. Synthesizes one short, cheerful, bouncy
    /// loop in code (a I-V-vi-IV major-key chord progression with a light bass + arpeggio melody)
    /// so the game ships with music and ZERO audio files — mirroring ProceduralSfx's approach for
    /// SFX. The clip is built once into a seamless looping AudioClip and played on a single looping
    /// AudioSource. Created after the first scene loads and survives scene changes — no scene wiring
    /// required. Volume tracks the Settings sliders live (Master * Music) and sits below SFX.
    public sealed class MusicManager : MonoBehaviour
    {
        /// The live instance, or null before bootstrap. Mirrors AudioManager.Instance.
        public static MusicManager Instance { get; private set; }

        private const int SampleRate = 44100;

        // Tempo: 130 BPM gives a sprightly, party-game bounce. Eight beats = two bars = one loop.
        private const float Bpm = 130f;
        private const int BeatsPerLoop = 8;

        // Baseline mix level so music sits UNDER the SFX channel. Settings sliders scale below this.
        private const float MusicBaseline = 0.32f;

        // Eighth-note arpeggio resolution: two melody notes per beat.
        private const int StepsPerBeat = 2;

        private AudioSource _source;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("MusicManager");
            Instance = go.AddComponent<MusicManager>();
            DontDestroyOnLoad(go);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f; // 2D
            _source.loop = true;
            _source.clip = BuildLoop();
            _source.volume = CurrentVolume();
            _source.Play();
        }

        private void Update()
        {
            // Cheap per-frame volume sync so the Settings sliders take effect live. No allocations.
            if (_source != null) _source.volume = CurrentVolume();
        }

        /// Master * Music trims from the Settings menu, scaled by the music baseline so the channel
        /// sits below SFX. Result is clamped to a valid AudioSource volume.
        private static float CurrentVolume()
        {
            return Mathf.Clamp01(SettingsStore.MasterVolume * SettingsStore.MusicVolume * MusicBaseline);
        }

        /// Synthesizes the seamless loop into a mono AudioClip once. Same Create/SetData pattern as
        /// ProceduralSfx.Tone, but the buffer holds the whole multi-voice arrangement.
        private AudioClip BuildLoop()
        {
            float secondsPerBeat = 60f / Bpm;
            float loopSeconds = BeatsPerLoop * secondsPerBeat;
            int n = Mathf.Max(1, (int)(loopSeconds * SampleRate));
            var data = new float[n];

            RenderChords(data, secondsPerBeat);
            RenderBass(data, secondsPerBeat);
            RenderMelody(data, secondsPerBeat);
            Normalize(data);

            var clip = AudioClip.Create("music_loop", n, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        // --- Arrangement -------------------------------------------------------------------------
        // I-V-vi-IV in C major (the upbeat "four chords" pop progression), one chord per two beats.
        // MIDI-style note numbers; 60 = middle C. Each chord is a major/minor triad.

        private static readonly int[] ChordRoots = { 60, 67, 69, 65 }; // C, G, Am, F
        private static readonly bool[] ChordMinor = { false, false, true, false };

        /// Soft sustained triad pad — three triangle voices per chord with a gentle bell envelope so
        /// chord changes breathe rather than click.
        private void RenderChords(float[] data, float secondsPerBeat)
        {
            float chordDur = secondsPerBeat * 2f;
            for (int c = 0; c < ChordRoots.Length; c++)
            {
                int root = ChordRoots[c];
                int third = ChordMinor[c] ? root + 3 : root + 4;
                int fifth = root + 7;
                float startT = c * chordDur;
                AddVoice(data, Hz(root), startT, chordDur, 0.16f, Tri, soft: true);
                AddVoice(data, Hz(third), startT, chordDur, 0.13f, Tri, soft: true);
                AddVoice(data, Hz(fifth), startT, chordDur, 0.13f, Tri, soft: true);
            }
        }

        /// Light root-note bass, one octave below the chord root, plucked on each beat for bounce.
        private void RenderBass(float[] data, float secondsPerBeat)
        {
            float chordDur = secondsPerBeat * 2f;
            for (int beat = 0; beat < BeatsPerLoop; beat++)
            {
                int chord = (int)(beat / 2f) % ChordRoots.Length;
                int note = ChordRoots[chord] - 12;
                float startT = beat * secondsPerBeat;
                AddPluck(data, Hz(note), startT, secondsPerBeat, 0.22f, Sine, decay: 6f);
            }
        }

        // A cheerful eighth-note melody that arpeggios up each chord (root-third-fifth-octave) and
        // back, kept in a bright register so it sings over the pad.
        private static readonly int[] ArpDegrees = { 0, 4, 7, 12, 7, 4, 7, 12 };

        private void RenderMelody(float[] data, float secondsPerBeat)
        {
            float stepDur = secondsPerBeat / StepsPerBeat;
            int totalSteps = BeatsPerLoop * StepsPerBeat;
            for (int step = 0; step < totalSteps; step++)
            {
                int beat = step / StepsPerBeat;
                int chord = (beat / 2) % ChordRoots.Length;
                int degree = ArpDegrees[step % ArpDegrees.Length];
                // Apply the minor third to the arpeggio when the chord is minor.
                if (ChordMinor[chord] && degree == 4) degree = 3;
                int note = ChordRoots[chord] + 12 + degree; // one octave up for sparkle
                float startT = step * stepDur;
                AddPluck(data, Hz(note), startT, stepDur, 0.2f, Tri, decay: 9f);
            }
        }

        // --- Voice synthesis ---------------------------------------------------------------------

        /// Adds a sustained voice with a soft attack/release bell so it loops without clicks.
        private static void AddVoice(float[] data, float hz, float startT, float dur, float gain,
            System.Func<float, float> wave, bool soft)
        {
            int start = (int)(startT * SampleRate);
            int len = (int)(dur * SampleRate);
            int end = Mathf.Min(start + len, data.Length);
            float phaseStep = hz / SampleRate;
            float phase = 0f;
            for (int i = start; i < end; i++)
            {
                float local = (i - start) / (float)len;       // 0..1 across the note
                float env = soft ? Bell(local) : 1f;
                data[i] += wave(phase) * gain * env;
                phase += phaseStep;
                if (phase >= 1f) phase -= 1f;
            }
        }

        /// Adds a plucked note: fast attack, exponential decay — the bouncy, percussive feel.
        private static void AddPluck(float[] data, float hz, float startT, float dur, float gain,
            System.Func<float, float> wave, float decay)
        {
            int start = (int)(startT * SampleRate);
            int len = (int)(dur * SampleRate);
            int end = Mathf.Min(start + len, data.Length);
            float phaseStep = hz / SampleRate;
            float phase = 0f;
            float invSr = 1f / SampleRate;
            for (int i = start; i < end; i++)
            {
                float t = (i - start) * invSr;
                float env = Mathf.Exp(-decay * t) * Mathf.Min(1f, t * 400f); // decay + 2.5ms attack
                data[i] += wave(phase) * gain * env;
                phase += phaseStep;
                if (phase >= 1f) phase -= 1f;
            }
        }

        // --- Math helpers (phase in 0..1 turns, like ProceduralSfx's Sine but pre-phased) --------

        private static float Sine(float phase) => Mathf.Sin(2f * Mathf.PI * phase);

        // Triangle wave: warmer and softer than a sine's pure tone, gentle on repeat.
        private static float Tri(float phase) => 4f * Mathf.Abs(phase - 0.5f) - 1f;

        // Symmetric bell (sine half-cycle) over a normalized 0..1 span — zero at both ends.
        private static float Bell(float x) => Mathf.Sin(Mathf.PI * Mathf.Clamp01(x));

        /// Converts a MIDI note number to frequency in Hz (A4 = note 69 = 440 Hz).
        private static float Hz(int midi) => 440f * Mathf.Pow(2f, (midi - 69) / 12f);

        /// Scales the mixed buffer to a safe peak so summed voices never clip the AudioClip.
        private static void Normalize(float[] data)
        {
            float peak = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                float a = data[i] < 0f ? -data[i] : data[i];
                if (a > peak) peak = a;
            }
            if (peak <= 1e-4f) return;
            float scale = 0.85f / peak; // leave headroom below full scale
            for (int i = 0; i < data.Length; i++) data[i] *= scale;
        }
    }
}
