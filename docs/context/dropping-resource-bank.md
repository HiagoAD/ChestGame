# Context: dropping Resource Bank

Working context from the pass that removed Resource Bank, the vendored third-party currency library
(TapNation, assembly `TapNation.Modules`, under `Assets/AssetLibrary`), and moved the part of it
`CurrencyManager` used into the project. That is the scope of this file: **why the library went, what
replaced it, the behaviour the replacement keeps, and what is still not pinned.** How currency is
saved is in [saving.md](../saving.md), "Currency: the first real caller". That section is mostly the
history of how currency was put on the save system while the library was still in the project, and its
first subsection points here.

**Kept current.** Where later work changes something described here, update the description rather
than appending a correction. Both earlier context files,
[assemblies-and-tests.md](assemblies-and-tests.md) and
[self-contained-minigames.md](self-contained-minigames.md), listed the `ICurrencyManager` leak as
open. They now mark it resolved and point here.

---

## 1. Why it was dropped

**The library leaked through `ICurrencyManager`.** The interface's three events were typed with the
library's `ResourceBankCallbacks<CurrencyType>.ResourceAmountChangedDelegate`, so every assembly that
subscribed needed a reference to `TapNation.Modules` for the sake of a delegate type.
`Company.ChestGame.UI` was the one that showed it, referencing a currency library purely to subscribe
to an event.

**It was a third-party dependency for about 60 lines of logic.** What the project used was a
dictionary of balances, validation of an add or a spend, three callbacks and a save seam.
`CurrencyManager` was the only caller, and `ResourceBank<T>` was only ever instantiated with
`CurrencyType`, so its generality bought nothing.

**Its default handler was a hazard.** Given no save handler, the library fell back silently to a
handler that wrote to `PlayerPrefs`. That would have written under the same key
`CurrencyLegacyImport` reads, and nothing would have thrown.

---

## 2. How this work was run

The protocol is the one in [self-contained-minigames.md](self-contained-minigames.md) section 2, with
one change the project owner asked for: the phases ran back to back instead of stopping for approval
after each, and the branch is reviewed as a whole at the end. Sonnet subagents wrote each phase
and never committed. The lead session reviewed every diff and committed each phase only after that
review.

0. **Harness.** No Unity Editor was available, so a .NET harness outside the repo compiled and ran
   the affected code instead. See section 7.
1. **Characterization tests**, written against the old, library-backed `CurrencyManager`, so each one
   pins what the library did and not what the replacement happened to do.
2. **Replace.** `CurrencyManager` and its seams rewritten, and `TapNation.Modules` removed from every
   asmdef.
3. **Delete.** The vendored folder.
4. **Docs.** This file and the pages it links to.

| Commit | What landed |
|---|---|
| `508fa22` | The characterization tests: zero spends with opt-in, negative and over-balance spends with opt-in, the cheat reset on a zero and a non-zero balance, event order within one operation, load-once at construction, and saves that miss a currency or carry a null `ResourceAmount` |
| `5dcbc5c` | `CurrencyManager` owning balances, validation and events; `CurrencyChangedHandler`; `ICurrencySaveHandler`; the renames below; `SaveException.NoSaveHandler`; `TapNation.Modules` out of every asmdef |
| `7d5ee19` | `Assets/AssetLibrary` deleted, its project dropped from `ChestGame.slnx`, and the two ordering tests renamed after the project's own methods |

---

## 3. Design decisions, and why

### Folded into `CurrencyManager`, not a separate bank class

There is one caller and nothing generic to serve, so a separate `CurrencyBank` would be a second type
that only `CurrencyManager` ever touched. The balances are a `Dictionary<CurrencyType, long>` inside
`CurrencyManager`, and the two operations are private methods beside it, with a private `Rejection`
enum standing in for the library's error type. Callers still learn only that an add or a spend
failed, not why.

### A project-owned delegate with the library's signature

`CurrencyChangedHandler(CurrencyType currency, long amount, long balance, string source)` replaced
`ResourceBankCallbacks<CurrencyType>.ResourceAmountChangedDelegate` in `ICurrencyManager`. The
parameters are the same, so every subscriber compiles unchanged and `Company.ChestGame.UI` no longer
needs the library. `amount` is positive on `OnCurrencyCollected` and `OnCurrencySpent` and signed on
`OnCurrencyChanged`, where a spend is negative. `balance` is the balance after the change.

### `ICurrencySaveHandler`, with `CurrencySaveDocument` as the only state type

`ICurrencySaveHandler` is `void Save(CurrencySaveDocument)` and `CurrencySaveDocument Load()`. It
replaces `IResourceBankSaveHandler<CurrencyType>`, and `ResourceBankState<CurrencyType>` has no
successor because the document was already the type the project persisted. See
[saving.md](../saving.md), "`CurrencySaveDocument`, and a `new()` constraint the library's model could
not satisfy".

