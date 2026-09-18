# Work in progress

Planned features, phases, pending changes, known gaps and TODOs. This is the only place future work
is recorded: not in code comments, and not in the reference docs, which describe what the code does
now. When an item ships, delete it here and document the result in the file that covers its area.

Each entry names the area, what is planned or missing, and the file or type it touches.

## Open decisions, awaiting the project owner

Raised during review, none ruled on. **Do not act on these without asking.**

1. **Split `IAssetProvider` in two**, so assemblies that only ever load by key stop being forced to
   reference Addressables. Nine asmdefs reference it now; one did before, and two of the nine
   (`Config` and `Popups`) gain nothing from it. The trade-off is in
   [context/self-contained-minigames.md](context/self-contained-minigames.md), section 5. Worth
   deciding together with `Release(string)`, which the seam still does not have: everything loaded by
   key today has session lifetime, so nothing needs it, and if the seam is ever split the key half is
   its natural home.
2. **The unused `TView` type parameter** on `MinigameBase<TController, TView, TMinigame>`
   (`Minigames/Core/MinigameBase.cs`). It constrains nothing now that the view is an
   `AssetReferenceGameObject`.
3. **The untyped `NullReferenceException` for a view prefab with no `MinigameViewBase` on it.**
   `MinigameContainer.BeginAsync` does `prefab.GetComponent<MinigameViewBase>()` and hands the result
   straight to the resolver, so an `AssetReference` pointing at the wrong prefab surfaces as a
   `NullReferenceException` rather than as something under `ChestGameException`. A wrong or absent
   GUID is covered (that is a `MissingAssetException`); a right GUID on the wrong asset is not.
4. **Filter the Addressables package's own test** out of `ci/run-tests.sh` via `-assemblyNames`, so
   the EditMode count is the project's own rather than one higher and needing explanation.
5. **`ICurrencyManager` leaks `ResourceBankCallbacks<CurrencyType>`.** Its events are typed with the
   vendored library's delegate, so every consumer, including `Company.ChestGame.UI`, has to reference
   `TapNation.Modules` purely to subscribe to an event. Wrapping the delegate in a project-owned type
   would let `UI` drop that reference. Kept out of earlier passes so it would not muddy their diffs;
   it deserves its own.

## Planned features

### Currency: analytics hooks

`CurrencyManager.AddCurrency` and `CurrencyManager.TrySpendCurrency`
(`Assets/_Project/Scripts/Currency/CurrencyManager.cs`) are where a production game would fire an
analytics event on each currency change. Neither is wired to anything today. The two calls that
used to sit there as commented-out examples, verbatim:

```csharp
// GameAnalytics.NewResourceEvent(GAResourceFlowType.Source, currencyType.ToString(), amount, GAItemType,
//     _currencyManager.ResourceIdMap[currencyType]);
```

```csharp
// GameAnalytics.NewResourceEvent(GAResourceFlowType.Sink, currencyType.ToString(), amount, nameof(ConsumableAddedType.Coin),
//     source);
```

The first belongs after a successful add, the second after a successful spend.

### Currency: purchase flow on insufficient currency

`TrySpendCurrency` takes a `spawnCurrencyPurchasePopup` flag for exactly this case, but when the bank
reports `InsufficientAmount` and the flag is set, nothing happens: the branch is empty, and the
method falls through to the same log-and-return-false path as any other failure. Opening a shop or
purchase flow so the player can make up the difference is planned, not built. The placeholder that
marked the spot:

```csharp
// TODO: Open shop to complete the resource amount
```

## Saving

### Save writes still cost main-thread frame time

`ThreadHoppingStore` is not wired into any composition this game ships. `CurrencyResourceBankSaveHandle`
structurally refuses to be paired with one (see [saving.md](saving.md), "`Load()` blocks"), and
`ChestsMinigameController.Inject` refuses for the same reason. So the frame cost of encoding and
writing a save still lands on the main thread, as it always did under `DefaultResourceBankSaveHandle`,
bounded to once per coalescing window rather than once per coin. The route [saving.md](saving.md)
describes is to pre-load those saves during boot, in an async step ahead of
`GameLifetimeScope.RegisterCoreServices`, so `Load()` hands back what was already fetched; only then
can the wrapper go into the composition. Adding it to today's composition is not an option.

