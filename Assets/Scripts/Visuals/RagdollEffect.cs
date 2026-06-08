using System.Collections;
using StumbleClone.Animation;
using UnityEngine;

namespace StumbleClone.Visuals
{
    /// Cosmetic slapstick ragdoll. When a racer is knocked back, <see cref="Trigger"/> makes the
    /// character VISUAL go limp and tumble for a short beat, then snap back to its animated pose —
    /// the keeled-over flop reads as a physics ragdoll without an actual joint rig.
    ///
    /// VISUAL ONLY. It never touches the Rigidbody, movement, knockback impulse, or any gameplay
    /// state: it suspends the <see cref="ProceduralCharacterAnimator"/> for the duration, drives the
    /// mesh transform itself, then resumes the animator. Works for both the player and bots — the
    /// caller (PlayerController / BotController) just invokes <see cref="Trigger"/> after a hit lands.
    ///
    /// Re-entrancy: a fresh hit while already tumbling just refreshes/extends the existing tumble
    /// rather than stacking coroutines. Null-guarded throughout; a no-op if no visual/animator exists.
    [DisallowMultipleComponent]
    public sealed class RagdollEffect : MonoBehaviour
    {
        // Tumble shaping. The flop tips the body toward a face-plant in the hit direction; the spin
        // adds a loose roll; the droop sinks it a touch toward the ground. All eased so the limb
        // reads as "goes limp -> tumbles -> springs upright", not a hard snap.
        private const float MaxFlopDeg = 88f;   // peak keel-over toward the hit direction
        private const float MinSpinDeg = 200f;  // random twirl floor over the tumble (deg, signed)
        private const float MaxSpinDeg = 520f;  // random twirl ceiling over the tumble
        private const float MaxDroop = 0.22f;   // peak downward dip of the visual (local units)
        private const float RecoverFrac = 0.74f; // fraction of the duration spent flopped before getting up

        private ProceduralCharacterAnimator _proc;
        private Behaviour _suspendedComponent; // fallback target when no Suspend() seam is available
        private Transform _visual;

        private Vector3 _baseLocalPos;
        private Quaternion _baseLocalRot;
        private bool _captured;

        private Coroutine _routine;
        private bool _active;
        private float _endTime;     // wall-clock time the current tumble should finish at
        private float _duration;    // length of the current tumble (refreshed on re-trigger)
        private Vector3 _flopAxis;  // local axis to tip about (perpendicular to the hit direction)
        private float _spinDeg;     // signed twirl applied over this tumble

        /// Play the limp tumble in the direction of <paramref name="hitDir"/> for
        /// <paramref name="duration"/> seconds. Cosmetic only. Safe to call every hit: a call while a
        /// tumble is already running refreshes its direction and extends it to a fresh full duration.
        /// A zero/near-zero hit direction defaults to a forward flop so the effect always reads.
        public void Trigger(Vector3 hitDir, float duration = 0.9f)
        {
            if (duration <= 0f) return;

            if (!ResolveVisual()) return; // no mesh/animator to animate — no-op gracefully

            // Convert the world hit direction to a planar local roll axis: the body tips AWAY from
            // the push, so we rotate about the axis perpendicular to it (cross with up). Fall back to
            // a forward flop when the push has no usable horizontal component.
            Vector3 dir = new Vector3(hitDir.x, 0f, hitDir.z);
            if (dir.sqrMagnitude < 1e-4f) dir = transform.forward;
            dir.Normalize();
            Vector3 worldAxis = Vector3.Cross(Vector3.up, dir); // tip forward over the push direction
            if (worldAxis.sqrMagnitude < 1e-4f) worldAxis = transform.right;
            _flopAxis = _visual.parent != null
                ? _visual.parent.InverseTransformDirection(worldAxis).normalized
                : worldAxis.normalized;

            _spinDeg = Random.Range(MinSpinDeg, MaxSpinDeg) * (Random.value < 0.5f ? -1f : 1f);
            _duration = duration;
            _endTime = Time.time + duration;

            // Already tumbling: just refresh direction/duration above; the running coroutine reads
            // _endTime / _flopAxis / _spinDeg live, so the new hit extends the same tumble.
            if (_active) return;

            // Fresh tumble: capture the rest pose, hand the visual off from the animator, and run.
            CaptureBase();
            SuspendAnimator(true);
            _active = true;
            _routine = StartCoroutine(TumbleRoutine());
        }