Ownership runs through copies. `CurrencyManager` hands `Save` a fresh document each time, so a
handler may keep what it is given: `InMemoryCurrencySaveHandler` keeps it in a field and
`CurrencySaveHandler` passes it to the scheduler, which holds it until the write. It copies what
`Load` returns entry by entry and does not adopt it, so a handler that still holds the loaded
document never sees the manager's later changes. `Load` returning null means a first run.

### Renames, GUIDs kept

| Before | After |
|---|---|
| `CurrencyResourceBankSaveHandle` | `CurrencySaveHandler` |
| `CurrencyResourceBankSaveHandleTests` | `CurrencySaveHandlerTests` |
| `InMemoryResourceBankSaveHandler` | `InMemoryCurrencySaveHandler` |
| `TryAddResourceAmount_HandsTheNewStateToTheSaveHandler_BeforeAnyCallbackFires` | `AddCurrency_HandsTheNewStateToTheSaveHandler_BeforeAnyCallbackFires` |
| `TryToSpendResource_HandsTheNewStateToTheSaveHandler_BeforeAnyCallbackFires` | `TrySpendCurrency_HandsTheNewStateToTheSaveHandler_BeforeAnyCallbackFires` |

The `.meta` files moved with the first three, so their GUIDs are unchanged. The last two are test
methods in `CurrencySaveHandlerTests`; their old names were library method names.

### A null handler throws

`new CurrencyManager(null)` throws `SaveException.NoSaveHandler()`. The library's silent fallback to
`PlayerPrefs` is gone on purpose, for the reason in section 1. `CurrencyManagerTests` pins it with
`Constructor_WithNoSaveHandler_ThrowsSaveException`.

### Strings that stayed frozen

The on-disk format is unchanged: the `currency` key, holding `{"ResourceAmount":{...}}`.
`CurrencySaveHandler.SaveKey` is still `"currency"`. `CurrencyLegacyImport.DefaultLegacyKey` is still
`"ResourceBankSaveData_CurrencyType"`, and the import still renames what it reads to
`<key>.migrated`. Installed players' saves depend on those strings, so the legacy key keeps the old
library's name for good. The `ResourceAmount` property on `CurrencySaveDocument` is a library name for
the same reason: its name, type and initializer are the JSON's shape.

---

## 4. The behaviour contract

The replacement keeps the library's behaviour. Most of it is pinned in `CurrencyManagerTests`. The
order of the save against the events, and saves that miss a currency or carry a null
`ResourceAmount`, are pinned in `CurrencySaveHandlerTests`.

- An add of 0 or less is rejected: no balance change, no events, no save, and an error logged by
  `AddCurrency`.
- A successful add saves, and then raises `OnCurrencyCollected` and `OnCurrencyChanged`, both with
  the positive amount.
- A successful spend saves, and then raises `OnCurrencySpent` with the positive amount and
  `OnCurrencyChanged` with the negative one.
- A spend of 0 is rejected unless the caller passes `acceptZeroAmount`. With it, the spend is a whole
  operation: one save, and both events raised with an amount of 0. A negative spend, or one beyond
  the balance, is rejected even with the opt-in, with no events and no save.
- The amount is judged before the balance, so a spend of 0 is a zero-amount rejection whatever is
  held, and only that rejection can be opted past.
- `CHEAT_ResetCurrencyAmount` spends the whole balance as `"CHEAT"`. On a balance of 0 it is refused
  without a log, an event or a save. On any other balance it is an ordinary spend of all of it.
- `Load` runs once, synchronously, from the constructor, and never again.
- A currency the loaded save does not list starts at 0, and so does every currency when the document
  or its `ResourceAmount` is null.
- The balance is changed in memory before the events and the save, in both operations, so a listener
  sees the new balance from `GetCurrencyAmount`.

---

## 5. Save before notify, and its known consequence

Both operations change the balance, save, and then raise their events. That is the library's order
as it stood when it was removed: as first vendored its add raised its events before saving, and it
was edited to save first because a throwing listener took the add's save down with it. Tests pin the
order for both operations (`AddCurrency_HandsTheNewStateToTheSaveHandler_BeforeAnyCallbackFires` and
`TrySpendCurrency_HandsTheNewStateToTheSaveHandler_BeforeAnyCallbackFires`), and
`ACollectedListenerThatThrows_DoesNotStopTheNewBalanceBeingSaved` pins the reason. What the order
means for durability is in [saving.md](../saving.md), "The save-then-notify ordering no longer means
what it used to".

