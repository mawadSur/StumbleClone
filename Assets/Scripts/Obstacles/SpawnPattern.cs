using System.Collections.Generic;
using UnityEngine;

namespace StumbleClone.Obstacles
{
    /// Eight discrete rim directions (octants), clockwise from North (+Z). Giving hazards a
    /// fixed vocabulary of directions — instead of a continuous random angle — is what makes
    /// the waves recognizable and learnable.
    public enum SpawnDirection { N, NE, E, SE, S, SW, W, NW }

    public static class ArenaDirections
    {
        public const int Count = 8;

        /// Unit offset for a direction. N=+Z, E=+X, S=-Z, W=-X, clockwise.
        public static Vector3 Offset(SpawnDirection dir)
        {
            float a = (int)dir * (Mathf.PI * 2f / Count);
            return new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
        }

        /// World point on the arena rim for a direction.
        public static Vector3 RimPoint(Vector3 center, float radius, SpawnDirection dir)
            => center + Offset(dir) * radius;

        public static SpawnDirection Opposite(SpawnDirection dir)
            => (SpawnDirection)(((int)dir + Count / 2) % Count);
    }

    /// One hazard in a wave: which rim direction, what type, and how long after the wave's
    /// spawn phase begins it appears.
    public struct SpawnEntry
    {
        public SpawnDirection dir;
        public ObstacleType type;
        public float delay;

        public SpawnEntry(SpawnDirection dir, ObstacleType type, float delay)
        {
            this.dir = dir;
            this.type = type;
            this.delay = delay;
        }
    }

    /// A named, recognizable wave. Build() fills an ordered list of entries; the scheduler
    /// telegraphs them, then spawns each at its delay. Difficulty `tier` (0..3) can add or
    /// tighten entries. Code-only (no ScriptableObject wiring) for MVP simplicity.
    public abstract class SpawnPattern
    {
        public abstract string Name { get; }

        /// Seconds the ground telegraph shows before the wave's hazards arrive.
        public virtual float TelegraphLead => 1.1f;

        public abstract void Build(List<SpawnEntry> into, int tier);
    }

    // ---- The six named patterns -------------------------------------------------

    /// Rams converge from all four cardinal directions at once; diagonals join at high tier.
    public sealed class CrossSweep : SpawnPattern
    {
        public override string Name => "Cross Sweep";
        public override void Build(List<SpawnEntry> into, int tier)
        {
            into.Add(new SpawnEntry(SpawnDirection.N, ObstacleType.SlidingRam, 0f));
            into.Add(new SpawnEntry(SpawnDirection.E, ObstacleType.SlidingRam, 0f));
            into.Add(new SpawnEntry(SpawnDirection.S, ObstacleType.SlidingRam, 0f));
            into.Add(new SpawnEntry(SpawnDirection.W, ObstacleType.SlidingRam, 0f));
            if (tier >= 2)
            {
                into.Add(new SpawnEntry(SpawnDirection.NE, ObstacleType.SlidingRam, 0.45f));
                into.Add(new SpawnEntry(SpawnDirection.SE, ObstacleType.SlidingRam, 0.45f));
                into.Add(new SpawnEntry(SpawnDirection.SW, ObstacleType.SlidingRam, 0.45f));
                into.Add(new SpawnEntry(SpawnDirection.NW, ObstacleType.SlidingRam, 0.45f));
            }
        }
    }

    /// Two opposite rams squeeze the arena; a second axis of bouncing balls follows.
    public sealed class Pincer : SpawnPattern
    {
        public override string Name => "Pincer";
        public override void Build(List<SpawnEntry> into, int tier)
        {
            into.Add(new SpawnEntry(SpawnDirection.E, ObstacleType.SlidingRam, 0f));
            into.Add(new SpawnEntry(SpawnDirection.W, ObstacleType.SlidingRam, 0f));
            if (tier >= 1)
            {
                into.Add(new SpawnEntry(SpawnDirection.N, ObstacleType.BouncingBall, 0.6f));
                into.Add(new SpawnEntry(SpawnDirection.S, ObstacleType.BouncingBall, 0.6f));
            }
        }
    }

