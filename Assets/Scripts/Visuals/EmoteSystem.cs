using System;
using UnityEngine;

namespace StumbleClone.Visuals
{
    /// Procedural EMOTES + VICTORY POSES for the cosmetics locker — ZERO new art.
    ///
    /// Every entry is animated by transforming a character's VISUAL transform as a whole, reusing
    /// the exact motion vocabulary the existing <see cref="StumbleClone.Animation.ProceduralCharacterAnimator"/>
    /// already ships (bob / forward-lean / side-sway / twirl / hop / squash-stretch / pulse) — no
    /// skeleton knowledge and no imported clips. Emotes play once (a short flourish); victory poses
    /// loop (held until the playback object is destroyed), matching the looping celebration the
    /// victory screen shows.
    ///
    /// This is a self-contained catalog + persistence + playback facade:
    ///   • <see cref="Emotes"/> / <see cref="VictoryPoses"/> expose ids + display names.
    ///   • <see cref="SelectedEmote"/> / <see cref="SelectedVictory"/> are PlayerPrefs-backed.
    ///   • <see cref="Play"/> attaches a runner to a character root and animates it.
    ///
    /// It deliberately does NOT touch PlayerController, PlayerAnimator, or the InputActions — the
    /// in-match keybind is out of scope; this is selection + playback only. The locker UI previews
    /// with it, and the victory screen / a future in-match trigger can call Play() to drive it.
    public static class EmoteSystem
    {
        /// One catalog entry: a stable id (PlayerPrefs key + lookup), a human name, the motion
        /// kind that drives it, and the token price (0 == free / owned by default).
        public readonly struct Entry
        {
            public readonly string Id;
            public readonly string Name;
            public readonly Motion Kind;
            public readonly int Price;

            public Entry(string id, string name, Motion kind, int price)
            {
                Id = id; Name = name; Kind = kind; Price = price;
            }
        }

        /// The procedural motion primitives, lifted from the existing character animator's repertoire.
        public enum Motion
        {
            Wave,       // lean + side sway, like a friendly hand wave (emote)
            Dance,      // bouncing twirl (emote)
            Cheer,      // repeated hops with a stretch pop (emote)
            Bow,        // deep forward lean then up (emote)
            Backflip,   // a quick full spin around the side axis (emote)
            Taunt,      // shimmy: fast side sway + squash (emote)
            PoseFlex,   // held flex: stretched tall with a slow pulse (victory)
            PoseSpin,   // continuous celebratory twirl, the classic win dance (victory)
            PoseHover   // floaty bob + gentle lean sway (victory)
        }

        // ---- Catalogs ----------------------------------------------------------
        // Index 0 of each is the always-owned free default. Everything else is buy-then-equip.

        /// Short, one-shot flourishes. ~6 entries.
        public static readonly Entry[] Emotes =
        {
            new Entry("wave",     "Wave",      Motion.Wave,     0),
            new Entry("dance",    "Dance",     Motion.Dance,    120),
            new Entry("cheer",    "Cheer",     Motion.Cheer,    120),
            new Entry("bow",      "Bow",       Motion.Bow,      150),
            new Entry("backflip", "Backflip",  Motion.Backflip, 220),
            new Entry("taunt",    "Taunt",     Motion.Taunt,    180),
        };

        /// Looping celebration stances shown on the victory screen. ~3 entries.
        public static readonly Entry[] VictoryPoses =
        {
            new Entry("spin",  "Spin Out",   Motion.PoseSpin,  0),
            new Entry("flex",  "Big Flex",   Motion.PoseFlex,  200),
            new Entry("hover", "Float On",   Motion.PoseHover, 260),
        };

        // ---- Persistence (PlayerPrefs, mirrors SkinStore / AbilityStore) -------
        private const string SelectedEmoteKey   = "stumbleclone.emote.selected";
        private const string SelectedVictoryKey = "stumbleclone.victory.selected";
        private const string OwnedPrefix        = "stumbleclone.locker.owned."; // + id

        /// Raised when ownership or a selection changes, so the locker UI can refresh.
        public static event Action Changed;

