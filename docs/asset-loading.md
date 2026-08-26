# Asset loading

Everything the game loads goes through `IAssetProvider`, and `Company.ChestGame.Assets` is the only
assembly that calls Addressables. This file is the contract: the two routes in, the lifetime rules
that go with them, and what the provider does with a failure.

For how content is grouped, built and shipped, see [content-delivery.md](content-delivery.md).

## The seam

`IAssetProvider` has two routes in, because content is named two different ways:

- A key, for a source that owns one.
- An `AssetReference`, for a definition asset that was authored pointing at its own content.

An `AssetReference` is a GUID the inspector filled in rather than a hard object reference, and that
indirection is the only reason a minigame's bundle is not dragged in by the mere act of loading its
descriptor.

`Release` is the third member and it takes a reference rather than a handle, so nothing outside the
seam ever holds an Addressables type. `GetDownloadSizeAsync` and `DownloadAsync` complete the surface
and work in labels, since a label is what names a whole minigame's content at once.

Swapping Addressables for something else is one registration and one class.

### What the seam does not buy

Naming `AssetReference` in the interface forces every assembly that authors one, and every assembly
that merely calls a method overload naming it, to reference the Addressables package for the
serializable type. A serialized reference is only authorable where the type is visible, and an
overload naming it has to be resolvable at the call site.

So the invariant is narrower than it used to be. What holds today is that only one assembly calls
the package, not that only one references it. The reference is the mechanism that keeps a minigame's
content out of its descriptor, so the wider invariant is the one that had to give.

## Lifetimes: the asymmetry that will bite you

The two routes are not symmetric about lifetime, and a caller has to know it, because nothing in the
compiler or the test suite will tell it.

Nothing loaded by key is ever released. There is only `Release(AssetReference)`, so an asset fetched
through the string route is resident for the rest of the session. That is a decision rather than an
omission. The key route exists for the four things the boot sequence names itself, the config
document, the minigame list, the popup list and the popup parent prefab, every one of which is read
once and then wanted for as long as the game is running. Loading one of them twice would not pile
handles up either, because Addressables hands back the operation it already has. What cannot be
undone is the asset staying loaded.

Content that is transient, wanted for one screen or one minigame or one popup, has to be reached
through an `AssetReference`. That is the only route that can be let go of again. Loading transient
content by key leaks it, the seam cannot report that, and no test will catch it.

If a case ever genuinely needs the key route with a lifetime, add `Release(string)` beside it and
track key loads the way the reference route is tracked. Do not decide the asset is small enough not
to matter.

### One release per load

Release counts the way Addressables counts: one release undoes one load. Two live callers that each
loaded the same asset each release once, and the first to finish does not pull the asset out from
under the second. A caller that loads twice and releases once keeps the asset for the session.

This is not hypothetical. The minigame framework hands out a fresh container per request, so two of
them running the same minigame is a supported state.

`Release` is safe on a reference that was never loaded and on a null one, because the teardown paths
that call it are documented as safe to call unconditionally and would otherwise all need the same
guard.

## AddressablesAssetProvider

The production implementation, and the only class in the project that calls Addressables. It has two
jobs beyond loading.

### Translating failures

Addressables reports its own exception types, which would leak the loading technology into every
catch site and let a test asserting "this throws" be satisfied by something unrelated. A key or
reference that is not in the catalog becomes `MissingAssetException`. Anything else that went wrong
while loading becomes `AssetLoadException`. Both sit under `ChestGameException`.

Cancellation is not a failure to load. It is the caller changing its mind, and it travels out
untouched, because the whole content load is already shaped to unwind on it.

An unwired or unresolvable reference is caught before the load rather than left to Addressables,
which would log an error on its way to throwing over a key that was never authored.

### Not leaking a load nobody received

`Addressables.LoadAssetAsync`, the package call, takes the ref-count before anything is awaited. (The
project's own seam method is `LoadAsync`; every unqualified `LoadAssetAsync` below is the package's.)
A load that was cancelled or that failed has therefore already taken one and is about to throw past
every caller that could have released it.

On the key route, nothing tracks keys, so such a load has to let go of its own ref-count in a
`finally` or the asset is resident for good. On the reference route, the handle is recorded before
the await, not after it, because a token that fires while the bytes are still coming makes the
awaiter throw straight past everything below and a handle that was never recorded is one nothing in
the session can ever release. A load that did not deliver then drops exactly what it took.

That unwind is conditioned on having recorded something rather than on having failed. A load that
threw before it took a ref-count has nothing of its own to give back, and releasing anyway would take
the handle out from under whoever loaded the same asset first.

The reference is handed to `LoadAssetAsync` as a key rather than loaded through
`AssetReference.LoadAssetAsync`, which stores the handle on the reference itself. That field lives on
a shared definition asset, so a second load would log an error and lose the first handle. Keeping the
bookkeeping in the provider is also what lets `Release` take a reference and hand the caller no
Addressables type.

The size query is an operation like any other and holds a handle until it is let go. It is released
explicitly rather than through `autoReleaseHandle`, which hands back a handle that is already invalid
and would make the await path ambiguous.

`DownloadAsync` fetches bundles into the cache without loading anything. Whatever actually wants an
asset out of them still goes through `LoadAsync` later.

## AssetHandleRegistry

What the provider is holding on behalf of each authored reference, kept in its own class because it
has a rule rather than a translation.

The rule is the key. `AssetReference` overrides neither `Equals` nor `GetHashCode`, so a dictionary
keyed on the reference has reference identity. It would work only for as long as every caller hands
back the very same serialized instance it loaded with, and any caller that rebuilds a reference from
the same GUID would get a silent no-op release and a leaked handle. Keying on the runtime key instead
gives the lookup the value semantics the type does not have. The runtime key is the GUID plus the
sub-object name when there is one, so two references naming the same thing are one entry, and a
reference naming a sub-asset is not confused with its parent.

Every handle is kept rather than one per reference, because Addressables ref-counts per load. Two
loads of one asset are two ref-counts and need two releases. Overwriting an entry would leak whatever
it replaced, and handing the whole list back on the first release would drop a ref-count a second
live caller is still relying on.

One `TryTake` therefore pairs with one `Remember`. Which of the handles held for a key comes back is
deliberately unspecified: they are ref-count tokens for the same runtime key, so releasing any one of
them decrements exactly the one count a single load added, and `Release` takes a reference rather
than a handle so it could not name a particular one anyway. The order is last-in-first-out, which is
what makes a load undoing its own bookkeeping take back the handle it just added rather than someone
else's.

A reference nothing is currently held for answers false rather than failing, which is what lets the
teardown paths release unconditionally.

The key is removed with its last handle, so an asset loaded and released over and over does not leave
an empty list behind for the rest of the session.

## Awaiting Addressables handles

Always `handle.ToUniTask(cancellationToken: ct)`, never `await handle`. UniTask version-gates some of
its awaiter extensions, so the explicit call is the form that cannot bind to the wrong overload. The
same applies to `AsyncOperation`: UniTask compiles its `AsyncOperation` awaiter out under
`#if !UNITY_2023_1_OR_NEWER`, so on this editor `await operation` binds to the `IEnumerator` overload
and fails to compile. See `docs/context/self-contained-minigames.md` section 6.
