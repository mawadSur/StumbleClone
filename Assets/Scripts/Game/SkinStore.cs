using UnityEngine;

namespace StumbleClone.Game
{
    /// The player's chosen skin id, persisted in PlayerPrefs so it survives scene loads and
    /// sessions. Set from the title screen; read by CharacterSkin when the player spawns.
    public static class SkinStore
    {
        private const string Key = "stumbleclone.skin";

        public static string Current
        {
            get
            {
                string id = PlayerPrefs.GetString(Key, SkinCatalog.Default);
                // Guard against an id left over from a removed catalog entry.
                return SkinCatalog.IndexOf(id) >= 0 ? id : SkinCatalog.Default;
            }
            set { PlayerPrefs.SetString(Key, value); PlayerPrefs.Save(); }
        }
    }
}
