using StumbleClone.Core;
using StumbleClone.Player;
using UnityEngine;

namespace StumbleClone.Game
{
    /// Applies the player's equipped perk at the start of every round. Self-bootstrapping: subscribes
    /// once to GameEvents.LevelStarted (which the mode managers raise after racers spawn), then drives
    /// the perk through PlayerController's existing public buff APIs — so no movement code changes and
    /// no scene wiring. A long buff duration makes the perk effectively last the whole round.
    public static class AbilityApplier
    {
        private const float RoundDuration = 100000f; // "permanent" for the round
        private static bool _hooked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            GameEvents.LevelStarted -= OnLevelStarted; // idempotent
            GameEvents.LevelStarted += OnLevelStarted;
        }

        private static void OnLevelStarted(LevelMode mode)
        {
            var player = Object.FindAnyObjectByType<PlayerController>();
            if (player == null) return;

            switch (AbilityStore.EffectOf(AbilityStore.EquippedPerk))
            {
                case PerkEffect.Speed: player.ApplySpeedBoost(1.12f, RoundDuration); break;
                case PerkEffect.Jump:  player.GrantJumpBoost(1.30f, RoundDuration); break;
                case PerkEffect.Shield: player.GrantShield(); break;
                case PerkEffect.None:
                default: break;
            }
        }
    }
}
