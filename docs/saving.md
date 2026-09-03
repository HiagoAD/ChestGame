# Saving

`Company.ChestGame.Saving` persists arbitrary state behind one seam, `ISaveService`. So far: a JSON
codec, no protection, a versioned envelope, four stores (`File`, `AtomicFile`, `PlayerPrefs`,
`InMemory`), the three selection enums, an authoring profile, and the factory that turns one into the
other. Nothing in the game references this assembly yet.

## The shape, and what it copies

Pooling's shape, with one structural difference. A pool is one of four mutually exclusive things, so
`PoolStrategy` is a flat enum and `PoolFactory` picks a branch. Saving's variants are orthogonal:
where the bytes land, how the object becomes bytes, and what protects them are independent choices,
and a flat enum covering them would need six times four times five members.

So the seam splits three ways and composition replaces selection:

```
state -> ISaveCodec -> IPayloadProtector -> ISaveStore -> disk
```

`SaveService` is the composition. `SaveServiceFactory` picks the three from an authored
`SaveProfileSO` and does not change the contract; see the sections below.

## The envelope

`SaveEnvelope` is always plain JSON, never encoded or encrypted, and carries the schema version, the
ids of the codec and protector that produced the body, how the body is embedded, and the body.

The ordering rule is the whole point: **the version must be readable before anything decides how to
decode the body.** Put it inside the protected body and a key rotation you get wrong is
unrecoverable, because there is no way to learn what you are holding.

`body` sits as raw embedded JSON when the codec and the protector are both text-safe, so a developer
can open a save and read or hand-edit it. Otherwise it is base64. `enc` records which, because
nothing about the other three fields says so.

### Byte-exactness, and where it stops

`GetBody(Wrap(x))` reproduces `x` byte for byte. This is not fussiness. Phase 3's HMAC protector
signs the exact bytes it was handed, and a body that came back merely *equivalent* rather than
identical would fail that signature on every valid save.

Getting there cost the obvious implementation. `JsonConvert.DeserializeObject<SaveEnvelope>` would
rebuild `Body` from Newtonsoft's own object model, and that model does not remember the text it came
from. A trailing zero on a decimal (`1.50` returns as `1.5`) and a date-shaped string
(`"2026-09-01"` returns as `"2026-09-01T00:00:00"`) both die on that trip, and the second one is in
this game's own save model. So `Parse` walks the envelope with a single `JsonTextReader`, captures
the body verbatim via `JRaw.Create`, and sets `DateParseHandling.None` and
`FloatParseHandling.Decimal` so nothing is reinterpreted on the way past.

The guarantee is best-effort rather than absolute: a number in scientific notation, or a literal
negative zero, can still come back reformatted, because both go through the same numeric path that
protects ordinary decimals. Nothing `JsonCodec` writes produces either.

One consequence is easy to reintroduce. `JRaw` captures *literal source text*, so a base64 body comes
back still wrapped in the quotes it was written with, and handing that to `Convert.FromBase64String`
throws. `Parse` unwraps it after the read loop rather than inside the switch, because JSON field
order is not guaranteed — `enc` can arrive after `body`, so the end of the loop is the first point at
which both are known.

## Loading, and the three ways a version goes wrong

`LoadAsync` returns a `new T()` when nothing is stored, and throws when something *is* stored and
cannot be read. Those are different events and conflating them is the failure this design exists to
prevent: a corrupt save silently returning a fresh object looks exactly like a first run, and the
player is never told the difference.

`Version` is nullable so an absent version reads as absent rather than as version 0 — including an
explicit `"v": null`, which carries exactly as much version information as no key at all and has to
be guarded for separately, because converting a null to an int quietly yields 0. That check has
to come *before* the newer-than and older-than comparisons and must never be folded into them: a null
compared with `>` or `<` answers false both ways, so a file carrying no `v` at all would pass both
guards and reach the codec. The nullable type makes the case representable; only the explicit check
catches it.

A version newer than this build is refused outright rather than partially read. Half-reading a format
this build has never seen is a guess, and it deletes progress the player can see they had.

`codec` and `prot` are then checked against the configured components. They are recorded so a reader
can tell what wrote a body before trusting itself to decode it, and a field nothing reads is
decoration. The check comes after the version check because a save from a newer build may
legitimately name a codec this one has never heard of, and "written by a newer build" is the more
useful thing to report.

