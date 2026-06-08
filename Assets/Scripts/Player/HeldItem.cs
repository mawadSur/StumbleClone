using System.Collections;
using System.Collections.Generic;
using StumbleClone.Audio;
using StumbleClone.Core;
using StumbleClone.Obstacles;
using UnityEngine;

namespace StumbleClone.Player
{
    /// Self-contained held-item state for the local player. One of these lives on the player root
    /// (added on demand by a <see cref="Powerup"/> collect, or auto-found by <see cref="PlayerController"/>'s
    /// push hook). It holds at most one item at a time and is driven entirely by the existing PUSH
    /// button: <see cref="TryUse"/> is called before the normal shove — if it returns true the item
    /// was used and the normal push is skipped; if false (no item) the normal push runs unchanged.
    ///
    /// Two items (design-locked, PLAYER-ONLY for this pass):
    ///  - BROOM  : a wide forward SWING that knocks every racer in a front hemisphere hard, breaks
    ///             after 3 uses (the held mesh hides/destroys on the third swing).
    ///  - SLIPPER: a single forward-thrown <see cref="Slipper"/> projectile, consumed in one use.
    ///
    /// All visuals are procedural (a thin elongated cube for the broom, parented to the player visual)
    /// via <see cref="RuntimeMaterial"/> — no new art assets. Bots never receive items.
    [DisallowMultipleComponent]
    public sealed class HeldItem : MonoBehaviour
    {
        /// The item currently held. None means the push button performs a normal shove.
        public enum ItemKind { None, Broom, Slipper }

        // ---- Use counts ----
        private const int BroomUses = 3;     // broom breaks after this many swings
        private const int SlipperUses = 1;   // slipper is a single throw

        // ---- Broom swing tuning (a stronger/wider version of PushInteraction) ----
        // Reach is a multiple of the normal push range; the swing covers a forward hemisphere so a
        // mistimed shove still catches anyone clustered ahead of the player.
        private const float BroomRadius = GameConstants.DefaultPushRange * 2.2f;
        private const float BroomForceMul = 1.6f;                 // vs DefaultPushForce
        private const float BroomUpwardShare = 0.45f;             // upward fraction folded into the knock dir
        private const float BroomFrontDot = -0.15f;               // accept slightly behind 90° (wide arc)
        private const float BroomSwingTime = 0.22f;               // visual swing duration

        // ---- Slipper throw tuning ----
        private const float SlipperSpeed = 16f;                   // forward launch speed (m/s)
        private const float SlipperUpwardShare = 0.18f;           // slight upward arc on the throw
        private const float SlipperSpawnAhead = 0.9f;             // spawn this far in front of the player
        private const float SlipperSpawnHeight = 0.9f;            // and this high (chest level)

        // ---- Held broom mesh (thin elongated cube held to the side) ----
        private static readonly Color BroomColor = new Color(0.62f, 0.42f, 0.18f);   // bristly tan/brown
        private static readonly Vector3 BroomHeldScale = new Vector3(0.12f, 0.12f, 1.1f);
        private static readonly Vector3 BroomHeldOffset = new Vector3(0.42f, 0.55f, 0.25f);

        private ItemKind _current = ItemKind.None;
        private int _remainingUses;

        private Transform _broomMesh;        // the visible held broom (null when not holding a broom)
        private Transform _visualParent;     // the player visual child the mesh hangs off (cached)
        private Coroutine _swingRoutine;

        /// The item currently held (None when the push performs a normal shove).
        public ItemKind Current => _current;

        /// True while an item is held — the push button uses the item instead of a normal shove.
        public bool HasItem => _current != ItemKind.None;

        /// Uses left on the held item (0 when nothing is held).
        public int RemainingUses => _remainingUses;

        /// Grant an item, replacing any current one. Broom = 3 swings, Slipper = 1 throw.
        /// Called by <see cref="Powerup"/> when the PLAYER collects a Broom/Slipper pickup.
        public void Give(ItemKind kind)
        {
            // Drop any existing held visual before adopting the new item.
            ClearBroomMesh();

            _current = kind;
            switch (kind)
            {
                case ItemKind.Broom:
                    _remainingUses = BroomUses;
                    BuildBroomMesh();
                    break;
                case ItemKind.Slipper:
                    _remainingUses = SlipperUses;
                    break;
                default:
                    _current = ItemKind.None;
                    _remainingUses = 0;
                    break;
            }
        }

        /// Try to consume the held item. Returns true if an item was used (so the caller skips the
        /// normal push), false if nothing is held (so the normal push proceeds). Broom swings and
        /// decrements (breaking at 0); Slipper throws once and is consumed.
        public bool TryUse()
        {
            switch (_current)
            {
                case ItemKind.Broom:
                    DoBroomSwing();
                    _remainingUses--;
                    if (_remainingUses <= 0) BreakBroom();
                    return true;
                case ItemKind.Slipper:
                    ThrowSlipper();
                    _current = ItemKind.None;
                    _remainingUses = 0;
                    return true;
                default:
                    return false; // no item — let the normal push run
            }
        }

        // ---- Broom swing -------------------------------------------------------

