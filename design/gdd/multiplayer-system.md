# Multiplayer System

> **STATUS: DORMANT / CODE-COMPLETE (Phase 1).** This system is reverse-engineered
> from the existing `Assets/Scripts/Net/` code. **It is NOT live in shipped builds.**
> Nothing in the running game starts a `NetworkManager`, so single-player is completely
> unaffected — `Player.prefab` is only turned into a `NetworkObject` by an editor/headless
> setup step that has not been run on shipped builds. To make it live the user must:
> 1. Link the Unity Cloud project (Edit > Project Settings > Services) and, in the Unity
>    Cloud dashboard, enable **Relay**, **Lobby**, **Authentication (Anonymous)** — and
>    (for the dormant backend) **Cloud Save** + **Leaderboards**.
> 2. Run `StumbleClone > Multiplayer > Setup` (or headless
>    `-executeMethod StumbleClone.EditorTools.MultiplayerSetup.Run`) — see
>    `Assets/Scripts/Editor/MultiplayerSetup.cs`.
> 3. Do a 2-client connect test (host + join-by-code on `127.0.0.1`).

## 1. Overview

A Phase-1 online multiplayer foundation built on **Netcode for GameObjects (NGO 2.x,
Unity.Netcode 2.2.0)** with online lobbies layered on top via the **Unity Multiplayer
Services Sessions SDK** (`com.unity.services.multiplayer`) and its default **NGO + Relay**
network handler. A host creates a session, shares a short **join code**, and other players
join by code; creating/joining auto-starts the NGO host/client over Relay. The server is
authoritative and **backfills bots** so a thin lobby still feels like a full field of 8.
The entire stack is dormant by default — see status banner.
Sources: `Assets/Scripts/Net/NetworkBootstrap.cs`, `MultiplayerService.cs`,
`NetworkGame.cs`, `UI/MultiplayerUI.cs`.

## 2. Player Fantasy

"Press one button, get a code, send it to a friend, and stumble together in the same
arena within seconds." The host fantasy is being the one who opens the room; the joiner
fantasy is the frictionless paste-a-code-and-you're-in. The bot backfill preserves the
party-game fantasy of a crowded, chaotic field even when only 2 humans are present — you
never feel like you're in an empty room. (Derived from the `MultiplayerUI` copy: "Create a
lobby and share the join code with friends", "Waiting for the host to start the game…".)

## 3. Detailed Rules

### 3.1 Lobby flow (host / join-by-code) — `UI/MultiplayerUI.cs`
- The lobby is a **code-built modal** (RuntimeUI + UITheme, no scene wiring), opened from
  the title screen's MULTIPLAYER button via `MultiplayerUI.Open()`.
- **HOST GAME** → `MultiplayerService.HostAsync(playerName, MaxPlayers=8)` → on success the
  view switches to the in-session roster showing the **join code** (selectable/copyable),
  the player list (`"n. Name (you)"` + `"(count / max)"`), and a **host-only START** button.
- **JOIN** → reads the code field (force-uppercased on type, trimmed, alphanumeric,
  `characterLimit = 12`) → `MultiplayerService.JoinByCodeAsync(code, playerName)`; guests see
  "Waiting for the host to start the game…".
- **START** (host only) loads the shared gameplay level via `GameManager.LoadLevel(...)`;
  because the host is the NGO server, a networked scene load propagates to every client. The
  hard-coded multiplayer level is **`LevelMode.LastStanding`** (the always-unlocked free mode,
  chosen as the safe shared default).
- **BACK** leaves/tears down the session (`LeaveAsync`) then closes the modal.
- All service calls run from `async void` handlers wrapped in try/catch; a `_busy` flag guards
  double-taps. `OnSessionChanged` can fire off the main thread, so it only sets a dirty flag and
  the UI rebuilds in `Update()`.

### 3.2 Relay-backed Sessions — `MultiplayerService.cs`
- A **static facade**; the UI talks ONLY to this class, never the UGS SDK directly.
- `EnsureSignedInAsync()` initializes Unity Services once (re-entrancy guarded) and signs in
  **anonymously** if not already signed in.
- `HostAsync` builds `SessionOptions { MaxPlayers }.WithRelayNetwork().WithPlayerName()`, also
  stamps the display name as a member player property (key `_player_name`), then
  `CreateSessionAsync` → returns `session.Code`. `.WithRelayNetwork()` with no custom
  `INetworkHandler` makes the SDK drive `NetworkManager.Singleton` itself (Relay transport +
  `StartHost`).