## `IsTextSafe` means valid JSON

Not merely valid UTF-8 text. The envelope embeds a text-safe body raw, so a codec emitting a bare
unquoted string would round trip through `UTF8Encoding` perfectly and still corrupt the envelope it
was pasted into. Both `ISaveCodec` and `IPayloadProtector` carry the flag and the same meaning.

## `FileStore`

The root is a constructor argument rather than `Application.persistentDataPath` read inside, so a
test can point it at a temp directory instead of the developer's real save. `DefaultRootDirectory()`
is what production code passes.

**A bad key is rejected, never rewritten.** An earlier version sanitised unsafe characters to `_`,
which maps `a/b` and `a_b` onto the same file — one save silently overwriting another, in the
component whose entire job is not losing data. A key is developer-chosen, so a bad one is a bug to
surface rather than a value to repair.

**`UnauthorizedAccessException` is not an `IOException`.** It derives straight from
`SystemException`, so catching only `IOException` lets a read-only file or a permissions failure
escape untyped and defeats the point of having a typed failure at all. `Directory.CreateDirectory`
throws it too, which is why that call sits inside the guarded region rather than in front of it.

The order of the key checks is load-bearing. The invalid-character check runs *before*
`Path.IsPathRooted`, because Mono's implementation throws an untyped `ArgumentException` on a
character like NUL rather than answering the question — which would let that key class escape as an
untyped exception on the runtime this game actually ships against, while passing on modern .NET.
Separators are excluded from that first check and tested separately, so a rooted key still reports
`KeyEscapesRoot` rather than being caught as merely invalid.

The containment check in `PathFor` is unreachable given the rejections above it. It is kept
because it states the invariant, rather than leaving it inferred from whatever those three happen to
catch.

## `SaveKeyPath`, and why the key logic is shared rather than mirrored

`AtomicFileStore` needs every one of `FileStore`'s key rules, in the same order, for the same
reasons. Giving it its own copy is the exact duplication `PoolFactory`'s header warns about:
`PoolFactory.Create` and `PoolFactory.CreateHolder` used to exist twice, once with a comment saying
the second copy mirrored the first *exactly*, and that comment was the tell — mirroring by hand is
the duplication, and nothing fails when a rule changes in one copy and not the other. So the five
rules — present, not an invalid character, not rooted, not `..`, no separator — and the unreachable
containment check that states the invariant, all live once in `SaveKeyPath`, and both stores call
it. `FileStoreTests` still pins every one of them; it exercises `SaveKeyPath` through `FileStore`
without knowing the type exists, which is the point — the rules did not change, only where they
live.

## `AtomicFileStore`

`FileStore` overwrites its file in place, so a kill mid-write can leave a truncated one behind.
`AtomicFileStore` writes the new bytes to a temp file next to the live one, calls
`FileStream.Flush(true)` to push them past the OS's own buffers onto disk — a plain `Dispose` only
empties the stream's own buffer into the OS, which a kill immediately after can still lose — and
only then swaps the temp file into place. The file it replaces is kept as `.bak` rather than
deleted. `ReadAsync` prefers the live file and falls back to `.bak` when the live one is absent or
throws while being read; that fallback is the entire reason the class exists, not an afterthought.

The swap prefers `File.Replace`, the platform's own swap-and-keep-a-backup primitive, over hand-
rolled copy-and-rename, but `File.Replace` is not something this code can assume works everywhere:
it can throw `PlatformNotSupportedException` on a runtime that has never implemented it, and it can
fail outright — across a filesystem boundary the rename can't cross, for instance. Either failure
falls back to a manual copy-then-delete-then-move sequence that reaches the same end state without
the OS's help. Where there is no live file yet — the first save under a key — there is nothing to
keep as `.bak`, so the swap is a plain move instead.

**What the `.bak` fallback protects against, and what it does not.** A kill during the write to the
temp file leaves the live file and its `.bak` exactly as they were; the half-written temp file is
simply overwritten the next time this key is saved. A kill during the swap itself is the case the
class exists for: on the `File.Replace` path the live file and the new content trade places in one
call, so a reader sees the old file or the new one, never a partially written one. On the platforms
where `File.Replace` is unavailable and the manual fallback runs, that guarantee is weaker: a kill
between the fallback copying the old live file to `.bak` and its final rename of the temp file into
place can leave the live file briefly absent, and `ReadAsync` returning the `.bak` copy in that
window is the fallback doing its job, not a bug — the write in progress is lost, but nothing already
saved is. What no path here protects against is corruption the filesystem introduces underneath a
write that already completed successfully — bit rot on the drive, say — because at that point both
the live file and `.bak` report success reading back, and there is no third copy to check either one
against.