        /// The equipped emote id (falls back to the free default if unset / no longer owned).
        public static string SelectedEmote
        {
            get => ResolveSelected(SelectedEmoteKey, Emotes);
            set => SetSelected(SelectedEmoteKey, value, Emotes);
        }

        /// The equipped victory pose id (falls back to the free default if unset / no longer owned).
        public static string SelectedVictory
        {
            get => ResolveSelected(SelectedVictoryKey, VictoryPoses);
            set => SetSelected(SelectedVictoryKey, value, VictoryPoses);
        }

        // ---- Catalog lookup helpers --------------------------------------------

        public static int IndexOf(Entry[] catalog, string id)
        {
            if (catalog == null || string.IsNullOrEmpty(id)) return 0;
            for (int i = 0; i < catalog.Length; i++)
                if (catalog[i].Id == id) return i;
            return 0;
        }

        public static bool TryGet(string id, out Entry entry)
        {
            int i = IndexOf(Emotes, id);
            if (Emotes[i].Id == id) { entry = Emotes[i]; return true; }
            int j = IndexOf(VictoryPoses, id);
            if (VictoryPoses[j].Id == id) { entry = VictoryPoses[j]; return true; }
            entry = Emotes[0];
            return false;
        }

        public static int PriceOf(string id) => TryGet(id, out var e) ? e.Price : 0;
        public static string NameOf(string id) => TryGet(id, out var e) ? e.Name : id;

        /// Index 0 of either catalog is free & always owned; everything else unlocks via tokens.
        public static bool IsOwned(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            if (PriceOf(id) <= 0) return true; // free defaults
            return PlayerPrefs.GetInt(OwnedPrefix + id, 0) == 1;
        }

