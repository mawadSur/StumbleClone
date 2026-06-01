using System.Collections;
using System.Text;
using StumbleClone.CameraRig;
using StumbleClone.Core;
using StumbleClone.Game;
using StumbleClone.Player;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StumbleClone.UI
{
    /// When the HUMAN player wins, this takes over: it freezes the winner and plays a looping
    /// victory dance, freezes the camera on them, and shows a VICTORY overlay with the player's
    /// position on the mode leaderboard plus the standings. Self-bootstrapping (no scene wiring);
    /// EndScreenUI defers to it on a player win so the two screens don't both appear.
    public sealed class VictoryScreen : MonoBehaviour
    {
        private static VictoryScreen _instance;
        private GameObject _overlay;
        private PlayerAnimator _danceAnim;
        private bool _shown;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            EnsureForScene(SceneManager.GetActiveScene());
        }

        private static void OnSceneLoaded(Scene s, LoadSceneMode m) => EnsureForScene(s);

        private static void EnsureForScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.name.StartsWith("Level")) return;
            if (FindAnyObjectByType<VictoryScreen>() != null) return;
            new GameObject("VictoryScreen").AddComponent<VictoryScreen>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
        }

        private void OnEnable() => GameEvents.LevelEnded += HandleLevelEnded;

        private void OnDisable()
        {
            GameEvents.LevelEnded -= HandleLevelEnded;
            if (_instance == this) _instance = null;
        }

        private void HandleLevelEnded(IRacer winner)
        {
            if (_shown || winner == null || !winner.IsPlayer) return; // only when the human wins
            _shown = true;

            Transform t = winner.Transform;

            // Stop the winner moving (disable control + kill momentum) so they dance in place.
            var pc = t.GetComponent<PlayerController>();
            if (pc != null) pc.enabled = false;
            var rb = t.GetComponent<Rigidbody>();
            if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }

            _danceAnim = t.GetComponent<PlayerAnimator>();
            if (_danceAnim != null) _danceAnim.SetVictory(true);

            // Freeze the camera on the winner for a stable hero shot (and stop mouse-look spinning
            // while the cursor is free for the buttons).
            var cam = FindAnyObjectByType<ThirdPersonCamera>();
            if (cam != null) cam.enabled = false;

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            StartCoroutine(BuildSoon());
        }

        private IEnumerator BuildSoon()
        {
            yield return null; // let GameManager submit this run's leaderboard entry first
            BuildUI();
        }

        private LevelMode Mode()
        {
            if (GameManager.Instance != null) return GameManager.Instance.currentMode;
            if (LevelSelfStart.Active != null) return LevelSelfStart.Active.Mode;
            return LevelMode.LastStanding;
        }

        private void BuildUI()
        {
            _overlay = RuntimeUI.Overlay("VictoryOverlay", 60);

            // Dim backdrop — keeps the dancing winner visible behind the text.
            RuntimeUI.Panel(_overlay.transform, "Dim", new Color(0.04f, 0.05f, 0.10f, 0.55f),
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            var title = RuntimeUI.Label(_overlay.transform, "VICTORY!", 130,
                new Vector2(0.5f, 0.88f), Vector2.zero, new Vector2(1400f, 200f));
            title.fontStyle = FontStyles.Bold;
            title.color = UITheme.Gold;

            LevelMode mode = Mode();
            string me = LeaderboardStore.GetPlayerName();
            var top = LeaderboardStore.GetTop(mode, 8);

            int pos = 0;
            for (int i = 0; i < top.Count; i++)
                if (top[i] != null && top[i].playerName == me) { pos = i + 1; break; }

            string posLine = pos > 0
                ? $"You're #{pos} on the {mode} leaderboard!"
                : $"You won the {mode}!";
            RuntimeUI.Label(_overlay.transform, posLine, 44,
                new Vector2(0.5f, 0.76f), Vector2.zero, new Vector2(1500f, 70f));

            var sb = new StringBuilder();
            if (top.Count == 0)
                sb.Append("No scores yet — you're the first!");
            for (int i = 0; i < top.Count; i++)
            {
                var e = top[i];
                if (e == null) continue;
                string row = $"{i + 1,2}.  {Trunc(e.playerName, 14),-14}  {Mathf.RoundToInt(e.score)}";
                bool mine = e.playerName == me && (i + 1) == pos;
                sb.Append(mine ? $"<color=#FFD24D>{row}</color>" : row);
                sb.Append('\n');
            }
            var list = RuntimeUI.Label(_overlay.transform, sb.ToString(), 38,
                new Vector2(0.5f, 0.45f), Vector2.zero, new Vector2(1000f, 540f));
            list.richText = true;

            RuntimeUI.Button(_overlay.transform, "PLAY AGAIN", UITheme.Primary,
                new Vector2(0.5f, 0.12f), new Vector2(-240f, 0f), new Vector2(440f, 92f), OnPlayAgain);
            RuntimeUI.Button(_overlay.transform, "MAIN MENU", UITheme.Neutral,
                new Vector2(0.5f, 0.12f), new Vector2(240f, 0f), new Vector2(440f, 92f), OnMenu);

            OverlayIntro.Play(_overlay);
        }

        private static string Trunc(string s, int n)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= n ? s : s.Substring(0, n);
        }

        private void OnPlayAgain()
        {
            if (_danceAnim != null) _danceAnim.SetVictory(false);
            if (GameManager.Instance != null) GameManager.Instance.LoadLevel(GameManager.Instance.currentMode);
            else SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        private void OnMenu()
        {
            if (_danceAnim != null) _danceAnim.SetVictory(false);
            if (GameManager.Instance != null) GameManager.Instance.ReturnToMenu();
            else SceneManager.LoadScene("MainMenu");
        }
    }
}
