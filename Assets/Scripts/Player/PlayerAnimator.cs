using System;
using StumbleClone.Animation;
using StumbleClone.Audio;
using StumbleClone.Core;
using UnityEngine;

namespace StumbleClone.Player
{
    /// Maps PlayerController state onto Animator parameters. If the Animator has no real clips
    /// bound (the project currently ships rigged meshes but no animation clips), it falls back
    /// to a ProceduralCharacterAnimator so the character still visibly moves. Silent no-op if
    /// there's no Animator at all.
    public sealed class PlayerAnimator : MonoBehaviour
    {
        [SerializeField] private Animator animator;
        [SerializeField] private Rigidbody body;
        [SerializeField] private PlayerController controller;
        [SerializeField] private float maxSpeedForNormalization = GameConstants.DefaultMoveSpeed;

        private static readonly int HashSpeed = Animator.StringToHash("Speed");
        private static readonly int HashGrounded = Animator.StringToHash("Grounded");
        private static readonly int HashJump = Animator.StringToHash("Jump");
        private static readonly int HashFall = Animator.StringToHash("Fall");
        private static readonly int HashKnockedDown = Animator.StringToHash("KnockedDown");
        private static readonly int HashDash = Animator.StringToHash("Dash");

        private const float DashLungeDur = 0.28f;
        private const float PushDur = 0.25f;
        private const float LandDur = 0.30f;

        // Landing feedback thresholds: downward contact speeds below SoftLandSpeed read as a light
        // step (quiet, low squash); at/above HardLandSpeed they read as a full thud (loud, deep squash).
        private const float SoftLandSpeed = 4f;
        private const float HardLandSpeed = 12f;

        private bool _wasGrounded = true;
        private float _prevAirborneFallSpeed; // |vel.y| cached each airborne frame for impact scaling
        private ProceduralCharacterAnimator _proc;

        /// Fired on the airborne -> grounded edge, carrying the normalized landing impact
        /// (0 = soft touch-down, 1 = hard slam — the same value that drives the squash/SFX). The
        /// single source of truth for the landing edge: subscribers (e.g. PlayerController, for dust
        /// + camera shake) get exactly one event per landing, so there's no double-trigger.
        public event Action<float> OnLanded;

        // Dash-lunge overlay for the REAL-animator path: the shipped controller has no "Dash"
        // state, so SetTrigger("Dash") would do nothing. Instead we briefly tilt + stretch the
        // animator's transform on top of whatever clip is playing, so the dash always reads.
        private bool _hasDashParam;
        private Transform _visual;
        private Vector3 _visualBasePos;
        private Quaternion _visualBaseRot;
        private Vector3 _visualBaseScale;
        private bool _visualCaptured;
        private float _dashLunge;
        private float _pushTimer;
        private float _landTimer;
        private float _landImpact = 1f;
        private bool _victory;

        private void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (body == null) body = GetComponent<Rigidbody>();
            if (controller == null) controller = GetComponent<PlayerController>();

            if (!AnimatorClipUtil.HasRealClips(animator))
            {
                _proc = AnimatorClipUtil.AttachFallback(this, animator);
            }
            else
            {
                // Real animator in use — set up the procedural dash overlay.
                _hasDashParam = HasParam(animator, HashDash);
                _visual = animator.transform;
                _visualBasePos = _visual.localPosition;
                _visualBaseRot = _visual.localRotation;
                _visualBaseScale = _visual.localScale;
                _visualCaptured = true;
            }
        }

        private void Update()
        {
            if (body == null) return;

            Vector3 v = body.linearVelocity;
            float planar = new Vector2(v.x, v.z).magnitude;
            float denom = Mathf.Max(0.01f, maxSpeedForNormalization);
            float speed01 = Mathf.Clamp01(planar / denom);

            bool grounded = controller != null && controller.IsGrounded;
            bool justJumped = _wasGrounded && !grounded && v.y > 0.1f;
            bool justLanded = !_wasGrounded && grounded;

            // While airborne, remember how fast we're dropping so the moment we touch down we can
            // scale the landing feedback by the speed AT CONTACT (post-land vel.y is ~0 and useless).
            if (!grounded) _prevAirborneFallSpeed = Mathf.Max(0f, -v.y);

            if (justLanded) HandleLanding(_prevAirborneFallSpeed);

            if (_proc != null)
            {
                _proc.SetLocomotion(speed01, grounded);
                if (justJumped) _proc.NotifyJump();
            }
            else if (animator != null)
            {
                animator.SetFloat(HashSpeed, speed01);
                animator.SetBool(HashGrounded, grounded);
                animator.SetBool(HashFall, !grounded && v.y < -0.1f);
                if (justJumped) animator.SetTrigger(HashJump);
            }

            _wasGrounded = grounded;
        }