        /// Mark an item owned (called after the wallet has been charged elsewhere). Idempotent.
        public static void GrantOwnership(string id)
        {
            if (string.IsNullOrEmpty(id) || IsOwned(id)) return;
            PlayerPrefs.SetInt(OwnedPrefix + id, 1);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        private static string ResolveSelected(string key, Entry[] catalog)
        {
            string fallback = catalog[0].Id;
            string id = PlayerPrefs.GetString(key, fallback);
            // Guard against a stale id (removed from the catalog) or one no longer owned.
            if (catalog[IndexOf(catalog, id)].Id != id) return fallback;
            return IsOwned(id) ? id : fallback;
        }

        private static void SetSelected(string key, string value, Entry[] catalog)
        {
            if (string.IsNullOrEmpty(value)) return;
            if (catalog[IndexOf(catalog, value)].Id != value) return; // not in this catalog
            PlayerPrefs.SetString(key, value);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        // ---- Playback ----------------------------------------------------------

        /// Play an emote / victory pose by id on a character root. Returns the runner driving it
        /// (or null if nothing could be played) so callers can stop it early via Destroy(runner) or
        /// runner.Stop(). Emotes auto-remove themselves when finished; victory poses loop until the
        /// runner is destroyed. Safe to call with a null root or unknown id (no-op).
        ///
        /// <paramref name="characterRoot"/> may be the racer root, the "Character" child, or the
        /// visual transform itself — the runner resolves the actual mesh transform to animate.
        public static EmotePlayer Play(string emoteId, Transform characterRoot)
        {
            if (characterRoot == null) return null;
            if (!TryGet(emoteId, out Entry entry)) return null;

            Transform visual = ResolveVisual(characterRoot);
            if (visual == null) return null;

            // One runner per visual: a fresh emote replaces any in-flight one on the same character.
            var existing = visual.GetComponent<EmotePlayer>();
            if (existing != null) UnityEngine.Object.Destroy(existing);

            var player = visual.gameObject.AddComponent<EmotePlayer>();
            player.Begin(entry.Kind, IsVictory(entry.Kind));
            return player;
        }

        /// Plays the player's currently equipped emote on a character (convenience for an in-match
        /// trigger built later — selection + playback only, no input wiring here).
        public static EmotePlayer PlaySelectedEmote(Transform characterRoot)
            => Play(SelectedEmote, characterRoot);

        /// Plays the player's currently equipped victory pose (looping) — for the victory screen.
        public static EmotePlayer PlaySelectedVictory(Transform characterRoot)
            => Play(SelectedVictory, characterRoot);

        private static bool IsVictory(Motion m)
            => m == Motion.PoseFlex || m == Motion.PoseSpin || m == Motion.PoseHover;

        /// Resolve the transform to animate: prefer a "Character" child (matches SkinSwapper's
        /// model root), else the first skinned mesh's transform, else the root itself.
        private static Transform ResolveVisual(Transform root)
        {
            Transform found = root.Find("Character");
            if (found == null)
            {
                var smr = root.GetComponentInChildren<SkinnedMeshRenderer>();
                if (smr != null) found = smr.transform;
            }
            return found != null ? found : root;
        }
    }

    /// Runtime driver attached to a character's visual transform that animates one emote / victory
    /// pose, then (for one-shot emotes) removes itself. Pure transform animation — the same approach
    /// ProceduralCharacterAnimator uses, so it composes with any skin and needs no clips. Captures
    /// and restores the visual's local pose so it never leaves the character permanently deformed.
    public sealed class EmotePlayer : MonoBehaviour
    {
        private EmoteSystem.Motion _kind;
        private bool _loop;            // victory poses loop; emotes play once
        private float _duration;       // one-shot length (ignored when looping)
        private float _time;
        private bool _running;

        private Vector3 _basePos;
        private Quaternion _baseRot;
        private Vector3 _baseScale;
        private bool _captured;

        /// True while a pose/emote is actively playing.
        public bool IsPlaying => _running;

        internal void Begin(EmoteSystem.Motion kind, bool loop)
        {
            CaptureBase();
            _kind = kind;
            _loop = loop;
            _duration = DurationFor(kind);
            _time = 0f;
            _running = true;
        }

        /// Stop playback, restore the rest pose, and remove the runner.
        public void Stop()
        {
            _running = false;
            RestoreBase();
            Destroy(this);
        }

        private void CaptureBase()
        {
            if (_captured) return;
            _basePos = transform.localPosition;
            _baseRot = transform.localRotation;
            _baseScale = transform.localScale;
            _captured = true;
        }

        private void RestoreBase()
        {
            if (!_captured) return;
            transform.localPosition = _basePos;
            transform.localRotation = _baseRot;
            transform.localScale = _baseScale;
        }

        // Animate in LateUpdate so we layer on TOP of whatever locomotion pose was written this
        // frame (exactly how ProceduralCharacterAnimator / PlayerAnimator apply their overlays).
        private void LateUpdate()
        {
            if (!_running || !_captured) return;

            // Use unscaled time so emotes still play on a paused victory/locker screen.
            _time += Time.unscaledDeltaTime;

            float pos = 0f;              // local vertical offset
            float pitch = 0f, yaw = 0f, roll = 0f; // euler offsets on the base rotation
            float sx = 1f, sy = 1f, sz = 1f;        // scale multipliers

            float t = _loop ? _time : Mathf.Clamp01(_time / _duration); // 0..1 for one-shots
            float pi = Mathf.PI;

            switch (_kind)
            {
                // ---- One-shot emotes (reuse bob / lean / sway / hop / squash) --------
                case EmoteSystem.Motion.Wave:
                {
                    // Friendly tilt + a couple of side sways, easing in and out.
                    float env = Mathf.Sin(t * pi);                  // 0→1→0 envelope
                    roll = Mathf.Sin(_time * 9f) * 16f * env;
                    pitch = 6f * env;
                    pos = 0.04f * env;
                    break;
                }
                case EmoteSystem.Motion.Dance:
                {
                    // Bouncing twirl, like the animator's victory dance but as a finite burst.
                    float env = Mathf.Sin(t * pi);
                    pos = Mathf.Abs(Mathf.Sin(_time * 6f)) * 0.30f * env;
                    yaw = _time * 200f * env;
                    roll = 7f * Mathf.Sin(_time * 12f) * env;
                    sy = 1f + Mathf.Sin(_time * 12f) * 0.05f * env;
                    break;
                }
                case EmoteSystem.Motion.Cheer:
                {
                    // Repeated hops with a stretch pop at the top of each hop.
                    float env = Mathf.Sin(t * pi);
                    float hop = Mathf.Abs(Mathf.Sin(_time * 7f));
                    pos = hop * 0.38f * env;
                    sy = 1f + hop * 0.16f * env;     // stretch up
                    sx = sz = 1f - hop * 0.08f * env; // squash in (volume read)
                    break;
                }
                case EmoteSystem.Motion.Bow:
                {
                    // Smooth deep forward lean, hold a beat, return — a polite bow.
                    float down = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.35f, t));
                    float up = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.7f, 1f, t));
                    float amt = Mathf.Clamp01(down - up);
                    pitch = 62f * amt;
                    pos = -0.06f * amt;
                    break;
                }
                case EmoteSystem.Motion.Backflip:
                {
                    // A quick full rotation around the side axis with a lift at the apex.
                    float e = 1f - Mathf.Pow(1f - t, 2f); // ease-out so it whips around then settles
                    pitch = -360f * e;
                    pos = Mathf.Sin(t * pi) * 0.6f;       // arc up and back down
                    break;
                }
                case EmoteSystem.Motion.Taunt:
                {
                    // Fast cocky shimmy: side sway + a squat squash.
                    float env = Mathf.Sin(t * pi);
                    roll = Mathf.Sin(_time * 16f) * 12f * env;
                    yaw = Mathf.Sin(_time * 8f) * 18f * env;
                    sy = 1f - 0.10f * env;
                    sx = sz = 1f + 0.06f * env;
                    break;
                }

