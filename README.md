# CompanionR.E.P.O
A mod for the game R.E.P.O that adds an NPC/PET as Aino from genshin impact that helps the player, still under development. (just doing some final tests to release it)


[![Here a video of it](https://img.youtube.com/vi/xXLUuVCpgSU/0.jpg)](https://youtu.be/xXLUuVCpgSU)


Below, a test of how the readme will look like:

# Ai-Chan Companion

> An intelligent companion for **REPO**. Ai-Chan follows the team, accepts light items (can change in configs), brings objects to the cart, can carry tumbled players, and includes chat commands, interactive physics, and Steam-powered multiplayer synchronization.

**Plugin GUID:** `com.neko3004.aichancompanion`

---

## Overview

Ai-Chan Companion adds one pet companion to the game. She spawns automatically at the start of each level and in the shop, appears near a player, and chooses an owner to follow. 

She uses advanced NavMesh navigation and Ghost Probing to move intelligently around obstacles, pathfinds toward her owner, can open doors natively, and includes recovery systems to prevent her from getting stuck. Players can grab and throw her as a physics object; after landing, she recovers and resumes following her owner.

In multiplayer, the host/Master Client is authoritative for the AI. Other players receive synchronized position, rotation, state, owner, and interaction events.

## Features

- Automatic Ai-Chan spawn in levels and in the shop.
- Floating **Ai-Chan** name tag above the character.
- **Minimap Tracker:** Ai-Chan appears as a distinct pink/magenta circle marker on the dirt finder minimap.
- Automatic owner following with configurable distance.
- Automatic owner switching in multiplayer.
- Manual owner switching by the current owner.
- Chat commands to call, send away, jump, play dead, change to preset sizes, or set a custom size.
- Petting system with animation, sound, and heart particles (heart particles not working properly yet).
- Can be grabbed, carried, and thrown by players.
- Falling physics, knockdown impact resistance, and recovery after being released or hit by fast-moving objects.
- No physical collision with players, preventing unwanted blocking and pushing.
- Advanced NavMesh movement with Ghost Probing to avoid tables/walls, automatic jumps when stuck, try door opening, and an emergency safety teleport when stuck.
- Accepts objects held by players based on configurable mass limits.
- **Carried Item Scaling:** Items can optionally inherit her scale while she carries them (host only config).
- Carries accepted items to the cart/delivery objective when a valid target is available. If two carts or more are present, she chooses the nearest; if no carts are present, she will go to the active extractor.
- Can carry players in a tumble state.
- Audio feedback for commands and interactions.
- Network Profiler for tracking the mod steam data usage.

## Installation

### Thunderstore Mod Manager

1. Install **GenshinImpactOverhaul_REPO** by `GoblinKingShmee`. (If you want Aino but don't want the GenshinImpactOverhaul_REPO mod to replace the enemies, simply disable the replacement in the GenshinImpactOverhaul_REPO mod settings; my mod should still work.)
2. Install **REPO_SteamNetworking_Lib** by `Rune580`.
3. Install **Ai-Chan Companion** in the same profile.
4. Install **MenuLib** by `nickklmao`.
5. Launch the game through Thunderstore Mod Manager or another compatible mod manager.

> **Important:**
> Ai-Chan uses the Aino prefab supplied by `GenshinImpactOverhaul_REPO`. The companion visual cannot be created without that dependency.
> REPO_SteamNetworking_Lib is necessary for multiplayer.

### Multiplayer

Multiplayer should already work for now...at least for what I tested with my friend, which I'm grateful for spending hours with me compiling, open the game, test, fail, fix, compile,open...etc <3 

For a consistent multiplayer experience, every player in the lobby should use:

- Ai-Chan Companion on the same version;
- GenshinImpactOverhaul_REPO;
- REPO_SteamNetworking_Lib;
- MenuLib;

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

Ai-Chan follows her owner through the NavMesh and stops near them according to the configured follow distances. With the new Ghost Probing system, she anticipates walls and obstacles to navigate. She can also  open closed doors(or at least try) in her path. 

If she encounters an obstacle or a partial path, she may perform an automatic jump. If she remains far above her owner for several seconds or falls into the void, the safety system teleports her to a navigable position near the owner.

She can be grabbed and thrown. While held or falling, her navigation is paused. When she touches the ground — or after the recovery timeout — she stands up, finds the NavMesh again, and returns to the following state. Fast-moving heavy objects hitting her will also knock her down based on the configured impact resistance.

### Items and rescue

1. Hold a physics item.
2. Stand near Ai-Chan.
3. Press the give-item key, `R` by default.
4. She picks up the object and attempts to bring it to the available cart/delivery destination.
5. If two or more players are holding the item, she will not carry it to prevent sync errors.
6. If she is grabbed or falls during the delivery, the item will drop on the ground near her.

The same interaction can be used with a player in a downed/tumble state. Hold a downed teammate and press `R`, or, if your own character is downed, press `R` while holding no item. Ai-Chan will attempt to carry that player.

> The accepted maximum item mass is configurable and defaults to 3. The internal item-give distance defaults to 4.5 m. Carried items can optionally scale to match Ai-Chan's current size by enabling the `Inherit Pet Scale On Carry` setting.

## Controls

| Action | Default key | Requirements |
|---|---:|---|
| Pet Ai-Chan | `E` | Be within 3 m, look at Ai-Chan, has an 1 second cooldown |
| Give item / carry player | `R` | Be near the pet; the item or player must be valid |
| Switch owner | `F5` | Only the current owner can use it |

All three keys can be changed in the configuration.

## Chat commands

To recognize a command, the chat message must include a pet keyword: `aino`, `ai-chan`, `aichan`, or `pet`.

| Command example | Effect | Who can use it |
|---|---|---|
| `Ai-Chan help` / `commands` | Shows the local help text | Any player |
| `Ai-Chan net` / `rede` | Prints network profiler stats to the local console | Any player (Local only) |
| `Ai-Chan jump` | Makes Ai-Chan jump | Any player |
| `Ai-Chan come` or `here` | Calls Ai-Chan close to the owner | Current owner |
| `Ai-Chan away` | Sends Ai-Chan away temporarily | Current owner |
| `Ai-Chan dead` or `play dead` | Makes Ai-Chan play dead for about 6 seconds | Current owner |
| `Ai-Chan small` | Sets small size (0.5x) | Current owner |
| `Ai-Chan big` | Sets large size (1.8x) | Current owner |
| `Ai-Chan normal` | Restores normal size (1.0x) | Current owner |
| `Ai-Chan size 2.5`/`Ai-Chan size 0.1` etc... | Sets a custom exact size (e.g., 2.5x) | Current owner |
| `Ai-Chan switch` / `pass` | Transfers Ai-Chan to another player | Current owner |

> The mod only identifies keywords, so "become small aino" will still be recognized as "Sets small size (0.5x)".
> Commands that change the AI are processed by the Master Client. This prevents different clients from controlling the same pet at the same time.
> Chat messages are read locally only to trigger pet commands and are not hosted, stored, or sent to any external server.
> Why not just a keybind? Aside from there being so many commands, I thought it would be cool to hear commands from friends, since the game has a chat-reading system.

## Configuration

After launching the game once, open the in‑game ModMenu configuration UI to adjust Ai‑Chan settings.

| Section | Option | Default | Range / description |
|---|---|---:|---|
| General | `GiveItemDistance` | `4.5` | Maximum distance to accept an item from a player's hand |
| Movement | `Speed` | `3.5` | Ai-Chan movement speed; 1 to 10 |
| Movement | `Auto Jump Stuck Delay` | `2.0` | Time spent stuck before attempting an automatic jump |
| Movement | `Follow Range (Start)` | `2.2` | Distance at which she begins following |
| Movement | `Stopping Distance(Stop)`| `2.0` | Distance at which she stops near the owner |
| Interaction | `Max Carried Mass` | `3.0` | Maximum item mass Ai-Chan can carry; 0.5 to 20 |
| Interaction | `Inherit Pet Scale On Carry`| `false` | Carried items scale proportionally with the pet's size |
| Physics | `Body Mass` | `1.5` | Ai-Chan physical mass; 0.2 to 20 |
| Physics | `Knockdown Impact Resistance`| `2.0` | Minimum impact speed (m/s) required to knock Ai-Chan down |
| Physics | `Stand Up Delay` | `2.0` | Delay before standing after a fall; 0 to 10 seconds |
| Physics | `Angular Drag` | `0.5` | Rotational resistance while thrown; 0 to 10 |
| Audio | `Volume` | `50` | Audio volume percentage; 0 to 100 |
| Multiplayer | `Owner Switch Interval` | `3.0` | Automatic owner-switch interval in minutes; `0` disables it |
| Controls | `Give Item Key` | `R` | Item/rescue interaction key |
| Controls | `Pet Key` | `E` | Petting key |
| Controls | `Switch Owner Key` | `F5` | Key to transfer the pet to the next player |
| Logs | `Enable Debug Logs` | `true` | Enables network and debug logs in the console |
| Logs | `Enable State Transition Logs`| `false` | Logs detailed pet state transitions |
| Logs | `Enable NavMesh Transition Logs`| `false`| Logs when Ai-Chan enters or leaves the NavMesh |
| Performance | `Enable Ghost Probing` | `true` | Enables multi-ray pathfinding to avoid tables/walls smoothly |
| Performance | `Ghost Probe Distance` | `2.5` | Distance ghost probes look ahead to avoid walls |
| Performance | `Ghost Probe Update Interval`| `0.1` | How often pathfinding is calculated (lower = faster reaction) |
| Performance | `Ghost Probe Rays` | `7` | Number of projection rays. Higher = Smarter, Lower = Better performance |
| Performance | `Enable Debug Rays` | `false` | Visualizes the ghost simulation paths in-game (Only the host sees it, since all the pet's navigation calculations are handled by the host.) |
| Performance | `Debug Rays Fade Time` | `0.25` | How long debug rays remain visible on screen |
| Performance | `Debug Breadcrumbs Fade Time`| `0.15` | How long breadcrumb trail rays remain visible |

## Multiplayer networking

Ai-Chan Companion combines the game's Photon room infrastructure with **REPO SteamNetworking Lib** to send custom pet packets through Steam.

> **REPO SteamNetworking Lib** is an API for mod developers to offload mod networking from REPO’s Photon servers, helping the REPO devs save bandwidth. Ai‑Chan Companion uses it to send pet packets over Steam networking.

### Authority

The Master Client is responsible for:

- creating Ai-Chan in online sessions;
- running navigation, AI, door interactions, and relevant physics;
- processing item-give and player-carry requests;
- switching the owner;

Remote clients do not run pet navigation or physics. They reproduce the received position, rotation, state, and owner, while keeping the `Rigidbody` kinematic to avoid physics divergence.

### State updates

The host sends a snapshot every 0.05 seconds, up to 20 updates per second, containing:

- `PetViewID` to identify the instance;
- `OwnerViewID` to identify the current owner;
- pet state;
- sequence number;
- position;
- rotation.

On clients, short movement corrections are smoothed through interpolation. If the position difference exceeds 3.0 m, the pet is moved directly to the received position to correct visible desynchronization.

### Synchronized events

In addition to continuous state, Steam networking sends dedicated packets for:

- pet spawning and synchronization for late-joining players;
- synchronization requests to the host;
- item delivery;
- player-carry requests;
- picking up and dropping an item or player (including scale inheritance);
- petting;
- manual owner switching.

Item delivery, player carrying, and owner-switch events are sent to the host. The host resolves the referenced objects through their `PhotonView`, executes the authoritative action, and sends the necessary visual results to the other players.

## Troubleshooting

### Ai-Chan does not spawn

- Confirm that `GenshinImpactOverhaul_REPO` is installed and loads before this mod.
- Confirm that `REPO_SteamNetworking_Lib` is installed.
- Check the BepInEx console for errors and keep `Enable Debug Logs = true` while diagnosing the issue.

### A client cannot see the pet

- Make sure the host and clients have the mod and all dependencies installed.
- Make sure every player uses compatible versions.
- Rejoin the room if the Steam handshake fails; a client requests pet reconstruction about 2 seconds after joining.

### Ai-Chan does not accept an item

- Stay within the configured interaction distance.
- Hold a valid physics item that does not exceed `Max Carried Mass`.
- Make sure the pet isn't already carrying something.

### Ai-Chan appears stuck

- Give the automatic jump or safety teleport a few seconds to activate.
- The new Ghost Probing system will attempt to navigate around most objects automatically.
- Avoid leaving her in areas without NavMesh.
- Adjust `Auto Jump Stuck Delay`, `Speed`, and `Ghost Probe Rays` settings if needed.

## Compatibility and notes

- This mod is made for REPO and relies on game-provided classes, physics layers, NavMesh, and networking.
- The mod uses the Aino visual/prefab supplied by GenshinImpactOverhaul_REPO.
- In multiplayer, the host should maintain a stable connection because it is authoritative for the AI.
- Mods that heavily alter Photon, `PhysGrabObject` physics, carts, NavMesh, or the Aino prefab may cause incompatibilities.

## Credits

- **Ai-Chan Companion:** mod development.
- **BepInEx:** plugin loader.
- **Harmony:** runtime patching.
- **GenshinImpactOverhaul_REPO:** Insert Aino prefab/visual into the game, by `GoblinKingShmee`.
- **REPO_SteamNetworking_Lib:** Steam-based packets and multiplayer synchronization by `Rune580`.
- **MenuLib (nickklmao):** in‑game configuration UI library.
- **REPO:** base game, physics, navigation, Photon, and interaction systems.

##Thanks

A big thank you to GoblinKingShmee. I asked him via Discord, and he gave me permission to use his mod to load the model it adds to the game into my own mod.


## Legal / Asset notice

- The Aino character model and related assets are the property of **HoYoverse**. This mod does not claim ownership of, nor distribute, HoYoverse assets.

## AI assistance disclaimer

This mod was developed with the assistance of AI tools for code generation and refactoring. Since I don't know C#, I heavily relied on AI to write the code while I tested, provided feedback, compiled, and rewrote parts the way I wanted. All gameplay logic, system design, implementation requirements, test plans, and iterative feedback were authored and directed by me through multiple review cycles (I gained over 50 hours of playtime just testing the mod -_-)

AI was used as a productivity aid, not as the designer of the mod's behavior or features. Most of the time, the AI would break a mechanic that was working perfectly (like grab, animations, or pathfinding/following) and couldn't fix it. When this happened, I was forced to roll back to an earlier save, losing progress. Sometimes, realizing my approach wasn't going to work, I had to start over from scratch again.

For example, I was suggested to use an in-game item as a grab mechanism for Ai-Chan by inserting the item invisibly inside the model. It kind of worked—or so I thought—but it was never going to truly work because of collision issues. If the item broke, it disappeared along with its value, mechanics, and the model of Ai-Chan. It just didn't work! :)

A lot was fixed through this back-and-forth process. Many hours were spent on this mod, which I created with a lot of care~

## Known issues

- Sometimes she won't find the path to the cart if it is too far away of if the mat has dead-ends on the way back.
- Her pathfinding is kinda experimental, so she might get lost if no one is in her base vision.
- When spawning, she sometimes won't attach to the NavMesh properly and won't move (or moves very slowly). Just grab her and release; she will correct it.
