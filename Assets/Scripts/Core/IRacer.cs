using System;
using UnityEngine;

namespace StumbleClone.Core
{
    /// Shared contract for anything that can race / be eliminated.
    /// Implemented by PlayerController and BotController.
    public interface IRacer
    {
        int RacerId { get; }
        string DisplayName { get; }
        Transform Transform { get; }
        bool IsAlive { get; }
        bool IsFinished { get; }
        bool IsPlayer { get; }

        void Knockback(Vector3 force);
        void Eliminate();
        void Finish();
        void Respawn(Vector3 position);

        event Action<IRacer> OnFinished;
        event Action<IRacer> OnEliminated;
    }
}
