using System.Text;
using StumbleClone.Core;
using StumbleClone.Game;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StumbleClone.UI
{
    /// A leaderboard button on the MainMenu that opens a panel of top local scores per mode,
    /// read from LeaderboardStore. Self-instantiates on MainMenu load — no scene wiring/rebuild.
    public sealed class LeaderboardUI : MonoBehaviour
    {
        public static LeaderboardUI Instance { get; private set; }

        private const string MenuScene = "MainMenu";
        private GameObject _panel;
        private TMP_Text _listText;
        private TMP_Text _tabLabel;
        private LevelMode _mode = LevelMode.LastStanding;

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
            if (FindFirstObjectByType<LeaderboardUI>() != null) return;
            new GameObject("LeaderboardUI").AddComponent<LeaderboardUI>();
        }

        private void Awake() => Instance = this;
        private void OnDestroy() { if (Instance == this) Instance = null; }

        public void Open()
        {
            if (_panel == null) Build();
            _panel.SetActive(true);
            Refresh();
        }

        private void Close()
        {
            if (_panel != null) _panel.SetActive(false);
        }

        private void Build()
        {
            _panel = RuntimeUI.Overlay("LeaderboardPanel", 110);
            var bg = RuntimeUI.Panel(_panel.transform, "Bg", new Color(0.06f, 0.07f, 0.12f, 0.97f),
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            RuntimeUI.Label(bg.transform, "LEADERBOARD", 72,
                new Vector2(0.5f, 0.92f), Vector2.zero, new Vector2(900f, 110f)).fontStyle = FontStyles.Bold;

            RuntimeUI.Button(bg.transform, "Race", new Color(0.25f, 0.3f, 0.42f),
                new Vector2(0.5f, 0.82f), new Vector2(-300f, 0f), new Vector2(260f, 64f), () => SetMode(LevelMode.Race));
            RuntimeUI.Button(bg.transform, "Survival", new Color(0.25f, 0.3f, 0.42f),
                new Vector2(0.5f, 0.82f), new Vector2(0f, 0f), new Vector2(260f, 64f), () => SetMode(LevelMode.Survival));
            RuntimeUI.Button(bg.transform, "Knockout", new Color(0.25f, 0.3f, 0.42f),
                new Vector2(0.5f, 0.82f), new Vector2(300f, 0f), new Vector2(260f, 64f), () => SetMode(LevelMode.LastStanding));

            _tabLabel = RuntimeUI.Label(bg.transform, "", 40, new Vector2(0.5f, 0.74f), Vector2.zero, new Vector2(900f, 60f));

            _listText = RuntimeUI.Label(bg.transform, "", 36, new Vector2(0.5f, 0.42f), Vector2.zero, new Vector2(1000f, 560f));
            _listText.alignment = TextAlignmentOptions.Top;

            RuntimeUI.Button(bg.transform, "Back", new Color(0.5f, 0.2f, 0.2f),
                new Vector2(0.5f, 0.08f), Vector2.zero, new Vector2(300f, 80f), Close);
        }

        private void SetMode(LevelMode m) { _mode = m; Refresh(); }

        private void Refresh()
        {
            if (_tabLabel != null) _tabLabel.text = ModeName(_mode);
            if (_listText == null) return;

            var top = LeaderboardStore.GetTop(_mode, 10);
            if (top.Count == 0) { _listText.text = "No scores yet — play a round!"; return; }

            var sb = new StringBuilder();
            for (int i = 0; i < top.Count; i++)
            {
                var e = top[i];
                sb.AppendLine($"{i + 1,2}.  {Trunc(e.playerName, 14),-14}  {Mathf.RoundToInt(e.score),8}");
            }
            _listText.text = sb.ToString();
        }

        private static string ModeName(LevelMode m) =>
            m == LevelMode.Race ? "Race — fastest run"
            : m == LevelMode.Survival ? "Survival — longest alive"
            : "Knockout — best placement";

        private static string Trunc(string s, int n) =>
            string.IsNullOrEmpty(s) ? "?" : (s.Length <= n ? s : s.Substring(0, n));
    }
}