`WriteAsync` also deletes the temp file if `Swap` throws an exception the process survives to catch
— a permissions failure partway through the manual fallback, say. That is a different case from a
kill: a kill leaves the temp file regardless, because there is no code left running to clean it up,
and that is fine, because the next write under the same key overwrites it via `FileMode.Create`
anyway. The cleanup only matters for the case where the process is still running after the failure,
so a caught exception does not leave a stale `.tmp` file sitting next to the live save looking like
a second, half-recoverable copy of it. The cleanup itself is best-effort: a failure deleting the
temp file is swallowed rather than replacing the exception that made cleanup necessary in the first
place.

## `PlayerPrefsStore`, and why it base64s

`PlayerPrefs` only stores strings, never bytes, so something has to translate between the two, and
`PlayerPrefsStore` does it rather than pushing that knowledge onto every caller — the same reasoning
that put `enc` in `SaveEnvelope` instead of asking `ISaveCodec` to know about encodings. Base64 in on
`WriteAsync`, decode back out on `ReadAsync`.

Only `SaveKeyPath`'s presence check carries over from `FileStore`'s rules. A `PlayerPrefs` key names
an entry in a key-value store, not a location on a filesystem, so there is no root to escape and no
path separator that means anything to it — the rest of `FileStore`'s rules exist solely to stop a key
from resolving to the wrong *file*, a risk that does not exist here.

The key prefix is a required constructor argument for the reason `FileStore`'s root is: a test can
namespace itself away from the real editor prefs instead of reading or clobbering them.
`PlayerPrefs.Save()` runs after every write because `PlayerPrefs` otherwise buffers changes until the
process quits normally, and a save that only survives a clean quit is not a save.

## `InMemoryStore`

The general form of `Tests/Common/InMemoryResourceBankSaveHandler`: a dictionary keyed by save key,
copying bytes on the way in and out so a caller mutating an array after handing it to `WriteAsync`,
or mutating one handed back from `ReadAsync`, cannot reach into what the store believes it holds. It
is not only a test double — an editor mode that must never touch the real save can point a
`SaveProfileSO` at `InMemory` and get exactly that as a production choice.

## The three selection enums are append-only

`SaveStorage`, `SaveCodec` and `SaveProtection` are what `SaveProfileSO` serializes and
`SaveServiceFactory` reads back. All three are append-only for the reason `PoolStrategy` documents:
a `ScriptableObject` field backed by an enum is serialized by its numeric index, not its name, so
inserting a member in the middle silently repoints every already-authored profile at a different
backend, codec or protector the next time it loads — silently, because the field still holds a valid
index for *some* member, just not the one whoever authored the profile picked. A new member always
goes on the end of whichever enum it belongs to, including once phase 3 gives `SaveCodec` and
`SaveProtection` a second member.

## `SaveServiceFactory`, and why every switch has a working default arm

The one place that turns a `SaveProfileSO`, or a bare `(SaveStorage, SaveCodec, SaveProtection)`
triple, into an `ISaveService` — static and stateless, like `PoolFactory` and `CatalogBuilder`. A
null or destroyed profile throws `SaveException.NoProfile()` — checked with `== null`, not `is
null`, because a destroyed `SaveProfileSO` is Unity-null rather than C#-null and only the overloaded
operator catches that.

Every one of its three internal switches has a working `_ =>` arm rather than a `throw`, for the
same reason `PoolFactory.Create`'s does: the enum it switches on is a serialized field, which can
legally hold a member this build's switch has never heard of — an older build's profile, read after
a newer build added a storage backend, say — and refusing to produce a save service at all is a
worse failure than falling back to a working default. `File`, `Json` and `None` are each that
default, which is also why each sits first in its own enum: index 0 is where a freshly serialized
field lands before anyone has touched the dropdown, so the member a missing case falls back to and
the member a new field starts on are the same one.

