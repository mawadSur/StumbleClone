using StumbleClone.Core;
using UnityEngine;

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

            int spawned = 0;
            int target = Mathf.Max(0, botCount);
            for (int i = 0; i < target; i++)
            {
                Transform sp = spawnPoints[(i + spawnPointOffset) % spawnPoints.Length];
                Vector3 pos = sp != null ? sp.position : transform.position;
                Quaternion rot = sp != null ? sp.rotation : Quaternion.identity;

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
                float skill = Random.Range(0.35f, 1f);
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
                    return new LastStandBotBehavior(arenaCenter, arenaRadius, skill: skill);
                default:
                    return new RaceBotBehavior(finishLine);
            }
        }
    }
}
