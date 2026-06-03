namespace StumbleClone.Core
{
    /// Single source of truth for layer/tag IDs and tuning knobs.
    /// Layers and Tags must be configured manually in Edit > Project Settings > Tags & Layers
    /// (the scaffolding doc lists the required setup).
    public static class GameConstants
    {
        // Layers (must match Project Settings)
        public const int LayerPlayer = 8;
        public const int LayerBot = 9;
        public const int LayerObstacle = 10;
        public const int LayerGround = 11;
        public const int LayerKillzone = 12;

        // Tags
        public const string TagPlayer = "Player";
        public const string TagBot = "Bot";
        public const string TagFinish = "Finish";
        public const string TagKillzone = "Killzone";
        public const string TagPushPad = "PushPad";
        public const string TagRespawnPoint = "Respawn";

        // Racer tuning
        public const float DefaultMoveSpeed = 6f;
        public const float DefaultJumpSpeed = 8f;
        public const float DefaultPushForce = 12f;
        public const float DefaultPushRange = 1.4f;
        public const float DefaultPushCooldown = 0.8f;
        public const float KnockbackUpward = 4f;

        // Air dash (triggered by a second jump press while airborne)
        public const float DefaultDashSpeed = 18f;     // planar burst speed
        public const float DefaultDashDuration = 0.18f; // gravity-cancelled window
        public const float DefaultSpawnSeparation = 1.5f; // min XZ gap between a bot spawn and the player

        // Bot tuning
        public const int DefaultBotsPerLevel = 7;
        public const float BotPathRefreshRate = 0.5f;

        // World
        public const float WorldKillY = -25f; // anything below this Y is eliminated/respawned by mode

        // Economy (token rewards). Tokens are earned per round and spent in the title-screen shop.
        public const int TokensForWin = 100;    // awarded when the human player wins the round
        public const int TokensForFinish = 25;  // consolation: player took part but didn't win

        // Bot edge-recovery: if a knocked-off bot can't get back onto the NavMesh within this many
        // seconds it is hard-warped to the nearest mesh around its RecoveryAnchor, so it never
        // freezes off-mesh or rides physics off the map (the "bots stand still / die randomly" bug).
        public const float BotRecoveryTimeout = 4f;
    }
}
