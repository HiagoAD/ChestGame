# ChestGame

Project made in Unity 6000.3.11f1

## Table of Contents
- [0. What is this?](#0-what-is-this)
- [1. High-Level Architecture](#1-high-level-architecture)
- [2. Key Design Decisions](#2-key-design-decisions)
- [3. Instructions to Build and Run](#3-instructions-to-build-and-run)

## 0. What is this?

The project started as a demonstration of async operations in Unity, specifically
managing the delayed opening of chests using UniTask with parallel tasks and
cancellation tokens.

It has since evolved into my personal study and showcase project. The goal is not to
build a functional, fun game, but rather to demonstrate architecture choices and
features commonly used across the mobile game industry: dependency injection,
event-driven currency systems, generic minigame and popup frameworks, and remote
config simulation.

### AI
This is not a vibe-coded project, but it is AI-enhanced. At the current state of the
project, nothing was done by AI, but my goal is to use it to write down the
boilerplate, while I handle the architecture and high-level decisions.
AI usage will be highlighted and documented as I proceed.

## 1. High-Level Architecture

The project uses the following libraries:
- [UniTask](https://github.com/Cysharp/UniTask) — async/await for Unity with no allocations
- [VContainer](https://github.com/hadashiA/VContainer) — Dependency Injection
- [Resource Bank](https://gitlab.com/tn-asset-library/resource-bank) — persistent currency management
- [Newtonsoft.Json](https://www.newtonsoft.com/json) — JSON deserialization for config loading

All project-specific files live under `Assets/_Project`. Dependencies are registered as singletons in `Assets/_Project/Scripts/Core/GameLifetimeScope.cs`.

### Entry point & game flow
- `GameManager.cs` is the main entry point. On button press, it requests a minigame instance from `IMinigameManager` and starts a new game.
- `ChestsMinigameController.cs` holds the core logic: managing chest states, remaining attempts, async opening (two parallel UniTasks), and reward distribution.
- `ChestsMinigameView.cs` and `ChestsMinigameChestElementView.cs` handle the UI, dynamically instantiating chest prefabs and updating visual state.

### Generic minigame framework
`MinigameBase`, `MinigameContainer`, and `MinigameManager` are a generic framework for registering and instantiating minigames through ScriptableObject definitions, with VContainer resolving all dependencies.

### Popup system
`PopupBase<TPopup, TData>` and `PopupManager` provide a typed popup framework. Popups receive strongly-typed data on initialization and are spawned onto a persistent `DontDestroyOnLoad` canvas.

### Currency & rewards
- `CurrencyManager` wraps Resource Bank, providing events (`OnCurrencyChanged`, `OnCurrencyCollected`, `OnCurrencySpent`) and persistence.
- `CurrencyWatcher.cs` subscribes to these events and updates TextMeshPro labels in the UI.
- `RewardsManager` selects a random currency reward based on config values and displays a `RewardReceivedPopup`.

## 2. Key Design Decisions

1. **Library adoption** — UniTask eases async handling, VContainer structures dependency management, and Resource Bank speeds up currency system development. These libraries were chosen to increase reliability and reduce boilerplate.

2. **Parallel async tasks for chest opening** — In `ChestsMinigameController`, the opening state is intentionally split into two concurrent UniTasks: one updating the progress slider every frame, the other awaiting the delay timer. This demonstrates how to handle multiple async operations running in parallel while also providing visual feedback of the chest state.

3. **Remote config simulation** — `LocalJsonGameConfig` loads a local JSON file with config values to simulate a server call. The config is accessed through an `IGameConfig` interface, so swapping to a real remote config provider requires no changes to consuming code.

4. **Generic minigame framework** — `MinigameBase<TController, TView, TMinigame>` uses generics and ScriptableObject definitions to make adding new minigames a matter of creating a new SO asset and controller/view pair, without modifying the framework itself.

5. **Typed popup system** — `PopupBase<TPopup, TData>` ensures each popup receives strongly-typed data at initialization, avoiding stringly-typed or untyped dictionaries.

6. **Prize location calculated per run** — The prize chest is determined at runtime on each attempt rather than stored in memory, to avoid being discoverable through memory inspection. Unrealistic for a production game, but it works as a demonstration.

More specific reasoning is written above key parts inside the scripts.

## 3. Instructions to Build and Run

* Open the project in **Unity 6000.3.11f1** (or compatible)
* To play in the Editor, open the main scene and press Play
* To build for Android, switch the target platform to Android and follow the standard build procedure

The project was validated on Editor and Android build, targeting 1080x1920 Portrait, with Landscape validated to be working. No further specific instructions are needed.