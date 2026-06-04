using System.Collections.Generic;
using StumbleClone.Core;
using StumbleClone.Game;
using UnityEngine;
using UnityEngine.AI;

namespace StumbleClone.Bots
{
    public sealed class BotSpawner : MonoBehaviour
    {
        [SerializeField] private GameObject botPrefab;
        [SerializeField] private Transform[] spawnPoints;
        [SerializeField] private int botCount = GameConstants.DefaultBotsPerLevel;
        [SerializeField] private LevelMode mode = LevelMode.Race;

        [Header("Mode references")]
        [SerializeField] private Transform finishLine;
        [SerializeField] private Transform arenaCenter;
        [SerializeField] private Transform safeAnchor;
        [SerializeField] private float arenaRadius = 15f;

        [Header("Spawn options")]
        [SerializeField] private bool spawnOnStart = true;
        [SerializeField] private int firstRacerId = 1;
        [Tooltip("Index of the first spawn point bots use. Set to 1 in the arena so bots don't stack on the player, who takes spawn point 0.")]
        [SerializeField] private int spawnPointOffset = 0;

        // ---- Networked-mode control --------------------------------------------------------------
        // In an online match the SERVER decides how many bots are needed to backfill empty human
        // slots, so it suppresses the offline auto-spawn and drives SpawnBots/DespawnExtraBots itself
        // (see StumbleClone.Net.NetworkGame). Offline this stays false and behaviour is unchanged.
        private bool _networkedMode;

        // Bots this spawner created, in spawn order. Used so DespawnExtraBots can remove the most
        // recently added bots (newest-first) without disturbing the rest of the field. Only populated
        // through SpawnInternal, so the offline SpawnAll path also fills it (harmless — offline never
        // despawns), keeping a single code path.
        private readonly List<BotController> _spawned = new List<BotController>(8);

        /// Number of live bots this spawner currently owns (prunes destroyed entries lazily).
        public int SpawnedBotCount
        {
            get
            {
                PruneSpawned();
                return _spawned.Count;
            }
        }

        /// <summary>
        /// Switch this spawner between offline (auto-fill 7 in <see cref="Start"/>) and networked mode
        /// (server-driven backfill via <see cref="SpawnBots"/>/<see cref="DespawnExtraBots"/>). Calling
        /// this with <c>true</c> before <see cref="Start"/> runs is what NetworkGame uses to suppress the
        /// offline auto-spawn so it controls the count. Idempotent.
        /// </summary>
        public void SetNetworkedMode(bool networked) => _networkedMode = networked;

        private void Start()
        {
            // Offline: auto-fill the field exactly as before. Networked: NetworkGame owns the count and
            // will call SpawnBots/DespawnExtraBots, so we must NOT also auto-spawn here (would double up).
            if (spawnOnStart && !_networkedMode) SpawnAll();
        }

        public void SpawnAll()
        {
            SpawnInternal(botCount, firstRacerId);
        }

        /// <summary>
        /// Spawn <paramref name="count"/> additional bots (server-side networked backfill). Bots are
        /// appended to the field; their racer ids continue past the highest id this spawner has used so
        /// they never collide with existing racers. Returns the number actually spawned.
        /// </summary>
        public int SpawnBots(int count)
        {
            if (count <= 0) return 0;
            return SpawnInternal(count, NextRacerId());
        }

        /// <summary>
        /// Despawn up to <paramref name="count"/> of the most-recently-spawned bots (server-side, when a
        /// human joins and frees up a slot). Removes newest-first so the longest-lived bots stay. Returns
        /// the number actually removed.
        /// </summary>
        public int DespawnExtraBots(int count)
        {
            if (count <= 0) return 0;
            PruneSpawned();

            int removed = 0;
            for (int i = _spawned.Count - 1; i >= 0 && removed < count; i--)
            {
                BotController bot = _spawned[i];
                _spawned.RemoveAt(i);
                if (bot != null)
                {
                    // OnDisable unregisters the bot from RacerRegistry, so win/rank logic stops counting it.
                    Destroy(bot.gameObject);
                    removed++;
                }
            }
            return removed;
        }

