# Context: dropping Resource Bank

Working context from the pass that removed Resource Bank, the vendored third-party currency library
(TapNation, assembly `TapNation.Modules`, under `Assets/AssetLibrary`), and moved the part of it
`CurrencyManager` used into the project. That is the scope of this file: **why the library went, what
replaced it, the behaviour the replacement keeps (all of it except what a failing listener or a
failing save does, section 5), and what is still not pinned.** How currency is saved is
in [saving.md](../saving.md), "Currency: the first real caller". That section is mostly the history
of how currency was put on the save system while the library was still in the project, and its first
subsection points here.

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
   pins what the library did and not what the replacement happened to do. The work was rebased onto
   `main`'s test-suite revamp afterwards; section 5 has what that changed.
2. **Replace.** `CurrencyManager` and its seams rewritten, and `TapNation.Modules` removed from every
   asmdef.
3. **Delete.** The vendored folder.
4. **Docs.** This file and the pages it links to.

| Commit | What landed |
|---|---|
| `683023f` | The characterization tests, against the library as edited on `main`: zero spends with opt-in, negative and over-balance spends with opt-in, the cheat reset on a zero and a non-zero balance, event order within one operation, load-once at construction, and saves that miss a currency or carry a null `ResourceAmount` |
| `64678aa` | `CurrencyManager` owning balances, validation and events; `CurrencyChangedHandler`; `ICurrencySaveHandler`; the renames below; `SaveException.NoSaveHandler`; `TapNation.Modules` out of every asmdef. Saves before notifying in both operations, as the library on `main` did; listeners not isolated |
| `fbedbef` | `Assets/AssetLibrary` deleted, its project dropped from `ChestGame.slnx`, and `main`'s two order tests renamed after the project's own methods (section 3) |
| `f15c15d` | What save-then-notify left open: the snapshot handed to `Save` before the balance is assigned, each listener isolated, specification tests added. `main`'s three order tests stay; the throwing-listener one now expects the logged exception |

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
methods in `CurrencySaveHandlerTests` that `main`'s test-suite revamp wrote. Their old names were
library method names, and they still exist under the new ones.

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

The replacement keeps the library's behaviour, except what happens when a listener or `Save` throws,
and where the in-memory assignment sits against the save (section 5). Most of it is pinned in
`CurrencyManagerTests`. Saves that miss a currency or carry a null `ResourceAmount` are pinned in
`CurrencySaveHandlerTests`, which also pins what a listener can do with a change it is told about.

- An add of 0 or less is rejected: no balance change, no events, no save, and an error logged by
  `AddCurrency`.
- A successful add saves, assigns the balance in memory, and then raises `OnCurrencyCollected` and
  `OnCurrencyChanged`, both with the positive amount.
- A successful spend does the same in the same order, raising `OnCurrencySpent` with the positive
  amount and `OnCurrencyChanged` with the negative one.
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
- By the time a listener runs, in both operations, the change is in memory (`GetCurrencyAmount` is
  current) and has been handed to the save handler. It is not durable yet, and a listener that
  flushes the currency scheduler writes it.
- A listener that throws is logged and stops nothing: not the other listeners, not `OnCurrencyChanged`
  after a throwing `Collected` or `Spent` listener, not the save, not the caller's result.
- A `Save` that throws propagates unchanged and nothing has changed: no balance change, no events,
  no success log.

---

## 5. The save and notify order, and failing listeners

Both operations follow one order. Validate, compute the new balance, hand a snapshot of the new
state to `ICurrencySaveHandler.Save`, and only after `Save` returns assign the balance in memory.
Then raise `OnCurrencyCollected` or `OnCurrencySpent`, and `OnCurrencyChanged` after it.

- Each listener is invoked on its own, inside a `try`/`catch`. A throw is logged with
  `Debug.LogException` and cannot stop the other listeners, the save or the caller's result:
  `AddCurrency` returns normally, `TrySpendCurrency` returns `true`, and `OnCurrencyChanged` is still
  raised after a throwing `Collected` or `Spent` listener. In Unity tests an unexpected
  `LogException` still fails the test.
- A `Save` that throws propagates unchanged and nothing has changed: no balance change, no events,
  no success log. In production the only synchronous `Save` failure is
  `SaveException.SchedulerDisposed` from `SaveScheduler.MarkDirty` once the scheduler is disposed,
  which is expected at root-scope teardown. That is read from the code and not verified without
  Unity.
- A listener can rely on `GetCurrencyAmount` being current and on the change having been handed to
  the save handler. It is not durable yet, and a listener that flushes the currency scheduler writes
  it. That holds whether memory is assigned before the save or after it. Saving the snapshot first is
  what additionally makes a throwing `Save` change nothing, so a retry cannot apply the change twice.

