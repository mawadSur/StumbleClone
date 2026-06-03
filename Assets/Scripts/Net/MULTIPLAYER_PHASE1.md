# Multiplayer — Phase 1 (Final Setup)

Phase-1 makes networked players actually **spawn + be controllable** over Unity Relay, using
Netcode for GameObjects (`com.unity.netcode.gameobjects` 2.2.0) + the Unity Multiplayer Services
SDK (`com.unity.services.multiplayer` 2.2.3). The editor wiring is now **automated** — one menu call
(or one headless `-executeMethod`) does it all. What remains is **account-level** work that only a human
can do in the Unity Cloud dashboard, plus the 2-client test.

## Code in place

| File | Role |
|------|------|
| `Editor/MultiplayerSetup.cs` | One-call, idempotent wiring. Adds `NetworkObject` + `NetworkTransform` + `NetworkInputProvider` + `NetworkPlayerLink` to `Player.prefab`, and builds `Assets/Resources/Net/NetworkManager.prefab` (`NetworkManager` + `UnityTransport`, `NetworkConfig.PlayerPrefab` = Player). Menu **StumbleClone > Multiplayer > Setup** and headless `StumbleClone.EditorTools.MultiplayerSetup.Run`. |
| `NetworkGame.cs` | Runtime glue. `EnsureManager()` instantiates the Resources NetworkManager before a session; on the server, positions each connected client's auto-spawned Player at a `"Respawn"` spawn point. No-op offline. |
| `NetworkPlayerLink.cs` | On spawn, non-owners get `PlayerController.SetInputSource(NetworkInputProvider)` (driven by replicated input); the owner keeps local input. |
| `NetworkInputProvider.cs` | `NetworkBehaviour : IPlayerInput`. Owner streams local input to the server; non-owners read replicated values. |
| `NetworkBootstrap.cs` | Lower-level `StartHost/StartClient/ConfigureTransport` helpers (direct/LAN fallback). |

NGO + `NetworkConfig.PlayerPrefab` auto-spawn one owned Player per connected client. The Multiplayer
Services SDK's NGO network handler drives `NetworkManager.Singleton` directly, so creating/joining a
session over Relay auto-starts the NGO host/client — no manual `StartHost/StartClient` needed when using
sessions.

## Setup steps (in order)

### (a) Link a Unity Cloud project
- **Edit > Project Settings > Services** (or **Window > General > Services**). Sign in.
- Create/select an **Organization** and **Project**, then **Link** it. This writes the project ID into
  `ProjectSettings/`. (Alternatively create the project at https://cloud.unity.com and link by ID.)
- Without a linked cloud project, Relay/Lobby/Authentication calls fail at runtime.

### (b) Enable services in the Unity Cloud dashboard
On the linked project at https://cloud.unity.com, enable:
- **Relay** — the transport for internet play (NAT/firewall traversal; what sessions use here).
- **Lobby** — backs session discovery / join-by-code.
- **Authentication** — turn on the **Anonymous** sign-in provider (every player must be signed in before
  creating/joining a session).

> Free tiers cover small-scale testing. The Multiplayer Services package already pulls in the Relay, Lobby,
> Authentication and Core dependencies — no extra package installs are required.

### (c) Run the editor wiring once
- In the editor: **StumbleClone > Multiplayer > Setup**. Watch the Console for `[MultiplayerSetup]` logs.
- It is **idempotent** — safe to run again any time; it only adds what's missing.
- Headless / CI (also safe to chain into a build step):
  ```bash
  Unity -batchmode -quit -projectPath . \
    -executeMethod StumbleClone.EditorTools.MultiplayerSetup.Run
  ```
- After it runs: `Player.prefab` is a NetworkObject and `Assets/Resources/Net/NetworkManager.prefab`
  exists. `NetworkGame.EnsureManager()` loads that prefab at runtime, so nothing needs to be placed in
  any scene.

### (d) Drive a session from game code
Sign in, ensure the manager, then create/join. Minimal flow (matches the cached SDK signatures):
```csharp
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Multiplayer;
using StumbleClone.Net;

// Once at startup:
await UnityServices.InitializeAsync();
if (!AuthenticationService.Instance.IsSignedIn)
    await AuthenticationService.Instance.SignInAnonymouslyAsync();

NetworkGame.EnsureManager();   // must exist before a session (SDK drives NetworkManager.Singleton)

// HOST — create a Relay-backed session and show the join code:
var hostSession = await MultiplayerService.Instance.CreateSessionAsync(
    new SessionOptions { MaxPlayers = 8 }.WithRelayNetwork());
Debug.Log($"Join code: {hostSession.Code}");   // give this to the other player

// CLIENT — join by that code:
var session = await MultiplayerService.Instance.JoinSessionByCodeAsync(joinCode);
```
`CreateSessionAsync` returns an `IHostSession` (has `.Code`, `.Id`, `.MaxPlayers`, `.PlayerCount`,
`.Players`); `JoinSessionByCodeAsync` returns an `ISession`. `.WithRelayNetwork()` is the extension that
makes the SDK allocate Relay and auto-start NGO host/client. Leave with `session.LeaveAsync()`.

### (e) Test with 2 clients
You need **two running instances** signed in to the **same linked cloud project**:
- **Fastest loop:** Unity **Multiplayer Play Mode** (Window > Multiplayer Play Mode) — enable a second
  virtual player; both run from this project. One hosts, the other joins with the printed code.
- **Closest to shipping:** two builds (two browser tabs for WebGL, or two desktop instances). One hosts
  and prints the join **Code**; type that code into the other to join.

**What you should see**
- Two Player objects spawn, each at a different `"Respawn"` spawn point (server-positioned via
  `NetworkGame` → `NetworkTransform.Teleport`).
- Each instance owns one Player (`IsOwner == true`) and drives it with local input; the remote Player is
  driven by replicated input (`NetworkPlayerLink` → `SetInputSource`) and its position replicates via
  `NetworkTransform`. Move/jump/push on one side → the other side sees it.

## Notes & Phase-1 limitations (by design)

- **WebGL** uses Relay over secure WebSockets automatically through the SDK — no manual transport protocol
  toggling is needed for session-based play (the old `ConfigureTransport(useWebSockets:…)` path is only for
  the direct/LAN fallback in `NetworkBootstrap`).
- **Input + transform replicate**, but there is **no client-side prediction** yet — remote players show ~1
  network tick of latency and rapid presses can coalesce (see `NetworkInputProvider` edge-handling notes).
- **No networked bots, win logic, or lobby UI** yet. NavMesh bots stay server-side; replicating them,
  server-authoritative `RacerRegistry`/win logic, prediction/reconciliation, and 8-player matchmaking/lobby
  UI are Phase 2.
- The `NetworkManager` prefab lives under `Resources/Net/` so it loads in headless/CI builds with no scene
  edits; `NetworkGame.EnsureManager()` makes it a `DontDestroyOnLoad` singleton.

---
*Editor wiring is automated and idempotent (`MultiplayerSetup`); the cloud-link + service-enable steps
(a)/(b) are the only human-only prerequisites. All networking code compiles against the cached SDK and is
dormant until a session is created/joined.*
