using StumbleClone.Game;
using UnityEngine;

namespace StumbleClone.Visuals
{
    /// Applies a skin to a racer the instant it spawns, before its animator driver caches the
    /// Animator. Player mode uses the title-screen selection (SkinStore); RandomBot mode rolls a
    /// random catalog skin so the field looks varied. Runs very early via DefaultExecutionOrder so
    /// PlayerAnimator/BotAnimator (default order) bind to the swapped-in model.
    ///
    /// Added to Player.prefab / Bot.prefab by the SkinSetup editor tool.
    [DefaultExecutionOrder(-500)]
    public sealed class CharacterSkin : MonoBehaviour
    {
        public enum Mode { Player, RandomBot }

        [SerializeField] private Mode mode = Mode.Player;

        private void Awake()
        {
            try
            {
                string id = (mode == Mode.Player) ? SkinStore.Current : RollRandom();
                // Default player skin is already baked into the prefab — no need to swap.
                if (string.IsNullOrEmpty(id)) return;
                if (mode == Mode.Player && id == SkinCatalog.Default) return;
                SkinSwapper.Apply(transform, id);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[CharacterSkin] swap failed, keeping default model: " + e.Message);
            }
        }

        // Vary by instance id so we don't need Random (which is fine here, but this avoids any
        // determinism concerns and spreads bots across the catalog).
        private static string RollRandom()
        {
            int n = SkinCatalog.Ids.Length;
            if (n == 0) return null;
            int i = Random.Range(0, n);
            return SkinCatalog.Ids[i];
        }
    }
}
