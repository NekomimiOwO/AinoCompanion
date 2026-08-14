# CompanionR.E.P.O
A mod for the game R.E.P.O that adds an NPC/PET as Aino from genshin impact that helps the player, still under development. (just doing some final tests to release it)


[![Here a video of it](https://img.youtube.com/vi/xXLUuVCpgSU/0.jpg)](https://youtu.be/xXLUuVCpgSU)


Below, a test of how the readme will look like:

# Ai-Chan Companion

> An intelligent companion for **REPO**, inspired by Aino. Ai-Chan follows the team, accepts light items, brings objects to the cart, can carry tumbled players, and includes chat commands, interactive physics, and Steam-powered multiplayer synchronization.

**Plugin GUID:** `com.neko3004.aichancompanion`

---

## Overview

Ai-Chan Companion adds one pet companion to the game. She spawns automatically at the start of each level and in the shop, appears near a player, and chooses an owner to follow.

She uses NavMesh navigation to move, pathfinds toward her owner, and includes recovery systems to prevent her from getting stuck. Players can grab and throw her as a physics object; after landing, she recovers and resumes following her owner.

In multiplayer, the host/Master Client is authoritative for the AI. Other players receive synchronized position, rotation, state, owner, and interaction events.

## Features

- Automatic Ai-Chan spawn in levels and in the shop.
- Floating **Ai-Chan** name tag above the character.
- Automatic owner following with configurable distance.
- Automatic owner switching in multiplayer.
- Manual owner switching by the current owner.
- Chat commands to call, send away, jump, play dead, and change size.
- Petting system with animation, sound, and heart particles.
- Can be grabbed, carried, and thrown by players.
- Falling physics and recovery after being released or hit.
- No physical collision with players, preventing unwanted blocking and pushing.
- NavMesh movement, automatic jumps when stuck, and an emergency safety teleport.
- Accepts light objects held by players.
- Carries accepted items to the cart/delivery objective when a valid target is available.
- Can carry players in a downed/tumble state.
- Movement, idle, falling, stunned, jumping, and petting animations.
- Audio feedback for commands and interactions.
- Synchronization for players who join an ongoing session(when level changes).

## Installation

### Thunderstore Mod Manager

1. Install **BepInExPack** for REPO if it is not already in your profile.
2. Install **GenshinImpactOverhaul_REPO** by `GoblinKingShmee`.
3. Install **REPO_SteamNetworking_Lib** by `Rune580`.
4. Install **Ai-Chan Companion** in the same profile.
5. Install **MenuLib** by `nickklmao`.
6. Launch the game through Thunderstore Mod Manager.

### Manual installation

1. Install a BepInEx version compatible with REPO.
2. Install `GenshinImpactOverhaul_REPO` and `REPO_SteamNetworking_Lib` in `BepInEx/plugins`.
3. Extract `AiChanCompanion.dll` to `REPO/BepInEx/plugins/`.
4. Launch the game once to create the configuration file.

> **Important:** Ai-Chan uses the Aino prefab supplied by `GenshinImpactOverhaul_REPO`. The companion visual cannot be created without that dependency.

### Multiplayer

For a consistent multiplayer experience, every player in the lobby should use:

- Ai-Chan Companion on the same version;
- GenshinImpactOverhaul_REPO;
- REPO_SteamNetworking_Lib;
- compatible game and dependency versions.

The mod requires strict compatibility with the Steam networking library. Using different versions can prevent loading or correct synchronization.

## How it works

### Spawning and ownership

- In single-player, Ai-Chan is created locally at the beginning of a level or in the shop.
- In multiplayer, only the **Master Client/host** creates the authoritative pet instance.
- When joining an active room, a client requests synchronization after the Steam handshake; the host replies with the pet position and `ViewID` so that client can recreate the same pet locally.
- On startup, Ai-Chan selects the nearest player as her owner and starts following them.
- In multiplayer, ownership can automatically change every 3 minutes by default. The selection prioritizes players who have not yet been selected during that level.
- Only the current owner can request a manual ownership switch.

### Movement and recovery

Ai-Chan follows her owner through the NavMesh and stops near them according to the configured follow distances. If she encounters an obstacle or a partial path, she may perform an automatic jump. If she remains far above her owner for several seconds or falls into the void, the safety system teleports her to a navigable position near the owner.

She can be grabbed and thrown. While held or falling, her navigation is paused. When she touches the ground — or after the recovery timeout — she stands up, finds the NavMesh again, and returns to the following state.

### Items and rescue

1. Hold a light physics item.
2. Stand near Ai-Chan.
3. Press the give-item key, `R` by default.
4. She picks up the object and attempts to bring it to the available cart/delivery destination.

The same interaction can be used with a player in a downed/tumble state. Hold a downed teammate and press `R`, or, if your own character is downed, press `R` while holding no item. Ai-Chan will attempt to carry that player.

> The accepted maximum item mass is configurable and defaults to 3. The internal item-give distance defaults to 4.5 m.

## Controls

| Action | Default key | Requirements |
|---|---:|---|
| Pet Ai-Chan | `E` | Be within 3 m, look at Ai-Chan, and respect the 1-second cooldown |
| Give item / carry player | `R` | Be near the pet; the item or player must be valid |
| Switch owner | `F5` | Only the current owner can use it |

All three keys can be changed in the BepInEx configuration.

## Chat commands

To recognize a command, the chat message must include a pet keyword: `aino`, `ai-chan`, `aichan`, or `pet`.

| Command example | Effect | Who can use it |
|---|---|---|
| `Ai-Chan help` / `Aino ajuda` / `pet commands` | Shows the local help text | Any player |
| `Ai-Chan jump` | Makes Ai-Chan jump | Any player |
| `Ai-Chan come` or `Ai-Chan here` | Calls Ai-Chan close to the owner | Current owner |
| `Ai-Chan away` | Sends Ai-Chan away temporarily | Current owner |
| `Ai-Chan dead` or `Ai-Chan play dead` | Makes Ai-Chan play dead for about 6 seconds | Current owner |
| `Ai-Chan small` | Sets small size (0.5x) | Current owner |
| `Ai-Chan big` | Sets large size (1.8x) | Current owner |
| `Ai-Chan normal` | Restores normal size (1.0x) | Current owner |
| `Ai-Chan switch` / `pass` / `leave` | Transfers Ai-Chan to another player | Current owner |

> Commands that change the AI are processed by the Master Client. This prevents different clients from controlling the same pet at the same time.

## Configuration

After launching the game once, open the in‑game ModMenu configuration UI to adjust Ai‑Chan settings.

| Section | Option | Default | Range / description |
|---|---|---:|---|
| General | `Enable Debug Logs` | `true` | Enables network and debug logs in the console |
| General | `Enable State Transition Logs` | `false` | Logs detailed pet state transitions |
| General | `GiveItemDistance` | `4.5` | Maximum distance for accepting an item from a player's hand |
| Movement | `Speed` | `3.5` | Ai-Chan movement speed; 1 to 10 |
| Movement | `Auto Jump Stuck Delay` | `2.0` | Time spent stuck before attempting an automatic jump; 0.5 to 10 seconds |
| Movement | `Follow Range` | `2.2` | Distance at which she begins following; 0.5 to 10 |
| Movement | `Stopping Distance` | `1.65` | Distance at which she stops near the owner; 0.1 to 5 |
| Interaction | `Max Carried Mass` | `3.0` | Maximum item mass Ai-Chan can carry; 0.5 to 20 |
| Physics | `Body Mass` | `1.5` | Ai-Chan physical mass; 0.2 to 20 |
| Physics | `Stand Up Delay` | `2.0` | Delay before standing after a fall; 0 to 10 seconds |
| Physics | `Angular Drag` | `0.5` | Rotational resistance while thrown; 0 to 10 |
| Audio | `Volume` | `50` | Audio volume; 0 to 100 |
| Multiplayer | `Owner Switch Interval (Minutes)` | `3.0` | Automatic owner-switch interval; `0` disables it |
| Controls | `Give Item Key` | `R` | Item/rescue interaction key |
| Controls | `Pet Key` | `E` | Petting key |
| Controls | `Switch Owner Key` | `F5` | Key to transfer the pet to the next player |

## Multiplayer networking

Ai-Chan Companion combines the game's Photon room infrastructure with **REPO SteamNetworking Lib** to send custom pet packets through Steam.

> **REPO SteamNetworking Lib** is an API for mod developers to offload mod networking from REPO’s Photon servers, helping the REPO devs save bandwidth. Ai‑Chan Companion uses it to send pet packets over Steam networking.

### Authority

The Master Client is responsible for world-changing simulation:

- creating Ai-Chan in online sessions;
- running navigation, AI, and relevant physics;
- processing item-give and player-carry requests;
- switching the owner;
- publishing authoritative state.

Remote clients do not run pet navigation or physics. They reproduce the received position, rotation, state, and owner, while keeping the `Rigidbody` kinematic to avoid physics divergence.

### State updates

The host sends a snapshot every 0.05 seconds, up to 20 updates per second, containing:

- `PetViewID` to identify the instance;
- `OwnerViewID` to identify the current owner;
- pet state;
- sequence number;
- position;
- rotation.

On clients, short movement corrections are smoothed through interpolation. If the position difference exceeds 2.5 m, the pet is moved directly to the received position to correct visible desynchronization.

### Synchronized events

In addition to continuous state, Steam networking sends dedicated packets for:

- pet spawning and synchronization for late-joining players;
- synchronization requests to the host;
- item delivery;
- player-carry requests;
- picking up and dropping an item or player;
- petting;
- manual owner switching.

Item delivery, player carrying, and owner-switch events are sent to the host. The host resolves the referenced objects through their `PhotonView`, executes the authoritative action, and sends the necessary visual results to the other players.

## Troubleshooting

### Ai-Chan does not spawn

- Confirm that `GenshinImpactOverhaul_REPO` is installed and loads before this mod.
- Confirm that `REPO_SteamNetworking_Lib` is installed.
- Start a run or enter the shop; Ai-Chan does not spawn in the menu or lobby.
- Wait for level generation and the NavMesh to become available.
- Check the BepInEx console for errors and keep `Enable Debug Logs = true` while diagnosing the issue.

### A client cannot see the pet

- Make sure the host and clients have the mod and all dependencies installed.
- Make sure every player uses compatible versions.
- Rejoin the room if the Steam handshake fails; a client requests pet reconstruction about 2 seconds after joining.

### Ai-Chan does not accept an item

- Stay within the configured interaction distance.
- Hold a valid physics item that does not exceed `Max Carried Mass`.
- Make sure the pet is not dead, grabbed, or stunned.
- In multiplayer, wait for the host to process the request.

### Ai-Chan appears stuck

- Give the automatic jump or safety teleport a few seconds to activate.
- Avoid leaving her in areas without NavMesh.
- Adjust `Auto Jump Stuck Delay`, `Speed`, and follow-distance settings if needed.

## Compatibility and notes

- This mod is made for REPO and relies on game-provided classes, physics layers, NavMesh, and networking.
- The mod uses the Aino visual/prefab supplied by GenshinImpactOverhaul_REPO.
- In multiplayer, the host should maintain a stable connection because it is authoritative for the AI.
- Mods that heavily alter Photon, `PhysGrabObject` physics, carts, NavMesh, or the Aino prefab may cause incompatibilities.

## Credits

- **Ai-Chan Companion:** mod development.
- **BepInEx:** plugin loader.
- **Harmony:** runtime patching.
- **GenshinImpactOverhaul_REPO:** Aino prefab/visual into the game, by `GoblinKingShmee`.
- **REPO_SteamNetworking_Lib:** Steam-based packets and multiplayer synchronization by `Rune580`.
- **MenuLib (nickklmao):** in‑game configuration UI library.
- **REPO:** base game, physics, navigation, Photon, and interaction systems.

## Legal / Asset notice

- The Aino character model and related assets are the property of **HoYoverse**. This mod does not claim ownership of, nor distribute, HoYoverse assets.
- All other code and logic in Ai-Chan Companion are original to this mod.


## AI assistance disclaimer

This mod was developed with the assistance of AI tools for code generation and refactoring. All gameplay logic, system design, implementation requirements, test plans, and iterative feedback were authored and directed by the mod author through multiple review cycles. AI was used as a productivity aid, not as the designer of the mod’s behavior or features.
