# Pooling

`Company.ChestGame.Pooling` (`Assets/_Project/Scripts/Pooling`) is the seam over where a prefab
instance comes from, with four implementations behind it, and `Company.ChestGame.Pooling.Demo`
(`Assets/_Project/Scripts/PoolingDemo`) is the four-lane race that demonstrates them against each
other and against the baseline. Why the seam exists, why `ParkedPool` is the default, the benchmark
numbers behind that choice, and what adding a fifth strategy takes are covered in
[design-decisions.md](design-decisions.md#14-pooling-and-why-the-board-is-rebuilt-rather-than-kept)
and its subsections. This doc covers the contract every implementation honours, where each one
differs from the others, the factory that builds them, the demo panel, and what the test suite
proves about all of it and how.

## The seam's contract details

`Get` always hands back an active instance, even when the prefab's authored root is inactive:
`Instantiate` already returns an active clone in the normal case, but `ActivationPool`, `ParkedPool`
and `DirectSpawner` all call `SetActive(true)` on a miss to cover that case too.

`Release` removes the instance from the handed-out set before checking it for null, not after, in
every implementation. Unity overloads equality so a destroyed instance reads as null, and checking
null first would short-circuit past the `Remove` call and strand the dead entry in the set forever.
`IPrefabPool<T>.Release` throws for either a foreign instance or one already released back, rather
than accepting either quietly, because accepting either would park the same instance twice and hand
it out to two callers later - once an instance is out of the pool's hands, it cannot tell a foreign
one from an already-released one apart, so both report the same `PoolException`.

`ReleaseAll` snapshots the handed-out set into a reused scratch list before iterating, because
`Release` edits the set a plain enumeration would be walking. It releases every instance and only
then throws the first failure, so one bad entry cannot strand the rest still held.

Reparenting always passes `worldPositionStays: false`, so a `RectTransform` keeps the anchored
layout it was authored with instead of drifting to wherever `Instantiate` happened to place it.

`DestroyInstance` destroys the `GameObject`, not the component: destroying a component alone leaves
an empty object behind, and Unity refuses outright on a `Transform`. It also guards against a null
instance first, because an instance handed out can be destroyed behind the pool's back, and
`.gameObject` on an already-destroyed `Component` throws `MissingReferenceException`. The method is
byte-for-byte the same in `ActivationPool`, `ParkedPool`, `DirectSpawner` and `UnityPool`.

The `T : Component` constraint on the seam is what lets every constructor's `prefab == null` check
use Unity's overloaded equality operator, which also catches a prefab that was destroyed after the
caller looked it up rather than only a literal null.

## PoolException, and why the messages live in one place

Every failure message is built by a static factory method on `PoolException` itself (`NoPrefab`,
`NoHolder`, `InactiveHolder`, `MaxSizeBelowOne`, `PrewarmPastMaxSize`, `NotHandedOut`, `Disposed`)
rather than written inline at each of the four implementations' throw sites. The wording lives here
rather than in each implementation, so a failure names the mistake instead of whichever of the four
implementations happened to catch it.

The file aliases `Object` to `UnityEngine.Object` because `using System;` brings `System.Object`
into scope under the same short name, and `NotHandedOut`'s parameter is a `UnityEngine.Object`; the
alias keeps that overload meaning what it says instead of silently resolving to the wrong `Object`.

## ActivationPool's traps

`Get` reparents a popped instance before reactivating it, not after: `OnEnable` and the first layout
pass need to already see the parent the instance will live under, or uGUI lays it out twice and shows
the instance for a frame wherever it was parked. `UnityPool.Get` reparents before activating for the
same reason.

On a miss, `Get` parents the newly created instance straight to the caller rather than to the holder
first: parking it under the holder only for the two lines that follow to reparent and reactivate it
would do the same work twice.

`CreateIdle` deactivates the instance it builds, which is the state `Release` leaves a parked
instance in: under the holder and switched off.

## ParkedPool's traps

A hit is the whole point of the class: one reparent, no activation, nothing woken up. A miss goes
straight to the caller's parent rather than to the holder first, because building it parked first
would be a reparent to the holder that `Create` immediately undoes. On the miss path only, `Get`
switches the new instance on - a parked instance is never deactivated in the first place, so a hit
has nothing to switch on.

`Create` parents an instance in the same call it instantiates it, so the instance never draws a
frame at the world origin where `Instantiate` left it.

`CreateIdle` leaves the instance it builds active, which is the state `Release` leaves a parked
instance in: under the holder, still active. That is the opposite of `ActivationPool.CreateIdle`,
whose idle instances are switched off.

## DirectSpawner, and why it still needs a handed-out set

`DirectSpawner` still tracks every instance it has handed out, in `_handedOut`, even though it parks
nothing: without that set, releasing a foreign instance would destroy something this class never
owned, and `Release` would have nothing to check a caller's instance against. `AvailableCount` is
always zero, and that is not a stub - holding nothing between a release and the next `Get` is the
whole of what this class is. Its constructor takes no holder and no max size, for the same reason: it
parks nothing and bounds nothing.

`Get` inlines what `ActivationPool.Create` does as a separate method, rather than calling out to a
shared helper, since there is nothing left to reuse once a class pools nothing.

## UnityPool, and what wrapping ObjectPool costs

`UnityPool` wraps the engine's own `ObjectPool<T>` instead of a hand-rolled stack. Once `ObjectPool`
owns the stack, the bound and the create/destroy callbacks, what `UnityPool` adds is the parenting,
the counters and the two rejections the seam promises. `ObjectPool` ships with
`UnityEngine.CoreModule`, so wrapping it costs no package reference.

`ObjectPool`'s own active count reads zero after a `Clear` call made while instances are still
handed out, because `Clear` resets the total that count is derived from. `UnityPool` keeps its own
`_handedOut` set regardless - it needs one anyway to tell whether an instance was ever given out -
and `ActiveCount` reads from that set rather than from `ObjectPool`.

The wrapped `ObjectPool` is built with `collectionCheck` left on as a second net under `UnityPool`'s
own release check, and no `actionOnGet` callback: activating an instance has to happen after its
parent is set, and `ObjectPool` fires `actionOnGet` before it hands the instance back, too early to
reparent first.

`Release` cannot rely on `ObjectPool`'s own collection check to report a foreign or
already-released instance: that check throws a bare `InvalidOperationException`, which names
nothing, where `PoolException` (a subclass of it) lets a caller catch something specific. Past the
bound, `ObjectPool.Release` would destroy the surplus itself, but only after firing
`actionOnRelease` first - parking, switching off and reparenting an instance on its way to being
destroyed - so `Release` checks the bound itself and calls `DestroyInstance` directly, skipping that
detour to match what the hand-rolled pools do.

`ReleaseAll`'s scratch list is reused across calls rather than allocated per call, because
`PoolRace.PrepareLanes` calls it four times per Run press in the pooling demo.

`Prewarm` creates its instances directly rather than getting and releasing them back through the
pool: `ObjectPool.Get` pops existing stock before it calls the factory, so get-then-release on a
pool already holding k instances would pop those k and hand them straight back, creating only
`count - k` new ones instead of `count`. The other three implementations always create on
`Prewarm`.

`Dispose` destroys every handed-out instance directly rather than releasing it first, which would
park it under the holder only for the next line to destroy it anyway.

## PoolFactory, and what CreateHolder decides for the caller

`Create` takes no ready-made holder parameter: which strategies need a holder at all is this
method's own decision, not the caller's, so `holderParent` is only ever where a holder gets built,
never a holder itself. The baseline (`DirectSpawner`) parks nothing, so building it a holder would
be an empty object in the hierarchy claiming a screen parks something it does not.

`CreateHolder` parents the holder under the caller's own transform rather than at the scene root, so
the holder and anything parked in it die with the screen instead of outliving the bundle the prefab
came from. The parent is a parameter rather than a fixed location precisely so that stays the
caller's decision, not this method's.

## The demo's fill modes and what they measure

`FillMode` controls how a lane's pool is prepared before `PoolRace`'s timed fill begins. `Cold` and
`Prewarmed` both start every lane's pool empty; the only difference is whether the instantiate cost
lands inside the timed race (`Cold`) or ahead of it (`Prewarmed`). `Reuse` starts every lane
wherever the previous race left it: nothing is trimmed between runs, so a pooled lane's `Get` calls
come back as hits and a repeat run instantiates nothing at all - which mirrors what
`ChestsMinigameView`'s own `NewGame` does against the real board, and is the one thing `Cold` and
`Prewarmed` cannot show.

`FrameCountingClock` wraps one lane's `IGameClock` to count the frames that lane's own fill yielded
on, without changing `FrameBudgetedLoop` or `IGameClock` themselves. Each lane in `PoolRace` gets
its own instance wrapping the same underlying clock, so four lanes sharing one player loop can each
answer "how many frames did my fill take" independently of the others. `FramesUsed` starts at one
rather than zero, because a fill that never yields still ran inside the first frame.

## IPoolRaceController, and why the panel is not generic

`IPoolRaceController` is the non-generic face of `PoolRace<T>`, so the panel `MonoBehaviour` that
drives the demo can hold one field and wire one set of buttons regardless of which prefab type the
race was built for. The generic type only has to exist where a real prefab is; nothing that sits on
a GameObject needs to be generic.

## PoolRace, and why simultaneous lanes are not solo timings

Four lanes run at once, all under one `CancellationTokenSource` and awaited together through
`UniTask.WhenAll`, all reading the same `IGameClock` and the same per-frame budget. Nothing paces a
lane against the others, so a strategy that places a unit more cheaply simply gets further within
the shared budget - but running four lanes at once also means no lane's elapsed time here is what
that lane would cost running alone. The simultaneous view proves the ordering between strategies,
not any one standalone number; solo mode, which runs exactly one lane, is what answers that question
instead.

## PoolRace's cancellation and identity traps

`PoolRace`'s constructor links `externalToken` into every race it starts, so a race still in flight
when the owner is torn down unwinds instead of filling into lanes that are going away.

`StartRace` cancels whatever is running and prepares every lane, not only the ones the new run will
use, so switching from all four lanes to solo does not leave the other three still holding what they
placed last time. It captures the new `CancellationTokenSource` in a local before assigning it to
the `_raceCancellation` field: the field can move on to a newer race while this one is still in
flight, and the completion path in `RunRaceAsync` compares against the captured local, not the
field, before touching either.

`RunRaceAsync` catches `OperationCanceledException` and does nothing with it, for the same reason
`ChestsMinigameView.FillBoardAsync` leaves its own catch empty: the only two things that cancel a
race are the next one, which has already prepared every lane before starting, and teardown, where
this resumes after `Dispose` has destroyed the pools. A result built from either would read counters
mid-collapse. Past that point, it only publishes a result and clears `_raceCancellation` if the
field still holds the token this run started with (checked with `ReferenceEquals`): `UniTask.WhenAll`
completing and this continuation resuming are not the same instant, since the continuation is queued
on the player loop, so a button press landing in that gap can already have installed a newer race's
token in the field. Disposing that token here would leave the new race uncancellable, and publishing
here would drop the newer race's numbers under a superseded result.

`RunLaneAsync` reads its lane's counters from inside its own task, at the moment that lane's fill
completes, rather than after every lane's task has been awaited together - awaiting first would give
every lane the slowest lane's finish time instead of its own.

`PoolRaceException.UnknownSoloStrategy` exists because solo mode names a strategy rather than an
index, so a caller can only ask to run alone a strategy this race actually has a lane for.

## PoolRaceException, and why authoring faults share one door

`NoDocument`, `NoItemPrefab`, `LaneSlotCountMismatch` and `MissingElement` are authoring faults in
the pooling demo panel rather than runtime ones, but they throw through the same `PoolRaceException`
door as the rest of the type, so the wording for every failure the demo can report stays in one
place.

## PoolRaceLaneFactory, and why the fill parent is not the holder

Every lane gets its own holder and its own fill parent, built by `PoolRaceLaneFactory`, so no
lane's pool can dirty another lane's layout by parking into it. `PoolRaceLane` itself needs nothing
more than a strategy, a pool and a fill parent transform: the pool already carries its own holder,
or none at all for the baseline, so the lane carries no separate reference to one.

The fill parent is never the holder. It carries a `GridLayoutGroup`, which is what turns a lane's
growth into visible motion in the demo; parking into it, the way releasing back to a holder does,
would dirty a rebuild no race needs to pay for. `CreateFillParent` anchors it stretched to its lane
root rather than leaving it at the default centered rect, because the lane root is the masked slot
the panel built and carries no layout group of its own - nothing else would give the fill parent a
width to lay its grid out inside.

`AllStrategies`' order is fixed because the UI and the pools agree on which strategy sits in which
column: the same index also drives `BuildAll`'s `laneRoots` alignment and the demo's lane count.

## RaceResult, and why Solo and FillMode travel with it

A finished race carries one `LaneMetrics` per lane that ran, in strategy order, with `Solo` carrying
exactly one. `Solo` and `FillMode` travel on the `RaceResult` itself rather than living only on the
request that started it, so a finished race is labelled with what it actually ran: `PoolingDemoPanel`
reads `result.FillMode` rather than its own `_fillMode` field when it writes the readout, because a
tap on the Fill button while a race is still in flight would otherwise relabel figures that already
landed with a mode that did not produce them.

## LaneMetrics, and why Instantiated/Destroyed are not what tests assert

`LaneMetrics.Instantiated` and `LaneMetrics.Destroyed` are display numbers, not proof: they read
`IPrefabPool`'s running totals as a delta across the timed fill, and a counter only proves a field
moved. `ChestBoardPoolingTests.SpawnProbe` counts real `Awake` calls instead, which is what actually
proves a rebuild instantiated, or did not instantiate, the objects it claims to.

## PoolingDemoPanel's startup order

`Start` binds and builds the race rather than `Awake`: `UIDocument` creates its `rootVisualElement`
in its own `OnEnable`, and Unity runs every `Awake` on an object before any `OnEnable` on it, so
binding in `Awake` would read a null tree. `Start` also begins expanded and immediately calls
`ToggleExpanded()` to collapse, rather than setting the collapsed visual state directly, so there is
exactly one definition of "collapsed" and the panel reaches it before the first frame anyone could
see, through the same path a tap on the toggle takes.

`OnDestroy` unregisters the geometry callback and the race's completion event and disposes the
race. Both die with the prefab in practice - it is teardown discipline rather than a fix for a live
leak.

`Update` measures `Time.unscaledDeltaTime`, not the `IGameClock` the race itself runs on: it is
reading the real frame time this `MonoBehaviour` is living in for the peak-frame readout, and the
race's own orchestration has to stay testable against a fake clock, so it cannot be the thing this
reads.

`BuildRace` gives the race its own `UnityGameClock` rather than an injected one, because this panel
is dropped into a scene and is not part of anything's object graph to inject one from. It links the
race to `this.GetCancellationTokenOnDestroy()`, so a race still in flight when the panel is torn
down unwinds instead of filling into lanes that are going away.

## PoolingDemoPanel's UI binding traps

`BindChrome` sets the root `VisualElement`'s `pickingMode` to `Ignore`. A full-screen root with the
default picking mode would swallow every tap meant for the game underneath, even collapsed with
nothing drawn; `pickingMode` is per-element, not inherited, so this opts the root out while the
chrome and toggle keep their own. It writes each lane's name label from the `PoolStrategy` enum
rather than trusting the authored UXML text, so a reordering of `PoolRaceLaneFactory.AllStrategies`
cannot leave a card labelled with the wrong strategy. It registers `OnLanesSlotGeometryChanged` on
the `lanes-slot` element so the uGUI lanes follow whatever height the stylesheet leaves over, rather
than a hard-coded height kept in step with it by hand.

`Required<T>` throws `PoolRaceException.MissingElement` rather than returning null: a missing name
is a broken `.uxml`, not a state to limp along in, and the alternative is a
`NullReferenceException` raised somewhere else with nothing naming the element that went missing.

`PlaceLanes` copies the stylesheet's answer onto the uGUI side rather than owning the layout
itself. Both are laid out against the same reference resolution - the prefab's `CanvasScaler` and
its `PanelSettings` are authored to match - so a logical pixel in the UXML tree is a canvas pixel in
the uGUI one.

## PoolingDemoPanel's collapse mechanics

Setting the chrome's `display` to `None` takes it out of both layout and picking, and disabling
the lanes' own `Canvas` hides the uGUI side without deactivating a single `GameObject` - which
matters, because `ParkedPool` refuses an inactive holder. The floating toggle button and the Close
button are the same control in two states, and exactly one is ever on screen: the toggle's band
runs through a control row, so showing both at once would have it sitting on top of that row's last
button.

## PoolingDemoPanel's control and readout wiring

Selection is a class (`is-selected`) the stylesheet reacts to, never a colour set from code: this
file only names which control is in which state, and `PoolingDemo.uss` decides what that looks
like. The fill-mode button's text is captioned ("Fill: Cold") rather than left bare ("Cold"),
because a button reading only "Cold" says nothing about what it is the cold setting of.

`ShortNameOf` supplies the segmented control's per-strategy label. Written out, the four
`PoolStrategy` names run to 46 characters combined and no phone-width row holds them, so the button
carries the short form while the full enum name still leads each lane card (see
[design-decisions.md](design-decisions.md#what-adding-a-pool-strategy-actually-takes), step 5, for
the same constraint from the authoring side).

`OnRaceCompleted` promotes each lane's `ElapsedMilliseconds` into the card's one large headline
figure: it is the number a race is watched for. The rest of a lane's counters go into the smaller
metrics line beneath it.

## How the race is measured

`FakePrefabPool<T>` costs each `Get` a chosen amount of `FakeGameClock` time instead of whatever a
real pool costs. `FakeGameClock` never advances on its own, and there is no engine underneath it to
make `SetActive` expensive, so the four real pool strategies are indistinguishable under it.
`FakePrefabPool`'s configurable per-`Get` cost stands in for "cheap" and "expensive" lanes, the same
way `FrameBudgetedLoopTests`' `CostlyStep` stands in for real work when it tests `FrameBudgetedLoop`
itself. `FakePrefabPool.Prewarm` is a deliberate no-op: nothing parks in this fake, `Get` pays its
cost fresh every call, so warming it would only inflate `CreatedCount` without the race ever seeing
a hit. The tests that care about a real prewarm hit use the real pools instead, where a hit is real.

`PoolRaceTests` proves that shape, not what a real pool costs: `FakeGameClock` cannot see a real
engine, so the cost-sensitive tests race `FakePrefabPool` lanes with a chosen cost, the way
`FrameBudgetedLoopTests` races synthetic steps.

`StartRace_EveryLaneAdvancesInTheSameFrames` costs all four lanes the same on purpose - four
milliseconds a unit against a ten millisecond budget places three a frame, the same shape
`FrameBudgetedLoopTests` uses. Equal cost keeps the test about whether one clock pumps every lane
each frame, not about which lane gets further.

`StartRace_ACheaperLaneReportsLessElapsedTime_ThanAnExpensiveOneAtTheSameBudget` reads the same
claim off the metrics rather than the pool: a lane that finishes sooner has to report a smaller
elapsed time. That is only true if each lane's finish is timestamped inside its own task; stamping
it after every lane has been awaited together would give them all the slowest lane's finish time
instead.

`StartRace_Prewarmed_InstantiatesNothingDuringTheRace` leaves the baseline out of its lane list on
purpose: `DirectSpawner` has nowhere to hold a prewarmed instance, so it always instantiates on
`Get`, which is correct behaviour for it (see
[design-decisions.md](design-decisions.md#the-seam-itself)) but would make a prewarm-race assertion
about it meaningless.

`StartRace_Reuse_InstantiatesNothingOnPooledLanes_ButTheFullBoardOnTheBaseline` is what `Cold` and
`Prewarmed` cannot show: a second race finding what the first one placed already parked, the way
`ChestsMinigameView`'s `NewGame` finds the board it released last time. The first `StartRace` call
in that test is what builds the stock the reuse race is supposed to find waiting. `DirectSpawner`
has nowhere to have parked anything - its release is a real destroy - so it is the one lane that
pays the instantiate cost again on the reuse pass.

## How the tests prove a release actually destroyed something

`TearDown` calls `Object.DestroyImmediate` on three roots - `_created`, the holder and the parent -
because `DestroyImmediate` is the edit-mode equivalent of a real destroy, and those three roots take
every instance with them: an instance is always under the holder or under the parent it was got
for, including the ones a pool believes it already destroyed.

`ExpectDestroys` exists because `Object.Destroy` in edit mode destroys nothing and logs an error
instead, once per call, and an unhandled error log fails the test. Naming the exact number turns
that nuisance into the assertion the counters cannot make: `DestroyedCount` only proves a field
moved, while an unmatched expectation or an unexpected destroy log proves `Object.Destroy` was
called exactly this many times and on nothing else. A test that does not call it is asserting that
it destroyed nothing. `LogAssert.ignoreFailingMessages` is not the tool for this: `SetUp`, the test
body and `TearDown` each run inside their own `LogScope`, so a flag set in `SetUp` is gone before
the test body starts. `ExpectDestroys` is called before the action that causes the destroys, since
the framework checks the expectation at teardown and a release the test is about to make is part of
its arrange, not a side effect to react to afterwards. `PoolRaceTests` reuses the same technique for
the same reason: releasing the baseline's board before a second race starts destroys it for real in
edit mode, matched by regex rather than an exact message because the real message spans two lines.

The count each test passes to `ExpectDestroys` follows from what actually destroys in that test, not
from `PoolCase.ReleaseDestroys` alone. `Get_OfAPrefabWithAnInactiveRoot_StillHandsBackSomethingVisible`
returns before the release on a destroying implementation, which keeps it out of the accounting
entirely - the baseline parks nothing, so there is no hit to check.
`Release_OfTheSameInstanceTwice_ThrowsPoolException` expects one destroy on the baseline, from the
accepted release, and none on the pools, which park it; the rejected second release must destroy
nothing either way. `ReleaseAll_TakesBackEveryInstanceThatWasHandedOut` expects three on the
baseline, one per instance taken back, and none on the pools, since a max size of 8 leaves room to
park all three - the second `ReleaseAll` call finds nothing and destroys nothing, which is half of
what it is asserting. `Dispose_DestroysEverythingThePoolOwns` expects two either way, from two
different shapes: the baseline destroys one at the release and the other inside `Dispose`, while the
pools park the first and destroy both in `Dispose`; the second `Dispose` is a no-op and adds none.
`Release_PastTheMaxSize_DestroysTheSurplus` expects exactly one: the third release is the one that
finds the two parking slots already taken, and the two gets afterwards come out of the pool and
destroy nothing. `Trim_DestroysWhatIsParkedAndLeavesWhatIsHandedOut` expects exactly one, the single
parked instance - if `Trim` reached the one still handed out this would see two, which is the
failure a counter assertion alone cannot tell from a counter that simply moved.
`DirectSpawner_Get_AfterRelease_ReturnsANewInstanceRatherThanTheOldOne` expects exactly one destroy,
from the single release. `UnityPool_AfterATrim_StillReportsWhatIsStillHandedOut` expects exactly
one, from the `Trim`; the release afterwards parks rather than destroying, because the trimmed pool
has room again.

## What the pool contract tests pin

`PrefabPoolTests` uses `TestCaseSource` rather than a generic fixture because the four constructors
do not agree: `DirectSpawner` takes no holder and no bound, so there is nothing a `new()`
constraint or a `typeof()` fixture argument could build. `PoolCase.ToString` keeps a failing case
named rather than indexed.

`PoolCase.ReleaseDestroys` is declared per case rather than read off `DestroyedCount`: deriving the
expectation from the counter under test would make the check prove nothing. It is also why
`PoolingImplementations` pins the baseline's answers separately rather than through the shared
cases - the baseline disagrees with the other three by design on every one of them.

`Get_OfAPrefabWithAnInactiveRoot_StillHandsBackSomethingVisible` exists because two of the four
implementations once never called `SetActive(true)` at all: swapping the serialized strategy
silently emptied the screen, permanently rather than for a frame.

`Prewarm_AgainstAPoolThatAlreadyHoldsStock_CreatesTheFullCount` starts from a pool already holding
parked instances rather than a fresh one, because a fresh pool cannot show the bug: warming by
get-then-release would pop the k instances already parked and hand them straight back (see
"UnityPool, and what wrapping ObjectPool costs" above), so only a pool starting non-empty can catch
a warm that quietly reuses instead of creating.

`AfterDispose_GettingOrPrewarmingThrowsPoolException` pins that a disposed pool refuses rather than
quietly instantiating again, which would otherwise be a second pool nobody owns.

`Release_PastTheMaxSize_DestroysTheSurplus` checks identity, not only the counters: the two
instances a `Get` returns after the surplus is destroyed have to be the two that were actually
parked, because the counters agreeing is not the same as the surplus being gone from the pool.

`DirectSpawner_Get_AfterRelease_ReturnsANewInstanceRatherThanTheOldOne` is what makes the baseline
a baseline: if this ever came back `AreSame`, the comparison the whole demonstration rests on would
be a pool measured against a pool.

## How the tests prove ParkedPool avoids OnDisable

`ParkedPool_AcrossAGetAndRelease_NeverDeactivatesTheInstance` and
`ActivationPool_Release_DeactivatesTheInstance_AndGetBringsItBack` pin the same property from both
pools' own side. [design-decisions.md](design-decisions.md#why-parkedpool-is-the-default) names the
`SetActive` toggle as the entire difference between the two implementations, so an assertion that
`ParkedPool` stays active alone would leave open whether `ActivationPool` also stays active; the
pair only proves a real difference once `ActivationPool`'s own deactivate-and-reactivate cycle is
pinned as well.

`ParkedPool_ParksWithoutFiringOnDisable_WhereActivationPoolFiresIt` goes past `activeSelf` to the
callback the state change is standing in for: the `OnDisable` call, and the canvas and layout
rebuild riding on it, which is what actually costs frames. It measures `ActivationPool` first, as a
control, because a plain `MonoBehaviour`'s `OnDisable` is play-mode only, and whether
`[ExecuteAlways]` brings it back in edit mode is the assumption the whole test rests on. A control
reading zero would mean edit-mode tests do not run `ExecuteAlways` callbacks at all, and the
`ParkedPool` half would then pass for any implementation rather than for the right reason. Should
the control ever read zero, the fix is to delete the test rather than repair it, since the
`activeSelf` pair above already covers the observable difference. `DisableProbe` counts only
disables because that is the one half of the callback pair this pair of tests is about.

## What PoolBenchmark measures and why

`BoardSize` is 500: big enough that the difference between strategies is real rather than noise,
small enough that the play-mode suite stays the thin layer [testing.md](testing.md) says it is.

`SetUp` builds the bench prefab shaped like the real chest prefab rather than as a bare
`Transform` - an `Image`, a `Slider` and a `Button` under a root - because what `Instantiate`
actually costs is a whole object graph; a one-component prefab would flatter the baseline strategy
it is compared against.

`MeasureOne` yields a frame of its own before each measured stretch, so the previous strategy's
deferred destroys have landed and are not timed as part of the next one.

## What only a real engine proves about pooling

`PoolRaceTests` (EditMode) proves the frame-budget orchestration against a fake clock; only a real
player loop, real `Instantiate` and a real `Canvas` can prove a race drives actual Unity object
lifecycles correctly, which is what `PoolRacePlayModeTests` adds.

`StartRace_AllFour_EachLaneEndsWithTheRequestedCount_CountedByARealProbe` resets
`SpawnProbe.Instantiations` after the rig is standing, so the one `Awake` the prefab itself ran on
its own is not counted as something the race built. Its headline assertion is that every one of the
four times twelve objects the race says it placed is one the engine actually built - a real `Awake`
count, not four pool counters agreeing with each other, which is exactly the gap a fake clock and a
fake pool cannot close. `SpawnProbe` counts constructions the same way
`ChestBoardPoolingTests.SpawnProbe` does: `CreatedCount` only proves a field moved, an `Awake`
proves `Instantiate` actually ran.

`ParkedPool_ThroughARealCanvasHolder_KeepsParkedInstancesActiveAndHidesThem` is the second thing
only a real engine can prove: the disabled-`Canvas` `ParkedPool` holder, which nothing else in the
suite exercises against a real engine.

## What the demo panel's tests prove and how

`BuildPanel` yields two frames rather than one because the panel binds in `Start` and Yoga resolves
layout on the panel's own update; nothing built by the prefab can be read on the frame the instance
was created.

`PanelRoot` reads the panel's extent from the visual tree, which `PanelSettings` sizes to the
screen, rather than from anything the demo itself builds. It has to come from outside the demo, or
a chrome collapsed to nothing would be measured against itself and report a perfect fit.

`TogglingExpanded_ShowsChromeAndLanes_AndClosingHidesBothAgain` collapses back through `Close`, not
the floating toggle: expanded, the floating toggle is the control that is hidden, and collapsing
through it would be testing a control no player can reach.

`ExpandedOverlay_FillsThePanel_RatherThanCollapsingToTheHeightOfItsContents` checks that `.chrome`
fills the panel because the document root holds only absolutely positioned children - nothing is in
flow to size it, so a chrome that grew to fit its contents instead would collapse to a band across
the top with its controls clipped out.

`ExpandedControls_AreInsideTheChrome_AndResolveToATouchFriendlyHeight` adds the containment checks
because height alone proves nothing: a control carrying a min-height resolves to it whether or not
the box around it can actually show a single pixel of that control.

`EitherWayRound_ExactlyOneToggleIsOnScreen_AndThePanelCanActuallyHitIt` asks through the panel's
own hit test (`panel.Pick`) rather than through display flags, because the failure this pins is
invisible to flags: the toggle and the chrome are absolutely positioned children of one root with
no z-index between them, so the later child wins the pixel. A toggle behind an opaque backdrop is
`display:Flex`, has a real resolved size, and answers a forced-target `SendEvent` while still being
unhittable to a real player. Painting the toggle last is not a fix either - its band runs through a
control row, so it would land on that row's last button instead.

`ControlRows_KeepEveryControlOnScreen_AtTheNarrowestWidthAPhoneGives` matches on height against a
1080x1920 reference, which makes the panel 1920 logical px tall on every device and
`1920 * (w / h)` px wide - so the reference's own 1080 is the width of a 9:16 phone, not a floor. A
9:20 phone gives 864, narrow enough that the control rows can wrap if they are not built to avoid
it. The test pins the narrowest width directly rather than reading the current run's screen, because
these controls shrink: a right edge measured at whatever width this run happens to have would not
transfer to 864. `chrome[0]` is the title bar; the two rows the test walks are `chrome[1]` and
`chrome[2]`.

`ThePrefabsOwnScalerAndPanelSettings_AgreeAboutWhatAPixelIs` exists because the chrome is UI
Toolkit and the lanes are uGUI, laid out by two different systems that only agree about scale if
both are configured to match. Nothing else would notice them drifting apart: each looks right on
its own, and the chrome only starts covering the lanes on hardware whose resolution does not match
the reference. It is an authoring guarantee rather than something reconciled at runtime, which is
why it needs its own test.

`ClickingRun_ThroughTheElementsRealClickPath_StartsARaceAndSettlesWithRealResults` expands the
panel first because reaching Run at all requires it - collapsed, its whole row is `display:None` -
so the test follows the same path a player is on rather than a shortcut around it. It picks the
smallest board on purpose: the test only cares that a real click reaches the race, not how long the
race takes. `SendEvent` does not run the handler inline, it enqueues, and the panel drains the queue
on its next update, so the size-0 click and the run click both land in that queue and are drained
together, in order, with neither having run yet by the time both `Click` calls return. The test
makes no assertion on the transient "Running..." text: an 8-item board against a 2ms budget can
finish inside the same frame the queued clicks are drained in. The metrics label only leaves
"not run yet" from inside `OnRaceCompleted`, which only runs after a full race, which only starts
from `OnRunClicked` - so the one settle-frame wait covers both "the click never reached the handler"
and "the handler ran but never started a race".

`AssertInside` is a from-scratch containment check rather than `Rect.Overlaps`, because overlapping
is not the question this fixture asks: a chrome clipping its own controls still overlaps every one
of them.

`Click` sends a `NavigationSubmitEvent` rather than a `PointerDown`/`PointerUp` pair because
`Clickable` only fires on the up event if the panel's picking still reports the element as the one
under the pointer, and forcing `target` on a synthetic event skips the picking that state depends
on. `NavigationSubmitEvent` is the real alternative rather than a workaround: `Button`'s constructor
registers `OnNavigationSubmit`, which calls `clickable.SimulateSingleClick` directly. That wiring
was confirmed by decompiling `UnityEngine.UIElementsModule.dll` for 6000.3.11f1, not assumed from
documentation.
