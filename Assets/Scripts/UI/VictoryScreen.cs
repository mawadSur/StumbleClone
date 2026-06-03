using System.Collections;
using System.Text;
using StumbleClone.Audio;
using StumbleClone.CameraRig;
using StumbleClone.Core;
using StumbleClone.Game;
using StumbleClone.Player;
using StumbleClone.Visuals;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
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
        private Transform _winner;       // the dancing winner — confetti rains above their head
        private bool _shown;

        // ---- Confetti shower tuning (celebratory; cheap multi-color bursts) ----
        private const float ConfettiHeadHeight = 2.6f;   // burst height above the winner's feet
        private const float ConfettiSpreadX = 1.6f;      // horizontal scatter of burst points
        private const int ConfettiBursts = 5;            // number of staggered bursts
        private const float ConfettiBurstGap = 0.35f;    // seconds between bursts
        private const float ConfettiStartDelay = 0.25f;  // wait for the dance to kick in first

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
            _winner = t;

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
            StartCoroutine(ConfettiShower());
        }

        /// A short, celebratory confetti shower over the dancing winner: a few staggered, bright
        /// multi-color bursts above their head. Cheap by construction (ImpactPuff uses collider-less
        /// fading meshes, not a ParticleSystem) and ReducedMotion-aware — a single gentle burst when
        /// the setting is on. Uses unscaled time so it still plays if the game is time-paused.
        private IEnumerator ConfettiShower()
        {
            if (_winner == null) yield break;

            bool reduced = SettingsStore.ReducedMotion;
            int bursts = reduced ? 1 : ConfettiBursts;

            yield return new WaitForSecondsRealtime(ConfettiStartDelay);

            for (int i = 0; i < bursts; i++)
            {
                if (_winner == null) yield break; // winner may have been torn down (level reload)

                // Scatter each burst a little around the head so the shower fills the hero shot.
                Vector3 head = _winner.position + Vector3.up * ConfettiHeadHeight;
                if (!reduced)
                    head += new Vector3(Random.Range(-ConfettiSpreadX, ConfettiSpreadX), 0f,
                                        Random.Range(-ConfettiSpreadX, ConfettiSpreadX));

                ImpactPuff.Confetti(head, 1f);

                if (i < bursts - 1) yield return new WaitForSecondsRealtime(ConfettiBurstGap);
            }
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

            // Gold NEW BEST! badge between the title and the position line when this winning run
            // is the player's best ever for the mode.
            if (IsNewBest(mode, me))
            {
                var badge = RuntimeUI.Label(_overlay.transform, "NEW BEST!", 60,
                    new Vector2(0.5f, 0.83f), Vector2.zero, new Vector2(900f, 90f));
                badge.fontStyle = FontStyles.Bold;
                badge.color = UITheme.Gold;
            }

            int pos = 0;
            for (int i = 0; i < top.Count; i++)
                if (top[i] != null && top[i].playerName == me) { pos = i + 1; break; }

            string posLine = pos > 0
                ? $"You're #{pos} on the {mode} leaderboard!"
                : $"You won the {mode}!";
            RuntimeUI.Label(_overlay.transform, posLine, 44,
                new Vector2(0.5f, 0.76f), Vector2.zero, new Vector2(1500f, 70f));

            // Gold token payout line under the placement, with a quick count-up. Reads the actual
            // granted amount from the run result; if the Token Doubler fired, a smaller subline
            // spells out the base x2 math so the bonus is legible.
            var result = GameManager.Instance != null ? GameManager.Instance.lastResult : null;
            int tokens = result != null ? result.tokensAwarded : 0;
            if (tokens > 0)
            {
                var tokenLine = RuntimeUI.Label(_overlay.transform, $"+{tokens} TOKENS", 56,
                    new Vector2(0.5f, 0.69f), Vector2.zero, new Vector2(900f, 80f));
                tokenLine.fontStyle = FontStyles.Bold;
                tokenLine.color = UITheme.Gold;
                StartCoroutine(CountUpTokens(tokenLine, tokens));

                if (result.doublerUsed)
                {
                    int baseTokens = tokens / 2;
                    var doublerLine = RuntimeUI.Label(_overlay.transform,
                        $"TOKEN DOUBLER!  base {baseTokens} x2 = {tokens}", 34,
                        new Vector2(0.5f, 0.645f), Vector2.zero, new Vector2(1100f, 54f));
                    doublerLine.fontStyle = FontStyles.Bold;
                    doublerLine.color = UITheme.Accent;
                }

                AudioManager.Play(Sfx.Win);
            }

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

            // SHARE sits on its own row above the primary actions so it never overlaps them.
            int sharePos = pos;
            RuntimeUI.Button(_overlay.transform, "SHARE", UITheme.Secondary,
                new Vector2(0.5f, 0.235f), Vector2.zero, new Vector2(440f, 84f), () => OnShare(mode, sharePos));

            RuntimeUI.Button(_overlay.transform, "PLAY AGAIN", UITheme.Primary,
                new Vector2(0.5f, 0.12f), new Vector2(-240f, 0f), new Vector2(440f, 92f), OnPlayAgain);
            RuntimeUI.Button(_overlay.transform, "MAIN MENU", UITheme.Neutral,
                new Vector2(0.5f, 0.12f), new Vector2(240f, 0f), new Vector2(440f, 92f), OnMenu);

            OverlayIntro.Play(_overlay);
        }

        /// Quick count-up so the gold payout reads as a reward instead of snapping. Uses unscaled
        /// time (the game is effectively paused on the victory screen) and ease-out so it settles on
        /// the final amount. Honors Reduced Motion by skipping straight to the total.
        private IEnumerator CountUpTokens(TMPro.TMP_Text label, int total)
        {
            if (label == null) yield break;
            if (SettingsStore.ReducedMotion) { label.text = $"+{total} TOKENS"; yield break; }

            const float Duration = 0.6f;
            float t = 0f;
            while (t < Duration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / Duration);
                float e = 1f - Mathf.Pow(1f - k, 3f); // ease-out cubic
                int shown = Mathf.Clamp(Mathf.RoundToInt(total * e), 0, total);
                if (label == null) yield break;
                label.text = $"+{shown} TOKENS";
                yield return null;
            }
            if (label != null) label.text = $"+{total} TOKENS";
        }

        /// True if this winning run is the player's best ever score for the mode. The player won,
        /// so GameManager already submitted the run (BuildSoon waits a frame for it); the run is
        /// therefore the new best exactly when the player's top stored entry is this run — i.e. no
        /// stored entry of theirs scores higher than this run's score.
        private static bool IsNewBest(LevelMode mode, string me)
        {
            if (GameManager.Instance == null || GameManager.Instance.lastResult == null) return false;
            float runScore = GameManager.Instance.lastResult.score;

            float playerBest = float.MinValue;
            // Pull the full retained set (50 == the per-mode cap) so a deep history can't hide the
            // player's true best. GetTop is sorted high-to-low, so the first of the player's entries
            // is their best.
            var all = LeaderboardStore.GetTop(mode, 50);
            for (int i = 0; i < all.Count; i++)
            {
                var e = all[i];
                if (e != null && e.playerName == me) { playerBest = e.score; break; }
            }
            return playerBest == float.MinValue || runScore >= playerBest;
        }

        // Game URL the share links back to (the deployed WebGL build).
        private const string ShareUrl = "https://stumbleclone.vercel.app";

        /// Opens an X/Twitter web-intent pre-filled with a brag about this win. OpenURL works on
        /// WebGL (new tab) and mobile (app/browser), so it stays cross-platform with no native plugin.
        private static void OnShare(LevelMode mode, int pos)
        {
            string place = pos > 0 ? $"#{pos} on the {mode} leaderboard" : $"the {mode}";
            string text = $"I just won {place} in StumbleKids! Can you beat me?";
            string url = "https://twitter.com/intent/tweet?text="
                         + UnityWebRequest.EscapeURL(text)
                         + "&url=" + UnityWebRequest.EscapeURL(ShareUrl);
            Application.OpenURL(url);
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
