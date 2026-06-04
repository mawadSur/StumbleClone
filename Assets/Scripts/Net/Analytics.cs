using System;
using System.Collections.Generic;
using System.Text;
using StumbleClone.Core;
using StumbleClone.Game;
using UnityEngine;
using UnityEngine.Networking;

namespace StumbleClone.Net
{
    /// Lightweight, SDK-pluggable analytics + event logger. NO external package — compiles and runs
    /// today with only built-in Unity APIs, and is structured so a real SDK can be dropped in behind
    /// it later without touching any call sites.
    ///
    /// WHAT IT DOES
    ///  * Every Analytics.Log(...) is timestamped (UtcNow), tagged with a stable anonymous device id
    ///    and a per-run session id, then buffered.
    ///  * Sink 1 (always): Debug.Log, so events are visible in the editor/player log immediately.
    ///  * Sink 2 (optional): a batched HTTP POST to a configurable endpoint via UnityWebRequest,
    ///    fire-and-forget and fully exception-guarded. If no endpoint is configured it is a silent
    ///    no-op (no errors, no network calls) — so the game ships and runs with analytics "off".
    ///  * The funnel is auto-instrumented: a self-bootstrapping [RuntimeInitializeOnLoadMethod]
    ///    listener subscribes to GameEvents + TokenWallet.Changed and logs the standard events below.
    ///    It only listens — it never edits or owns those systems.
    ///
    /// EVENT TAXONOMY (auto-instrumented; param keys in []):
    ///   session_start       — first scene load this run. []: device, session, platform, version
    ///   level_start         — a level began.            []: mode
    ///   level_complete      — a level ended.            []: won (bool), mode, duration (seconds)
    ///   player_eliminated   — a racer was eliminated.   []: isPlayer (bool), racer
    ///   currency_changed    — token balance changed.    []: balance, delta
    /// Add manual events anywhere with Analytics.Log("my_event", "key", value, ...). Event + param
    /// names should stay snake_case and stable so downstream dashboards don't break.
    ///
    /// POINTING IT AT A REAL BACKEND LATER (no call-site changes required):
    ///   Option A — generic HTTP / your own collector:
    ///     Set the endpoint once, e.g. Analytics.SetEndpoint("https://your-host/collect"); (or set the
    ///     PlayerPrefs key "stumbleclone.analytics.endpoint"). Each batch POSTs a JSON array of events
    ///     to that URL. Stand up any tiny collector (or a serverless function) that accepts it.
    ///   Option B — Firebase Analytics:
    ///     Install the Firebase Unity SDK, then replace the body of EmitToSdk(...) with
    ///     FirebaseAnalytics.LogEvent(eventName, parameters[]). Leave the endpoint unset so the HTTP
    ///     sink stays off, or use both.
    ///   Option C — GameAnalytics:
    ///     Install the GameAnalytics Unity SDK + call GameAnalytics.Initialize() at boot, then in
    ///     EmitToSdk(...) map events to GameAnalytics.NewDesignEvent(eventName, value) /
    ///     NewProgressionEvent(...). Map level_start/level_complete to progression events for funnels.
    ///   In all cases SHIPPING REAL DATA still requires YOUR account + key/config (a Firebase
    ///   google-services file, a GameAnalytics game key/secret, or your own endpoint URL + auth).
    ///   Until then this runs locally and logs to the console only.
    public static class Analytics
    {
        // ---- Configuration -----------------------------------------------------

        /// Optional default collector URL compiled into the build. Leave empty to keep the HTTP sink
        /// OFF by default; a PlayerPrefs override (EndpointPrefKey) takes precedence when set. When
        /// the resolved endpoint is empty, no network calls are ever made.
        private const string DefaultEndpoint = "";

        /// PlayerPrefs key for a runtime endpoint override (e.g. set from a debug menu or build step).
        private const string EndpointPrefKey = "stumbleclone.analytics.endpoint";