        // Resolve (once, cached) the procedural animator and the exact visual transform it drives.
        // Returns false if there is genuinely nothing to animate.
        private bool ResolveVisual()
        {
            if (_visual != null) return true;

            if (_proc == null) _proc = GetComponentInChildren<ProceduralCharacterAnimator>();

            if (_proc != null && _proc.Visual != null)
            {
                _visual = _proc.Visual;
                return true;
            }

            // No procedural fallback in use (real clips are bound, or it's just missing). Resolve the
            // mesh root the same way the animator does: "Character" child -> SkinnedMeshRenderer ->
            // Animator -> first child -> self.
            Transform found = transform.Find("Character");
            if (found == null)
            {
                var smr = GetComponentInChildren<SkinnedMeshRenderer>();
                if (smr != null) found = smr.transform;
            }
            if (found == null)
            {
                var anim = GetComponentInChildren<Animator>();
                if (anim != null) found = anim.transform;
            }
            if (found == null && transform.childCount > 0) found = transform.GetChild(0);

            _visual = found; // may stay null on a bodyless racer -> Trigger no-ops
            return _visual != null;
        }

        private void CaptureBase()
        {
            if (_visual == null) return;
            _baseLocalPos = _visual.localPosition;
            _baseLocalRot = _visual.localRotation;
            _captured = true;
        }

        // Suspend / resume the locomotion animator for the tumble. Prefer the explicit Suspend seam
        // (keeps the component enabled so it can restore its own base pose on resume); fall back to
        // disabling the component outright if for some reason it isn't a ProceduralCharacterAnimator.
        private void SuspendAnimator(bool on)
        {
            if (_proc != null)
            {
                _proc.Suspend(on);
                return;
            }
            if (_suspendedComponent != null) _suspendedComponent.enabled = !on;
        }

        private IEnumerator TumbleRoutine()
        {
            while (Time.time < _endTime && _visual != null)
            {
                // Progress across the CURRENT (possibly refreshed) tumble window. Re-trigger pushes
                // _endTime / _duration out, so this naturally restarts the flop from the new hit.
                float remaining = Mathf.Max(0f, _endTime - Time.time);
                float t = 1f - Mathf.Clamp01(remaining / Mathf.Max(0.0001f, _duration)); // 0 -> 1

                // Flop envelope: ease over hard during the first RecoverFrac, then ease back upright
                // over the tail. Peaks at full keel-over, returns to 0 so we land on the rest pose.
                float flop01;
                if (t < RecoverFrac)
                    flop01 = Mathf.Sin(Mathf.Clamp01(t / RecoverFrac) * (Mathf.PI * 0.5f)); // 0 -> 1
                else
                    flop01 = Mathf.Cos(Mathf.Clamp01((t - RecoverFrac) / (1f - RecoverFrac)) * (Mathf.PI * 0.5f)); // 1 -> 0

                float flopDeg = MaxFlopDeg * flop01;
                // Loose roll: most of the spin is spent while flopped, then settles as it gets up.
                float spinDeg = _spinDeg * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t)) * flop01;
                float droop = MaxDroop * flop01;

                Quaternion flopRot = Quaternion.AngleAxis(flopDeg, _flopAxis);
                Quaternion spinRot = Quaternion.AngleAxis(spinDeg, Vector3.up);

                _visual.localRotation = _baseLocalRot * spinRot * flopRot;
                _visual.localPosition = _baseLocalPos + Vector3.down * droop;

                yield return null;
            }

            Finish();
        }

        // Restore the rest pose and hand the visual back to the animator. Safe to call once per
        // tumble; guarded so a re-trigger that ends the routine early can't double-restore.
        private void Finish()
        {
            if (!_active) return;
            _active = false;
            _routine = null;

            if (_visual != null && _captured)
            {
                _visual.localPosition = _baseLocalPos;
                _visual.localRotation = _baseLocalRot;
            }
            SuspendAnimator(false);
        }

        // If the racer is disabled / destroyed mid-tumble, make sure the animator is handed back and
        // the visual isn't left frozen in a flopped pose.
        private void OnDisable()
        {
            if (_active) Finish();
        }
    }
}