**Open follow-up, not fixed here.** There is no `try` around the listeners, so a listener that throws
escapes `AddCurrency` or `TrySpendCurrency` after the change has been saved. A caller then sees an
exception for an operation that happened, and on a spend never sees `true`. If the throw came from
an `OnCurrencyCollected` or `OnCurrencySpent` listener, `OnCurrencyChanged` is never raised. A save
that throws leaves the balance already changed in memory. Fixing either changes the contract, so it
was left alone.

---

## 6. What is not pinned

No test covers these, and the pass did not change them.

- **A save that names a currency no longer in `CurrencyType`.** Whether the codec lets such a document
  reach `CurrencyManager` is untested. If it did, the constructor copies every entry it is given, so
  the stray balance would be kept and written back on the next save.
- **`long` overflow on an add.** `CurrencyManager` adds to the balance with no overflow guard and no
  test goes near `long.MaxValue`.
- **The empty `spawnCurrencyPurchasePopup` branch.** `TrySpendCurrency` has a branch for an
  insufficient spend with the flag set, and it holds only a `TODO` to open the shop. Passing `true`
  does nothing today, and nothing asserts that.

---

## 7. What is verified, and what is not

**Nothing here ran in Unity.** The session that did this work had no Unity Editor. Everything below
was checked with a .NET harness built for this pass, which lived in that session's scratch space and
is not in the repo.

### What the harness is

One project per asmdef, generated from the asmdef files on every run, so each assembly sees only what
its `references` list. `Company.ChestGame.Common`, `Saving` and `Currency` compile in full, and
`TapNation.Modules` did while it existed. `Company.ChestGame.UI` compiles `CurrencyWatcher.cs` only.
The test projects compile the doubles from `Tests/Common` that the currency tests need, the three
currency fixtures, and 28 Saving and Common fixtures. Production assemblies target netstandard2.1 at
C# 9, the tests run on NUnit 3.5, UniTask is built from the commit `packages-lock.json` pins, and the
UnityEngine surface these assemblies use is stubbed. That includes a reimplementation of `LogAssert`,
so an unexpected `Debug.LogError` fails a test the way it does in Unity.

It enforces asmdef boundaries. On a copy of `main`, removing `TapNation.Modules` from the UI asmdef
made `CurrencyWatcher.cs` fail with CS0012.

### Results, each on a `git archive` snapshot of the commit

| Snapshot | `CurrencyManagerTests` | Save handler fixture | `CurrencyLegacyImportIntegrationTests` | Whole harness |
|---|---|---|---|---|
| `main` | 17/17 | 9/9 | 4/4 | 306/306 |
| `508fa22`: the characterization tests against the library | 25/25 | 11/11 | 4/4 | 316/316 |
| `5dcbc5c`: the replacement, library still on disk but unreferenced | 28/28 | 11/11 | 4/4 | 319/319 |
| After `7d5ee19`: library deleted | 28/28 | 11/11 | 4/4 | 319/319 |

The `508fa22` row is the point of phase 1. Every new characterization test passed against the
library's own code before any of it was replaced.

### Mutation

Each new test was checked by breaking the code it guards and confirming that test fails.

- **On `508fa22`, against the library (10, all caught):**
  - a zero spend with opt-in returning early without saving;
  - a negative spend, or one beyond the balance, let through by the opt-in;
  - the cheat's source changed;
  - the cheat accepting a zero balance;
  - Collected and Changed swapped, and Spent and Changed swapped;
  - a reload on every read;
  - missing currencies filled only when the save is empty;
  - a null `ResourceAmount` dereferenced.
- **On HEAD, against `CurrencyManager` and `CurrencySaveDocument` (15, all caught):** the same ten,
  plus:
  - `From` aliasing the live dictionary;
  - the zero-fill writing into the loaded document;
  - the null-handler guard removed;
  - an add saving before its events, and a spend raising its events before it saves.

In every case the targeted test failed. Most failed alone.

### Not verified

- **Unity's own compile and test run.** Neither has happened. `GameLifetimeScope.cs` and
  `GameLifetimeScopeTests.cs` changed and are outside the harness, because they need VContainer,
  Addressables and the rest of the scope. They were checked by reading only. The PlayMode suite was
  not run.
- **Harness fidelity.** It runs on CoreCLR, not Mono. Its `LogAssert` is a reimplementation, not
  Unity's. `PlayerPrefs` is in memory, and UniTask has no PlayerLoop.
- **No playtest:** booting, the Coins and Gems labels, a chest reward landing, a relaunch keeping the
  balance, and an old `ResourceBankSaveData_CurrencyType` entry importing once. No Android build
  either.

To close these, run `rm -rf ci-results && ci/run-tests.sh` with the editor closed, then the playtest
above.
