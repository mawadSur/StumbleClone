# StumbleClone — Road to Ship 🏁

Living checklist toward the goal: **a fully working game (title, menu, leaderboard, bots,
animations, sounds), published to iOS + Android + Web, with a public GitHub repo that
auto-deploys the web build to Vercel — and a Last-Standing arena where hazards spawn from
recognizable directional patterns.**

Status legend: ✅ done · 🟡 partial / in progress · ⬜ todo · 🧑 needs you (account/art/secrets/playtest)
Last updated: 2026-05-31. Source of truth for "what's left." Update the boxes as work lands.

**Repo is LIVE:** https://github.com/mawadSur/StumbleClone (public, pushed to `main`).
**Session progress:** EPIC 0 (bug fixes) + EPIC 1 (spawn patterns) + EPIC 8 (safe-rebuild entry)
written in code; EPIC 7 (repo + CI/Vercel scaffold) shipped. Pending a Unity compile checkpoint
+ scenes rebuild to make EPIC 0/1 live in-editor.

---

## EPIC 0 — Last-Standing playtest fixes (your reported bugs) — **CODE DONE, pending scene rebuild**

Root cause of all three: pressing Play on `Level_LastStanding` **directly** (no MainMenu →
no `GameManager`) made the KillZone fall back to Race and *respawn* the fallen player on
top (= infinite lives), and never created the spectate overlay.

- ✅ Eliminate-on-fall in Last-Standing/Survival regardless of GameManager — `KillZone.cs` now resolves mode via `GameManager → LevelSelfStart → fallback`
- ✅ New `LevelSelfStart.cs` — a directly-played scene now runs a full round (obstacles, spectate, win-check)
- ✅ One life per game (no respawn-on-top) — falling eliminates; spectate kicks in
- ✅ Player no longer starts on the edge — arena spawn ring 17→**11** (inside the rim), bots offset to point 1 so they don't stack on the player (point 0)
- ✅ Spectate "End Run"/Restart now work in direct-play too (were no-ops without GameManager) — `SpectateController.cs`
- 🟡 **Rebuild the 3 level scenes** so these builder changes materialize (see EPIC 8) — *blocks in-editor verification*
- 🧑 Playtest-confirm: fall → spectate a survivor → "End Run" returns to menu; one life respected

---

## EPIC 1 — Arena spawn PATTERNS (headline feature: "different directions, recognizable patterns")

Today: `ObstacleSpawner` picks a **uniformly-random** rim angle + random type every spawn — no
directions, no patterns, no telegraph, not learnable. Replace with a telegraphed wave system.

- ✅ `SpawnPattern.cs` — 8-octant `SpawnDirection` + rim-point helper (the shared vocabulary)
- ✅ `SpawnPattern.cs` — wave model: ordered `{direction, type, delay}` entries + telegraph lead + tier
- ✅ 6 named patterns (CrossSweep, Pincer, ClockwiseRotation, Spiral, Rain, Gauntlet)
- ✅ `TelegraphIndicator.cs` — pulsing yellow→red ground disc before each spawn
- ✅ Wave scheduler in `ObstacleSpawner` — coroutine TELEGRAPH → SPAWN (reuses factories) → REST
- ✅ Deterministic, difficulty-tiered, seeded selection (fixed early seed = learnable; harder later)
- ⬜ Thread explicit direction/spin into `RollingBoulder`/`SweepingBar` so internal randomness stops fighting patterns `P1`
- ⬜ `GameEvents.WaveTelegraphed(name,dir)` so audio/UI can play a directional "tell" `P2`
- ⬜ Wire `arenaCenter` in `LastStandLevelBuilder` (today only `MvpBootstrap` sets it) `P2`
- 🧑 Playtest-tune telegraph lead / rest gaps / tier thresholds (the "recognize & learn" gate) `P3`

---

## EPIC 2 — Bots & gameplay polish (bots already navigate + compete)

- 🟡 Verify NavMesh baked + BotSpawner wired in all 3 scenes (only verifiable in-editor) `P0` 🧑
- ⬜ Remove dead shrink-ring path (`ShrinkRadiusChanged`, `ShrinkingZoneVisualizer`, inert HUD zoneIndicator) `P1`
- ⬜ Last-Standing bot hazard-avoidance (scan `ArenaObstacle`, sidestep/jump) so the contest feels earned `P2`
- ⬜ Per-bot skill variation (speed/aggression/reaction jitter) — currently all 7 identical `P2`
- ⬜ EditMode tests for win conditions (Race/Survival/LastStanding + GameEvents symmetry) — required by CLAUDE.md `P3`

---

## EPIC 3 — Animations ("proper animations") — currently FROZEN bind-pose (no clips exist)

32 Quaternius meshes + controller + driver scripts exist, but **no `.anim` clips** anywhere, so
nothing plays. Avatars unbound (`m_Avatar:0`); controller motions are dangling refs.

- ✅ `ProceduralCharacterAnimator.cs` — bob/lean/sway/jump-squash/knockdown-topple fallback so it looks alive *before* clips land
- ✅ `AnimatorClipUtil` — auto-attaches the fallback when an Animator has no clips (no prefab edits); auto-off once clips exist
- ✅ `PlayerAnimator`/`BotAnimator` route to the fallback; fixed `maxSpeedForNormalization` 8→6
- ✅ `PlayerController.Knockback` now triggers the knockdown reaction (was dead code)
- 🧑 Import an **animated** character pack (Quaternius Ultimate Animated — same `CharacterArmature` rig — or Mixamo) → upgrades from procedural to real skeletal anim `P0`
- 🟡 Fix `CharacterAnimSetup.cs` + regenerate controller + bind Avatar once clips exist `P0`
- ⬜ Drive **bot** knockdown from `BotController` knockback (player done; bot hook added, call site TODO) `P1`
- 🧑 Screenshot/GIF evidence that characters animate (no T-pose) `P2`

