using UnityEngine;

namespace StumbleClone.Animation
{
    /// A zero-asset "make it look alive" animator. When the real Animator has no clips bound
    /// (the project currently ships rigged meshes but no animation clips), PlayerAnimator /
    /// BotAnimator attach this at runtime and feed it locomotion + jump + knockdown signals.
    /// It animates the character's VISUAL transform as a whole — no skeleton knowledge needed:
    /// walk bob, forward lean, side sway, idle breathing, jump squash/stretch, knockdown topple
    /// and recover. Auto-disabled once real clips are imported (the drivers stop attaching it).
    public sealed class ProceduralCharacterAnimator : MonoBehaviour
    {
        [SerializeField] private Transform visual;
        [SerializeField] private float bobHeight = 0.12f;
        [SerializeField] private float maxLeanDeg = 11f;
        [SerializeField] private float swayRollDeg = 7f;
        [SerializeField] private float stepFrequency = 2.2f; // strides/sec at full speed
        [SerializeField] private float idleBreath = 0.03f;

        private const float JumpDur = 0.35f;
        private const float KnockDur = 1.3f;
        private const float DashDur = 0.28f;
        private const float PushDur = 0.25f;
        private const float LandDur = 0.30f;

        private Vector3 _basePos;
        private Quaternion _baseRot;
        private Vector3 _baseScale;
        private bool _captured;

        private float _phase;
        private float _speed01;
        private float _jumpTimer;
        private float _knockTimer;
        private float _dashTimer;
        private float _pushTimer;
        private float _landTimer;
        private float _landImpact = 1f; // 0..1 strength of the active landing squash
        private bool _victory;

        /// Point the animator at the mesh root to animate (usually the Animator's own transform).
        public void SetVisual(Transform t)
        {
            visual = t;
            CaptureBase();
        }

        private void Awake()
        {
            if (visual == null)
            {
                // Prefer a child named "Character"; else the first skinned mesh; else first child.
                Transform found = transform.Find("Character");
                if (found == null)
                {
                    var smr = GetComponentInChildren<SkinnedMeshRenderer>();
                    if (smr != null) found = smr.transform;
                }
                if (found == null && transform.childCount > 0) found = transform.GetChild(0);
                visual = found != null ? found : transform;
            }
            CaptureBase();
        }

        private void CaptureBase()
        {
            if (visual == null) return;
            _basePos = visual.localPosition;
            _baseRot = visual.localRotation;
            _baseScale = visual.localScale;
            _captured = true;
        }

        public void SetLocomotion(float speed01, bool grounded)
        {
            _speed01 = Mathf.Clamp01(speed01);
        }

        public void NotifyJump() => _jumpTimer = JumpDur;

        public void NotifyKnockedDown() => _knockTimer = KnockDur;

        public void NotifyDash() => _dashTimer = DashDur;

        public void NotifyPush() => _pushTimer = PushDur;

        /// Touchdown squash on landing — the inverse of the jump's stretch: the body compresses
        /// down on impact then springs back. <paramref name="impact"/> (0..1) scales how hard the
        /// squash reads, so a soft step barely dips while a hard fall really crunches.
        public void NotifyLand(float impact)
        {
            _landImpact = Mathf.Clamp01(impact);
            _landTimer = LandDur;
        }

        /// Looping celebration (hop + twirl) held while true — used by the victory screen.
        public void SetVictory(bool v) => _victory = v;

        private void LateUpdate()
        {
            if (visual == null || !_captured) return;
            float dt = Time.deltaTime;

            // ---- Victory dance: bouncing twirl, held until cleared ------------------
            if (_victory)
            {
                float vt = Time.time;
                float hop = Mathf.Abs(Mathf.Sin(vt * 5f)) * 0.35f;
                float spin = vt * 220f;                          // continuous celebratory twirl
                float pulse = 1f + Mathf.Sin(vt * 10f) * 0.05f;
                visual.localPosition = _basePos + Vector3.up * hop;
                visual.localRotation = _baseRot * Quaternion.Euler(-8f * Mathf.Sin(vt * 5f), spin, 6f * Mathf.Sin(vt * 10f));
                visual.localScale = new Vector3(_baseScale.x, _baseScale.y * pulse, _baseScale.z);
                return;
            }

            // ---- Knockdown: topple over, lie a beat, get back up -------------------
            if (_knockTimer > 0f)
            {
                _knockTimer -= dt;
                float k = 1f - Mathf.Clamp01(_knockTimer / KnockDur);   // 0→1 over the move
                float down = Mathf.Sin(Mathf.Clamp01(k * 1.7f) * Mathf.PI * 0.5f);
                float up = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.72f, 1f, k));
                float amount = Mathf.Clamp01(down - up);
                visual.localRotation = _baseRot * Quaternion.Euler(amount * 82f, 0f, amount * 14f);
                visual.localPosition = _basePos + Vector3.up * (-0.18f * amount);
                visual.localScale = _baseScale;
                return;
            }

