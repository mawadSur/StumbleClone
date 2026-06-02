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

            // Centered rounded card. Tall enough that the title, control list, goal line and
            // button each get their own horizontal band with clear gaps (laid out top→bottom
            // below; card spans y ∈ [-450, 450] in its own space).
            var card = RuntimeUI.Panel(_overlay.transform, "Card", UITheme.Surface,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-460f, -450f), new Vector2(460f, 450f));
            card.sprite = UITheme.RoundedSprite();
            card.type = UnityEngine.UI.Image.Type.Sliced;

            // Title band: top of card, y ≈ [300, 390].
            var title = RuntimeUI.Label(card.transform, "HOW TO PLAY", 68,
                new Vector2(0.5f, 1f), new Vector2(0f, -60f), new Vector2(840f, 90f));
            title.fontStyle = FontStyles.Bold;
            title.color = UITheme.Gold;

            // Control list band: card centre, top edge y ≈ 205, flows down ~6–8 lines to ≈ -130.
            // Rows are short one-liners (no wrapping) so the block height is predictable and stays
            // clear of the goal line below.
            var controls = RuntimeUI.Label(card.transform, BuildControlsText(), 34,
                new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(820f, 330f),
                TextAlignmentOptions.Top);
            controls.color = UITheme.OnSurface;
            controls.richText = true;

            // Goal band: above the button, top edge y ≈ -209 — clears the controls above.
            var goal = RuntimeUI.Label(card.transform,
                "Goal: Be the <b>LAST one standing</b> — don't fall off, and stay inside the safe zone.",
                30, new Vector2(0.5f, 0f), new Vector2(0f, 165f), new Vector2(820f, 76f),
                TextAlignmentOptions.Top);
            goal.color = UITheme.OnSurfaceMuted;
            goal.richText = true;

            // Button band: bottom of card, y ≈ [-395, -309] — clears the goal line above.
            RuntimeUI.Button(card.transform, "GOT IT!", UITheme.Primary,
                new Vector2(0.5f, 0f), new Vector2(0f, 55f), new Vector2(360f, 86f), Dismiss);

            OverlayIntro.Play(_overlay);
        }

        /// One control per line (action + keys), kept short so no row wraps at the card width.
        /// A single muted mobile note follows so touch players aren't left out.
        private static string BuildControlsText()
        {
            string muted = ColorUtility.ToHtmlStringRGB(UITheme.OnSurfaceMuted);
            string Row(string action, string keys) =>
                $"<b>{action}</b>   <color=#{muted}>{keys}</color>";

            string rows = string.Join("\n", new[]
            {
                Row("Move", "WASD / Left Stick"),
                Row("Look", "Mouse / Drag"),
                Row("Jump", "Space"),
                Row("Dash", "Double-tap Jump (in air)"),
                Row("Push", "Left-Click"),
            });

            // Blank line, then a compact mobile hint in muted text.
            return rows +
                $"\n\n<color=#{muted}>On mobile: left side moves, right side looks, on-screen buttons jump & push.</color>";
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
