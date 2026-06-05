namespace StumbleClone.Core
{
    /// Single source of truth for "did the human player win this round?". Both GameManager
    /// (token payout + Token Doubler consume) and QuestSystem (RoundsWon) must agree on this.
    /// Computing it independently in two files risks silent drift — a future tie / team / spectated-
    /// winner rule could update one copy and not the other, paying a doubler on a non-win or crediting
    /// a win quest on a loss. Centralised here so there is exactly one definition.
    public static class RoundOutcome
    {
        /// True iff <paramref name="winner"/> is the registered human player. Null-safe: a bot-won or
        /// no-winner round returns false.
        public static bool PlayerWon(IRacer winner)
        {
            var player = RacerRegistry.Player;
            return winner != null && player != null && ReferenceEquals(winner, player);
        }
    }
}