        /// PlayerPrefs key holding the persisted anonymous device id (hashed, never the raw id).
        private const string DeviceIdPrefKey = "stumbleclone.analytics.deviceid";

        /// Flush the buffer once it reaches this many events, so we batch network traffic.
        private const int BatchSize = 20;

        /// Hard cap on the buffer so a misconfigured endpoint (or pure offline play) can never grow
        /// memory without bound — oldest events are dropped past this.
        private const int MaxBufferedEvents = 200;

        // ---- Identity ----------------------------------------------------------

        // Lazily initialised inside methods (never in static field initializers — keeps SystemInfo /
        // time access off the type-initializer path, which is the project convention).
        private static string _deviceId;
        private static string _sessionId;
        private static bool _identityReady;

        private static readonly List<string> _buffer = new List<string>(BatchSize);
        private static int _lastTokenBalance = int.MinValue; // for currency_changed delta

        // ---- Public API --------------------------------------------------------

        /// Log an event. Extra args are interpreted as alternating key/value pairs:
        ///   Analytics.Log("level_start", "mode", "Race");
        /// Always Debug.Logs; also buffers for the optional batched HTTP sink. Never throws.
        public static void Log(string eventName, params object[] keyValuePairs)
        {
            try
            {
                if (string.IsNullOrEmpty(eventName)) return;
                EnsureIdentity();

                string ts = DateTime.UtcNow.ToString("o"); // ISO-8601, sortable
                string paramsJson = BuildParamsJson(keyValuePairs);
                string json = BuildEventJson(eventName, ts, paramsJson);

                Debug.Log($"[Analytics] {eventName} {paramsJson}");

                EmitToSdk(eventName, keyValuePairs); // no-op hook for a real SDK (Firebase/GameAnalytics)

                Buffer(json);
            }
            catch (Exception e)
            {
                // Analytics must never break gameplay.
                Debug.LogWarning($"[Analytics] Log failed for '{eventName}': {e.Message}");
            }
        }

        /// Override the collector endpoint at runtime (persisted). Pass null/empty to disable the
        /// HTTP sink again. Useful from a debug menu or a build/CI step instead of editing code.
        public static void SetEndpoint(string url)
        {
            try
            {
                if (string.IsNullOrEmpty(url)) PlayerPrefs.DeleteKey(EndpointPrefKey);
                else PlayerPrefs.SetString(EndpointPrefKey, url);
                PlayerPrefs.Save();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Analytics] SetEndpoint failed: {e.Message}");
            }
        }

        /// Force-send any buffered events now (best-effort, fire-and-forget). Safe to call anytime.
        public static void Flush()
        {
            try { FlushInternal(); }
            catch (Exception e) { Debug.LogWarning($"[Analytics] Flush failed: {e.Message}"); }
        }

        // ---- Identity helpers --------------------------------------------------

        private static void EnsureIdentity()
        {
            if (_identityReady) return;
            _deviceId = ResolveAnonymousDeviceId();
            _sessionId = Guid.NewGuid().ToString("N");
            _identityReady = true;
        }

        // A STABLE ANONYMOUS id: a hash of SystemInfo.deviceUniqueIdentifier (never the raw id, so we
        // don't store/transmit a hardware fingerprint). Persisted so it's consistent across sessions.
        private static string ResolveAnonymousDeviceId()
        {
            try
            {
                string cached = PlayerPrefs.GetString(DeviceIdPrefKey, "");
                if (!string.IsNullOrEmpty(cached)) return cached;

                string raw = SystemInfo.deviceUniqueIdentifier;
                if (string.IsNullOrEmpty(raw) || raw == SystemInfo.unsupportedIdentifier)
                    raw = Guid.NewGuid().ToString("N"); // fall back to a random stable id

                string hashed = StableHash(raw);
                PlayerPrefs.SetString(DeviceIdPrefKey, hashed);
                PlayerPrefs.Save();
                return hashed;
            }
            catch
            {
                return "anon";
            }
        }