        /// <summary>
        /// Shared spawn body for both the offline <see cref="SpawnAll"/> and the networked
        /// <see cref="SpawnBots"/> path. <paramref name="count"/> bots are created starting at racer id
        /// <paramref name="startRacerId"/>. Returns the number actually spawned. Offline behaviour is
        /// identical to the original SpawnAll (called with botCount / firstRacerId).
        /// </summary>
        private int SpawnInternal(int count, int startRacerId)
        {
            if (botPrefab == null)
            {
                Debug.LogError("BotSpawner: botPrefab is not assigned.");
                return 0;
            }
            if (spawnPoints == null || spawnPoints.Length == 0)
            {
                Debug.LogError("BotSpawner: no spawn points assigned.");
                return 0;
            }

            // Only reset the shared name generator for a full field build (offline SpawnAll / the first
            // networked fill). Incremental backfill keeps the existing names unique by NOT resetting.
            if (_spawned.Count == 0)
                BotNameGenerator.Reset();

            // Difficulty (set in the main menu) picks the per-bot skill range. Skill drives agent
            // speed, charge aggression and hazard-dodge reliability, so this scales the whole field.
            BotDifficulty.SkillRange(out float skillMin, out float skillMax);

            // Reserve the player's spawn point. Spawning a bot on the exact same point as the
            // human player stacks two colliders; PhysX resolves the overlap with a violent
            // depenetration impulse that launches the player off the map at level start.
            // Build a usable list that excludes any point sitting on top of the player, so the
            // fix holds even in scenes saved before spawnPointOffset was set correctly.
            // Find the player by tag (robust to the registry being cleared on scene load),
            // falling back to the registry if the tag lookup misses.
            GameObject playerGo = GameObject.FindGameObjectWithTag(GameConstants.TagPlayer);
            Transform playerT = playerGo != null ? playerGo.transform : RacerRegistry.Player?.Transform;
            var usable = new List<Transform>(spawnPoints.Length);
            for (int s = 0; s < spawnPoints.Length; s++)
            {
                Transform sp = spawnPoints[s];
                if (sp == null) continue;
                if (playerT != null)
                {
                    Vector3 d = sp.position - playerT.position;
                    d.y = 0f;
                    if (d.sqrMagnitude < GameConstants.DefaultSpawnSeparation * GameConstants.DefaultSpawnSeparation)
                        continue; // too close to the player — skip it
                }
                usable.Add(sp);
            }
            if (usable.Count == 0) // degenerate: no points left, fall back to the originals
                for (int s = 0; s < spawnPoints.Length; s++)
                    if (spawnPoints[s] != null) usable.Add(spawnPoints[s]);

            // Offset the spawn-point walk by how many bots already exist so backfilled bots don't all
            // pile onto the same point as the first wave. Offline (first call) this is just spawnPointOffset.
            int pointBase = spawnPointOffset + _spawned.Count;

            int spawned = 0;
            int target = Mathf.Max(0, count);
            for (int i = 0; i < target; i++)
            {
                Transform sp = usable[(i + pointBase) % usable.Count];
                Vector3 pos = sp != null ? sp.position : transform.position;
                Quaternion rot = sp != null ? sp.rotation : Quaternion.identity;

                // Snap onto the baked NavMesh before instantiating. Spawn points sit ~0.6m above
                // the surface, which is too far for the bot's NavMeshAgent to auto-connect on
                // enable ("Failed to create agent because it is not close enough to the NavMesh"),
                // leaving bots frozen. Sampling lands them exactly on the mesh so the agent binds.
                if (NavMesh.SamplePosition(pos, out NavMeshHit navHit, 8f, NavMesh.AllAreas))
                    pos = navHit.position;

                GameObject go = Instantiate(botPrefab, pos, rot);
                BotController bot = go.GetComponent<BotController>();
                if (bot == null)
                {
                    Debug.LogError("BotSpawner: botPrefab missing BotController.");
                    Destroy(go);
                    continue;
                }

                bot.racerId = startRacerId + i;
                bot.displayName = BotNameGenerator.GetUnique();

                // Per-bot skill (0.35..1) drives both move speed and behavior aggression/reaction
                // so the 7 bots feel distinct and finishing order is earned, not arbitrary.
                float skill = Random.Range(skillMin, skillMax);
                float aggression = BotDifficulty.Aggression;
                bot.behavior = CreateBehavior(mode, skill);

                // Guarantee every bot has a non-null edge-recovery target for EVERY mode. Without
                // one, a bot shoved off the platform with no NavMesh within its scan radius just
                // falls to its death (Race/Survival bots had no anchor at all). Prefer the arena
                // centre, then the survival safe spot, then the finish line, then the spawner itself.
                bot.RecoveryAnchor = arenaCenter != null ? arenaCenter
                    : safeAnchor != null ? safeAnchor
                    : finishLine != null ? finishLine
                    : transform;

                if (bot.Agent != null)
                {
                    // Keep pace with the human: floor the base at the player's run speed (6) even for
                    // low-skill bots (they used to bottom out ~0.85x = slower, which read as sluggish),
                    // and let skill/aggression push them past it. Snappy accel + fast turning so they
                    // don't arc around lazily and feel slow.
                    bot.Agent.speed = GameConstants.DefaultMoveSpeed
                        * Mathf.Lerp(1.0f, 1.28f, skill)
                        * Mathf.Lerp(1f, 1.18f, aggression);
                    bot.Agent.acceleration = 40f;
                    bot.Agent.angularSpeed = 520f;
                }
                // Aggressive bots shove harder and far more often (half the cooldown, +30% force on Hard).
                bot.SetCombatTuning(Mathf.Lerp(1f, 0.5f, aggression), Mathf.Lerp(1f, 1.3f, aggression));

                go.name = "Bot_" + bot.displayName;
                _spawned.Add(bot);
                spawned++;
            }

            return spawned;
        }

        /// Drop destroyed/null bots from the tracking list (bots can die to a killzone independently of
        /// despawn). Cheap linear sweep — the field is tiny (≤ ~8).
        private void PruneSpawned()
        {
            for (int i = _spawned.Count - 1; i >= 0; i--)
                if (_spawned[i] == null) _spawned.RemoveAt(i);
        }

        /// Next free racer id: continues past the highest id this spawner has assigned so backfilled bots
        /// never reuse an id. Falls back to firstRacerId when the field is empty.
        private int NextRacerId()
        {
            PruneSpawned();
            int max = firstRacerId - 1;
            for (int i = 0; i < _spawned.Count; i++)
                if (_spawned[i] != null && _spawned[i].racerId > max) max = _spawned[i].racerId;
            return max + 1;
        }

        private BotBehavior CreateBehavior(LevelMode m, float skill)
        {
            switch (m)
            {
                case LevelMode.Race:
                    return new RaceBotBehavior(finishLine);
                case LevelMode.Survival:
                    return new SurvivalBotBehavior(safeAnchor);
                case LevelMode.LastStanding:
                    return new LastStandBotBehavior(arenaCenter, arenaRadius, skill: skill,
                        aggression: BotDifficulty.Aggression);
                default:
                    return new RaceBotBehavior(finishLine);
            }
        }
    }
}
