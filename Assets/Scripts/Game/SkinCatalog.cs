namespace StumbleClone.Game
{
    /// The selectable character skins. Each entry maps to a Quaternius model that shares the common
    /// "CharacterArmature" skeleton, so every skin is driven by the same locomotion controller and
    /// animates identically. Skin prefabs are generated into Resources/Skins/&lt;id&gt; by the editor
    /// tool SkinSetup; the runtime loads them by id.
    public static class SkinCatalog
    {
        /// Resource ids — must match the FBX base name SkinSetup builds a prefab from.
        public static readonly string[] Ids =
        {
            "BlueSoldier_Male",
            "Casual_Male",
            "Chef_Male",
            "Cowboy_Male",
            "Knight_Male",
            "Ninja_Male",
            "Goblin_Male",
            "Elf",
        };

        public static readonly string[] DisplayNames =
        {
            "Blue Soldier",
            "Casual",
            "Chef",
            "Cowboy",
            "Knight",
            "Ninja",
            "Goblin",
            "Elf",
        };

        /// The player's starting skin (also the model already baked into Player.prefab, so the
        /// default path requires no swap).
        public static string Default => Ids[0];

        public static int IndexOf(string id)
        {
            for (int i = 0; i < Ids.Length; i++)
                if (Ids[i] == id) return i;
            return 0;
        }

        public static string DisplayFor(string id) => DisplayNames[IndexOf(id)];

        /// Cycle to the next skin id (wraps), for a tap-to-change button.
        public static string Next(string id)
        {
            int i = (IndexOf(id) + 1) % Ids.Length;
            return Ids[i];
        }
    }
}