    /// Boulders fire one octant at a time, sweeping clockwise — the most learnable pattern.
    public sealed class ClockwiseRotation : SpawnPattern
    {
        public override string Name => "Clockwise Rotation";
        public override float TelegraphLead => 0.9f;
        public override void Build(List<SpawnEntry> into, int tier)
        {
            float step = tier >= 2 ? 0.26f : 0.4f;
            for (int i = 0; i < ArenaDirections.Count; i++)
                into.Add(new SpawnEntry((SpawnDirection)i, ObstacleType.RollingBoulder, i * step));
        }
    }

    /// Boulders from a rotating octant with an accelerating cadence — a tightening spiral.
    public sealed class Spiral : SpawnPattern
    {
        public override string Name => "Spiral";
        public override void Build(List<SpawnEntry> into, int tier)
        {
            float t = 0f, gap = 0.5f;
            int steps = tier >= 2 ? 10 : 8;
            for (int i = 0; i < steps; i++)
            {
                into.Add(new SpawnEntry((SpawnDirection)((i * 3) % ArenaDirections.Count), ObstacleType.RollingBoulder, t));
                t += gap;
                gap *= 0.82f;
            }
        }
    }

    /// Bouncing balls rain in from announced octants in sequence.
    public sealed class Rain : SpawnPattern
    {
        public override string Name => "Rain";
        public override float TelegraphLead => 1.3f;
        public override void Build(List<SpawnEntry> into, int tier)
        {
            var ds = new[] { SpawnDirection.N, SpawnDirection.E, SpawnDirection.S, SpawnDirection.W };
            for (int i = 0; i < ds.Length; i++)
                into.Add(new SpawnEntry(ds[i], ObstacleType.BouncingBall, i * 0.3f));
            if (tier >= 2)
            {
                var dg = new[] { SpawnDirection.NE, SpawnDirection.SE, SpawnDirection.SW, SpawnDirection.NW };
                for (int i = 0; i < dg.Length; i++)
                    into.Add(new SpawnEntry(dg[i], ObstacleType.BouncingBall, 1.2f + i * 0.25f));
            }
        }
    }

    /// Step blocks rise on one side (forcing a climb) while a ram and a sweep press from elsewhere.
    public sealed class Gauntlet : SpawnPattern
    {
        public override string Name => "Gauntlet";
        public override float TelegraphLead => 1.4f;
        public override void Build(List<SpawnEntry> into, int tier)
        {
            into.Add(new SpawnEntry(SpawnDirection.W, ObstacleType.StepBlocks, 0f));
            into.Add(new SpawnEntry(SpawnDirection.E, ObstacleType.SlidingRam, 1.0f));
            if (tier >= 2)
                into.Add(new SpawnEntry(SpawnDirection.N, ObstacleType.SweepingBar, 0.5f));
        }
    }

    /// Picks a pattern for the current difficulty tier. Driven by a SEEDED RNG so the early
    /// sequence is identical every round (learnable); higher tiers unlock harder patterns.
    public static class PatternLibrary
    {
        public static SpawnPattern Select(int tier, System.Random rng)
        {
            // Pools are cumulative: each tier keeps the easier patterns and adds harder ones.
            int roll = rng.Next(100);
            switch (tier)
            {
                case 0:
                    return (roll < 60) ? new CrossSweep() : (SpawnPattern)new Rain();
                case 1:
                    return roll < 35 ? new CrossSweep()
                         : roll < 60 ? new Rain()
                         : roll < 80 ? new Pincer()
                         : new ClockwiseRotation();
                case 2:
                    return roll < 22 ? new CrossSweep()
                         : roll < 42 ? new Rain()
                         : roll < 60 ? new Pincer()
                         : roll < 80 ? new ClockwiseRotation()
                         : new Spiral();
                default: // tier 3+
                    return roll < 18 ? new Pincer()
                         : roll < 42 ? new ClockwiseRotation()
                         : roll < 66 ? new Spiral()
                         : roll < 84 ? new CrossSweep()
                         : new Gauntlet();
            }
        }
    }
}