                // ---- Looping victory poses (held until the runner is destroyed) ------
                case EmoteSystem.Motion.PoseSpin:
                {
                    // The classic celebratory twirl (matches the animator's victory dance feel).
                    pos = Mathf.Abs(Mathf.Sin(_time * 5f)) * 0.32f;
                    yaw = _time * 220f;
                    pitch = -8f * Mathf.Sin(_time * 5f);
                    roll = 6f * Mathf.Sin(_time * 10f);
                    sy = 1f + Mathf.Sin(_time * 10f) * 0.05f;
                    break;
                }
                case EmoteSystem.Motion.PoseFlex:
                {
                    // Stand tall and proud, slow pulse + tiny sway — a held flex.
                    sy = 1.10f + Mathf.Sin(_time * 3f) * 0.04f;
                    sx = sz = 1.06f;
                    roll = Mathf.Sin(_time * 2f) * 4f;
                    pos = 0.04f + Mathf.Sin(_time * 3f) * 0.02f;
                    break;
                }
                case EmoteSystem.Motion.PoseHover:
                {
                    // Floaty bob with a gentle lean sway — serene victory.
                    pos = 0.12f + Mathf.Sin(_time * 2.2f) * 0.12f;
                    roll = Mathf.Sin(_time * 1.6f) * 6f;
                    pitch = Mathf.Sin(_time * 1.2f) * 4f;
                    break;
                }
            }

            transform.localPosition = _basePos + Vector3.up * pos;
            transform.localRotation = _baseRot * Quaternion.Euler(pitch, yaw, roll);
            transform.localScale = new Vector3(_baseScale.x * sx, _baseScale.y * sy, _baseScale.z * sz);

            // One-shot emotes clean themselves up the instant they finish.
            if (!_loop && _time >= _duration)
            {
                RestoreBase();
                _running = false;
                Destroy(this);
            }
        }

        private void OnDestroy()
        {
            // Defensive: never leave the visual deformed if torn down externally.
            if (_running) RestoreBase();
        }

        // Per-emote one-shot lengths (seconds). Looping poses ignore this.
        private static float DurationFor(EmoteSystem.Motion m)
        {
            switch (m)
            {
                case EmoteSystem.Motion.Wave:     return 1.4f;
                case EmoteSystem.Motion.Dance:    return 1.8f;
                case EmoteSystem.Motion.Cheer:    return 1.6f;
                case EmoteSystem.Motion.Bow:      return 1.5f;
                case EmoteSystem.Motion.Backflip: return 0.9f;
                case EmoteSystem.Motion.Taunt:    return 1.5f;
                default:                          return 1.5f;
            }
        }
    }
}
