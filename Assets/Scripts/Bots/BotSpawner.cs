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

        private void Start()
        {
            if (spawnOnStart) SpawnAll();
        }

        public void SpawnAll()
        {
            if (botPrefab == null)
            {
                Debug.LogError("BotSpawner: botPrefab is not assigned.");
                return;
            }
            if (spawnPoints == null || spawnPoints.Length == 0)
            {
                Debug.LogError("BotSpawner: no spawn points assigned.");
                return;
            }

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
            var usable = new System.Collections.Generic.List<Transform>(spawnPoints.Length);
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

            int spawned = 0;
            int target = Mathf.Max(0, botCount);
            for (int i = 0; i < target; i++)
            {
                Transform sp = usable[(i + spawnPointOffset) % usable.Count];
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

                bot.racerId = firstRacerId + i;
                bot.displayName = BotNameGenerator.GetUnique();

                // Per-bot skill (0.35..1) drives both move speed and behavior aggression/reaction
                // so the 7 bots feel distinct and finishing order is earned, not arbitrary.
                float skill = Random.Range(skillMin, skillMax);
                bot.behavior = CreateBehavior(mode, skill);
                if (bot.Agent != null)
                    bot.Agent.speed = GameConstants.DefaultMoveSpeed * Mathf.Lerp(0.85f, 1.15f, skill);

                go.name = "Bot_" + bot.displayName;
                spawned++;
            }
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