        // Overlays played on top of whatever clip the real Animator is running, after it writes
        // its pose for the frame: looping victory dance (held), the dash slide, or a push thrust.
        private void LateUpdate()
        {
            if (!_visualCaptured) return;
            float dt = Time.deltaTime;

            if (_victory) // bouncing twirl, held until cleared
            {
                float vt = Time.time;
                float hop = Mathf.Abs(Mathf.Sin(vt * 5f)) * 0.35f;
                float spin = vt * 220f;
                float pulse = 1f + Mathf.Sin(vt * 10f) * 0.05f;
                _visual.localPosition = _visualBasePos + Vector3.up * hop;
                _visual.localRotation = _visualBaseRot * Quaternion.Euler(-8f * Mathf.Sin(vt * 5f), spin, 6f * Mathf.Sin(vt * 10f));
                _visual.localScale = new Vector3(_visualBaseScale.x, _visualBaseScale.y * pulse, _visualBaseScale.z);
                return;
            }

            if (_dashLunge > 0f) // low feet-first slide
            {
                _dashLunge -= dt;
                float d = Mathf.Clamp01(_dashLunge / DashLungeDur); // 1 -> 0
                float s = Mathf.Sin(Mathf.Clamp01(d) * Mathf.PI);   // 0 -> 1 -> 0
                _visual.localPosition = _visualBasePos - new Vector3(0f, 0.32f * s, 0f);
                _visual.localRotation = _visualBaseRot * Quaternion.Euler(-38f * d, 0f, 0f);
                _visual.localScale = new Vector3(
                    _visualBaseScale.x * (1f - 0.10f * s),
                    _visualBaseScale.y * (1f - 0.34f * s),
                    _visualBaseScale.z * (1f + 0.28f * s));
                if (_dashLunge <= 0f) RestoreBase();
                return;
            }

            if (_pushTimer > 0f) // quick forward shove
            {
                _pushTimer -= dt;
                float p = 1f - Mathf.Clamp01(_pushTimer / PushDur); // 0 -> 1
                float s = Mathf.Sin(p * Mathf.PI);                  // 0 -> 1 -> 0
                _visual.localPosition = _visualBasePos + Vector3.up * (0.05f * s);
                _visual.localRotation = _visualBaseRot * Quaternion.Euler(28f * s, 0f, 0f);
                _visual.localScale = _visualBaseScale;
                if (_pushTimer <= 0f) RestoreBase();
                return;
            }

            if (_landTimer > 0f) // touchdown squash — compress down on impact, spring back
            {
                _landTimer -= dt;
                float l = 1f - Mathf.Clamp01(_landTimer / LandDur); // 0 -> 1
                float s = Mathf.Sin(l * Mathf.PI);                  // 0 -> 1 -> 0
                float k = s * _landImpact;
                _visual.localPosition = _visualBasePos - Vector3.up * (0.12f * k);
                _visual.localRotation = _visualBaseRot;
                _visual.localScale = new Vector3(
                    _visualBaseScale.x * (1f + 0.12f * k),
                    _visualBaseScale.y * (1f - 0.20f * k),
                    _visualBaseScale.z * (1f + 0.12f * k));
                if (_landTimer <= 0f) RestoreBase();
            }
        }

        private void RestoreBase()
        {
            _visual.localPosition = _visualBasePos;
            _visual.localRotation = _visualBaseRot;
            _visual.localScale = _visualBaseScale;
        }

        public void TriggerKnockedDown()
        {
            if (_proc != null) _proc.NotifyKnockedDown();
            else if (animator != null) animator.SetTrigger(HashKnockedDown);
        }

        // Airborne -> grounded transition. Plays the Land SFX and a touchdown squash, both scaled by
        // the downward speed at contact: a soft step under ~SoftLandSpeed barely registers, a hard
        // fall above ~HardLandSpeed lands a loud, deep thud. fallSpeed is |vel.y| (>= 0).
        private void HandleLanding(float fallSpeed)
        {
            // 0 at a soft touch-down, 1 at a hard slam — drives both volume/pitch and squash depth.
            float impact = Mathf.Clamp01(Mathf.InverseLerp(SoftLandSpeed, HardLandSpeed, fallSpeed));

            // Skip the SFX on near-zero-speed transitions (e.g. stepping off a tiny lip, ground
            // jitter) so we don't spam a footstep every frame the ground check flickers.
            if (fallSpeed > 0.75f)
            {
                float volume = Mathf.Lerp(0.35f, 1f, impact);
                float pitch = Mathf.Lerp(1.1f, 0.85f, impact); // heavier landings sound lower
                AudioManager.Play(Sfx.Land, volume, pitch);
            }

            TriggerLand(impact);

            // Notify listeners of the landing edge with the same normalized impact. Fires exactly
            // once per touch-down (HandleLanding is only called on the justLanded edge), so
            // dust/shake hooks can't double-trigger.
            OnLanded?.Invoke(impact);
        }

        // Landing squash on the active visual path. Procedural fallback gets NotifyLand; the
        // real-animator path runs its own overlay timer in LateUpdate.
        private void TriggerLand(float impact)
        {
            if (_proc != null) { _proc.NotifyLand(impact); return; }
            _landImpact = Mathf.Clamp01(impact);
            _landTimer = LandDur;
        }

        public void TriggerDash()
        {
            if (_proc != null)
            {
                _proc.NotifyDash();
                return;
            }
            if (_hasDashParam && animator != null) animator.SetTrigger(HashDash);
            _dashLunge = DashLungeDur; // procedural overlay — works with or without a Dash state
        }

        public void TriggerPush()
        {
            if (_proc != null) { _proc.NotifyPush(); return; }
            _pushTimer = PushDur;
        }

        /// Start/stop the looping victory dance (used by the victory screen).
        public void SetVictory(bool on)
        {
            if (_proc != null) { _proc.SetVictory(on); return; }
            _victory = on;
            if (!on && _visualCaptured) RestoreBase();
        }

        private static bool HasParam(Animator a, int nameHash)
        {
            if (a == null || a.runtimeAnimatorController == null) return false;
            var ps = a.parameters;
            for (int i = 0; i < ps.Length; i++)
                if (ps[i].nameHash == nameHash) return true;
            return false;
        }
    }
}
