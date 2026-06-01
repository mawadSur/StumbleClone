using System.Collections.Generic;
using UnityEngine;

namespace StumbleClone.Audio
{
    public enum Sfx { UiClick, Jump, Land, Push, Hit, Eliminate, Win, Start }

    /// Self-bootstrapping 2D audio singleton. Plays short, procedurally-synthesized SFX
    /// (ProceduralSfx) through a small pooled-source rig, so the game has sound with ZERO audio
    /// files. Swap ProceduralSfx for imported clips later without touching call sites. Created
    /// before the first scene loads and survives scene changes — no scene wiring required.
    public sealed class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        [Range(0f, 1f)] public float sfxVolume = 0.7f;
        [Range(0f, 1f)] public float musicVolume = 0.4f;

        private const int PoolSize = 8;
        private readonly List<AudioSource> _pool = new List<AudioSource>();
        private int _next;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("AudioManager");
            Instance = go.AddComponent<AudioManager>();
            DontDestroyOnLoad(go);
            go.AddComponent<GameAudioHooks>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            for (int i = 0; i < PoolSize; i++)
            {
                var s = gameObject.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 0f; // 2D
                _pool.Add(s);
            }
        }

        public void PlaySfx(Sfx sfx, float volumeScale = 1f, float pitch = 1f)
        {
            AudioClip clip = ProceduralSfx.Get(sfx);
            if (clip == null || _pool.Count == 0) return;
            var src = _pool[_next];
            _next = (_next + 1) % _pool.Count;
            src.pitch = pitch;
            src.PlayOneShot(clip, Mathf.Clamp01(sfxVolume * volumeScale));
        }

        /// Null-safe static helper so call sites stay one-liners.
        public static void Play(Sfx sfx, float volumeScale = 1f, float pitch = 1f)
        {
            if (Instance != null) Instance.PlaySfx(sfx, volumeScale, pitch);
        }
    }
}