        // Small, dependency-free FNV-1a hash rendered as hex. Not cryptographic — just a stable,
        // non-reversible-enough anonymiser so we never ship the raw device identifier.
        private static string StableHash(string s)
        {
            const ulong offset = 1469598103934665603UL;
            const ulong prime = 1099511628211UL;
            ulong hash = offset;
            for (int i = 0; i < s.Length; i++)
            {
                hash ^= s[i];
                hash *= prime;
            }
            return hash.ToString("x16");
        }

        // ---- Buffering + sink --------------------------------------------------

        private static void Buffer(string eventJson)
        {
            _buffer.Add(eventJson);
            if (_buffer.Count > MaxBufferedEvents)
                _buffer.RemoveRange(0, _buffer.Count - MaxBufferedEvents); // drop oldest

            if (_buffer.Count >= BatchSize) FlushInternal();
        }

        private static void FlushInternal()
        {
            if (_buffer.Count == 0) return;

            string endpoint = ResolveEndpoint();
            if (string.IsNullOrEmpty(endpoint)) return; // HTTP sink disabled — keep buffering/console only

            string payload = "[" + string.Join(",", _buffer) + "]";
            _buffer.Clear();

            PostBatch(endpoint, payload); // fire-and-forget
        }

        private static string ResolveEndpoint()
        {
            try
            {
                string overrideUrl = PlayerPrefs.GetString(EndpointPrefKey, "");
                return string.IsNullOrEmpty(overrideUrl) ? DefaultEndpoint : overrideUrl;
            }
            catch
            {
                return DefaultEndpoint;
            }
        }

