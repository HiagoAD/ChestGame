# MVC

What the three layers mean in this project, what each may and may not do, and why the minigame
framework states the view half in its own terms rather than inheriting it.

The pattern was not invented for this pass. The chests minigame has followed it since it was
written, and it is the reason that minigame's rules run in edit mode with no scene and no player
loop. What this pass did was name the contract, give it two types the compiler can check, and apply
it to the screens that had drifted away from it.

## The three layers

### Model

A plain C# object holding state, with no engine types in it, that raises an event when that state
changes. `ChestsMinigameChestModel` is the reference: four states, a completion fraction, and one
`OnStateChanged` event fired from the property setter.

There is no `ModelBase`. Models differ too much for a shared base to carry anything but ceremony -
some are a single enum, some are a document that gets saved - so the rule is a convention rather than
a type. What matters is the two halves of it: no engine types, and a change event. The first is what
lets a model be asserted in edit mode; the second is what lets a view bind to it without polling.

### Controller

A plain C# class, never a `MonoBehaviour`, implementing `IController`. It holds the rules and the
session state, takes its dependencies by injection, exposes state plus change events, and receives
input as ordinary method calls. It is `IDisposable` because it owns subscriptions and cancellation
tokens that outlive no scene object.

Being a plain class is the whole point rather than a stylistic preference. `ChestsMinigameController`
runs two parallel `UniTask`s, every cancellation path and the full save round trip inside an EditMode
test, because nothing in it needs a frame to happen. A controller that were a `MonoBehaviour` would
need a scene, a player loop and a yield to assert anything at all.

`IController` carries no members of its own beyond `IDisposable`. It earns its place by naming the
layer and by giving `ViewBase<TController>` a constraint that means something; a controller that
does not implement it cannot be bound to a view, which is the check worth having.

### View

A `MonoBehaviour` deriving from `ViewBase<TController>`. It holds serialized scene references and
cached widgets, subscribes to its controller in `OnBind`, releases in `OnUnbind`, writes to widgets,
and forwards raw input to the controller. It decides nothing.

"Decides nothing" is the line that is easiest to cross and the one worth policing. Formatting a
number into a label is a decision. Choosing which message to show when a run ends is a decision.
Destroying yourself when a button is clicked is a decision. Each belongs in the controller, which is
where it can be asserted without a scene.

`Bind` is called once per instance and throws if called twice, because a view rebound to a second
controller would keep the first one's subscriptions. `OnDestroy` is `virtual` rather than private on
purpose: a subclass that declares its own `OnDestroy` without `override` gets a CS0114 warning out of
the compiler, where a private base method would simply have been hidden and the view's subscriptions
would have outlived it silently.

## Why the minigame framework keeps its own view base

`MinigameViewBase` does not derive from `ViewBase<TController>`, and that is deliberate.

`MinigameContainer` has to hold a view without naming its controller type. It does
`prefab.GetComponent<MinigameViewBase>()`, hands the result to `IObjectResolver.Instantiate`, and
calls `SetController` on the instance. That needs a non-generic handle. A view also has to be one
concrete type, so it cannot be both a `ViewBase<ChestsMinigameController>` and a non-generic anchor -
single inheritance does not allow it, and the two ways around that are both worse than the
duplication:

- Making `MinigameViewBase` derive from `ViewBase<MinigameControllerBase>` and having the typed
  subclass narrow `Controller` requires `new`-hiding the property. Member hiding in a base class that
  every future minigame derives from is a trap set for the next author, not a saving.
- Replacing the anchor with an `IView` interface breaks `IObjectResolver.Instantiate<T>`, whose
  constraint is `T : Component`; an interface cannot satisfy it, so the container would have to cast
  to `MonoBehaviour` to instantiate and back to `IView` to bind. It would also ripple into
  `MinigameBase<TController, TView, TMinigame>`, whose `TView` constraint names the anchor.

So the controller half unifies and the view half does not. `MinigameControllerBase` implements
`IController`, which is a real unification: a minigame controller is a controller in the same sense
every other one is. `MinigameViewBase` restates the binding contract in four members of its own, and
`MinigameViewBase<TController>` does the cast once so a concrete view never writes one.

The cost is honest and bounded: one extra base class, stating a contract the shared one states too.
The alternative was to make the one part of the codebase that already did MVC correctly carry a trap
so that a class diagram would look tidier.

## What `MinigameViewBase<TController>` changed

Before it, every concrete minigame view took the untyped base, asserted the type with `Debug.Assert`
and cast:

```csharp
Debug.Assert(controller is ChestsMinigameController, "Wrong controller type, ...");
_controller = (ChestsMinigameController)controller;
```

`Debug.Assert` compiles out of a release build, so the wrong controller type reached the cast and
surfaced as an `InvalidCastException` in the player and as a logged assertion plus the same exception
in the editor. The typed base does the check once, in one place, and throws an `ArgumentException`
naming both the expected and the actual type in every build. Concrete views now override
`OnControllerSet` and read the inherited `Controller`.

## Where each piece lives

`Company.ChestGame.Mvc` (`_Project/Scripts/Mvc/`) is a leaf assembly referencing nothing. It holds
`IController` and `ViewBase<TController>` and will not grow beyond the vocabulary: anything that
needs a dependency belongs in the assembly that owns the screen, not here.

It is a separate assembly rather than a corner of `Company.ChestGame.Common` because `Common` is the
engine seams, `FrameBudgetedLoop` and the exception types, and those are a different concern.
Keeping them apart means an assembly can take the MVC vocabulary without taking the engine seams, and
the reference lists say which assemblies have screens in them.