---

## EPIC 4 — Audio ("sounds") — currently 100% absent (only an AudioListener)

- ⬜ `Audio/AudioManager.cs` — DontDestroyOnLoad singleton, pooled one-shot SFX + music source + AudioMixer buses `P0`
- ⬜ `Audio/SfxLibrary.cs` (ScriptableObject) + `GameAudioHooks.cs` wired to GameEvents (jump/land/push/hit/eliminate/finish) `P1`
- ⬜ UI click SFX on menu/HUD buttons (reusable component via UIBootstrapper) `P1`
- 🧑 Provide/approve actual SFX + music files (synth placeholders or CC0 packs) `P1`
- ⬜ Audio settings panel (master/music/SFX sliders → PlayerPrefs) `P2`

---

## EPIC 5 — Title + Menu + Leaderboard (menu flow works; no title screen, no leaderboard)

- ✅ Unified score model: `score` + per-mode `ScoreFor()` in `LevelResult.cs`
- ✅ `LeaderboardEntry` + `LeaderboardStore` (JSON via Newtonsoft → PlayerPrefs, cross-platform incl. WebGL)
- ✅ Recording on `LevelEnded` (in `GameManager`, player-runs only) + player-name get/set in store
- ⬜ Capture player name UI (TMP_InputField → `LeaderboardStore.SetPlayerName`) `P1` 🧑 *(needs scene)*
- ⬜ Real TITLE screen (panel in MainMenu: logo + "press/tap to start" + music hook) `P1` 🧑 *(needs scene)*
- ⬜ `LeaderboardUI` panel + mode tabs + Leaderboard button on main menu `P1` 🧑 *(needs scene)*
- ⬜ "View Leaderboard" / "New best!" on EndScreen `P2`
- 🧑 Wire new SerializeField refs in `MainMenu.unity` (binary scene — in-editor) `P1`
- 🧑 *Optional* global leaderboard: Vercel serverless (`/api/scores`) + KV/Upstash + `GlobalLeaderboardClient` w/ offline fallback `P2`

---

## EPIC 6 — Build & Publish (iOS / Android / Web) — `PlatformBuilder` exists, never run

- ⬜ Persist build-ready PlayerSettings (run `ConfigureAllPlatforms` once; commit landscape + bundle id + IL2CPP) `P0`
- ⬜ First headless **WebGL** build succeeds (verify `Builds/WebGL/index.html`; needs WebGL module) `P0`
- 🟡 Verify mobile touch overlay on device/browser (code path fully wired) `P1` 🧑
- ⬜ First headless **Android** APK/AAB build (needs Android module + NDK) `P1`
- 🧑 Android release keystore (signing) `P1`
- 🧑 App icons (1024² master) + branded splash (Unity Personal keeps watermark) `P1`
- 🧑 **iOS** Xcode project → sign/provision on a Mac + Apple Developer account `P2`
- ⬜ Reconcile PlatformBuilder vs ProjectSettings drift (minSdk 24/25, iOS 13/15, empty iOS usage strings) `P2`

---

## EPIC 7 — Public repo + CI + Vercel auto-deploy

- ✅ Unity `.gitignore` (+ `.gitattributes`) at project root before git init
- ✅ Stripped `unity-mcp` git package from `Packages/manifest.json` so CI can resolve packages
- ✅ `git init` + initial commit
- ✅ Created **public** GitHub repo under `mawadSur` + pushed → https://github.com/mawadSur/StumbleClone
- ✅ `vercel.json` — gzip WebGL `Content-Encoding`/`Content-Type` headers
- ✅ GitHub Actions `deploy-web.yml`: `game-ci/unity-builder` (WebGL) → deploy to Vercel on push to main
- 🧑 **Add repo secrets** (Settings → Secrets → Actions): `UNITY_LICENSE`/`UNITY_EMAIL`/`UNITY_PASSWORD` + `VERCEL_TOKEN`/`VERCEL_ORG_ID`/`VERCEL_PROJECT_ID` — until then CI fails at Unity activation `P0`
- ⬜ End-to-end verify: push → CI builds → Vercel URL loads the game `P1`

---

## EPIC 8 — Rebuild & verify (cross-cutting)

The level-builder + settings changes only reach the binary scenes via an editor pass. The
catch: a **full** `MvpBootstrap.Run` rebuilds Player/Bot prefabs as **capsules**, clobbering the
FBX character variants. So we need a **scenes-only / non-destructive** rebuild path.

- ✅ Added scenes-only editor entry: `StumbleClone ▸ Rebuild Scenes Only (keep prefabs)` (`MvpBootstrap.RebuildScenesOnly`)
- ⬜ Run it (headless if editor closed, or via menu) to materialize EPIC 0 `P0`
- ⬜ Compile-verify (0 `error CS`) — needs the editor to recompile the new scripts `P0`
- 🧑 In-editor smoke test each mode before tagging a build `P1`
