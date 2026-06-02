using StumbleClone.Core;
using StumbleClone.Game;
using StumbleClone.Player;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StumbleClone.Obstacles
{
    /// Progressive "the whole floor is tipping" pressure for the Knockout (Last-Standing) arena.
    /// As the round wears on, the arena platform slowly leans — and the lean direction sweeps
    /// around — so the human player is dragged downhill and has to keep walking uphill to hold
    /// ground. Combined with the shrinking safe-zone (<see cref="ArenaShrinker"/>) it ramps the
    /// late game from "stay in the circle" to "stay in the circle while the ground fights you."
    ///
    /// Implementation notes:
    /// - The arena's transform (visual + MeshCollider together) is rotated, so the PLAYER's
    ///   physics body genuinely sits on, and slides along, the tilted surface.
    /// - Bots run on a baked, flat NavMesh, so they are NOT tilted or slid — they keep their
    ///   footing. That is deliberate: it makes the tilt a pressure on the human specifically
    ///   ("make it harder"), and it avoids desyncing the NavMesh. The trade-off is bots can look
    ///   slightly off the floor near the rim at max tilt; the angle is capped low and only ramps
    ///   in late once the safe ring has pulled everyone toward the centre, where the offset is tiny.
    /// - Self-bootstrapping on "Level_LastStanding" only, mirroring <see cref="ArenaShrinker"/>.
    [DisallowMultipleComponent]
    public sealed class ArenaTilt : MonoBehaviour
    {
        // ---- Tuning -------------------------------------------------------------
        private const float StartDelay   = 10f;  // calm opening — no tilt for the first seconds
        private const float RampDuration = 50f;  // time to grow from flat → MaxTiltDeg
        private const float MaxTiltDeg   = 7f;    // peak lean angle (kept low so bots barely float)
        private const float MaxSlide     = 3f;    // peak downhill drift fed to the player (m/s; < run speed so it's always counterable)
        private const float DirRotateDeg = 14f;   // how fast the downhill direction sweeps (deg/s)

        private static ArenaTilt _instance;

        private Transform _arena;            // the platform we lean
        private Quaternion _arenaRest;       // its flat orientation, restored if needed
        private float _roundStartTime;
        private bool _running;
        private float _dirAngleDeg;          // current downhill heading (sweeps over time)

        // ---- Bootstrap ----------------------------------------------------------

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            EnsureForScene(SceneManager.GetActiveScene());
        }

        private static void OnSceneLoaded(Scene s, LoadSceneMode m) => EnsureForScene(s);

        private static void EnsureForScene(Scene scene)
        {
            if (!scene.IsValid() || scene.name != "Level_LastStanding") return;
            if (_instance != null) return;
            _instance = new GameObject("ArenaTilt").AddComponent<ArenaTilt>();
        }

        // ---- Lifecycle ----------------------------------------------------------

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
        }

        private void OnEnable() => GameEvents.LevelStarted += HandleLevelStarted;

        private void OnDisable()
        {
            GameEvents.LevelStarted -= HandleLevelStarted;
            ClearPlayerSlide();
        }

        private void OnDestroy()
        {
            RestoreArena();
            if (_instance == this) _instance = null;
        }

        // ---- Round start --------------------------------------------------------

        private void HandleLevelStarted(LevelMode mode)
        {
            if (mode != LevelMode.LastStanding) return;
            ResolveArena();
            _roundStartTime = Time.time;
            _dirAngleDeg = 0f;
            _running = _arena != null;
        }

        /// Find the platform disc to lean. The Last-Standing builder names it "Arena"; fall back
        /// to the LastStandingManager's centre object if the name ever changes.
        private void ResolveArena()
        {
            var go = GameObject.Find("Arena");
            if (go == null)
            {
                var mgr = FindFirstObjectByType<LastStandingManager>();
                if (mgr != null && mgr.ArenaCenter != null) go = mgr.ArenaCenter.gameObject;
            }
            if (go == null) return;
            _arena = go.transform;
            _arenaRest = _arena.localRotation;
        }

        // ---- Per-frame ----------------------------------------------------------

        private void Update()
        {
            if (!_running || _arena == null) return;

            float elapsed = Time.time - _roundStartTime;
            float ramp = Mathf.Clamp01((elapsed - StartDelay) / RampDuration);
            // Ease the lean in so it creeps on rather than snapping.
            float tiltDeg = MaxTiltDeg * Mathf.SmoothStep(0f, 1f, ramp);

            // Sweep the downhill heading so players can't just camp the high side.
            _dirAngleDeg += DirRotateDeg * Time.deltaTime;
            float rad = _dirAngleDeg * Mathf.Deg2Rad;
            Vector3 downhill = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad));

            // Lean the platform so 'downhill' is the low side. Cross(up, downhill) is the hinge axis.
            Vector3 axis = Vector3.Cross(Vector3.up, downhill);
            if (axis.sqrMagnitude > 0.0001f)
                _arena.localRotation = Quaternion.AngleAxis(tiltDeg, axis.normalized) * _arenaRest;

            // Drag the human downhill in proportion to the current lean. Bots are left alone (flat
            // NavMesh) — see the class summary. PlayerController only applies this while grounded
            // and past the spawn settle-grace, so spawn protection still holds.
            if (RacerRegistry.Player is PlayerController player && player.IsAlive)
            {
                float slide = MaxSlide * (MaxTiltDeg > 0.01f ? tiltDeg / MaxTiltDeg : 0f);
                player.SetArenaSlide(downhill * slide);
            }
        }

        // ---- Cleanup ------------------------------------------------------------

        private void ClearPlayerSlide()
        {
            if (RacerRegistry.Player is PlayerController player)
                player.SetArenaSlide(Vector3.zero);
        }

        private void RestoreArena()
        {
            if (_arena != null) _arena.localRotation = _arenaRest;
            ClearPlayerSlide();
        }
    }
}