The reasoning is in [saving.md](../saving.md), "Save, then notify, for both operations", including
why durability cannot come from the order.

The tests are specification tests, named after what a caller needs. `CurrencyManagerTests` has six
listener-failure tests, two that listeners see the new balance already saved and in memory, and a
re-entrant add. `CurrencySaveHandlerTests` has `main`'s three order tests
(`AddCurrency_`/`TrySpendCurrency_HandsTheNewStateToTheSaveHandler_BeforeAnyCallbackFires` and
`ACollectedListenerThatThrows_DoesNotStopTheNewBalanceBeingSaved`), flush tests for an add and for a
spend, a blocking flush after an always-throwing `Changed` listener, and one test each for an add and
a spend after the scheduler is disposed. `RewardsManagerTests` has
`GiveRandomCurrencyReward_WhenACurrencyListenerThrows_StillCreditsShowsThePopupAndAnnounces`, which
the harness does not compile.

**Provenance.** As vendored, the library ordered the two operations differently: an add raised its
events and then saved, a spend saved and then raised them, and a listener that threw escaped the
call. No reason for the difference was recorded. The save-system work pinned both orders with two
characterization tests. `main`'s test-suite revamp then replaced them with specification tests and
edited the library so an add saved before notifying. This branch kept that order in
`CurrencyManager`, then added what it left open, for two reasons. A listener that throws still
escapes a completed, saved operation: a spend whose listener throws never returns `true`, so
`if (TrySpendCurrency(...)) Grant()` takes the currency without granting, and `RewardsManager` skips
its popup and announcement. And a `Save` that throws leaves the balance changed in memory, so a retry
applies it twice. The details are in saving.md.

**The lesson.** A characterization test pins what the code does and never judges it. Here the order
and the propagation of listener exceptions were pinned without judgement. `main`'s revamp judged the
order and applied that lesson first. This branch judged what was left: the propagation, and the
save-failure case. After a parity refactor, each pinned behaviour needs a separate "is this right?"
pass.

---

## 6. What is not pinned

No test covers the first three, and the pass did not change them. The last four, and the final
sentence of the overflow bullet, are open follow-ups that the isolation change and its review raised.

- **A save that names a currency no longer in `CurrencyType`.** Whether the codec lets such a document
  reach `CurrencyManager` is untested. If it did, the constructor copies every entry it is given, so
  the stray balance would be kept and written back on the next save.
- **`long` overflow on an add.** `CurrencyManager` adds to the balance with no overflow guard and no
  test goes near `long.MaxValue`. A guard would need a typed exception.
- **The empty `spawnCurrencyPurchasePopup` branch.** `TrySpendCurrency` has a branch for an
  insufficient spend with the flag set, and it holds only a `TODO` to open the shop. Passing `true`
  does nothing today, and nothing asserts that.
- **`FakeCurrencyManager` still lets listener exceptions escape.** Its header says not to use it for
  listener-failure tests. Retiring it in `RewardsManagerTests` is open.
- **`CurrencyWatcher` subscribes before its first render.** A watcher whose render throws stays
  subscribed, and now logs on every change instead of breaking saves.
- **Cross-key atomicity.** The chests controller clears its run on one scheduler before crediting
  currency on another, and nothing makes the two writes land together.
- **`TrySpendCurrency` logs `Debug.LogError` for insufficient funds.** A future shop would have to
  `LogAssert.Expect` it.

---

## 7. What is verified, and what is not

The .NET-harness results below were measured before the rebase onto `main`'s test-suite revamp, on
the history as it then stood, and are not re-run. The pre-rebase hashes are the old ones: `508fa22`
is now `683023f`, `5dcbc5c` is `64678aa`, `7d5ee19` is `fbedbef` and `9ca027a` is `f15c15d`.

The session that did that work had no Unity Editor. Everything in the harness subsections was
checked with a .NET harness built for the pass, which lived in that session's scratch space and is
not in the repo.

### What the harness is

Measured on the pre-rebase history, which sat on an older `main`.

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

Measured on the pre-rebase history, which sat on an older `main`.

| Snapshot (pre-rebase) | `CurrencyManagerTests` | Save handler fixture | `CurrencyLegacyImportIntegrationTests` | Whole harness |
|---|---|---|---|---|
| older `main` | 17/17 | 9/9 | 4/4 | 306/306 |
| `508fa22` (now `683023f`): the characterization tests against the library | 25/25 | 11/11 | 4/4 | 316/316 |
| `5dcbc5c` (now `64678aa`): the replacement, library still on disk but unreferenced | 28/28 | 11/11 | 4/4 | 319/319 |
| After `7d5ee19` (now `fbedbef`): library deleted | 28/28 | 11/11 | 4/4 | 319/319 |

