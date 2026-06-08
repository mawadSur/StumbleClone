using System.Collections;
using StumbleClone.Bots;
using StumbleClone.Core;
using StumbleClone.Player;
using StumbleClone.UI;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StumbleClone.Obstacles
{
    /// Fires ONE random chaos event per Last-Standing round, mid-round (~30s in after
    /// the ArenaShrinker has started but before the floor is tiny). Six event types:
    ///
    ///   1. WIND_SHEAR    — lateral force on every non-kinematic Rigidbody for 12s
    ///   2. SPEED_DEMON   — all bots + the player run 1.6x faster for 10s
    ///   3. GRAVITY_FLIP  — inverted gravity for 6s (3s countdown first)
    ///   4. DANGER_ZONE   — heavier gravity (faster falling) for 8s
    ///   5. BOT_FRENZY    — bots push faster and harder for 12s
    ///   6. POWER_UP_RAIN — scatter 4–6 collectible powerups across the arena floor
    ///
    /// Self-bootstrapping via [RuntimeInitializeOnLoadMethod(BeforeSceneLoad)];
    /// attaches a driver MonoBehaviour and subscribes to GameEvents.
    /// Only fires on LevelMode.LastStanding. No scene wiring required.
    public static class RoundModifier
    {
        // How many seconds into the round before the chaos event fires.
        private const float FireDelay = 30f;

        private static RoundModifierDriver _driver;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Destroy any stale driver from the previous scene.
            if (_driver != null)
            {
                Object.Destroy(_driver.gameObject);
                _driver = null;
            }

            // Only wire up in the Last-Standing level.
            if (!scene.IsValid() || scene.name != "Level_LastStanding") return;

            var go = new GameObject("RoundModifier");
            Object.DontDestroyOnLoad(go);
            _driver = go.AddComponent<RoundModifierDriver>();
        }

        // ---- Internal driver MonoBehaviour ------------------------------------------------

        /// The MonoBehaviour that owns coroutines and Update. Kept internal; all public
        /// surface is on the outer RoundModifier static class.
        internal sealed class RoundModifierDriver : MonoBehaviour
        {
            // ---- Tuning ------------------------------------------------------------
            private const int EventCount = 6;

            // Wind Shear
            private const float WindDuration = 12f;
            private const float WindForce = 6f;

            // Speed Demon
            private const float SpeedMul = 1.6f;
            private const float SpeedDuration = 10f;

            // Gravity Flip
            private const float GravFlipWarn = 3f;   // countdown before flip
            private const float GravFlipDuration = 6f;
            private static readonly Vector3 GravNormal = new Vector3(0f, -9.81f, 0f);
            private static readonly Vector3 GravFlipped = new Vector3(0f, 5f, 0f);

            // Danger Zone
            private const float GravHeavy = -14f;
            private const float DangerDuration = 8f;

            // Bot Frenzy
            private const float FrenzyCooldownMul = 0.3f;
            private const float FrenzyForceMul = 1.5f;
            private const float FrenzyDuration = 12f;

            // Power-Up Rain
            private const int PowerupCountMin = 4;
            private const int PowerupCountMax = 6;
            private const float PowerupRadiusFraction = 0.7f;

            // ---- State -------------------------------------------------------------
            private float _roundStartTime;
            private bool _fired;
            private int _eventIndex;

            private void OnEnable()
            {
                GameEvents.LevelStarted += HandleLevelStarted;
                GameEvents.LevelEnded += HandleLevelEnded;
            }

            private void OnDisable()
            {
                GameEvents.LevelStarted -= HandleLevelStarted;
                GameEvents.LevelEnded -= HandleLevelEnded;
                // Safety: restore physics gravity on disable in case a coroutine was cut short.
                Physics.gravity = GravNormal;
            }

            private void HandleLevelStarted(LevelMode mode)
            {
                if (mode != LevelMode.LastStanding) return;
                _roundStartTime = Time.unscaledTime;
                _fired = false;
                _eventIndex = Random.Range(0, EventCount);
            }

            private void HandleLevelEnded(IRacer _)
            {
                // Restore gravity on round end regardless of which coroutine ran.
                Physics.gravity = GravNormal;
            }

            private void Update()
            {
                if (_fired) return;
                if (Time.unscaledTime - _roundStartTime < FireDelay) return;
                _fired = true;
                FireEvent(_eventIndex);
            }

            private void FireEvent(int index)
            {
                switch (index)
                {
                    case 0: StartCoroutine(WindShear()); break;
                    case 1: StartCoroutine(SpeedDemon()); break;
                    case 2: StartCoroutine(GravityFlip()); break;
                    case 3: StartCoroutine(DangerZone()); break;
                    case 4: StartCoroutine(BotFrenzy()); break;
                    case 5: StartCoroutine(PowerUpRain()); break;
                    default: StartCoroutine(WindShear()); break;
                }
            }

            // ---- Overlay helper ----------------------------------------------------------------

            /// Build a centered full-screen announce banner: semi-transparent backing + large label.
            /// Auto-destroys after <paramref name="lifetime"/> seconds.
            private static GameObject ShowBanner(string text, Color color, float lifetime)
            {
                var overlay = RuntimeUI.Overlay("ChaosBanner", 200);

                // Semi-transparent dark backing.
                RuntimeUI.Panel(overlay.transform, "Backing", new Color(0f, 0f, 0f, 0.55f),
                    new Vector2(0f, 0.38f), new Vector2(1f, 0.62f),
                    Vector2.zero, Vector2.zero);

                // Headline text.
                var lbl = RuntimeUI.Label(overlay.transform, text, 80,
                    new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1600f, 200f));
                lbl.color = color;
                lbl.fontStyle = FontStyles.Bold;

                Object.Destroy(overlay.transform.root.gameObject, lifetime);
                return overlay;
            }

            // ---- 1. WIND SHEAR -----------------------------------------------------------------

            private IEnumerator WindShear()
            {
                // Pick a random lateral direction.
                Vector2 raw = Random.insideUnitCircle.normalized;
                if (raw.sqrMagnitude < 0.01f) raw = Vector2.right;
                Vector3 windDir = new Vector3(raw.x, 0f, raw.y).normalized;

                // Arrow label: show for 2s before the force starts.
                string arrow = ArrowLabel(windDir);
                ShowBanner($"WIND SHEAR  {arrow}", UITheme.Secondary, 2f);
                yield return new WaitForSeconds(2f);

                // Apply force for WindDuration seconds.
                float endTime = Time.time + WindDuration;
                while (Time.time < endTime)
                {
                    var bodies = Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None);
                    for (int i = 0; i < bodies.Length; i++)
                    {
                        if (bodies[i] == null || bodies[i].isKinematic) continue;
                        bodies[i].AddForce(windDir * WindForce, ForceMode.Acceleration);
                    }
                    yield return new WaitForFixedUpdate();
                }
            }

            /// Approximate the wind direction as a unicode compass arrow for the label.
            private static string ArrowLabel(Vector3 dir)
            {
                float angle = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                if (angle < 0f) angle += 360f;
                int sector = Mathf.RoundToInt(angle / 45f) % 8;
                string[] arrows = { "↑", "↗", "→", "↘", "↓", "↙", "←", "↖" };
                return arrows[sector];
            }

            // ---- 2. SPEED DEMON ----------------------------------------------------------------

            private IEnumerator SpeedDemon()
            {
                ShowBanner("SPEED DEMON", UITheme.Primary, 2f);

                // Apply to player.
                if (RacerRegistry.Player is PlayerController player && player.IsAlive)
                    player.ApplySpeedBoost(SpeedMul, SpeedDuration);

                // Apply to all living bots.
                var bots = Object.FindObjectsByType<BotController>(FindObjectsSortMode.None);
                for (int i = 0; i < bots.Length; i++)
                {
                    if (bots[i] == null || !bots[i].IsAlive) continue;
                    bots[i].ApplySpeedBoost(SpeedMul, SpeedDuration);
                }

                yield return null; // SpeedBoost is self-restoring via its own timer.
            }

            // ---- 3. GRAVITY FLIP ---------------------------------------------------------------

            private IEnumerator GravityFlip()
            {
                // 3-second countdown overlay.
                var overlay = RuntimeUI.Overlay("GravityFlipWarn", 200);
                RuntimeUI.Panel(overlay.transform, "Backing", new Color(0f, 0f, 0f, 0.55f),
                    new Vector2(0f, 0.38f), new Vector2(1f, 0.62f),
                    Vector2.zero, Vector2.zero);
                var countdownLbl = RuntimeUI.Label(overlay.transform, "GRAVITY FLIP  3", 80,
                    new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1600f, 200f));
                countdownLbl.color = UITheme.Accent;
                countdownLbl.fontStyle = FontStyles.Bold;

                for (int i = 3; i >= 1; i--)
                {
                    if (countdownLbl != null) countdownLbl.text = $"GRAVITY FLIP  {i}";
                    yield return new WaitForSeconds(1f);
                }
                if (overlay != null)
                    Object.Destroy(overlay.transform.root.gameObject);

                // Flip.
                Physics.gravity = GravFlipped;
                ShowBanner("GRAVITY FLIP!", UITheme.Accent, 2f);

                yield return new WaitForSeconds(Mathf.Min(GravFlipDuration, 6f));

                Physics.gravity = GravNormal;
            }

            // ---- 4. DANGER ZONE ----------------------------------------------------------------

            private IEnumerator DangerZone()
            {
                ShowBanner("DANGER ZONE", UITheme.Danger, 2f);

                Vector3 heavyGrav = new Vector3(0f, GravHeavy, 0f);
                Physics.gravity = heavyGrav;

                yield return new WaitForSeconds(DangerDuration);

                Physics.gravity = GravNormal;
            }

            // ---- 5. BOT FRENZY ----------------------------------------------------------------

            private IEnumerator BotFrenzy()
            {
                ShowBanner("BOT FRENZY", UITheme.Danger, 2f);

                var bots = Object.FindObjectsByType<BotController>(FindObjectsSortMode.None);

                // Capture original tuning per bot before overriding (default = 1.0, 1.0).
                // BotController doesn't expose the current multipliers, so we snapshot them
                // by calling SetCombatTuning with the originals we know from GameConstants:
                // DefaultPushCooldown/pushForce multipliers start at 1.0/1.0 at construction.
                // Apply frenzy values now; restore defaults after the window.
                for (int i = 0; i < bots.Length; i++)
                {
                    if (bots[i] == null) continue;
                    bots[i].SetCombatTuning(FrenzyCooldownMul, FrenzyForceMul);
                }

                yield return new WaitForSeconds(FrenzyDuration);

                // Restore original defaults (1.0, 1.0 — BotController's construction values).
                for (int i = 0; i < bots.Length; i++)
                {
                    if (bots[i] == null) continue;
                    bots[i].SetCombatTuning(1f, 1f);
                }
            }

            // ---- 6. POWER-UP RAIN -------------------------------------------------------------

            private IEnumerator PowerUpRain()
            {
                ShowBanner("POWER UP RAIN", UITheme.Accent, 2f);

                // Determine the spawn ring from the live arena.
                float radius = ArenaShrinker.Active
                    ? ArenaShrinker.CurrentSafeRadius * PowerupRadiusFraction
                    : 12f;
                Vector3 center = ArenaShrinker.Active ? ArenaShrinker.Center : Vector3.zero;
                float groundY = center.y + 0.5f;

                int count = Random.Range(PowerupCountMin, PowerupCountMax + 1);
                PowerupType[] types = { PowerupType.Speed, PowerupType.Shield, PowerupType.SuperJump };

                for (int i = 0; i < count; i++)
                {
                    float angle = Random.Range(0f, Mathf.PI * 2f);
                    float dist = Random.Range(0f, radius);
                    Vector3 pos = new Vector3(
                        center.x + Mathf.Cos(angle) * dist,
                        groundY,
                        center.z + Mathf.Sin(angle) * dist);

                    PowerupType type = types[Random.Range(0, types.Length)];
                    SpawnPowerup(type, pos);
                }

                yield return null;
            }

            /// Spawn a single Powerup pickup at the given world position.
            /// Tries Resources.Load first; if the prefab is absent, builds one from scratch
            /// (the Powerup component self-builds its visual via Configure).
            private static void SpawnPowerup(PowerupType type, Vector3 groundPoint)
            {
                GameObject go;

                var prefab = Resources.Load<GameObject>("Powerup");
                if (prefab != null)
                {
                    go = Object.Instantiate(prefab, groundPoint, Quaternion.identity);
                    // If the prefab already has a Powerup component, configure it.
                    var existing = go.GetComponent<Powerup>();
                    if (existing != null)
                    {
                        existing.Configure(type, groundPoint);
                        return;
                    }
                }
                else
                {
                    go = new GameObject("Powerup_Rain");
                }

                // Build from scratch: Powerup.Configure sets position, creates visuals, sizes the
                // trigger. RequireComponent ensures SphereCollider is present.
                go.AddComponent<SphereCollider>(); // Powerup requires it
                var pickup = go.AddComponent<Powerup>();
                pickup.Configure(type, groundPoint);
            }
        }
    }
}