`CreateCodec` and `CreateProtector` list `SaveCodec.Json` and `SaveProtection.None` as explicit arms
*alongside* the discard, even though today the discard alone would return the same thing. An enum
with one member makes that redundancy easy to "clean up" into just the discard, and that is exactly
the shape of the mistake `PoolFactory.Create` warns about: skip the arm for a new member and the
switch still compiles, and it quietly keeps returning `JsonCodec` or `NoProtection` for a codec or
protector that was supposed to be a real choice. `SaveService`'s codec/protector id check on load —
see "Loading, and the three ways a version goes wrong" — is a second line of defence if that ever
happens, since a save written by one codec and loaded through another fails as
`UnexpectedComponent` rather than silently decoding garbage. That check does not make the explicit
arm optional; it is what keeps a missing arm from being *invisible* rather than what makes it safe.

`SaveStorage.PlayerPrefs` needs a key prefix that `SaveProfileSO` has no field for, because nothing
in this assembly is allowed to know a concrete game key. `Create` and `CreateFrom` both take a
`playerPrefsKeyPrefix` parameter alongside `rootDirectory`, defaulting to a fixed prefix the same
way a null `rootDirectory` defaults to `FileStore.DefaultRootDirectory()` — for the same reason:
a test pointing a `File`-backed profile at a throwaway directory but leaving `PlayerPrefs` on the
default prefix would still be writing into the developer's real editor prefs.

### What adding a storage backend takes

In the shape the pool strategy list in `docs/design-decisions.md` uses.

1. Write the implementation in `_Project/Scripts/Saving/`, implementing `ISaveStore` and calling into
   `SaveKeyPath` for whichever of its rules actually apply to the new backend — see the
   `PlayerPrefsStore` section above for what "apply" means here.
2. Add the enum member to `SaveStorage`, **appending it after `InMemory`**. The values are serialized
   by index, so inserting in the middle silently repoints every authored `SaveProfileSO` at a
   different backend.
3. Add the arm to `SaveServiceFactory.CreateStore`. Skipping this compiles cleanly and quietly hands
   back a `FileStore`.
4. If the constructor needs something beyond a root directory — a key prefix, a bucket name,
   whatever the backend calls its namespace — decide where the factory gets it. `playerPrefsKeyPrefix`
   is the precedent: a same-shaped optional parameter on both `Create` and `CreateFrom`, defaulting
   to a fixed value, so a test can redirect it exactly as it redirects `rootDirectory` rather than
   being stuck writing into whatever the default actually points at.

Nothing in `ISaveStore`, `SaveService` or `SaveException` needs to change: `SaveService` composes
whatever `ISaveStore` it is handed, and a new backend reports its own storage failures through
`SaveException.Io`, the same as `FileStore` and `AtomicFileStore` do.

## Exceptions

`SaveException` derives from `ChestGameException`; `PoolException` deliberately does not. The split
is not inconsistency. Every failure a pool reports — an unassigned prefab, an inactive holder — is a
wiring mistake only a developer can cause. Every failure saving reports can happen to a player who
wired the game correctly: a full disk, a save a newer build wrote, a file that got truncated. The
first kind should crash where a developer can see it; the second kind the game owes the player a
sentence about.

## The assembly is not a leaf

`Company.ChestGame.Pooling` references no project assembly at all. Saving was meant to match that and
does not: it references `Company.ChestGame.Common`.

That is deliberate rather than a compromise to tidy up later. Saving needs `ChestGameException` for
the reason above, and phase 5's write coalescing needs `IGameClock` — pooling needs neither, because
it is synchronous and knows nothing about frames. Keeping the leaf property would have meant
duplicating a clock abstraction to avoid a dependency that `Config`, `Rewards` and `Minigame` all
take anyway. The honest version of the property is narrower: this assembly knows nothing about
chests, currency, minigames, popups or Addressables.

## Not built yet

Other codecs (gzip, binary), other protection (base64, xor, HMAC, AES), the migration chain, the
thread hop and write coalescing, the `ResourceBank` adapter, a profile validator, and any
registration in `GameLifetimeScope`. Nothing in the game references this assembly yet.

One decision is already fixed for that work: `BinaryFormatter` is not an option for the binary
codec. Obsolete from .NET 5, removed in .NET 9, and a remote-code-execution vector on input an
attacker can influence, which a save file on a player's device is.
