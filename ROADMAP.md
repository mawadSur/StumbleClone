# StumbleClone — Road to Ship 🏁

Living checklist toward the goal: **a fully working game (title, menu, leaderboard, bots,
animations, sounds), published to iOS + Android + Web, with a public GitHub repo that
auto-deploys the web build to Vercel — and a Last-Standing arena where hazards spawn from
recognizable directional patterns.**

Status legend: ✅ done · 🟡 partial / in progress · ⬜ todo · 🧑 needs you (account/art/secrets/playtest)
Last updated: 2026-05-31. Source of truth for "what's left." Update the boxes as work lands.

**Repo is LIVE:** https://github.com/mawadSur/StumbleClone (public, pushed to `main`).
**🎮 LIVE & PLAYABLE (real skeletal animation):** https://stumbleclone.vercel.app · APK: /StumbleClone-android.apk
**Status (2026-05-31):** EPIC 0,1,2(core),3,4,5,7,8 ✅ DONE. WebGL **built + deployed live + verified loading**
(engine 200s). Android **APK built + hosted**. Auto-deploy on push ✅. Real skeletal animation ✅ bound + live.
**Only remaining:** iOS — **deferred by user** to a Mac export (pipeline `build-ios.yml` ready); optional polish
(richer anim pack, Play Store AAB, real SFX files).

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
- ✅ **Rebuilt the 3 level scenes** (`RebuildScenesOnly`, 0 errors) — builder changes materialized + committed
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