The `508fa22` row is the point of phase 1. Every new characterization test passed against the
library's own code before any of it was replaced.

### Mutation

Measured on the pre-rebase history, which sat on an older `main`.

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
- **On `7d5ee19`, against `CurrencyManager` and `CurrencySaveDocument` (15, all caught):** the same
  ten, plus:
  - `From` aliasing the live dictionary;
  - the zero-fill writing into the loaded document;
  - the null-handler guard removed;
  - an add saving before its events, and a spend raising its events before it saves.

In every case the targeted test failed. Most failed alone. The last two were caught by the two
characterization tests that pinned the library's order, which `main`'s revamp has since replaced.
Catching them showed the tests had teeth, not that the order was right; section 5 has the history.

### The isolation and snapshot fix, `9ca027a` (now `f15c15d`)

Measured on the pre-rebase history, which sat on an older `main`. Its "old order" is the pre-rebase
one, where an add still raised its events before saving.

| Snapshot (pre-rebase) | `CurrencyManagerTests` | Save handler fixture | `CurrencyLegacyImportIntegrationTests` | Whole harness |
|---|---|---|---|---|
| `9ca027a` | 39/39 | 16/16 | 4/4 | 335/335 |
| `9ca027a`'s tests over `7d5ee19`'s `CurrencyManager` | 30/39 | 11/16 | 4/4 | 321/335 |

The second row is the point. 14 of the new tests fail against the old order, so they state what a
caller needs instead of recording what the code did. The 5 new tests that pass on both are guards
for properties the old spend path already had: a spend listener can flush its spend, a spend
listener sees the new balance saved and in memory, and a re-entrant add keeps the sum.

Ten mutations of the new code, each caught by the test aimed at it:
- a listener not isolated;
- the first throw stopping the remaining listeners;
- `Changed` skipped after a throwing `Collected` listener;
- an add, or a spend, raising its events before it commits;
- memory assigned before `Save`;
- a `Save` failure swallowed;
- the snapshot handed to `Save` without the new balance;
- the snapshot finished after `Save` returned;
- the catch narrowed to `InvalidOperationException`.

The last two survived until an independent review of the change pointed out how. The "already saved"
tests read the in-memory handler's live document rather than what `Save` was handed. And every
throwing listener threw the same exception type. Both were fixed before the commit, along with a
contract comment on `ICurrencyManager` that understated when the `balance` argument goes stale. The
same review found nothing in the changed files that Unity would treat differently from the harness.

### Unity, after rebasing onto the test-suite revamp

Run on the branch tip with Unity 6000.3.11f1, `rm -rf ci-results && ci/run-tests.sh`, editor closed:

| Suite | Total | Passed | Failed | Skipped |
|---|---|---|---|---|
| EditMode | 720 | 720 | 0 | 0 |
| PlayMode | 74 | 74 | 0 | 0 |

That covers everything the harness could not reach: `GameLifetimeScopeTests` (14),
`GameLifetimeScopePauseQuitFlushTests` (6), `RewardsManagerTests` (9), `ChestsMinigameControllerTests`
(26), `ChestsMinigameSaveTests` (17), `GameBootstrapperFailureTests` (7) and the PlayMode
`GameBootstrapperTests` (9), which boots the real scene, the real `CurrencyManager` and the legacy
import. The currency fixtures ran in full: `CurrencyManagerTests` 39, `CurrencySaveHandlerTests` 19,
`CurrencyLegacyImportIntegrationTests` 4.

The same EditMode currency and rewards fixtures were then run against `64678aa`'s `CurrencyManager`
(save before notify, listeners not isolated, balance assigned before `Save`) with the tip's tests:
13 of 67 failed. They are the eight listener-isolation cases in `CurrencyManagerTests`, the two
disposed-scheduler tests, `AnAddWhoseChangedListenerAlwaysThrows_IsStillWrittenByTheBlockingFlush`,
`ACollectedListenerThatThrows_DoesNotStopTheNewBalanceBeingSaved` and the `RewardsManagerTests`
throwing-listener case. The flush and save-order tests passed on both, as they should: `main` had
already made both operations save first.

The first Unity run failed to compile: the rebase merge of `RewardsManagerTests` dropped the closing
brace of `main`'s config test. It was fixed in `f15c15d` before this run.

### Not verified

- **The harness on the rebased history.** It was not re-run after the rebase; the Unity run above
  replaces it.
- **Intermediate commits.** Only the branch tip went through Unity. The commits before it were not
  compiled one by one.
- **No playtest:** booting, the Coins and Gems labels, a chest reward landing, a relaunch keeping the
  balance, and an old `ResourceBankSaveData_CurrencyType` entry importing once. No Android build
  either.
