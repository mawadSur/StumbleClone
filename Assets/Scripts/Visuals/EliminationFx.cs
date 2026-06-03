using StumbleClone.Core;
using UnityEngine;

namespace StumbleClone.Visuals
{
    /// Self-bootstrapping glue that turns every racer elimination into a smoke <see cref="ImpactPuff"/>.
    ///
    /// There is nothing to wire in the scene: a <see cref="RuntimeInitializeOnLoadMethodAttribute"/>
    /// subscribes this once to <see cref="GameEvents.RacerEliminated"/> at startup. On each
    /// elimination it spawns a greyish-white puff slightly above the racer's feet, so every bot
    /// (and the player) "poofs" into a cloud when knocked out.
    ///
    /// The subscription is idempotent (it always unsubscribes before subscribing) so it stays
    /// healthy across scene loads and domain reloads without ever double-firing or leaking.
    public static class EliminationFx
    {
        // How far above the racer's transform origin to place the puff. The racer's Transform
        // sits at the feet/base, so lift the cloud to roughly torso height for a readable poof.
        private const float PuffHeight = 0.9f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            // Unsubscribe-then-subscribe: safe to run again after a scene load or a domain reload
            // (when "Reload Domain" is off the static stays alive, so this avoids a double hookup).
            GameEvents.RacerEliminated -= OnRacerEliminated;
            GameEvents.RacerEliminated += OnRacerEliminated;
        }

        private static void OnRacerEliminated(IRacer racer)
        {
            // Guard against a null racer or one whose GameObject was already destroyed this frame
            // (Unity's overloaded == makes a destroyed Transform compare equal to null).
            if (racer == null) return;
            Transform t = racer.Transform;
            if (t == null) return;

            ImpactPuff.Spawn(t.position + Vector3.up * PuffHeight);
        }
    }
}