        // Fire-and-forget POST. We intentionally do NOT await it (no coroutine host needed): the
        // UnityWebRequest is sent and disposed on completion. Failures are swallowed so a flaky
        // network or a wrong URL never affects the game.
        private static void PostBatch(string endpoint, string payload)
        {
            try
            {
                byte[] body = Encoding.UTF8.GetBytes(payload);
                var req = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST)
                {
                    uploadHandler = new UploadHandlerRaw(body),
                    downloadHandler = new DownloadHandlerBuffer(),
                };
                req.SetRequestHeader("Content-Type", "application/json");

                var op = req.SendWebRequest();
                op.completed += _ =>
                {
                    try
                    {
                        if (req.result != UnityWebRequest.Result.Success)
                            Debug.LogWarning($"[Analytics] POST failed ({req.responseCode}): {req.error}");
                    }
                    catch { /* ignore */ }
                    finally { req.Dispose(); }
                };
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Analytics] POST setup failed: {e.Message}");
            }
        }

        // ---- SDK hook ----------------------------------------------------------

        // Swap-in point for a real analytics SDK. Today a no-op so we compile with NO new package.
        // To ship to Firebase/GameAnalytics, install that SDK and forward the event here (see the
        // header comment for exact call mappings). Keeping this separate from the HTTP sink means a
        // project can use the SDK, the HTTP collector, both, or neither.
        private static void EmitToSdk(string eventName, object[] keyValuePairs)
        {
            // intentionally empty until a real SDK is wired in
        }

        // ---- JSON (tiny, dependency-free) --------------------------------------

        private static string BuildEventJson(string eventName, string isoTimestamp, string paramsJson)
        {
            var sb = new StringBuilder(128);
            sb.Append('{');
            AppendJsonField(sb, "event", eventName); sb.Append(',');
            AppendJsonField(sb, "ts", isoTimestamp); sb.Append(',');
            AppendJsonField(sb, "device", _deviceId); sb.Append(',');
            AppendJsonField(sb, "session", _sessionId); sb.Append(',');
            sb.Append("\"params\":").Append(paramsJson);
            sb.Append('}');
            return sb.ToString();
        }

        // Turn the alternating key/value args into a flat JSON object. Values are emitted as numbers
        // or booleans when possible, otherwise as JSON strings. Malformed (odd) arg lists degrade
        // gracefully rather than throwing.
        private static string BuildParamsJson(object[] kv)
        {
            var sb = new StringBuilder(64);
            sb.Append('{');
            if (kv != null)
            {
                bool first = true;
                for (int i = 0; i + 1 < kv.Length; i += 2)
                {
                    string key = kv[i]?.ToString();
                    if (string.IsNullOrEmpty(key)) continue;
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append('"').Append(Escape(key)).Append("\":");
                    AppendJsonValue(sb, kv[i + 1]);
                }
            }
            sb.Append('}');
            return sb.ToString();
        }

        private static void AppendJsonField(StringBuilder sb, string key, string value)
        {
            sb.Append('"').Append(Escape(key)).Append("\":\"").Append(Escape(value)).Append('"');
        }

        private static void AppendJsonValue(StringBuilder sb, object value)
        {
            switch (value)
            {
                case null:
                    sb.Append("null");
                    break;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    break;
                case int i:
                    sb.Append(i.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    break;
                case long l:
                    sb.Append(l.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    break;
                case float f:
                    sb.Append(f.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                    break;
                case double d:
                    sb.Append(d.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                    break;
                default:
                    sb.Append('"').Append(Escape(value.ToString())).Append('"');
                    break;
            }
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        // ====================================================================
        //  Auto-instrumentation: a self-bootstrapping listener that subscribes
        //  to the game's existing event buses and logs the standard funnel. It
        //  ONLY listens — it never edits GameEvents/TokenWallet (other agents
        //  own those files).
        // ====================================================================

        private static bool _funnelHooked;
        private static float _levelStartTime;
        private static LevelMode _currentMode;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void BootstrapFunnel()
        {
            if (_funnelHooked) return;
            _funnelHooked = true;

            try
            {
                EnsureIdentity();

                // session_start fires once per run, at first scene load.
                Log("session_start",
                    "platform", Application.platform.ToString(),
                    "version", Application.version,
                    "device_model", SystemInfo.deviceModel);

                // Subscribe defensively (unsubscribe-then-subscribe) so a domain reload / re-entry
                // can never double-register. We never null these buses — that's the owners' job.
                GameEvents.LevelStarted -= OnLevelStarted;
                GameEvents.LevelStarted += OnLevelStarted;

                GameEvents.LevelEnded -= OnLevelEnded;
                GameEvents.LevelEnded += OnLevelEnded;

                GameEvents.RacerEliminated -= OnRacerEliminated;
                GameEvents.RacerEliminated += OnRacerEliminated;

                TokenWallet.Changed -= OnCurrencyChanged;
                TokenWallet.Changed += OnCurrencyChanged;

                Application.quitting -= OnQuitting;
                Application.quitting += OnQuitting;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Analytics] Funnel bootstrap failed: {e.Message}");
            }
        }

        private static void OnLevelStarted(LevelMode mode)
        {
            _currentMode = mode;
            _levelStartTime = Time.unscaledTime;
            Log("level_start", "mode", mode.ToString());
        }

        // GameEvents.LevelEnded carries the winner (or null). We derive `won` from whether the
        // winner is the local player, and compute duration from the level_start timestamp.
        private static void OnLevelEnded(IRacer winner)
        {
            float duration = _levelStartTime > 0f ? Time.unscaledTime - _levelStartTime : 0f;
            bool won = winner != null && winner.IsPlayer;
            Log("level_complete",
                "won", won,
                "mode", _currentMode.ToString(),
                "duration", duration);
            Flush(); // good moment to push the batch — a funnel boundary
        }

        private static void OnRacerEliminated(IRacer racer)
        {
            bool isPlayer = racer != null && racer.IsPlayer;
            Log("player_eliminated",
                "isPlayer", isPlayer,
                "racer", racer != null ? racer.DisplayName : "unknown");
        }

        private static void OnCurrencyChanged(int balance)
        {
            int delta = _lastTokenBalance == int.MinValue ? 0 : balance - _lastTokenBalance;
            _lastTokenBalance = balance;
            Log("currency_changed", "balance", balance, "delta", delta);
        }

        private static void OnQuitting() => Flush();
    }
}
