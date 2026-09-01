# Saving

`Company.ChestGame.Saving` persists arbitrary state behind one seam, `ISaveService`. Only the
walking skeleton exists so far: a JSON codec, a file store, no protection, and a versioned envelope.
Nothing in the game references it yet.

## The shape, and what it copies

Pooling's shape, with one structural difference. A pool is one of four mutually exclusive things, so
`PoolStrategy` is a flat enum and `PoolFactory` picks a branch. Saving's variants are orthogonal:
where the bytes land, how the object becomes bytes, and what protects them are independent choices,
and a flat enum covering them would need six times four times five members.

So the seam splits three ways and composition replaces selection:

```
state -> ISaveCodec -> IPayloadProtector -> ISaveStore -> disk
```

`SaveService` is the composition. A factory that picks the three from an authored profile is later
work and does not change the contract.

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

Other stores (atomic, PlayerPrefs, in-memory), other codecs (gzip, binary), protection (base64, xor,
HMAC, AES), the three selection enums, the profile asset, the factory, the migration chain, the
thread hop and write coalescing, the `ResourceBank` adapter, and any registration in
`GameLifetimeScope`.

Two decisions are already fixed for that work. Enum members will be serialized by index, so all three
selection enums are append-only for the reason `PoolStrategy` documents. And `BinaryFormatter` is not
an option for the binary codec: obsolete from .NET 5, removed in .NET 9, and a remote-code-execution
vector on input an attacker can influence, which a save file on a player's device is.