            _phase += dt * Mathf.PI * 2f * Mathf.Lerp(0.6f, stepFrequency, _speed01);

            // ---- Jump squash / stretch --------------------------------------------
            float jumpStretchY = 1f, jumpSquashXZ = 1f, jumpPop = 0f;
            if (_jumpTimer > 0f)
            {
                _jumpTimer -= dt;
                float j = 1f - Mathf.Clamp01(_jumpTimer / JumpDur); // 0→1
                float s = Mathf.Sin(j * Mathf.PI);                  // 0→1→0
                jumpStretchY = 1f + 0.18f * s;
                jumpSquashXZ = 1f - 0.10f * s;
                jumpPop = 0.10f * s;
            }

            // ---- Land: touchdown squash — INVERSE of the jump (compress Y, splay XZ, dip) ----
            float landSquashY = 1f, landSplayXZ = 1f, landDip = 0f;
            if (_landTimer > 0f)
            {
                _landTimer -= dt;
                float l = 1f - Mathf.Clamp01(_landTimer / LandDur); // 0→1
                float s = Mathf.Sin(l * Mathf.PI);                  // 0→1→0 (squash in, ease back)
                float k = s * _landImpact;
                landSquashY = 1f - 0.20f * k;  // compress vertically on contact
                landSplayXZ = 1f + 0.12f * k;  // splay out (volume preservation read)
                landDip = 0.12f * k;           // sink toward the ground briefly
            }

            // ---- Dash: low feet-first SLIDE — drop, recline back, stretch along travel ----
            float dashLean = 0f, dashStretchZ = 1f, dashSquashY = 1f, dashLower = 0f;
            if (_dashTimer > 0f)
            {
                _dashTimer -= dt;
                float d = Mathf.Clamp01(_dashTimer / DashDur); // 1→0 over the move
                float s = Mathf.Sin(Mathf.Clamp01(d) * Mathf.PI); // 0→1→0 ease
                dashLean = -34f * d;          // recline BACK for a feet-first slide
                dashStretchZ = 1f + 0.30f * s; // stretch along facing (slide streak)
                dashSquashY = 1f - 0.32f * s;  // crouch low
                dashLower = 0.30f * s;         // drop toward the ground
            }

            // ---- Push: quick forward shove (lean in, small pop) -------------------
            float pushLean = 0f, pushPop = 0f;
            if (_pushTimer > 0f)
            {
                _pushTimer -= dt;
                float p = 1f - Mathf.Clamp01(_pushTimer / PushDur); // 0→1
                float s = Mathf.Sin(p * Mathf.PI);                  // 0→1→0
                pushLean = 30f * s;   // thrust forward
                pushPop = 0.05f * s;
            }

            // ---- Locomotion: bob + lean + sway, idle breathing --------------------
            float bob = Mathf.Abs(Mathf.Sin(_phase)) * bobHeight * _speed01;
            float breath = (_speed01 < 0.05f) ? Mathf.Sin(Time.time * 2f) * idleBreath : 0f;
            float lean = maxLeanDeg * _speed01 + dashLean + pushLean;
            float sway = Mathf.Sin(_phase) * swayRollDeg * _speed01;

            visual.localPosition = _basePos + Vector3.up * (bob + jumpPop - dashLower + pushPop - landDip);
            visual.localRotation = _baseRot * Quaternion.Euler(lean, 0f, sway);
            visual.localScale = new Vector3(
                _baseScale.x * jumpSquashXZ * landSplayXZ,
                _baseScale.y * jumpStretchY * dashSquashY * landSquashY * (1f + breath),
                _baseScale.z * jumpSquashXZ * dashStretchZ * landSplayXZ);
        }
    }
}
