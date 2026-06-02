using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StumbleClone.UI
{
    /// First-run onboarding card shown over the MainMenu the very first time the game is
    /// launched. Teaches both keyboard/mouse and touch controls, then dismisses for good once
    /// the player taps "GOT IT!". Sorts above the TitleScreen (sortingOrder 100) so it lands on
    /// top; dismissing it reveals the title beneath. Self-instantiates on MainMenu load — no
    /// scene wiring, no rebuild, and it never touches TitleScreen.
    public sealed class TutorialOverlay : MonoBehaviour
    {
        private const string MenuScene = "MainMenu";

        /// PlayerPrefs flag: set to 1 once the tutorial has been seen so it never shows again.
        private const string SeenKey = "stumbleclone.tutorialSeen";

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
            // Only ever show on the first launch; respect the persisted flag.
            if (PlayerPrefs.GetInt(SeenKey, 0) == 1) return;
            if (FindFirstObjectByType<TutorialOverlay>() != null) return;
            new GameObject("TutorialOverlay").AddComponent<TutorialOverlay>();
        }

        private void Start() => Build();

        private void Build()
        {
            // Sort above the title (100) so the tutorial sits on top on first run.
            _overlay = RuntimeUI.Overlay("TutorialOverlay", 200);

            // Dim full-screen backdrop — darkens the title beneath without fully hiding it.
            RuntimeUI.Panel(_overlay.transform, "Backdrop", new Color(0f, 0f, 0f, 0.72f),
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            // Centered rounded card. Taller than wide so the title, control list, goal line and
            // button each get their own band with no overlap (regions laid out top→bottom below).
            var card = RuntimeUI.Panel(_overlay.transform, "Card", UITheme.Surface,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-460f, -410f), new Vector2(460f, 410f));
            card.sprite = UITheme.RoundedSprite();
            card.type = UnityEngine.UI.Image.Type.Sliced;

            // Title band: top of card, y ≈ [250, 340] in card space.
            var title = RuntimeUI.Label(card.transform, "HOW TO PLAY", 68,
                new Vector2(0.5f, 1f), new Vector2(0f, -70f), new Vector2(840f, 90f));
            title.fontStyle = FontStyles.Bold;
            title.color = UITheme.Gold;

            // Control list band: card centre, y ≈ [-150, 210] — clears the title above.
            var controls = RuntimeUI.Label(card.transform, BuildControlsText(), 34,
                new Vector2(0.5f, 0.5f), new Vector2(0f, 30f), new Vector2(820f, 360f),
                TextAlignmentOptions.Top);
            controls.color = UITheme.OnSurface;
            controls.richText = true;

            // Goal band: above the button, y ≈ [-240, -160] — clears the controls above.
            var goal = RuntimeUI.Label(card.transform,
                "Goal: Be the <b>LAST one standing</b> — don't fall off, and stay inside the safe zone.",
                30, new Vector2(0.5f, 0f), new Vector2(0f, 170f), new Vector2(820f, 80f),
                TextAlignmentOptions.Top);
            goal.color = UITheme.OnSurfaceMuted;
            goal.richText = true;

            // Button band: bottom of card, y ≈ [-350, -266] — clears the goal line above.
            RuntimeUI.Button(card.transform, "GOT IT!", UITheme.Primary,
                new Vector2(0.5f, 0f), new Vector2(0f, 60f), new Vector2(360f, 84f), Dismiss);

            OverlayIntro.Play(_overlay);
        }

        /// One control per line: action, then keyboard/mouse, then the touch equivalent.
        private static string BuildControlsText()
        {
            string muted = ColorUtility.ToHtmlStringRGB(UITheme.OnSurfaceMuted);
            string Row(string action, string keys) =>
                $"<b>{action}</b>   <color=#{muted}>{keys}</color>";

            return string.Join("\n\n", new[]
            {
                Row("Move", "WASD / left stick  (left side of screen on mobile)"),
                Row("Look", "Mouse / drag  (right side on mobile)"),
                Row("Jump", "Space / jump button"),
                Row("Dash", "double-tap jump in the air"),
                Row("Push", "Left-click / push button"),
            });
        }

        /// Hide the overlay and persist the "seen" flag so it never appears again.
        private void Dismiss()
        {
            PlayerPrefs.SetInt(SeenKey, 1);
            PlayerPrefs.Save();
            // Destroy (not just deactivate) the overlay canvas so it doesn't linger as a dead,
            // input-blocking GraphicRaycaster over the menu after the card is dismissed.
            if (_overlay != null) Destroy(_overlay);
            Destroy(gameObject);
        }
    }
}