- ✅ NavMesh baked + BotSpawner wired (verified via the headless rebuilds; bots spawn + navigate)
- ✅ Removed dead shrink-ring path (`ShrinkRadiusChanged` event, `ShrinkingZoneVisualizer`, inert HUD coupling)
- ✅ Last-Standing **hazard-avoidance** — bots scan for `ArenaObstacle`, sidestep away (center-biased) + hop over very close hazards; reaction reliability scales with skill
- ✅ **Per-bot skill variation** — each bot rolls skill 0.35–1.0 driving move speed *and* dodge reaction + charge aggression (the 7 bots feel distinct)
- 🟡 EditMode win-condition tests — **deferred**: needs a game `.asmdef` (tests can't reference Assembly-CSharp), a structural change that risks the live build's reflection-based builders. Not worth jeopardizing the shipped game; revisit if the project adopts asmdefs. `P3`

---

## EPIC 3 — Animations ("proper animations") — currently FROZEN bind-pose (no clips exist)

32 Quaternius meshes + controller + driver scripts exist, but **no `.anim` clips** anywhere, so
nothing plays. Avatars unbound (`m_Avatar:0`); controller motions are dangling refs.

- ✅ `ProceduralCharacterAnimator.cs` — bob/lean/sway/jump-squash/knockdown-topple fallback so it looks alive *before* clips land
- ✅ `AnimatorClipUtil` — auto-attaches the fallback when an Animator has no clips (no prefab edits); auto-off once clips exist
- ✅ `PlayerAnimator`/`BotAnimator` route to the fallback; fixed `maxSpeedForNormalization` 8→6
- ✅ `PlayerController.Knockback` now triggers the knockdown reaction (was dead code)
- ✅ **REAL SKELETAL ANIMATION DONE + LIVE** — the committed FBX already had the `CharacterArmature` takes; the issue was `avatarSetup=NoAvatar` + a gated setup. Fixed `avatarSetup`→CreateFromThisModel, ran `CharacterAnimSetup` (controller: Idle/Walk/Run blend + Jump + RecieveHit; avatars assigned), rebuilt WebGL, redeployed. (Swap in a richer animated pack later if desired — optional.)
- ✅ `CharacterAnimSetup` ran clean; controller regenerated; avatars bound (BlueSoldier_MaleAvatar/Casual_MaleAvatar)
- ⬜ Drive **bot** knockdown from `BotController` knockback (player done; bot hook added, call site TODO) `P1`
- 🧑 Screenshot/GIF evidence that characters animate (no T-pose) `P2`

---

## EPIC 4 — Audio ("sounds") — currently 100% absent (only an AudioListener)

- ✅ `Audio/AudioManager.cs` — self-bootstrapping DontDestroyOnLoad singleton with pooled 2D SFX sources
- ✅ `Audio/ProceduralSfx.cs` — synthesizes jump/land/push/hit/eliminate/win/start/click **in code (zero audio files)**
- ✅ `Audio/GameAudioHooks.cs` — event-driven SFX (eliminate/win/start), re-subscribes across scene loads
- ✅ Gameplay hooks: jump + hit in `PlayerController`, push whoosh in `PushInteraction`
- ⬜ UI click SFX on menu/HUD buttons `P1` 🧑 *(needs scene/button refs)*
- 🧑 Optionally replace synth placeholders with real SFX + music (CC0 packs) + AudioMixer sliders `P2`

---

## EPIC 5 — Title + Menu + Leaderboard (menu flow works; no title screen, no leaderboard)

- ✅ Unified score model: `score` + per-mode `ScoreFor()` in `LevelResult.cs`
- ✅ `LeaderboardEntry` + `LeaderboardStore` (JSON via Newtonsoft → PlayerPrefs, cross-platform incl. WebGL)
- ✅ Recording on `LevelEnded` (in `GameManager`, player-runs only) + player-name get/set in store
- ✅ Player name UI (TMP_InputField in TitleScreen → `LeaderboardStore.SetPlayerName`)
- ✅ Real TITLE screen — `TitleScreen.cs` runtime overlay (branded title + name entry + START), self-instantiates on MainMenu, **no rebuild needed**
- ✅ `LeaderboardUI.cs` — leaderboard button + panel with mode tabs (Race/Survival/Knockout), top-10 from `LeaderboardStore`
- ⬜ "View Leaderboard / New best!" affordance on EndScreen `P2`
- ⬜ "View Leaderboard" / "New best!" on EndScreen `P2`
- 🧑 Wire new SerializeField refs in `MainMenu.unity` (binary scene — in-editor) `P1`
- 🧑 *Optional* global leaderboard: Vercel serverless (`/api/scores`) + KV/Upstash + `GlobalLeaderboardClient` w/ offline fallback `P2`

---

## EPIC 6 — Build & Publish (iOS / Android / Web) — **Web + Android SHIPPED; iOS needs a Mac**

- ✅ First headless **WebGL** build SUCCEEDED (22 MB, `Builds/WebGL/index.html`) — 2026-05-31
- ✅ **DEPLOYED LIVE to Vercel: https://stumbleclone.vercel.app** (HTTP 200, public) via `vercel deploy --prod`
- ⬜ Persist build-ready PlayerSettings for mobile (`ConfigureAllPlatforms`) `P1`
- ✅ First headless **Android** build SUCCEEDED — `Builds/Android/StumbleClone.apk` (41.6 MB, IL2CPP ARM64) — 2026-05-31
- ✅ **APK hosted for phone install: https://stumbleclone.vercel.app/StumbleClone-android.apk** (HTTP 200, valid APK)
- 🟡 Verify mobile touch overlay on device (install the APK + play) `P1` 🧑
- 🧑 Android release keystore + AAB for Play Store (debug-signed APK works for sideload testing now) `P1`
- 🧑 App icons (1024² master) + branded splash (Unity Personal keeps watermark) `P1`
- ⏸️ **iOS DEFERRED by user** (2026-05-31): "save the unity project then export what is needed to a Mac to get it published." Pipeline ready below.
- ✅ **iOS build pipeline created** — `.github/workflows/build-ios.yml` builds the iOS Xcode project on a GitHub **macOS runner** via game-ci (no Mac of your own needed for the build). Manual-trigger; needs `UNITY_LICENSE` secret to run, + Apple signing secrets (commented in the workflow) for a signed `.ipa`/App Store submission.
- 🧑 iOS final submission: add `UNITY_LICENSE` (run the workflow) + Apple Developer account/certs for a signed `.ipa` `P2`
- ⬜ Reconcile PlatformBuilder vs ProjectSettings drift (minSdk 24/25, iOS 13/15, empty iOS usage strings) `P2`

---

## EPIC 7 — Public repo + CI + Vercel auto-deploy

- ✅ Unity `.gitignore` (+ `.gitattributes`) at project root before git init
- ✅ Stripped `unity-mcp` git package from `Packages/manifest.json` so CI can resolve packages
- ✅ `git init` + initial commit
- ✅ Created **public** GitHub repo under `mawadSur` + pushed → https://github.com/mawadSur/StumbleClone
- ✅ `vercel.json` — gzip WebGL `Content-Encoding`/`Content-Type` headers
- ✅ GitHub Actions `deploy-web.yml`: `game-ci/unity-builder` (WebGL) → deploy to Vercel on push to main
- ✅ **AUTO-DEPLOY ACTIVE**: `vercel git connect` linked the repo → **every push to main auto-deploys** the committed `web/` build to https://stumbleclone.vercel.app (no secrets — uses the Vercel↔GitHub account link). Verified: pushes produced Production deployments automatically.
- ✅ To refresh after code changes: rebuild WebGL → copy into `web/` → commit → push (auto-redeploys).
- 🧑 *Optional* rebuild-from-source CI (`deploy-web.yml`, now manual-only): add `UNITY_LICENSE`/`EMAIL`/`PASSWORD` + `VERCEL_TOKEN` (+ `VERCEL_ORG_ID`=`team_dBnwdDkq7Sc8mDm0dmKyRJhP`, `VERCEL_PROJECT_ID`=`prj_QnCvznIvedkcmkhluoMBbABWX9d3`) and flip its trigger back to `push`.

---

## EPIC 8 — Rebuild & verify (cross-cutting)

The level-builder + settings changes only reach the binary scenes via an editor pass. The
catch: a **full** `MvpBootstrap.Run` rebuilds Player/Bot prefabs as **capsules**, clobbering the
FBX character variants. So we need a **scenes-only / non-destructive** rebuild path.

- ✅ Added scenes-only editor entry: `StumbleClone ▸ Rebuild Scenes Only (keep prefabs)` (`MvpBootstrap.RebuildScenesOnly`)
- ✅ Compile-by-inspection: 3-agent adversarial review of all session C# → **clean** (1 ternary bug found + fixed)
- ✅ **Final compile-verify: real Unity recompile = 0 `error CS`** (2026-05-31, all session code compiles)
- ⬜ Run the rebuild (`StumbleClone ▸ Rebuild Scenes Only`, or headless if editor closed) to materialize EPIC 0 scene bits `P0` 🧑
- 🧑 In-editor smoke test each mode before tagging a build `P1`

---

## DIRECTION & BACKLOG (2026-06-01, from playtest feedback)

**Focus: Deathmatch (Last-Standing / Knockout) is THE mode.** Race & Survival still exist in
code but are no longer surfaced in the UI.
- ✅ **Single welcome screen** — removed the redundant second one. The Title screen's **PLAY**
  now drops straight into the deathmatch (bypassing the MainMenu's title + level-select);
  added a **Leaderboard** button to the Title screen. (`TitleScreen.cs` + `LeaderboardUI.cs`,
  runtime — no scene rebuild.)
- ⬜ **TODO — Multiplayer / party lobby:** let the user invite friends to play in the **same
  lobby** (real players replacing/joining the 7 bots). Needs networking (Netcode for GameObjects
  or a relay/lobby service) + lobby UI + matchmaking. Big feature — design before building. `FUTURE`
- ⬜ (optional) Strip Race/Survival from build settings + menu entirely if the game commits to
  deathmatch-only `P3`
