using System.Collections.Generic;

namespace StumbleClone.Core
{
    /// Lightweight registry — managers iterate this each frame to count alive/finished racers
    /// and to assign ranks. Player and bots self-register in OnEnable / unregister in OnDisable.
    public static class RacerRegistry
    {
        private static readonly List<IRacer> _all = new List<IRacer>(16);

        public static IReadOnlyList<IRacer> All => _all;

        public static void Register(IRacer racer)
        {
            if (racer == null || _all.Contains(racer)) return;
            _all.Add(racer);
        }

        public static void Unregister(IRacer racer)
        {
            _all.Remove(racer);
        }

        public static void Clear() => _all.Clear();

        public static int AliveCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _all.Count; i++) if (_all[i].IsAlive) n++;
                return n;
            }
        }

        public static IRacer Player
        {
            get
            {
                for (int i = 0; i < _all.Count; i++) if (_all[i].IsPlayer) return _all[i];
                return null;
            }
        }
    }
}