- `JoinByCodeAsync` uses `JoinSessionOptions().WithPlayerName()` →
  `JoinSessionByCodeAsync(code.Trim(), options)` → auto-starts the NGO **client** over Relay.
- `LeaveAsync` deletes (host) or leaves (client), detaches listeners first to avoid double
  `OnSessionChanged`, then defensively shuts NGO down.
- **No cloud call ever throws to the caller** — every path is try/catch and degrades to a logged
  warning + null/false return, because the UGS project may not be linked.
- Session events (`Changed`, `PlayerJoined`, `PlayerHasLeft`, `PlayerPropertiesChanged`,
  `Deleted`, `RemovedFromSession`) are funnelled into one `OnSessionChanged` event the UI listens to.

### 3.3 NGO lifecycle & server-authoritative backfill — `NetworkGame.cs`
- `EnsureManager()` instantiates the single `NetworkManager` prefab from
  `Resources/Net/NetworkManager` (created by `MultiplayerSetup`) as a `DontDestroyOnLoad`
  singleton; idempotent. Must be called before `CreateSessionAsync`/`JoinSessionByCodeAsync`
  because the SDK's NGO handler drives `NetworkManager.Singleton` directly.
- NGO auto-spawns **one owned `Player` per connected client** via `NetworkConfig.PlayerPrefab`.
  This system does **not** spawn players itself.
- On **server start**, the scene's `BotSpawner` is switched to networked mode
  (`SetNetworkedMode(true)`) so it does NOT run its offline auto-fill, and an initial
  `BackfillBots()` runs.