### Naming debt: the shared save service still has currency names

`GameLifetimeScope.BuildCurrencySaveService`, `CurrencySaveInputsOverride` and
`LegacyCurrencyPlayerPrefsKeyOverride` keep their currency-flavoured names, but the service they build
is what all three save keys (`currency`, `chests`, `meta`) read and write through. Renaming them would
touch `GameBootstrapperTests`, `GameLifetimeScopeTests` and [saving.md](saving.md) for no behavioural
gain, so the names stay for now and their declarations say what they actually govern.

## `RewardsManager` still throws an untyped `NotImplementedException`

`RewardsManager.cs:36` has the same shape the MVC pass fixed in `RewardReceivedPopup`: a
`CurrencyType` switch whose default arm throws `NotImplementedException`, so an unmapped currency
escapes as an exception nothing under `ChestGameException` can catch. The popup's version became
`UnmappedCurrencyIconException`; this one was left alone because it is reward logic rather than a
view, and widening the MVC pass into it would have muddied that diff. It wants the same treatment.

## Known gaps in the tests

- **`GameShellView`'s own behaviour is untested.** The shell's rules moved to `GameShellController`
  and `GameShellControllerTests` asserts them, so what is left unasserted is the view half: that a
  click before VContainer injection is swallowed by the `IsBound` guard, and that the button tracks
  `OnBusyChanged`. Both need a scene or the `ChestElementViewLifetimeTests` pattern (deactivate, add
  the component, reflect the fields in, reactivate).
- **No test proves the two currency labels get different currencies.** `CurrencyLabelControllerTests`
  asserts the controller's filtering and formatting, and `GameBootstrapperTests` asserts a
  `CurrencyLabelView` in the scene got bound, but it finds one with `FindAnyObjectByType`, so nothing
  asserts that the "GemsArea" and "CoinsArea" instances resolve to controllers watching
  `CurrencyType.Gems` and `CurrencyType.Coins` respectively.
- **`GameLifetimeScope.Configure`'s unwired-label branch is unexercised.** `BootStatusLabelTests`
  reads the label back and `BootStatusModelTests` covers the model, but `Configure` only runs through
  the real `LifetimeScope` lifecycle, so the case where the serialized `_bootStatus` is missing and
  the model ends up bound to nothing is not asserted anywhere.
- **The play-mode ordering test is dormant.**
  `ChestsMinigameIntegrationTests.OnTheRealPlayerLoop_NoProgressTickLandsAfterAChestOpens` passes
  with or without the `SetOpening` guard, because the real player loop currently orders the two tasks
  favourably. It is a canary for a future ordering change, not an active check.
- **`FakeGameClock` cannot reproduce the ordering bug it was meant to guard.** In the fake,
  `passedTime` and the delay share one clock, so the progress loop always exits on exactly the frame
  the delay comes due, under either ordering. The real risk is drift between `UniTask.Yield`
  accumulation and `UniTask.Delay`, which are separate accumulators in the engine but one in the fake.
  What actually protects the invariant is the `SetOpening` guard, covered by
  `SetOpening_AfterTheChestIsOpen_IsIgnored`. `OpeningIsUnaffectedByScheduling` is kept because
  scheduling independence is worth asserting, but it does not guard this.

## Pending verification

The checks that remain.

- **A stalled fetch is bounded only by a deadline nobody has watched fire in a real session.** Both
  the on-demand path and the preloader translate a deadline into a typed failure, tested against a
  fake that stalls on demand, but not against a real server that stops answering mid-download.
- **The timeout and retry values are not covered by any test, and are baked into the catalog at
  content-build time.** `ci/build-addressables.sh` has to run again before they mean anything on the
  wire; the bundles in `ServerData/` at the time of writing still carried 0.