        // A wide, hard shove: knock every OTHER racer inside a forward hemisphere within BroomRadius.
        // We iterate the racer registry (small, allocation-free) rather than a physics overlap so the
        // forward-cone filter is cheap and both players and bots are covered uniformly.
        private void DoBroomSwing()
        {
            AudioManager.Play(Sfx.Push, 1.1f, 0.85f);   // beefier, lower-pitched than a normal shove
            HitStop.Do(0.05f);

            Vector3 origin = transform.position;
            Vector3 forward = transform.forward;
            float force = GameConstants.DefaultPushForce * BroomForceMul;
            float r2 = BroomRadius * BroomRadius;

            IReadOnlyList<IRacer> all = RacerRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                IRacer racer = all[i];
                if (racer == null || racer.Transform == null) continue;
                if (ReferenceEquals(racer.Transform, transform)) continue; // never hit self
                if (!racer.IsAlive) continue;

                Vector3 to = racer.Transform.position - origin;
                to.y = 0f;
                if (to.sqrMagnitude > r2) continue;                 // out of reach

                Vector3 flatDir = to.sqrMagnitude > 0.0001f ? to.normalized : forward;
                if (Vector3.Dot(forward, flatDir) < BroomFrontDot) continue; // behind the swing arc

                // Knock hard, away from the player, with a strong upward component to launch them.
                Vector3 knock = flatDir + Vector3.up * BroomUpwardShare;
                racer.Knockback(knock.normalized * force);
            }

            PlaySwingVisual();
        }

        // Quick arc of the held broom mesh — a one-shot rotate-and-return, purely cosmetic.
        private void PlaySwingVisual()
        {
            if (_broomMesh == null) return;
            if (_swingRoutine != null) StopCoroutine(_swingRoutine);
            _swingRoutine = StartCoroutine(SwingRoutine());
        }

        private IEnumerator SwingRoutine()
        {
            Transform mesh = _broomMesh;
            if (mesh == null) yield break;

            Quaternion rest = Quaternion.identity;
            Quaternion wound = Quaternion.Euler(0f, -70f, 0f);   // wind back
            Quaternion swung = Quaternion.Euler(0f, 90f, 0f);    // follow through across the front

            float t = 0f;
            while (t < BroomSwingTime)
            {
                if (mesh == null) yield break;
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / BroomSwingTime);
                // Wind back fast, sweep across, settle back to rest.
                Quaternion from = k < 0.3f ? rest : wound;
                Quaternion to = k < 0.3f ? wound : (k < 0.85f ? swung : rest);
                float seg = k < 0.3f ? k / 0.3f : (k < 0.85f ? (k - 0.3f) / 0.55f : (k - 0.85f) / 0.15f);
                mesh.localRotation = Quaternion.Slerp(from, to, seg);
                yield return null;
            }
            if (mesh != null) mesh.localRotation = rest;
            _swingRoutine = null;
        }

        private void BreakBroom()
        {
            AudioManager.Play(Sfx.Hit, 0.8f, 1.3f);    // sharp snap
            ClearBroomMesh();
            _current = ItemKind.None;
            _remainingUses = 0;
        }

        // ---- Slipper throw -----------------------------------------------------

        private void ThrowSlipper()
        {
            AudioManager.Play(Sfx.Dash, 0.9f, 1.15f);   // whoosh on the throw

            Vector3 forward = transform.forward;
            Vector3 spawnPos = transform.position + forward * SlipperSpawnAhead + Vector3.up * SlipperSpawnHeight;
            Vector3 dir = (forward + Vector3.up * SlipperUpwardShare).normalized;

            // The player's own collider(s) are passed so the projectile ignores the thrower on spawn.
            Collider[] ownColliders = GetComponentsInChildren<Collider>(true);
            Slipper.Spawn(spawnPos, dir * SlipperSpeed, transform, ownColliders);
        }

        // ---- Held broom mesh (procedural, parented to the player visual) -------

        private void BuildBroomMesh()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "HeldBroom";

            // Strip the primitive's collider — it must never interfere with movement/push casts.
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            var r = go.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = RuntimeMaterial.Make(BroomColor);

            _broomMesh = go.transform;
            _broomMesh.SetParent(ResolveVisualParent(), false);
            _broomMesh.localPosition = BroomHeldOffset;
            _broomMesh.localRotation = Quaternion.identity;
            _broomMesh.localScale = BroomHeldScale;
        }

        private void ClearBroomMesh()
        {
            if (_swingRoutine != null) { StopCoroutine(_swingRoutine); _swingRoutine = null; }
            if (_broomMesh != null)
            {
                Destroy(_broomMesh.gameObject);
                _broomMesh = null;
            }
        }

        // Parent the held mesh to the player's visual child (the animated "Character" model) when
        // present so it moves with the body; fall back to the player root otherwise. Cached once.
        private Transform ResolveVisualParent()
        {
            if (_visualParent != null) return _visualParent;

            // Prefer a child literally named "Character" (the project's model child), else the first
            // child with a SkinnedMeshRenderer/Animator, else this transform.
            Transform character = transform.Find("Character");
            if (character == null)
            {
                var skinned = GetComponentInChildren<SkinnedMeshRenderer>();
                if (skinned != null) character = skinned.transform;
            }
            if (character == null)
            {
                var anim = GetComponentInChildren<Animator>();
                if (anim != null && anim.transform != transform) character = anim.transform;
            }
            _visualParent = character != null ? character : transform;
            return _visualParent;
        }

        private void OnDisable()
        {
            // Tidy the mesh if the player is torn down mid-hold (e.g. scene change).
            ClearBroomMesh();
        }
    }
}
