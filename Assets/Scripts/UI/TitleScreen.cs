using StumbleClone.Core;
using StumbleClone.Game;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StumbleClone.UI
{
    /// A real title gate shown over the MainMenu: branded title, name entry, and a START button
    /// that saves the player's name (for the leaderboard) and reveals the menu beneath. Self-
    /// instantiates whenever MainMenu loads — no scene wiring, no rebuild required.
    public sealed class TitleScreen : MonoBehaviour
    {
        private const string MenuScene = "MainMenu";
        private TMP_InputField _nameInput;
        private TMP_Text _difficultyLabel;
        private TMP_Text _skinLabel;
        private GameObject _overlay;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            if (SceneManager.GetActiveScene().name == MenuScene) Create();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == MenuScene) Create();
        }

        private static void Create()
        {
            if (FindFirstObjectByType<TitleScreen>() != null) return;
            new GameObject("TitleScreen").AddComponent<TitleScreen>();
        }

        private void Start()
        {
            _overlay = RuntimeUI.Overlay("TitleOverlay", 100);
            var bg = RuntimeUI.Panel(_overlay.transform, "Bg", UITheme.SurfaceDeep,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            var title = RuntimeUI.Label(bg.transform, "STUMBLE CLONE", 120,
                new Vector2(0.5f, 0.82f), Vector2.zero, new Vector2(1500f, 200f));
            title.fontStyle = FontStyles.Bold;
            title.color = UITheme.Gold;

            RuntimeUI.Label(bg.transform, "Knockout party arena — last one standing wins", 38,
                new Vector2(0.5f, 0.71f), Vector2.zero, new Vector2(1400f, 80f));

            // Name entry: label sits just above its input box. (Anchors below are tuned so the
            // input, SKIN, BOTS, PLAY and LEADERBOARD bands never overlap at the 1080 reference.)
            RuntimeUI.Label(bg.transform, "YOUR NAME", 28,
                new Vector2(0.5f, 0.63f), Vector2.zero, new Vector2(600f, 46f));
            _nameInput = RuntimeUI.InputField(bg.transform, "Player", LeaderboardStore.GetPlayerName(),
                new Vector2(0.5f, 0.63f), new Vector2(0f, -56f), new Vector2(470f, 64f));

            // Skin — tap to cycle the player's character. Persisted; applied on spawn.
            var skinBtn = RuntimeUI.Button(bg.transform, "SKIN: " + SkinCatalog.DisplayFor(SkinStore.Current),
                UITheme.Secondary,
                new Vector2(0.5f, 0.475f), Vector2.zero, new Vector2(460f, 64f), OnCycleSkin);
            _skinLabel = skinBtn.GetComponentInChildren<TMP_Text>();

            // Bot difficulty — tap to cycle Easy / Normal / Hard. Persisted for every round.
            var diffBtn = RuntimeUI.Button(bg.transform, "BOTS: " + BotDifficulty.Label,
                UITheme.Neutral,
                new Vector2(0.5f, 0.39f), Vector2.zero, new Vector2(460f, 64f), OnCycleDifficulty);
            _difficultyLabel = diffBtn.GetComponentInChildren<TMP_Text>();

            // PLAY drops straight into the deathmatch (the focused mode) — no second menu.
            // Single primary CTA (pink); leaderboard is the subordinate secondary action (purple).
            RuntimeUI.Button(bg.transform, "PLAY", UITheme.Primary,
                new Vector2(0.5f, 0.27f), Vector2.zero, new Vector2(440f, 92f), OnPlay);
            RuntimeUI.Button(bg.transform, "LEADERBOARD", UITheme.Secondary,
                new Vector2(0.5f, 0.13f), Vector2.zero, new Vector2(440f, 66f), OnLeaderboard);

            OverlayIntro.Play(_overlay);
        }

        private void OnCycleDifficulty()
        {
            BotDifficulty.Cycle();
            if (_difficultyLabel != null) _difficultyLabel.text = "BOTS: " + BotDifficulty.Label;
        }

        private void OnCycleSkin()
        {
            string next = SkinCatalog.Next(SkinStore.Current);
            SkinStore.Current = next;
            if (_skinLabel != null) _skinLabel.text = "SKIN: " + SkinCatalog.DisplayFor(next);
        }

        private void OnPlay()
        {
            if (_nameInput != null) LeaderboardStore.SetPlayerName(_nameInput.text);
            if (GameManager.Instance != null) GameManager.Instance.LoadLevel(LevelMode.LastStanding);
            else if (_overlay != null) _overlay.SetActive(false); // editor-direct fallback
        }

        private void OnLeaderboard()
        {
            if (LeaderboardUI.Instance != null) LeaderboardUI.Instance.Open();
        }
    }
}