- On **client connect** (server-side, incl. the host's own client): the new player is
  teleported to a `"Respawn"`-tagged spawn point (server-authoritative
  `NetworkTransform.Teleport`, distributed by `clientId % spawnPointCount`), then `BackfillBots()`
  sheds a bot.
- On **client disconnect** (server-side): the player despawns automatically and `BackfillBots()`
  tops the field back up.
- A `[RuntimeInitializeOnLoadMethod]` self-bootstrap subscribes to `SceneManager.sceneLoaded`
  and reconciles bot count for each freshly loaded gameplay scene (so a scene loaded *after* the
  host started still suppresses the offline auto-fill in time). No-ops entirely when not a live
  server — single-player unaffected.

### 3.4 Networked input seam — `NetworkInputProvider.cs` + `NetworkPlayerLink.cs`
- `NetworkInputProvider : NetworkBehaviour, IPlayerInput` — implements the **same**
  `StumbleClone.Player.IPlayerInput` surface as the local `PlayerInputHandler`, so
  `PlayerController` movement code is identical for local vs remote input.
- **Owner** streams local input up: analog `Move`/`Look`/`LookFromGamepad` as owner-writable
  `NetworkVariable`s; edge presses (`Jump`/`Push`/`Pause`) sent as **ServerRpcs** that bump
  server-owned monotonic counters (so a one-frame edge can't be lost/doubled by replication).
- **Non-owner** (server + remote clients) reads the replicated values AS the input;
  `ConsumeEdge` fires "pressed" exactly once per counter increment. Input masking
  (`InputLocked`, `MoveMasked`, `*PressedMasked`) mirrors the local handler so knockback/stun
  windows behave identically for remote players.
- `NetworkPlayerLink` (requires `NetworkInputProvider`) decides input routing on spawn:
  **owner keeps its local `PlayerInputHandler`** (Awake default — direct, responsive); **non-owner**
  is switched to the replicated provider via `PlayerController.SetInputSource(networkInput)`.

### 3.5 Dormant analytics / backend
- `BackendService.cs` — UGS **Cloud Save** (single JSON blob `stumble_progress_v1`: tokens,
  owned skin ids, unlocked modes, season tier) + **Leaderboards** (submit / top-N). Merge policy
  on load is "cloud wins if newer" via a UTC `savedAtUtc` stamp; tokens `max()`'d, ownership/unlocks
  additive (never revoke). **DORMANT** — nothing calls `SaveAsync`/`LoadAsync`/`SubmitScoreAsync`;
  PlayerPrefs stays the runtime source of truth; offline-safe (every call degrades to a warning).
- `Analytics.cs` — local event logger. Always `Debug.Log`s; optional batched HTTP POST sink is
  **OFF** when no endpoint is configured (no network calls). Auto-instruments a funnel
  (`session_start`, `level_start`, `level_complete`, `player_eliminated`, `currency_changed`) by
  listening to `GameEvents` + `TokenWallet.Changed`. **DORMANT** for remote data — no SDK wired
  (`EmitToSdk` is an empty hook), no default endpoint.

## 4. Formulas

| Quantity | Formula | Source |
|----------|---------|--------|
| Field size (max players incl. host) | `NetworkedFieldSize = 8` | `NetworkGame.cs:34`; mirrors `MultiplayerService` default `MaxPlayers = 8` (`MultiplayerService.cs:117`), `MultiplayerUI.MaxPlayers = 8` |
| Desired backfill bots | `desiredBots = max(0, NetworkedFieldSize − humans)` where `humans = NetworkManager.ConnectedClientsIds.Count` | `NetworkGame.cs:160-161` |
| Backfill delta (spawn) | if `currentBots < desiredBots` → `SpawnBots(desiredBots − currentBots)` | `NetworkGame.cs:164-165` |
| Backfill delta (despawn) | if `currentBots > desiredBots` → `DespawnExtraBots(currentBots − desiredBots)` (newest-first) | `NetworkGame.cs:166-167`, `BotSpawner.cs:86-104` |
| Spawn-point assignment (humans) | `index = clientId % spawnPointCount` (server `NetworkTransform.Teleport`) | `NetworkGame.cs:206` |
| Default direct-connect port (LAN fallback) | `DefaultPort = 7777` | `NetworkBootstrap.cs:29`, `MultiplayerSetup.cs:174` |
| Edge consume | a getter returns `true` once when replicated `count != localConsumed`, then `localConsumed = count` (coalesces a burst to one edge) | `NetworkInputProvider.cs:128-138` |

## 5. Edge Cases

- **WebGL transport (WebSockets) vs native (UDP).** WebGL CANNOT open raw UDP sockets, so a
  WebGL build MUST use WebSockets; native iOS/Android/desktop use plain UDP for lower latency.
  `NetworkBootstrap.ConfigureTransport(address, port, useWebSockets)` sets
  `UnityTransport.UseWebSockets` before connecting (flipping it live has no effect). Host and
  client must agree on the protocol. Note: when the SDK's Relay handler runs a session it supplies
  its own allocation data and overwrites the literal endpoint, so `ConfigureTransport`/`DefaultPort`
  only matter for a direct/LAN fallback. (`NetworkBootstrap.cs:34-71`)
- **KNOWN FOLLOW-UP — backfill bots are NOT replicated to remote clients.** Backfill bots are
  spawned server-locally as ordinary `NavMeshAgent`-driven `BotController` GameObjects via
  `BotSpawner.SpawnBots` — they are **`Instantiate`'d, NOT `NetworkObject.Spawn`'d**, so they exist
  only on the server/host and are invisible to remote clients. The Phase roadmap explicitly defers
  server-owned, replicated bots ("NavMeshAgent has no net API → puppet via replicated transforms")
  to Phase 2. (`NetworkBootstrap.cs:17-19`, `BotSpawner.cs:112-228`)
- **Setup step not run / prefab missing.** `NetworkGame.EnsureManager()` logs an error and returns
  null if `Resources/Net/NetworkManager` is absent (Setup never run). `NetworkBootstrap` methods warn
  and return false when there's no `NetworkManager`/`UnityTransport`. All safe to call from dormant code.
- **Cloud project not linked.** Every UGS call (`MultiplayerService`, `BackendService`) is try/catch
  → warning + null/false; gameplay never sees an exception. Warnings name the exact dashboard toggles.
- **Off-main-thread session callbacks.** `MultiplayerUI` never touches UGUI in `OnSessionChanged`;
  it flips a dirty flag and rebuilds in `Update`. Roster accessors are defensively null-guarded
  (a member can be transiently null mid-transition).
- **No spawn points in scene.** `PlaceAtSpawnPoint` leaves the player at its prefab/scene position;
  `GetSpawnPoints` guards an undefined `"Respawn"` tag (which would otherwise throw).
- **Late-joining input reader.** `NetworkInputProvider.OnNetworkSpawn` seeds consume cursors to
  current counter values so a late joiner doesn't fire spurious presses for edges before it spawned.

## 6. Dependencies

Bidirectional links (slugs: `movement-system`, `bot-ai-system`, `level-modes-system`,
`token-economy-system`, `progression-system`, `multiplayer-system`):

- **`movement-system`** — `NetworkInputProvider` implements the same `IPlayerInput` seam the local
  `PlayerInputHandler` does; `NetworkPlayerLink` routes non-owner `PlayerController.SetInputSource`.
  This system is the network producer/consumer for that seam. (cross-review with movement)
- **`bot-ai-system`** — server backfill drives `BotSpawner.SetNetworkedMode/SpawnBots/DespawnExtraBots`
  to reconcile to 8; the offline auto-fill of 7 is suppressed in networked mode. (cross-review with bots)
- **`level-modes-system`** — multiplayer round hard-codes `LevelMode.LastStanding`; host START uses
  `GameManager.LoadLevel`; spawn points use the `"Respawn"` tag (`GameConstants.TagRespawnPoint`).
- **`token-economy-system`** — DORMANT `BackendService` Cloud Save mirrors `TokenWallet` balance;
  `Analytics` listens to `TokenWallet.Changed` (`currency_changed`). No live coupling.
- **`progression-system`** — DORMANT `BackendService` Cloud Save mirrors `SkinInventory` /
  `LevelProgress` (owned skins, unlocked modes, season tier). No live coupling.
- **Engine packages:** `com.unity.netcode.gameobjects` 2.2.0 (NGO 2.x), `com.unity.transport`
  (UnityTransport), `com.unity.services.multiplayer` (Sessions + Relay), `com.unity.services.core`,
  `com.unity.services.authentication`, `com.unity.services.cloudsave`,
  `com.unity.services.leaderboards`, `com.unity.ai.navigation` (bot NavMeshAgents).

## 7. Tuning Knobs

| Knob | Default | Location |
|------|---------|----------|
| `NetworkGame.NetworkedFieldSize` | 8 | `NetworkGame.cs:34` |
| `MultiplayerService.HostAsync` `maxPlayers` | 8 | `MultiplayerService.cs:117` |
| `MultiplayerUI.MaxPlayers` | 8 | `MultiplayerUI.cs:39` |
| `MultiplayerUI.MultiplayerLevel` | `LevelMode.LastStanding` | `MultiplayerUI.cs:38` |
| `NetworkBootstrap.DefaultPort` | 7777 | `NetworkBootstrap.cs:29` |
| `ConfigureTransport` `useWebSockets` | false (UDP); true for WebGL | `NetworkBootstrap.cs:46` |
| Join code input limit / content type | 12 / Alphanumeric (uppercased) | `MultiplayerUI.cs:169-170` |
| `MultiplayerSetup` Player prefab path | `Assets/Prefabs/Player.prefab` | `MultiplayerSetup.cs:35` |
| `MultiplayerSetup` NetworkManager resource path | `Net/NetworkManager` | `MultiplayerSetup.cs:40` |
| `ConnectionApproval` | false (dormant; sessions started by code only) | `MultiplayerSetup.cs:170` |
| Cloud Save key | `stumble_progress_v1` | `BackendService.cs:37` |
| Analytics batch size / buffer cap | 20 / 200 | `Analytics.cs:67,71` |
| Analytics endpoint (HTTP sink) | empty (OFF); override via PlayerPrefs `stumbleclone.analytics.endpoint` | `Analytics.cs:58,61` |

## 8. Acceptance Criteria

Single-player (must hold today, dormant):
- [ ] With shipped builds (Setup not run), no `NetworkManager` ever starts; `Player.prefab` is not
      networked; single-player plays identically. (`NetworkBootstrap.IsOnline == false` everywhere)
- [ ] `BackendService` and `Analytics` make zero remote calls when UGS is unlinked / no endpoint set;
      any failure surfaces only as a `Debug.LogWarning`.

Enablement (after dashboard toggles + `StumbleClone/Multiplayer/Setup` + 2-client test):
- [ ] Running Setup adds `NetworkObject`, `NetworkTransform`, `NetworkInputProvider`,
      `NetworkPlayerLink` to `Player.prefab` and creates `Resources/Net/NetworkManager.prefab`
      (NetworkManager + UnityTransport, `PlayerPrefab` set, `ConnectionApproval = false`); idempotent.
- [ ] HOST returns a non-empty join code; a second client JOINs by that code and both reach the
      in-session roster; the host's START loads `LevelMode.LastStanding` for both.
- [ ] On the server, `humans + bots == 8` after each connect/disconnect (backfill reconciles).
- [ ] Owner drives its own character via local input; a non-owner's character is driven entirely by
      the replicated `NetworkInputProvider` (move/look/jump/push), with input masking matching local.
- [ ] WebGL build connects only with `UseWebSockets = true`; native builds connect over UDP.

Known follow-up (Phase 2 — explicitly NOT in acceptance for Phase 1):
- [ ] Backfill bots become replicated `NetworkObject`s visible to remote clients (today they are
      server-local NavMeshAgents only).
