# Saving

`Company.ChestGame.Saving` persists arbitrary state behind one seam, `ISaveService`. It holds three
JSON codecs, five protectors, a versioned envelope, four stores (`File`, `AtomicFile`, `PlayerPrefs`,
`InMemory`), three selection enums with an authoring profile and a validator over them, the
enum-to-type mapping (`SaveComponentFactory`) and the environment and key material it needs
(`SaveFactoryInputs`), a thin convenience that assembles those into an undecorated `ISaveService`
(`SaveServiceFactory`), a migration chain, the seam a pre-envelope legacy import plugs into, a store
decorator that hops encoded bytes onto a worker thread and back (`ThreadHoppingStore`), a
write-coalescing scheduler (`SaveScheduler<T>`) built on top of `ISaveService` rather than inside it,
and the registry that lets a composition root flush every scheduler at pause and quit without naming
any of their types (`ISaveFlushRegistry`).

The assembly knows nothing about chests, currency, minigames, popups or Addressables. Three keys go
through it today, each from a different assembly: `"currency"` from `Company.ChestGame.Currency`,
`"chests"` from `Company.ChestGame.Minigame.Chests`, and `"meta"` from `Company.ChestGame.Core`. A
separate demo assembly, `Company.ChestGame.Saving.Demo`, makes the combination space visible and
lets you tamper with a save to see which pipelines notice.

Read it in whatever order suits you; the sections below go roughly from the seam outwards, and the
reasoning behind the choices — including the ones that were wrong first — is in
[design-decisions.md](design-decisions.md) sections 15 to 17.

## The shape, and what it copies

Pooling's shape, with one structural difference. A pool is one of four mutually exclusive things, so
`PoolStrategy` is a flat enum and `PoolFactory` picks a branch. Saving's variants are orthogonal:
where the bytes land, how the object becomes bytes, and what protects them are independent choices,
and a flat enum covering them would need six times four times five members.

So the seam splits three ways and composition replaces selection:

```
state -> ISaveCodec -> IPayloadProtector -> ISaveStore -> disk
```

`SaveService` is the composition. `SaveComponentFactory` picks the three from an authored
`SaveProfileSO`, and `SaveServiceFactory` assembles them into a working service, without either
changing the contract; see the sections below.

## The envelope

`SaveEnvelope` is always plain JSON, never encoded or encrypted, and carries the schema version, the
ids of the codec and protector that produced the body, how the body is embedded, and the body.

The ordering rule is the whole point: **the version must be readable before anything decides how to
decode the body.** Put it inside the protected body and a key rotation you get wrong is
unrecoverable, because there is no way to learn what you are holding.

`body` sits as raw embedded JSON when the codec and the protector are both text-safe, so a developer
can open a save and read or hand-edit it. Otherwise it is base64. `enc` records which, because
nothing about the other three fields says so.

A byte-exactness assertion alone would also pass a base64-only implementation that happened to
round-trip cleanly, so `SaveEnvelopeTests.ARawBody_IsEmbeddedLiterallyRatherThanAsAQuotedString` pins
the raw branch's own shape directly: the body has to sit in the envelope as literal JSON, not as text
wrapped in quotes the way a base64 body would appear.

### Value-exactness, and where the formatting stops

Every **value** in a text-safe body round-trips through `GetBody(Wrap(x))` exactly, and that is the
correctness property this section exists for. `"2026-09-01"` coming back as
`"2026-09-01T00:00:00"`, or `1.50` coming back as `1.5`, is not a hypothetical — the first is in this
game's own save model, and either would silently change a value the player never touched.

Getting there cost the obvious implementation. `JsonConvert.DeserializeObject<SaveEnvelope>` would
rebuild `Body` from Newtonsoft's own object model, and that model does not remember the text it came
from. A trailing zero on a decimal (`1.50` returns as `1.5`) and a date-shaped string
(`"2026-09-01"` returns as `"2026-09-01T00:00:00"`) both die on that trip, and the second one is in
this game's own save model. So `Parse` walks the envelope with a single `JsonTextReader` and sets
`DateParseHandling.None` and `FloatParseHandling.Decimal` on it so neither is reinterpreted on the
way past, then captures the body with `JRaw.Create`.

**`GetBody(Wrap(x))` does not reproduce `x` byte for byte, and an earlier version of this section
claimed it did.** `JRaw.Create(reader)` re-serializes the token it captures through a fresh
`JsonTextWriter` at the default `Formatting.None` rather than preserving the source text — so
insignificant whitespace inside a text-safe body is normalised to compact form on read. This was
invisible while `JsonCodec` was the only text-safe codec, because its own output was already
compact; `PrettyJsonCodec` is the first one where it shows. Do not "fix" `Parse` over this: capturing
true source text means tracking reader positions across the whole read loop, and it would buy only
the preservation of whitespace nothing reads. What actually matters is untouched — the two reader
settings above are what stop a date or a decimal being reinterpreted, and neither depends on source
text being preserved.

The file **on disk** still carries the codec's own formatting regardless: `Wrap` builds the body
`JRaw` directly from the codec's own output string, and `Serialize` writes that `JRaw` verbatim
through `WriteRawValue`, so a `PrettyJsonCodec` save is genuinely indented and readable in a text
editor — the whole reason that codec exists. Only a subsequent `Parse` normalises the whitespace
away, and `SaveService` never re-wraps and re-writes what it loaded, so a save that is never
re-saved keeps its original formatting for as long as it sits on disk.

No protector depends on the stronger, false claim. Every protector phase 3 adds is non-text-safe, so
a signed or encrypted body always travels the envelope's base64 path, and base64 round-trips exactly
on its own regardless of what `JRaw.Create` does to whitespace — this is worth stating plainly, since
it is the second time this section's justification has needed correcting.

The value guarantee is best-effort rather than absolute: a number in scientific notation, or a
literal negative zero, can still come back reformatted, because both go through the same numeric path
that protects ordinary decimals. Nothing `JsonCodec` or `PrettyJsonCodec` writes produces either.

One consequence is easy to reintroduce. `JRaw` captures *literal source text*, so a base64 body comes
back still wrapped in the quotes it was written with, and handing that to `Convert.FromBase64String`
throws. `Parse` unwraps it after the read loop rather than inside the switch, because JSON field
order is not guaranteed — `enc` can arrive after `body`, so the end of the loop is the first point at
which both are known.

Byte-exactness is not merely nice to have here: a protector that signs or encrypts the body signs or
encrypts the exact bytes `GetBody` hands it, so a body that merely decoded to something value-equal
would fail `HmacSignedProtector`'s signature check (or `AesProtector`'s) on every valid save.
`SaveEnvelopeTests` asserts on the raw bytes `GetBody` returns for this reason, never on a value
decoded from them.

For every codec, the value itself survives `GetBody(Wrap(x))` regardless of what happens to
formatting; per codec, `SaveCodecEnvelopeValueExactnessTests.AssertSurvives` pins one further thing
explicitly. `JsonCodec`'s output is already compact, so `Parse` has nothing to normalise and the
bytes come back unchanged — the same guarantee `SaveEnvelopeTests` pins with a fake codec.
`PrettyJsonCodec`'s indentation normalises to compact on `Parse`; asserting the resulting bytes equal
what `JsonCodec` would have written for the same value pins that normalisation explicitly, so it
cannot quietly regress into no normalisation, or into reformatting the value itself, without a test
noticing either way. `GzipJsonCodec` is not text-safe, so its body only ever travels as base64,
already proven exact on its own in `SaveEnvelopeTests`; there is nothing further to pin about
formatting for it.

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

A version older than this build is refused too, unless `SaveService` was built with a `SaveMigrator`
— see "The migration chain" below for what changes once one is supplied. Without one this still
throws `NoMigrationPath` exactly as it always has; older is not symmetric with newer, and only the
older side ever grows a second branch.

`codec` and `prot` are then checked against the configured components — for every version, migrated
or not. They are recorded so a reader can tell what wrote a body before trusting itself to decode it,
and a field nothing reads is decoration. The check comes after the version check because a save from a
newer build may legitimately name a codec this one has never heard of, and "written by a newer build"
is the more useful thing to report.

## The migration chain

Phase 4 is the mechanism, proven against a frozen corpus and test-only migrations — not a real
migration. `CurrentSchemaVersion` stays at `1`: there is no v1 → v2 step to write until a save model
exists to define what v2 looks like, and inventing one to exercise the chain would put a lie in
production code. Phase 6/7 is what finally makes this branch reachable outside a test.

### `ToJson`, and why a migration cannot go through `Decode<T>`

A migration rewrites a document into a shape the current `T` no longer matches — that is the entire
point of one existing. `Decode<T>` cannot serve that: it is built to produce exactly one `T`, not to
hand back something in between. So `ISaveCodec` gains `string ToJson(byte[] encoded)` — the codec's
own bytes as a JSON document — which is what lets a migration reach the JSON directly instead of the
typed value on the other side of it.

Every codec this assembly ships is JSON underneath, so every one of them can implement this
honestly. `JsonCodec` and `PrettyJsonCodec` just decode UTF-8, the same first step `Decode<T>` already
took. `GzipJsonCodec` decompresses first, then defers to `JsonCodec.ToJson` for the same reason its
`Decode<T>` defers to `JsonCodec.Decode<T>` — gzip is the layer it adds, not the JSON underneath. This
is a real constraint stated on purpose, not hidden: a codec that were not JSON-shaped under its own
encoding could not participate in migration at all, and nothing here pretends otherwise.

`SaveCodecToJsonTests` asserts each codec's `ToJson` against exactly what its own `Encode` produced,
never against a value merely equal after a fresh deserialize: a comparison by re-deserializing would
not catch `ToJson` quietly reformatting the JSON, or routing through the wrong underlying codec. For
`GzipJsonCodec` specifically, since it composes `JsonCodec` rather than duplicating its serialization,
`ToJson` has to hand back exactly what a plain `JsonCodec` would have produced for the same value, not
merely something that deserializes to an equal object.

### `ISaveMigration` and `SaveMigrator`

`ISaveMigration` is one step: `FromVersion` and `JObject Apply(JObject document)`, from `FromVersion`
to `FromVersion + 1` and never further. `SaveMigrator` is what walks a chain of these from a document's
stored version up to a target, strictly ascending and one version at a time — never skipping a step,
never applying two at once. It takes no Unity type anywhere in its surface, so it is exercised by a
plain NUnit test with no player loop, the same reason `SaveMigrator` and `SaveEnvelope` both stay free
of `UnityEngine`.

`SaveMigrator`'s constructor accepts a null migrations collection and treats it as an empty chain
(`Array.Empty<ISaveMigration>()` in the constructor's null-coalesce) rather than throwing: a
`fromVersion == toVersion` walk over it takes zero steps and succeeds, and a walk of one step fails as
`NoMigrationPath` exactly as it would for an explicitly empty collection.

Four failures are construction- or call-site errors, all `SaveMigrationException` rather than
`SaveException`: two migrations declaring the same `FromVersion` is a wiring mistake only a developer
can cause, so `SaveMigrator`'s constructor throws immediately rather than waiting for the first old
save that happens to need that step; asking it to migrate down to a target below the version it was
handed is the same kind of mistake at the call site instead; a null entry document has no step to work
from; and a step handing back null instead of a document — a missing return path, or one that gives up
on input it does not recognise — is caught the moment it happens, rather than surfacing as a
`NullReferenceException` on the next step's `Apply(null)`, or as an unexplained failure inside
`SaveService`'s `ToObject<T>()` several calls away from the step that actually caused it.
`SaveMigrationException` sits beside `SaveException` without being one, exactly the split
`PoolException` and `FrameBudgetException` each already draw against their own assembly's
player-facing exception — see "Exceptions" below.

A missing step partway through the walk is different: a stored save genuinely older than every chain
this build was shipped with is data a player can hand back to the game, not a mistake a developer
made, so it reuses `SaveException.NoMigrationPath` — naming whichever version the walk actually got
stuck at, which is only the originally stored version if the very first step is the one missing.

### Wiring: what `LoadAsync` does with a migrator

`SaveService` takes an optional `SaveMigrator` (and an optional `ILegacyImport`, below) as trailing
constructor arguments defaulting to `null`, so every existing call site keeps compiling and behaving
exactly as it did before this phase. Three routes, chosen by comparing the stored version to
`CurrentSchemaVersion`:

- **equal** — unchanged: `codec.Decode<T>(body)`, the same call this made before phase 4.
- **above** — unchanged: `VersionTooNew`, refused outright, never reaching the codec.
- **below**, with a migrator supplied — `codec.ToJson(body)` → `JObject.Parse` → `migrator.Migrate`
  up to `CurrentSchemaVersion` → `document.ToObject<T>()`.
- **below**, with none supplied — unchanged: `NoMigrationPath`, exactly as before this phase existed.

Materialising the migrated `JObject` straight through `ToObject<T>()` rather than re-serializing it
back to text and handing it to `Decode<T>` is consistent with `Decode<T>` itself, not a shortcut around
it: both are Newtonsoft over the same document, one reading it from the codec's bytes and the other
from the chain's own output. Nothing about the codec/protector id checks changes for a migrated
save — those still run first, for every version, because they answer "did the configured pipeline
write this" rather than "what shape is inside it".

### What adding a schema version takes

In the shape `docs/design-decisions.md`'s pool strategy list and this file's own storage-backend list
both use — the ordering matters because a save on disk cannot be reformatted the moment the number
changes.

1. Define what changes about the save model, then write the `ISaveMigration` step from the old
   `CurrentSchemaVersion` to the new one — `FromVersion` is the *old* value.
2. Bump `SaveService.CurrentSchemaVersion` to the new value. Do this in the same change as step 1;
   a version bump with no step to reach it is a save this build can no longer read its own recent
   output from, and a step nothing asks for is dead code nothing exercises outside a test.
3. Register the new migration wherever `SaveMigrator` gets constructed. A `SaveMigrator` is built once
   from every step the game ships, not one per version bump.
4. Add the new version's file to the golden corpus — see below — generated from a build that still
   writes the *old* version, before `CurrentSchemaVersion` moves. Generating it after the bump would
   defeat the entire reason the corpus exists.
5. Add a test that loads the new corpus file through a `SaveMigrator` carrying the real chain and
   asserts the result matches what the new save model expects. This is the test that actually proves
   the new step against bytes an old build really wrote, not bytes today's build invented to check
   itself.

Nothing about `ISaveCodec`, `IPayloadProtector` or `ISaveStore` needs to change: a schema version is a
property of the document inside the envelope, not of anything that carries it.

## The legacy import

`ILegacyImport` is the seam a save that predates the envelope entirely plugs into. What ships today
under `ResourceBankSaveData_CurrencyType` is exactly that: a bare `{"ResourceAmount":{…}}` written by
`DefaultResourceBankSaveHandle` straight into `PlayerPrefs`, under a different key than anything
`ISaveStore` in this assembly would ever use, with no envelope and no version field at all. It never
reaches `SaveMigrator`, because there is no `v` for the chain to start walking from — this is why the
import runs *before* the chain rather than as a step inside it, the same way `LoadAsync`'s own
first-run check runs before the envelope is even looked at.

`ILegacyImport` stays generic: `IsPresent()`, `Import()` returning a `JObject` already reshaped to
`CurrentSchemaVersion`, and `Clear()`. Nothing in its signature or in `SaveService` names
`CurrencyType`, `ResourceBank`, or any other game type — that knowledge belongs entirely to whatever
adapter phase 6/7 builds against this interface, never to this assembly.

**The ordering guarantee is the entire reason this exists as three separate methods instead of one.**
`SaveService.LoadAsync` only ever reaches `ILegacyImport` when its own store answers nothing under
`key` — a real save always wins first. When it does reach it: `Import()` produces the document,
`SaveAsync` writes and durably persists it through the normal pipeline, and only *then* does
`Clear()` run. The window between "old data cleared" and "new save durable" is where a player loses a
balance nobody can get back, so the ordering is write-then-clear, never the reverse, and nothing here
clears first to be tidy about it.

That ordering is also what makes the whole thing safe under interruption without `IsPresent()` having
to be exactly right about every intermediate state. If the process dies before the write finishes, the
legacy data is untouched and the next `LoadAsync` call finds it exactly as before. If it dies after the
write succeeds but before `Clear()` runs, the *next* call finds a real save under `key` and never asks
`ILegacyImport` anything again for that key — the legacy data is simply orphaned, not re-applied and
not corrupted. That is also the idempotency guarantee: running the import twice is not "safe" because
`IsPresent()` is trusted to say false the second time, it is safe because a successful write makes the
whole branch unreachable regardless of what `IsPresent()` would have answered. A failure to `Clear()`
itself is swallowed rather than allowed to fail an otherwise-successful load — the same best-effort
reasoning `AtomicFileStore`'s own temp-file cleanup follows — because the new save already exists and a
leftover legacy entry is inert, not lost. It is also logged as an error (`SaveService.ImportLegacyOrFreshAsync`'s
own catch), so a legacy `Clear()` that keeps failing is still visible somewhere even though it never
fails the load itself.

Cancellation follows the same ordering guarantee. Cancelling before `SaveAsync`'s write completes has
to actually abort the write rather than let the import path race ahead of it, so nothing durable ever
lands under the key and `Clear()` never runs. Cancelling after the write has already succeeded, while
`Clear()` itself is running, must not be able to turn a completed import back into a failed load — the
imported value is already durable by that point, so a cancellation token from here on cannot un-succeed
it.

### `TargetKey`, and the defect a second save key exposed

`LoadAsync<T>(key, ct)` used to run `ImportLegacyOrFreshAsync` for *any* key whose store read answered
null. That was harmless for exactly as long as `"currency"` was the only key any composition ever
loaded — one import, one key, no way for them to disagree. Phase 7 adds `"chests"` and `"meta"` over
the same registered `ISaveService`, and the moment it does, the old behaviour is a data-destruction
path: the first load of `"chests"` on an upgrading install finds nothing stored, reaches the currency
import, parses `{"ResourceAmount":{…}}`, deserialises it into a `ChestsRunSaveDocument` — which
*succeeds*, because Newtonsoft ignores unknown fields and hands back a default-valued document —
writes that under `"chests"`, and then calls `Clear()`. The currency legacy entry is gone before
currency ever asked for it. Nothing throws, and the player's balance is simply zero on next launch.

`ILegacyImport.TargetKey` is the save key an import's data belongs under.
`ImportLegacyOrFreshAsync` compares it against the key it was actually asked for, ordinally, **before
`IsPresent()` is ever called** — not merely before `Import()`, because asking an import whether it has
data for a key it knows nothing about is already the wrong question regardless of the answer.
`CurrencyLegacyImport.TargetKey` returns `CurrencyResourceBankSaveHandle.SaveKey` rather than
restating the `"currency"` literal, so the two cannot drift.

Worth stating what this does *not* fix: one `ILegacyImport` per `SaveService` is still the shape, so a
composition needing legacy imports for two different keys needs two services or a composite import.
Nothing needs that today, and a `TargetKey` that answers for exactly one key is the smallest thing
that closes the defect.

## The golden corpus

`Assets/Tests/EditMode/SaveCorpus/` holds one committed envelope file per historical schema version —
`v1.json` today, the only one that exists while `CurrentSchemaVersion` stays at `1`. These are real
bytes a build actually produced through the real pipeline (`SaveService` over `JsonCodec` and
`NoProtection`), frozen the moment they were generated, not JSON typed by hand to look plausible.

`SaveCorpusGenerator` writes them through an in-memory store rather than a real file. What a corpus
file has to be faithful to is the envelope, the codec and the protector - the parts a future build
reads back. Where the bytes momentarily landed on the way to being written out is not part of that,
and using a real file store would only add a filesystem to a step whose output is captured either
way.

**Regenerating a file already in the corpus defeats the entire point of having one.** A corpus file's
value is that it is what an old build actually wrote; a file regenerated by today's build only proves
today's build agrees with itself, which a normal round-trip test already does far more directly. The
corpus exists for the version *after* the one a file was frozen for: once `CurrentSchemaVersion` moves
and a migration is written to reach it, the old file is what proves that migration against bytes that
predate it, not against a convenient fiction.

`Assets/_Project/Scripts/Editor/SaveCorpusGenerator.cs` is the only sanctioned way to add a new file —
a menu command under `Tools/Saving/`, so producing one is a deliberate, reproducible action logged in
source control rather than a manual paste into a text editor. It refuses to overwrite a file that
already exists, for the reason above; extending it for a new schema version is part of step 4 in "What
adding a schema version takes". `GenerateV1` blocks on the awaiter (`GetAwaiter().GetResult()`) when
saving and reading back the fixture; that is safe rather than a deadlock risk because every component
in this pipeline (`InMemoryStore`, `SaveService`, `JsonCodec`, `NoProtection`) completes
synchronously.

### The fixture values are chosen, not incidental

`v1.json`'s body is not an arbitrary POCO. This is the one file whose entire purpose is to be read by
a build that does not exist yet — the long-term guard against someone quietly removing
`DateParseHandling.None` or `FloatParseHandling.Decimal` from `SaveEnvelope.Parse`, or "simplifying"
`JRaw.Create` back to a plain `JToken` property, long after everyone who remembers why has moved on. A
fixture holding only values that survive a naive parse anyway — an integer, a plain string, a `1.5`
with no trailing zero — proves the envelope still parses, but not that it still parses *correctly*,
which is the harder and more valuable claim.

Every field past `Note`/`Coins` earned its place by an actual A/B comparison: `SaveEnvelope.Parse` as
shipped, against a copy with `DateParseHandling`/`FloatParseHandling` stripped out, and separately
against `JsonConvert.DeserializeObject<SaveEnvelope>` — the implementation this type's own header
warns against reintroducing. Only the values below came back *different* between those two:

- `Multiplier` keeps a trailing zero (`1.50`). Stripped of `FloatParseHandling.Decimal`, it comes back
  `1.5`. `SaveGoldenCorpusTests.V1Json_OnDisk_StillLiteralyContainsTheTrailingZero_NotShortenedTo1Point5`
  is independent of the read-side check above: it pins that the corpus file itself, as committed, still
  carries `"1.50"` verbatim — the write-time half of the same guarantee. A round trip that happened to
  fix up the read path but wrote a shortened `"1.5"` on some other occasion would not be caught by the
  read-side test alone.
- `HighPrecisionTimestamp` carries nine fractional-second digits — one more than the seven (100ns
  ticks) a .NET `DateTime` can hold. Anything that round-trips it through `DateTime` truncates it to
  seven, silently.
- `ZonedTimestamp` carries a UTC offset (`+05:00`). The naive path converts it to whatever the parsing
  machine's local offset happens to be — a corruption that would only show up on a machine whose clock
  disagrees with UTC, which is exactly the kind of thing a frozen expected value catches and a
  developer re-running the same test on their own machine would not.
- `LargeId` sits one past 2^53, the largest integer a `double` can represent exactly — insurance
  against a future migration step or a materialisation path that ever routes it through one, even
  though nothing today does.

A bare calendar date with no time component (`"2026-09-01"`) was tried first and dropped: on the
Newtonsoft version this project ships, it round-trips correctly whether or not the envelope's reader
settings are in place, so it would not have caught anything. Do not add it back on the assumption that
a date-shaped string is inherently risky — verify against the actual behaviour first, the same way
these five values were chosen.

**`v1.json` was regenerated once, during phase 4's review, to carry these values instead of the three
inert ones it originally shipped with.** This is the one legitimate exception to "never regenerate an
existing corpus file": at that point `CurrentSchemaVersion` had never moved past `1`, so the file had
never been read by a build other than the one that wrote it — it was not yet a historical artefact,
only a draft of one. Once a second schema version exists and this file is what a real migration proves
itself against, this exception is spent; regenerating it after that point is exactly the mistake this
section exists to prevent.

## `IsTextSafe` means valid JSON

Not merely valid UTF-8 text. The envelope embeds a text-safe body raw, so a codec emitting a bare
unquoted string would round trip through `UTF8Encoding` perfectly and still corrupt the envelope it
was pasted into. Both `ISaveCodec` and `IPayloadProtector` carry the flag and the same meaning.

## The codecs

`JsonCodec` is unchanged. `PrettyJsonCodec` is the same serialization with `Formatting.Indented`
instead of `Formatting.None` — the bytes a developer or the phase 8 demo wants to actually look at,
never the codec a shipped save should use, since indentation is pure size with no reader on the other
end once a save leaves a text editor. `GzipJsonCodec` composes `JsonCodec` rather than duplicating
its serialization — the reason is the one `SaveKeyPath`'s header gives for not mirroring `FileStore`'s
key rules by hand — and runs its output through `GZipStream`. Both new codecs report `IsTextSafe` for
the reason the flag exists at all: `PrettyJsonCodec`'s output is still JSON, so it embeds raw;
`GzipJsonCodec`'s is gzip's own magic bytes, which would corrupt the envelope if embedded raw the same
way a bare unquoted string would.

`GzipJsonCodec.Encode` compresses through a `GZipStream` constructed with `leaveOpen: true` over the
destination `MemoryStream`. `GZipStream`'s own `Dispose` is what flushes the compressed trailer, so
it has to run (via the inner `using` block) before `compressed.ToArray()` reads the result; disposing
`compressed` afterward as well would be redundant, since nothing further writes to it once the gzip
stream has flushed.

`Decode<T>` and `ToJson` share the same `Decompress` step, so both have to fail the same way on the
same bad input, not a fixed assumption about which .NET exception type `GZipStream` throws. On this
runtime, truncated gzip bytes (as few as 5) degrade quietly rather than throwing at all: `Decompress`
produces zero bytes, and `JsonConvert.DeserializeObject` of an empty string returns `null` — exactly
the case `SaveService.LoadAsync`'s own null-guard exists to catch. Bytes that are not gzip at all do
throw, with whatever exception type `GZipStream` actually throws on this runtime — read from
`Decode<T>`'s own observed behaviour with `Assert.Catch` rather than assumed in advance with
`Assert.Throws`, and held to the same type for `ToJson`.

### Why there is no binary codec

The original plan for this phase listed one. It cannot be built against this seam without weakening
it. `ISaveCodec.Encode<T>(T value)` carries no constraint on `T`, which is what lets `SaveService`
stay generic over every save model the game will ever define — and a hand-written `BinaryWriter`
schema has no way to serialize an arbitrary, unconstrained `T`. The two ways around that both cost
more than the codec is worth: reflect over `T`'s fields and reimplement a serializer, badly and
slower than Newtonsoft's own, or require every save model to implement a marker interface the codec
can call into, which pushes a serialization concern into every game type that ever wants to be saved
— exactly the coupling `ISaveCodec` exists to keep out of the rest of the game. `JsonCodec` is the
default not because a binary format was skipped for time, but because nothing about this seam can
build one without giving up the constraint that makes the seam worth having.

One decision was already fixed for this work regardless: `BinaryFormatter` was never an option.
Obsolete from .NET 5, removed in .NET 9, and a remote-code-execution vector on input an attacker can
influence, which a save file on a player's device is.

## The protectors, and what a key shipping inside the binary buys

Every protector past `NoProtection` reports `IsTextSafe` as false — none of the four emits valid
JSON, so a protected body always travels the envelope's base64 path (see "`IsTextSafe` means valid
JSON" above). Each takes its key material as a constructor argument, the same reasoning
`FileStore`'s root and `PlayerPrefsStore`'s prefix follow: a caller supplies its own and never
touches whatever a default would otherwise resolve to. `SaveFactoryInputs.Defaults()` supplies a
fixed default key per keyed protector — see "`SaveComponentFactory`, `SaveFactoryInputs` and
`SaveServiceFactory`" below for exactly what those defaults are and are not; a test wanting a
specific key builds its own `SaveFactoryInputs` or constructs the protector directly, either way
without touching `SaveComponentFactory`'s defaults.

**The key ships in the binary either way, and that is a real limit, not an oversight.** Nothing under
`IPayloadProtector` defends a save against the one machine that already has the game installed on it
— a player with the binary can extract whatever key it carries and undo `Base64Obfuscator`,
`XorObfuscator` or `AesProtector` exactly as this assembly does. What all three do buy: a save file
copied off the device, or opened in a text editor, or attached to a bug report, does not hand its
contents to whoever now has the file instead of the game. That is a real and common threat model for
a local save — a curious player poking at their own save with a hex editor, not a determined attacker
targeting this specific installation — and it is the whole of what these protectors are for.

**`Base64Obfuscator`** (`"base64"`) base64-encodes the codec's bytes and nothing else. Because the
envelope already base64-encodes any body that is not text-safe, and this protector's output never is,
choosing it means the envelope base64-encodes an already-base64 string — see
`SaveProfileValidator` below. That doubled encoding is not a bug to fix; it is the clearest
demonstration in this codebase that base64 is not protection, only illegibility, and the envelope was
always going to produce that illegibility on its own for any non-text-safe body.

**`XorObfuscator`** (`"xor"`) is repeating-key XOR, its own inverse, so `Protect` and `Unprotect`
share one method. It hides a save from a casual look at the file. It does not hide much from anyone
who tries: JSON's own repeated field names give a known-plaintext attack against a short repeating
key an easy foothold. Treat it as obfuscation, the same word `Base64Obfuscator`'s name and
`NoProtection`'s comment both already use for this tier, never as encryption.

**`HmacSignedProtector`** (`"hmac"`) prepends an HMAC-SHA256 of the payload to the payload itself.
`Unprotect` recomputes the hash over what follows the signature and compares the two through
`ConstantTimeCompare`. A mismatch, or a stored payload too short to even carry a signature, throws
the internal `PayloadTamperedException` below rather than returning corrupted bytes. `Hmac` proves
the save was not modified. It does not hide it — the payload underneath the signature is exactly
what `JsonCodec` or `GzipJsonCodec` wrote, readable by anyone who reaches it.

**`AesProtector`** (`"aes"`) is AES-256-CBC with a random IV per save, then HMAC-SHA256 over the IV
and ciphertext — encrypt-then-MAC, in that order, verified through the same `ConstantTimeCompare`
before a single byte reaches the AES transform. One key goes into the constructor; two subkeys, for
encryption and for the MAC, come out of it through an HMAC-based derivation, so the same secret is
never handed to two different primitives — a cheap way to avoid a known way to weaken both. The
stored layout is `iv (16 bytes) || ciphertext || tag (32 bytes)`.

`ConstantTimeCompare` is where the comparison both protectors verify a tag against actually lives — a
hand-written loop that XORs every byte into an accumulator and checks it only once the loop is over,
rather than `CryptographicOperations.FixedTimeEquals`. That type compiles against this project's
`netstandard2.1` API surface, but it belongs to the same .NET Core 3.0-era cryptography work as
`AesGcm` below, and this document already treats that whole surface as not dependable under IL2CPP;
the hand-written loop costs one small method and removes the question entirely. It lives once,
`internal static`, beside `SaveKeyPath` rather than inside either protector: the same reasoning
`SaveKeyPath`'s own header gives applies word for word to a security-critical comparison — a comment
in one copy claiming it agrees with the other is not a guarantee that it does, and nothing would fail
if a future edit landed in one protector's copy and not the other's, leaving one timing-safe and the
other not. Both `HmacSignedProtector` and `AesProtector` call the one implementation.

Not `AesGcm`. This project ships `apiCompatibilityLevel: 6` (.NET Standard 2.1) with IL2CPP on
Android, and `AesGcm` is documented to throw `PlatformNotSupportedException` there on platforms where
IL2CPP's linked native crypto library carries no AEAD support — a runtime failure, not a compile-time
one. Checked against this project's actual `netstandard2.1` target: `AesGcm` compiles cleanly, which
confirms it as a real option to compile against and not merely a hypothetical one — and makes it more
dangerous rather than less, since a developer reaching for it would see no warning until the failure
showed up on-device. CBC-then-HMAC needs two primitives instead of `AesGcm`'s one, but both are the
plain `System.Security.Cryptography` surface that has shipped since long before .NET Standard 2.1 and
carries no equivalent native-library gap.

### Tamper detection is a different failure from a corrupt payload

`PayloadTamperedException` is internal — a protector has no key of its own to report a failure
against, the same reason `SaveKeyPath`'s exceptions are all `SaveException` factory methods rather
than something thrown from inside a key rule. `HmacSignedProtector` and `AesProtector` are the only
two that throw it, on a failed comparison or on a stored payload too short to carry what it claims
to. `SaveService.LoadAsync` is the only thing that ever catches it, translating it into
`SaveException.PayloadTampered(key)` — a caller can tell "this save was modified after it was
written" from `PayloadUnreadable`'s "this save is missing, malformed, or from a build that cannot
read it," which no site downstream of `LoadAsync` could otherwise distinguish. Every other exception a
protector or codec throws — a bad base64 string, a truncated gzip stream — still lands on
`PayloadUnreadable`, unchanged from before this phase.

**That claim was true in prose and false in the type system until phase 8 tested it.** Both outcomes
were a plain `SaveException`, separable only by matching `"integrity check"` against the message —
which the phase 3 tests duly did, and which nobody noticed was a gap for as long as only tests needed
the answer. The save inspector is the first non-test caller that has to act on the distinction, and
having to grep an error message to find it is the evidence the API could not say what this section
claims. `SaveException.PayloadTampered` now returns `SaveTamperedException`, a sealed subclass, so
`catch (SaveException)` everywhere keeps behaving exactly as before while a caller that cares can ask
by type. The message is unchanged; the tests now assert on the type and keep the message assertion as
a secondary check rather than the whole contract.

One ambiguity is inherent to a MAC rather than a gap in this design: a body encrypted or signed under
a different key than the one `LoadAsync` is configured with fails its comparison exactly the way a
genuinely tampered body does, and `IPayloadProtector` has no way to tell the two apart. A protector
proves the bytes match what *some* key produced; it cannot prove which key that was.

## `SaveProfileValidator`

A static method, `Validate`, returning human-readable warnings for a profile's codec and protector —
never errors, and nothing it returns stops `SaveServiceFactory` from building exactly what the
profile asks for. Worth having now that `SaveCodec` and `SaveProtection` each carry more than one
real choice, and therefore combinations that compile and run but waste something or promise more than
they deliver.

**`JsonPretty` paired with anything but `SaveProtection.None`** spends bytes indenting a body for a
person to read, then hands that body to a protector whose entire `IsTextSafe` is false — the
indentation is paid for and immediately made unreadable by the next stage of the same pipeline.

**`Base64` protection, on any codec,** always produces the doubled base64 encoding described under
`Base64Obfuscator` above.

**`Hmac` protection, on any codec,** is flagged as proving integrity without confidentiality, so a
profile picking it for privacy rather than tamper-evidence is told what it actually got. The warning
is asserted against the implementation's own wording ("proves a save was not modified" / "does not
hide it"), never against the abstract vocabulary ("integrity" / "confidentiality") used to describe it
in prose: asserting the literal words would be testing a paraphrase the warning message never promised
to use.

**`Xor` protection, on any codec,** is flagged with the same obfuscation-not-encryption reasoning as
its type header.

**Deliberately not flagged: `JsonGzip` paired with an encrypting protector.** The instinct — "encrypt
after compress" wastes the compression, because encrypted bytes do not compress — describes the
opposite of what this pipeline does. `state -> ISaveCodec -> IPayloadProtector -> ISaveStore` runs the
codec first: `SaveService.SaveAsync` calls `_codec.Encode` and only then `_protector.Protect` on the
result. `GzipJsonCodec` always compresses plain JSON before any protector sees the bytes, which is the
effective order, not the wasteful one. The wasteful order would need `IPayloadProtector` to run before
`ISaveCodec`, which nothing in this architecture does, so there is nothing here to warn about — adding
the warning the instinct suggests would tell a profile author the opposite of what actually happens.

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

`WriteAsync` and `DeleteAsync` turn a `PlayerPrefsException` into `SaveException.Io`, the same
translation the file stores apply to their IO failures, so a caller catching `SaveException` around a
save is not bypassed by this backend. Unity documents that exception only as thrown by `PlayerPrefs`
in a Web build, where PlayerPrefs data is capped at 1 MB, and does not say which call throws it. So
every call that writes (`SetString`, `DeleteKey` and `Save`) sits inside the catch, and the two reads
do not. The catch names that one type rather than `Exception`, so a genuine bug is not relabelled as
a storage failure. No test reproduces the failure, because it cannot happen outside a Web build.

## `InMemoryStore`

The general form of `Tests/Common/InMemoryResourceBankSaveHandler`: a dictionary keyed by save key,
copying bytes on the way in and out so a caller mutating an array after handing it to `WriteAsync`,
or mutating one handed back from `ReadAsync`, cannot reach into what the store believes it holds. It
is not only a test double — an editor mode that must never touch the real save can point a
`SaveProfileSO` at `InMemory` and get exactly that as a production choice.

## The three selection enums are append-only

`SaveStorage`, `SaveCodec` and `SaveProtection` are what `SaveProfileSO` serializes and
`SaveComponentFactory` reads back. All three are append-only for the reason `PoolStrategy` documents:
a `ScriptableObject` field backed by an enum is serialized by its numeric index, not its name, so
inserting a member in the middle silently repoints every already-authored profile at a different
backend, codec or protector the next time it loads — silently, because the field still holds a valid
index for *some* member, just not the one whoever authored the profile picked. A new member always
goes on the end of whichever enum it belongs to — phase 3 appended `JsonPretty` and `JsonGzip` to
`SaveCodec` and `Base64`, `Xor`, `Hmac` and `Aes` to `SaveProtection`, in that order, after the
member each enum already had.

`File` is `SaveStorage`'s first member because first place is what a newly serialized field lands
on: a `SaveProfileSO` field added before any asset authors a value defaults to index 0, so whichever
member sits there is the backend a never-configured profile silently gets.

`SaveInspectorPanel` labels each segmented button by reading the enum value at that index
(`Storages`, `Codecs` and `Protections`, built from `Enum.GetValues`) and mapping it through a
`ShortNameOf` switch, rather than hardcoding a display order or trusting what SaveInspector.uxml
happens to say at that index. That keeps the panel safe against the one reordering the append-only
rule above does not rule out by itself: even if a future edit changed declaration order in a way
that still respected "append only," every button would still show the label for the value it
actually selects.

**No `SaveProfileSO` asset ships in this project today**, so the rule above is forward-looking rather
than protecting anything currently authored. Do not read `SaveProfileSO` and `SaveProfileValidator` as
the thing that configures the shipped save: they are an authoring seam with no asset behind it, and
what the game actually saves through is composed in code by `GameLifetimeScope.RegisterCoreServices`.
See "What ships, and where the composition asserts its own constraints" below. Changing the shipped
storage backend means editing that composition, not hunting for a profile asset to re-point.

## `SaveComponentFactory`, `SaveFactoryInputs` and `SaveServiceFactory`

Through phase 5, one type — `SaveServiceFactory` — did five different jobs: map each enum member to
a concrete component; assemble the three into a `SaveService`; supply a default file root and
PlayerPrefs prefix; hold the default XOR, HMAC and AES keys as static fields; and implicitly decide
that no decorator and no scheduler ever participate. Only the first of those is genuinely a factory's
job. Phase 6a splits the other four out, before phase 6b's adapter has to decide where its own
pieces — a `SaveMigrator`, an `ILegacyImport`, a `ThreadHoppingStore`, a `SaveScheduler<T>` — plug in.
Deciding that boundary after the adapter exists would mean rewriting the adapter around whatever the
boundary turned out to be; this phase is a pure boundary move, not a redesign — the same triple still
produces the same component types, the same defaults, and the same round-trip behaviour as before.

**`SaveFactoryInputs`** is an explicit inputs type carrying everything the three selection enums
cannot: the file root, the PlayerPrefs key prefix, and the key material `XorObfuscator`,
`HmacSignedProtector` and `AesProtector` each need before they can run at all. It is a plain
instance — a caller can build one directly, or call `SaveFactoryInputs.Defaults(rootDirectory,
playerPrefsKeyPrefix)`, which produces exactly today's values: `FileStore.DefaultRootDirectory()`
and `"save."` for whichever of the two optional arguments is left null, and the same three
default keys this assembly has always shipped. **No static field anywhere in this assembly holds
key material any more** — `Defaults()` is a method that builds fresh byte arrays on every call, not
a cached constant, so nothing about moving this type out of `SaveServiceFactory` changed where those
bytes came from or how long they live.

Say plainly what those three default keys are, rather than let three constants that happen to
compile look like considered key material: each is its own name, UTF-8 encoded —
`"Company.ChestGame.Saving.DefaultXorKey"` and so on. That is a fine default for a showcase with no
key-management story to demonstrate, and it is exactly as strong as "the key ships in the binary
either way" already concedes above — nothing about *where* the bytes come from changes what they
buy. A real game ships a key generated for that build and baked in at build time, not typed into
source control, and — for whichever protector is meant to resist more than a curious player poking
at their own file — issues or derives that key per install rather than sharing one key across every
copy of the binary. Neither of those is built here; both are a sentence, not a mechanism, because
nothing about this phase's scope calls for one.

**`SaveComponentFactory`** is the part that must stay a factory, because no DI container can read a
serialized enum's numeric value at runtime and pick a type for it the way it can for something
registered by interface. It is the genuine job the old `SaveServiceFactory` also did, isolated: one
focused entry point per axis — `CreateStore(SaveStorage, SaveFactoryInputs)`,
`CreateCodec(SaveCodec)`, `CreateProtector(SaveProtection, SaveFactoryInputs)` — each taking
`SaveFactoryInputs` only where it actually needs environment or key material, which is why
`CreateCodec` does not take one at all. The `SharedInMemoryStore` field that makes
`SaveStorage.InMemory` behave like the other three process-global backends instead of like a fresh
scratchpad on every call lives here now, unchanged in every other respect from how it lived in
`SaveServiceFactory` before this phase — it is a store, not key material, so it is not what "no
static field may hold key material" is about.

`CreateStore` and `CreateProtector` both refuse a null `inputs` outright — `SaveException.
NoFactoryInputs()` — rather than let it surface as a `NullReferenceException` the moment an arm
below dereferences it. `PoolFactory.Create`'s own missing-prefab case is not a precedent for leaving
this unguarded: that guard exists too, one level deeper, inside the pool constructor `Create` hands
the prefab to. There is no deeper level here — `FileStore`, `PlayerPrefsStore`, `XorObfuscator` and
the rest never see `inputs` itself, only the one field this method already read out of it — so the
guard belongs here or nowhere. This is the eighth instance of a shape this assembly already guards
seven other times (`NoStore`, `NoSaveService`, `NoClock`, `NoProfile`, `NoRootDirectory`,
`NoKeyPrefix`, `NoProtectorKey`), not a new one, and it matters most exactly where it is least
exercised by a test: 6b's composition root is `SaveComponentFactory`'s first real caller outside
`SaveServiceFactory`'s own defaulting, assembling by hand and therefore the first place a null can
actually reach it.

Every one of its three switches keeps a working `_ =>` arm rather than a `throw`, for the same
reason `PoolFactory.Create`'s does: the enum it switches on is a serialized field, which can legally
hold a member this build's switch has never heard of — an older build's profile, read after a newer
build added a storage backend, say — and refusing to produce a component at all is a worse failure
than falling back to a working default. `File`, `Json` and `None` are each that default, which is
also why each sits first in its own enum: index 0 is where a freshly serialized field lands before
anyone has touched the dropdown, so the member a missing case falls back to and the member a new
field starts on are the same one. `CreateCodec` and `CreateProtector` list `SaveCodec.Json` and
`SaveProtection.None` as explicit arms *alongside* the discard for the reason given when phase 3
first added a second member to each enum: an enum with only one member makes the redundancy easy to
"clean up" into just the discard, which is exactly the shape of the mistake `PoolFactory.Create`
warns about — skip the arm for a new member and the switch still compiles, quietly keeping every
profile on `JsonCodec` or `NoProtection` regardless of what its dropdown says. `SaveService`'s
codec/protector id check on load — see "Loading, and the three ways a version goes wrong" — is a
second line of defence if a case is ever missed anyway, since a save written by one codec and loaded
through another fails as `UnexpectedComponent` rather than silently decoding garbage. That check
does not make the explicit arm optional; it is what keeps a missing arm from being *invisible*
rather than what makes it safe.

**`SaveServiceFactory`** is what remains: a thin assembly convenience that turns a `SaveProfileSO`,
or a bare `(SaveStorage, SaveCodec, SaveProtection)` triple, into a plain, undecorated `SaveService`,
by handing each enum to `SaveComponentFactory` and composing the three results — static and
stateless, like `PoolFactory` and `CatalogBuilder`. `Create`'s null-or-destroyed-profile check is
unchanged: `SaveException.NoProfile()`, checked with `== null` rather than `is null`, because a
destroyed `SaveProfileSO` is Unity-null rather than C#-null and only the overloaded operator catches
that. `Create` and `CreateFrom` both take an optional `SaveFactoryInputs`, defaulting to
`SaveFactoryInputs.Defaults()` when left null, so a caller that does not care about the file root,
the PlayerPrefs prefix, or any protector's key keeps getting exactly what it always got.

Driving a `SaveProfileSO`'s private serialized fields through `SerializedObject` rather than through
reflection matters because the point is to prove `SaveServiceFactory` actually reads what the
inspector's dropdowns write, not a value only a test's own reflection could see.
`AProfileAuthoredForAStorage_DrivesTheFactoryToTheMatchingBackend` proves round-tripping end to end,
but a round trip alone cannot catch `CreateCodec`/`CreateProtector` always falling back to its default
arm regardless of what the dropdown says, because the same (wrong) component would then be used for
both the save and the load and still agree with itself.
`AProfileAuthoredForACodec_WritesThatCodecsIdIntoTheEnvelope` and
`AProfileAuthoredForAProtection_WritesThatProtectionsIdIntoTheEnvelope` instead pin the id actually
written into the envelope on disk against what each enum member is supposed to produce — the same
check `AFreshlySerializedProfile_NamesJsonAndNoneInTheWrittenEnvelope` already runs for the default
(Json, None) pair, extended to every member of both enums. `AProfileAuthoredForACodec_StillRoundTrips`
and `AProfileAuthoredForAProtection_StillRoundTrips` complete the coverage the other direction: not
just that the right id lands in the envelope, but that what comes back out through a full save/load is
also correct.

**The ceiling is stated as a rule, not left to be inferred from what this type happens not to take
yet: `SaveServiceFactory` will never grow a parameter for a `SaveMigrator`, an `ILegacyImport`, a
`ThreadHoppingStore`, or a `SaveScheduler<T>`.** Which store a scheduler is safe to call
`FlushBlocking` on, whether this game even has a legacy save to import, whether the frame cost of
encoding is worth trading for a worker-thread hop — none of that is answerable from a profile's
three dropdowns, and every one of them is a composition-root decision, not a factory's. Folding them
in here would regrow the five-job factory this phase just split apart, one "just this one extra
parameter" at a time. A caller that needs any of them — phase 6b's adapter, and whatever registers a
`SaveScheduler<T>` in a `GameLifetimeScope` after it — composes a `SaveService` by hand from
`SaveComponentFactory.CreateStore`/`CreateCodec`/`CreateProtector` instead of going through
`SaveServiceFactory`, the same way a 24-call-site test suite still goes through `SaveServiceFactory`
for the common case of "just give me a working, undecorated service."

This is exactly `PoolFactory`'s own boundary, drawn a second time. `PoolFactory.Create` maps a
`PoolStrategy` to a pool and nothing more — it does not know a screen exists, does not decide where
the holder it builds gets parented beyond the `Transform` its caller already handed it, and does not
assemble anything past the one pool it was asked for. `ChestsMinigameView` is what builds the screen
the pool lives in, exactly as phase 6b's adapter — not this assembly — is what will build the graph
a `SaveScheduler<T>` lives in. `SaveComponentFactory` is `PoolFactory.Create`'s counterpart; the
assembly convenience `SaveServiceFactory` still offers is closer to what `PoolFactory` deliberately
does *not* also provide — a one-call assembly of "the pool plus the screen it sits in" — because
`SaveService` under a profile's three dropdowns is common enough, and cheap enough to keep undecorated,
that this assembly can afford the one thin convenience `Pooling` never needed.

`SaveStorage.PlayerPrefs` needs a key prefix that `SaveProfileSO` has no field for, because nothing
in this assembly is allowed to know a concrete game key. That prefix, and the file root
`SaveStorage.File`/`AtomicFile` need, both now live on `SaveFactoryInputs` rather than as loose
parameters on `Create`/`CreateFrom` directly — the reasoning for redirecting either away from the
developer's real save directory or real editor prefs in a test has not changed, only where the
values that do the redirecting are carried.

### What adding a storage backend takes

In the shape the pool strategy list in `docs/design-decisions.md` uses.

1. Write the implementation in `_Project/Scripts/Saving/`, implementing `ISaveStore` and calling into
   `SaveKeyPath` for whichever of its rules actually apply to the new backend — see the
   `PlayerPrefsStore` section above for what "apply" means here.
2. Add the enum member to `SaveStorage`, **appending it after `InMemory`**. The values are serialized
   by index, so inserting in the middle silently repoints every authored `SaveProfileSO` at a
   different backend.
3. Add the arm to `SaveComponentFactory.CreateStore`. Skipping this compiles cleanly and quietly
   hands back a `FileStore`.
4. If the constructor needs something beyond a root directory — a key prefix, a bucket name,
   whatever the backend calls its namespace — decide where it comes from. `PlayerPrefsKeyPrefix` is
   the precedent: a property on `SaveFactoryInputs`, filled with a fixed default by
   `SaveFactoryInputs.Defaults()` unless a caller overrides it, so a test can redirect it exactly as
   it redirects `RootDirectory` rather than being stuck writing into whatever the default actually
   points at.
5. Answer `CompletesOnCallingThread` honestly. It is not documentation — `SaveScheduler<T>` reads it
   through `CanFlushBlocking`, `SaveFlushRegistry.Register` refuses a scheduler that answers false,
   and `CurrencyResourceBankSaveHandle` and `ChestsMinigameController` both refuse at construction to
   block on a load through a store that answers false. A backend that reaches the network or hops a
   thread must say so, or the composition that wraps it will look correct and fail at
   `OnApplicationPause` on a device.

Nothing in `ISaveStore`, `SaveService` or `SaveException` needs to change: `SaveService` composes
whatever `ISaveStore` it is handed, and a new backend reports its own storage failures through
`SaveException.Io`, the same as `FileStore`, `AtomicFileStore` and `PlayerPrefsStore` do.

## Exceptions

`SaveException` derives from `ChestGameException`; `PoolException` deliberately does not. The split
is not inconsistency. Every failure a pool reports — an unassigned prefab, an inactive holder — is a
wiring mistake only a developer can cause. Every failure saving reports can happen to a player who
wired the game correctly: a full disk, a save a newer build wrote, a file that got truncated. The
first kind should crash where a developer can see it; the second kind the game owes the player a
sentence about.

`SaveMigrationException` is the same split drawn a second time, inside this one assembly rather than
between it and `Pooling`. A duplicate `FromVersion`, a target below the stored version, a null entry
document, or a step handing back null, cannot be caused by anything a player's save contains — only by
how `SaveMigrator` was built, called, or by a bug in a migration step itself — so it sits under
`InvalidOperationException` beside `SaveException` rather than under it, exactly the way
`PoolException` and `FrameBudgetException` each sit beside `ChestGameException` in their own
assemblies. A save this build genuinely has no path forward for is the opposite case — data, not
wiring — which is why that one stays `SaveException.NoMigrationPath`.

## The assembly is not a leaf

`Company.ChestGame.Pooling` references no project assembly at all. Saving was meant to match that and
does not: it references `Company.ChestGame.Common`.

That is deliberate rather than a compromise to tidy up later. Saving needs `ChestGameException` for
the reason above, and phase 5's write coalescing needs `IGameClock` — pooling needs neither, because
it is synchronous and knows nothing about frames. Keeping the leaf property would have meant
duplicating a clock abstraction to avoid a dependency that `Config`, `Rewards` and `Minigame` all
take anyway. The honest version of the property is narrower: this assembly knows nothing about
chests, currency, minigames, popups or Addressables.

## The async layer: `ThreadHoppingStore` and `SaveScheduler<T>`

Phase 5 adds two things and touches nothing else in the pipeline `state -> ISaveCodec ->
IPayloadProtector -> ISaveStore -> disk` already built. Four invariants shaped both, and they pull
against each other: `PlayerPrefs` is main-thread only; a torn save must be impossible; `SaveAsync`'s
contract must survive coalescing; and a blocking flush must not deadlock. The sections below are the
reasoning behind the design that satisfies all four at once, because none of the four is negotiable
and no one of them is satisfiable by itself.

### The thread hop, and why it is not inside `SaveService`

The obvious design — hop to the thread pool somewhere inside `SaveAsync`/`LoadAsync`, hop back before
returning — is wrong for a reason that only shows up once the existing test suite is read rather than
the interface. `SynchronousUniTask.Result`/`.Complete`, which every `SaveService`, `FileStore` and
`AtomicFileStore` test in the 533-test gate calls through, asserts `task.Status != Pending`
*immediately after the call returns, on the same call stack*. A genuine `await
UniTask.SwitchToThreadPool()` anywhere inside `SaveAsync`, `LoadAsync`, or any of `FileStore` /
`AtomicFileStore`'s own members, suspends that call stack at the first such `await` — the method
returns a `Pending` task to a caller that has not yielded, and every one of those hundreds of tests
fails immediately, not eventually. This is not a hypothetical to guard against speculatively: it was
checked directly against `SynchronousUniTask`'s own source before anything else in this section was
designed, because a design that requires touching `SaveService.SaveAsync`'s or `FileStore.WriteAsync`'s
internal control flow to add a real suspension point cannot coexist with the gate this phase is built
under. So `ISaveService`, `SaveService`, `FileStore`, `AtomicFileStore`, `InMemoryStore` and
`PlayerPrefsStore` gain no new suspension point anywhere in this phase: every member each already had
still resolves inside the same synchronous-in-a-UniTask call it always did, and `SynchronousUniTask`
cannot tell phase 5's version of any of them from phase 4's. What they do gain is small and additive —
one marker interface (`PlayerPrefsStore` implementing `IMainThreadOnlyStore`), one read-only property
each (`CompletesOnCallingThread`, below) that only ever returns a constant `true` on these five types,
and one stale comment in `FileStore` corrected to point at where the hop actually landed. Every test
that passed before phase 5 passes for the same reason it did before: nothing it drives can suspend.

The hop instead lives in a new type that wraps a store from the outside: **`ThreadHoppingStore`**, an
`ISaveStore` decorating another `ISaveStore`. `SaveService` never knows it exists — it just calls
`_store.WriteAsync`/`ReadAsync`/`ExistsAsync`/`DeleteAsync` exactly as it always has, on whatever
`ISaveStore` it was constructed with. Nothing in this phase wires `ThreadHoppingStore` into
`SaveServiceFactory` or any composition root, and it remains unwired into every composition this game
ships today. So it is available but inert, proven by the scratch harness this phase's own review used
rather than by a shipped call site.

`ThreadHoppingStore` checks `inner is IMainThreadOnlyStore` once, in its constructor, and every member
either calls straight through (main-thread-only inner store — today, only `PlayerPrefsStore`) or hops
via `UniTask.RunOnThreadPool(..., cancellationToken: CancellationToken.None)` with the default
`configureAwait: true`, which is what brings execution back to the main thread through `UniTask.Yield`
once the wrapped call finishes. `CancellationToken.None` is deliberate, not an oversight: every member
already checks the real `ct` before calling `RunOnThreadPool` at all, and the closure handed in checks
it again as the first thing the wrapped store does, unchanged from what `FileStore` and
`AtomicFileStore` already did. `RunOnThreadPool` re-checking its own `cancellationToken` argument a
*third* time on the way back across `UniTask.Yield` — after the write has already reached disk — would
report a save as canceled that, in fact, already happened. Passing `None` there is what keeps
cancellation observed only before a write starts, never lied about after one already finished.

`SaveScheduler<T>` depends on the return trip. It holds no lock, and the code after its
`await _saveService.SaveAsync(...)` reads and writes the same fields `MarkDirty` does: `_pending`,
`_hasPending` and `_activeFlush`. That is safe only because the hop resumes on the main thread before
that code runs. Passing `configureAwait: false` here, or swapping in anything else that resumes on a
worker thread, would turn those field accesses into a data race with no change to the scheduler at
all.

`IMainThreadOnlyStore` is deliberately an empty marker rather than a member every `ISaveStore` has to
implement. A store declares its own thread affinity — the brief for this phase raised that as one
option among possibly-better ones — and a marker interface was chosen over, say, a `bool
RequiresMainThread { get; }` on `ISaveStore` itself, because the latter would have forced `FileStore`,
`AtomicFileStore` and `InMemoryStore` to each add a member that always answers the same constant, for
a distinction only `ThreadHoppingStore` ever asks about.

**`ISaveStore.CompletesOnCallingThread` is the different question a marker cannot answer, and is a
real member for exactly that reason.** `IMainThreadOnlyStore` states a fact fixed for a *type* —
`PlayerPrefsStore` is always main-thread-only, so a marker on the type is enough. Whether a store
*completes on the calling thread* is instead a fact about a particular *instance* of
`ThreadHoppingStore`: the same class answers `true` when it happens to wrap an `IMainThreadOnlyStore`
and `false` otherwise, decided by a constructor argument at runtime, not by which class was written. A
marker interface cannot express "sometimes, depending on what I was built with" — only a member each
instance actually evaluates can. `FileStore`, `AtomicFileStore`, `InMemoryStore` and `PlayerPrefsStore`
all answer `true` unconditionally, the same constant `RequiresMainThread` would have forced onto three
of them above; `ThreadHoppingStore` answers `_mainThreadOnly`. `ISaveService.CompletesOnCallingThread`
carries the same fact up one layer as a pure pass-through to whichever store `SaveService` was composed
with, never a type check against `ThreadHoppingStore` by name, so `SaveScheduler<T>.CanFlushBlocking` —
see "`FlushBlocking`, and why it cannot deadlock" below — can answer honestly without knowing this
assembly's own store types any more than `SaveService` itself does.

### Where the hop sits, and why not `SaveService` — the torn-save invariant

This is the part that is more design than code. `SaveService.SaveAsync` calls
`_codec.Encode(state)` and then `_protector.Protect(plain)` **before** it ever reaches `_store`, and
that ordering is completely unchanged by this phase — see "The shape, and what it copies" above.
Wrapping the *store* rather than `SaveAsync` as a whole is what makes that ordering load-bearing
instead of incidental: `state`, the caller-owned and still-mutable object `SaveAsync` was handed, is
consumed into an immutable `byte[]` entirely on whatever thread called `SaveAsync` — always the main
thread, since nothing in this assembly ever calls it from anywhere else — before a single byte crosses
into `ThreadHoppingStore`'s hop. Only that `byte[]`, which nothing else holds a reference to once
`Encode`/`Protect` return it, ever touches a worker thread. Gameplay is free to keep mutating `state`
the instant `SaveAsync` returns control (in `SaveScheduler<T>`'s case, the instant `MarkDirty` is
called again) without any risk of a worker thread reading it mid-mutation, because nothing on a worker
thread ever reads `state` at all — encode already turned it into bytes before the hop existed.

The alternative this phase considered and rejected: hop before calling `SaveAsync`, so `codec.Encode`
also runs on a worker thread and the whole pipeline — encode, protect, write — moves off the main
thread. That buys more (encode and protect, not just the disk IO, stop costing a frame), but it means
`codec.Encode(state)` reads `state`'s fields from a worker thread while the main thread can still be
mutating it — a genuine data race, not a hypothetical one, on anything `state` holds that is not
itself thread-safe (a plain `Dictionary` mid-enumeration throws; most other shapes just corrupt
silently). `ISaveService.SaveAsync<T>`'s `state` parameter carries no constraint beyond `class`
precisely so this assembly never has to ask a save model to be immutable or cloneable — the same
reasoning "Why there is no binary codec" gives for not asking a save model to implement anything at
all. Given that constraint, encode cannot safely move to a worker thread without either weakening it
(requiring a snapshot/clone hook on every future save model) or accepting the race. Between "a frame
spike from encoding on the main thread" and "a torn read on gameplay's own state," this phase picks
the frame spike: it is a real, measurable cost, but it degrades gracefully (a slightly longer frame),
where a torn read degrades into a save that is silently wrong and nothing reports it — exactly the
failure invariant 2 exists to rule out. For the store-level IO this phase actually does move off the
main thread, the real, common-case cost is disk access, not JSON serialisation of a modest save model —
`AtomicFileStore.WriteAsync`'s call to `FileStream.Flush(true)` in particular, which forces the OS to
push bytes past its own buffers onto the physical device before returning, is a genuinely blocking
syscall, and is the specific cost this phase's hop was built to get off the main thread.

### Write coalescing, and why it cannot live inside `SaveAsync`

**`SaveScheduler<T>`** is the second new type: it owns an `ISaveService` rather than being one, and its
own two members are `MarkDirty(T state)` and `FlushAsync`/`FlushBlocking` — a different, and
deliberately weaker, surface than `ISaveService`. `SaveAsync`'s contract — "when it completes, the
save is written" — has to survive untouched, because callers that never hear about `SaveScheduler<T>`
still call `SaveAsync` directly and still get exactly that guarantee. Folding coalescing into
`SaveAsync` itself would mean `SaveAsync` sometimes means "written" and sometimes means "queued,
depending on how recently something else in the process last saved this key" — the same value
silently meaning two different things depending on invisible history, which is exactly the kind of
ambiguity "Loading, and the three ways a version goes wrong" above already refuses to allow into this
codebase once, for `LoadAsync`. `MarkDirty` gets its own name instead of hiding a weaker promise
behind `SaveAsync`'s name.

`SaveScheduler<T>` is fixed to one key and one `T` at construction, not parameterised per call the way
`ISaveService.SaveAsync<T>(key, ...)` is. A resource bank that calls `Save()` on every add and every
spend has exactly one save slot to coalesce; a scheduler juggling several independent keys would need
a table of pending writes instead of one `_pending`/`_hasPending` pair, for a generality nothing this
phase (or the resource-bank adapter phase 6/7 actually builds) needs. A game with several save slots
constructs several `SaveScheduler<T>` instances — the same per-key granularity `SaveAsync` already
has, just decided once at construction instead of on every call.

**The window is a throttle, not a debounce.** `MarkDirty` starts a countdown — `IGameClock.Delay`,
`SaveScheduler<T>.DefaultCoalesceWindowMilliseconds` (1000ms, overridable per instance) — the first
time state becomes dirty, and does **not** restart it on every subsequent `MarkDirty` call inside that
window. A debounce (reset-on-every-call) would let a caller that never stops mutating state — plausible
for a resource bank during an active minigame — starve the flush indefinitely, deferring every save to
"whenever things go quiet," which for a save system is the wrong failure mode: the whole point is a
bounded worst case between a mutation and its persistence. When the fixed window elapses, whatever is
currently in `_pending` — the *latest* state as of that moment, because every `MarkDirty` call
overwrites it rather than queuing a history of them — is what gets saved. That is the whole of what
"coalescing" means here: many calls, one window, one write, carrying the newest state rather than the
first or an average of them.

`WaitThenFlushAsync` catches `OperationCanceledException` around the clock delay without clearing
`_waiting` itself. Cancellation reaches that catch only two ways: `Dispose` cancelling `_disposedCts`,
or `InterruptWindow` cancelling `_windowCts` when a flush pre-empts the window. Both call sites already
set `_waiting = false` before cancelling, so by the time the catch runs there is nothing left for it to
clear.

A `MarkDirty` call after a coalescing window has already fired starts dirtying state again from
scratch, not more of the window that just closed, so it produces its own later write:
`SaveSchedulerPlayModeTests.MarkDirty_SeveralCallsInsideOneWindow_ProduceExactlyOneWrite_CarryingTheLastState`
follows its first, coalesced write with one more `MarkDirty` call and asserts a second write follows,
distinct from the first.

### A frozen clock is a frozen save

The countdown `MarkDirty` starts is `IGameClock.Delay`, and `UnityGameClock` — the only implementation
this game registers — binds `UniTask.Delay`'s default `ignoreTimeScale: false`, the same choice its own
header already documents for a different reason: "pausing the game pauses any chest mid-open." That
reasoning is exactly backwards for anything built on `SaveScheduler<T>`. A chest mid-open freezing when
`Time.timeScale` hits zero is the intended behaviour a pause menu wants. A save silently refusing to
reach disk until whatever set `Time.timeScale` to zero sets it back is not: `MarkDirty` still records
what changed the instant it is called, but the window that turns that record into a real `SaveAsync`
call never elapses while the scale stays at zero, so currency (and anything else a future phase builds
on `SaveScheduler<T>`) stops persisting for as long as the game is paused that way. `FlushBlocking` from
`OnApplicationPause`/`OnApplicationQuit` is unaffected — it never waits on the clock at all, see
"`FlushBlocking`, and why it cannot deadlock" below — so this is a smaller trap than it could have been:
only the *organic* coalescing window is scale-dependent, not the two callbacks this phase actually
built to close the durability gap on suspend and quit.

Nothing in this game sets `Time.timeScale` today, so this is latent rather than live — written down
here, and on `UnityGameClock` itself, for whichever future phase adds a pause menu and reaches for the
obvious `Time.timeScale = 0f` without first checking what else in the codebase already listens to it.
The fix, when one is needed, is not to this file: either give `SaveScheduler<T>` its own
`IGameClock` that ignores time scale (a real-time-only clock distinct from `UnityGameClock`, the same
way `ElapsedMilliseconds` on this type already reads real time rather than scaled time for a frame
budget's own sake), or accept that a paused save is a bounded, resumed-on-unpause delay rather than a
lost one and document that trade-off explicitly wherever the pause menu itself gets built. Nothing
about this phase needs to pick between them, because nothing about this phase can trigger the trap.

`UnityGameClock.Delay` leaves `ignoreTimeScale` at `UniTask.Delay`'s own false default rather than
passing it explicitly, so that a future change to it shows up as an argument appearing in a diff
rather than as a silent edit to an already-present one.

### One write in flight

A window elapsing while a previous flush is still running (a real `SaveAsync` call in flight, tracked
by `SaveScheduler<T>.IsFlushing`, distinct from merely `HasPendingWrite` — waiting out the window is
not yet a write) must not start a second, concurrent `SaveAsync` call over the same key: two writers
racing the same store is exactly the kind of corruption `AtomicFileStore` exists to survive a *kill*
during, not something this layer should manufacture on purpose during normal operation. `MarkDirty`
arriving mid-flush does not start a new window at all — it simply leaves `_pending`/`_hasPending` set,
which the flush already in flight is written to notice: `RunFlushLoopAsync`'s `while (_hasPending)`
loop re-checks that flag the instant the in-flight `SaveAsync` call returns, and if something newer
arrived while it was running, loops immediately for exactly one follow-up write carrying whatever is
now latest — never a queue of every value that arrived in between, and never a second wait for another
full window. `FlushAsync` and the coalescing loop both fold into the same `EnsureFlushingAsync`, so a
caller invoking `FlushAsync` while a flush is already running joins the same
`UniTaskCompletionSource` instead of racing it with one of its own.

A write that fails is not treated as if it had succeeded: `RunFlushLoopAsync` restores the value that
just failed back into `_pending` — unless something newer already arrived while it was in flight, which
wins over resurrecting the stale one — and schedules a fresh window to retry automatically, rather than
stranding the failure until an unrelated `MarkDirty` call happens to arrive later. A `SaveException`
from a genuinely broken store (a full disk, a revoked permission) surfaces to whoever is awaiting
`FlushAsync` at the time, and is also logged directly — see "A failed write now says so" below for why
the second half of that sentence had to be added — so the *data* that failed to save is not silently
dropped on the floor because the retry that would have picked it up was never given anything to notice,
and the failure itself is not silently dropped on the floor either, whether or not anyone happened to be
awaiting it. If the underlying cause does not go away — a disk that stays full stays full — this repeats
once per coalescing window rather than spinning: each retry is exactly one more `SaveAsync` call, gated
by the same throttle as any other write, never a tight loop hammering a store that is already failing.

### A failed write now says so

`MarkDirty` is the only caller most of this class ever has in this codebase — `CurrencyResourceBankSaveHandle.Save`
calls it and nothing ever awaits the result, because `Save()` cannot be `async` and still satisfy
`IResourceBankSaveHandler<T>` — so the organic path through `RunFlushLoopAsync`, the one a coalescing
window elapsing on its own takes, had nobody positioned to observe a failure at all. Before this was
fixed, an exception there reached `completion.TrySetException`, passed through
`WaitThenFlushAsync`'s `.SuppressCancellationThrow()` unchanged (that method only suppresses
`OperationCanceledException`, on purpose — see "`FlushBlocking`, and why it cannot deadlock" for the
same distinction applied to a different exception), and escaped the `.Forget()`-ed `UniTaskVoid` this
runs inside of to `UniTaskScheduler`'s own unobserved-exception handler. That handler does log it — so
this was never a fully silent failure — but with no key, no indication a *save* is what failed, and
nothing to connect it to `CurrencyResourceBankSaveHandle` at all: indistinguishable, in a device log,
from any other unrelated unobserved exception anywhere in the process. `AtomicFileStore` is what makes
this reachable in a way `PlayerPrefs` never practically was for this game — a full disk or a revoked
permission are real `IOException`s a file store reports, where `PlayerPrefs.SetString` essentially
never fails in practice — and the fix is one `Debug.LogError` inside `RunFlushLoopAsync`'s own catch,
naming the key and the exception's message, before `completion.TrySetException` runs. `FlushBlocking`
and `Dispose` already attributed their own failures at their own call sites before this; this closes
the one path that had not.

Logged unconditionally, not only when nothing else is watching: a caller that does use `FlushAsync` and
also logs its own catch now sees one duplicate line rather than this class trying to guess whether it is
the only one about to report the failure. A duplicate log line is the direction to err in over a
caller that logs nothing and neither did this class.

**Retried at the same fixed `coalesceWindowMilliseconds` rather than backing off — a deliberate choice,
not an oversight left for later.** A persistent failure (a disk that stays full) now retries, and now
logs, once per window, forever, at the same rate every other retry in this class already runs at.
Backing off would mean `MarkDirty`'s own contract — one write per `coalesceWindowMilliseconds` — started
meaning two different intervals depending on history a caller has no way to observe, the exact ambiguity
"Write coalescing, and why it cannot live inside `SaveAsync`" above already refuses to let a value's
own meaning silently carry. What actually mattered here — a persistent failure being loud rather than
silent — is what the logging fix above buys; a slower retry does not make a failure any louder, only
slower to notice once it is.

### `FlushBlocking`, and why it cannot deadlock

`OnApplicationPause(true)` on mobile is the last callback with any durability guarantee, and there is
no time in it for an async round trip. `FlushBlocking` exists for exactly that call site, and it is
genuinely synchronous end to end rather than a blocking wait dressed up to look like one: it calls
`SaveAsync` and inspects `task.Status` **once**, immediately, on the same call stack. If the task is
already finished — `Succeeded`, `Faulted` or `Canceled` — `GetAwaiter().GetResult()` reads out a
recorded outcome that is already sitting there; there is nothing to wait for, so nothing blocks. If the
task's status is still `Pending`, `FlushBlocking` never waits for it to stop being pending — it puts
the claimed state back into `_pending` and throws `SaveException.FlushWouldBlock` immediately. That
check is what makes the deadlock this method exists to avoid structurally unreachable rather than
merely unlikely: the one thing that could make `SaveAsync` still be pending at this exact point is a
composed store that needs to leave the calling thread to finish (a `ThreadHoppingStore`-wrapped one),
and the continuation that would resume it and mark the task finished needs the very thread
`FlushBlocking` would otherwise be blocking — the same reasoning that makes a `[Test]` blocking on a
task suspended at `UniTask.SwitchToThreadPool` hang the Unity Editor's own `EditorApplication.update`
pump forever, verified directly against `PlayerLoopHelper`'s edit-mode initialisation while this phase
was designed. `FlushBlocking` refuses to be that caller. Waiting is the only thing that can deadlock
here, and this method never waits.

This pushes a real, load-bearing obligation onto whatever composes a `SaveScheduler<T>` that will ever
have `FlushBlocking` called on it: build it over an `ISaveService` whose store never hops off the
calling thread — a raw `FileStore`, `AtomicFileStore`, `PlayerPrefsStore` or `InMemoryStore`, never one
wrapped in `ThreadHoppingStore`. A scheduler built over a hopping store is not unsafe to construct —
`MarkDirty` and `FlushAsync` both work over it exactly as designed — but `FlushBlocking` on it will
throw the instant a flush is genuinely in flight when it is called, every time, by design. That is a
real tension this phase surfaces rather than resolves for a future caller: the same scheduler cannot
both get the frame-cost relief `ThreadHoppingStore` buys during normal play *and* offer a `FlushBlocking`
that is guaranteed to succeed at pause time. Phase 6/7's integration, when it composes a real
`SaveScheduler<T>` for the resource bank, has to pick one of those two things for that scheduler
instance, and this document is where that choice needs to be made deliberately rather than discovered
by an `OnApplicationPause` handler throwing in production.

**That choice is checkable, not just documented.** `ISaveStore.CompletesOnCallingThread` is the store
declaring, about itself, whether any of its members ever leaves the thread that called them — every
store this assembly ships answers `true`; `ThreadHoppingStore` answers `_mainThreadOnly`, `true` only
when the store it wraps is itself `IMainThreadOnlyStore` and it therefore never actually hops. The same
question travels up: `ISaveService.CompletesOnCallingThread` is a pure pass-through to whatever store it
was composed with, and `SaveScheduler<T>.CanFlushBlocking` reads that from the `ISaveService` it owns.
None of the three inspects a concrete type to answer this — the same reasoning `IMainThreadOnlyStore`
already follows for the *different* question of whether a store may be moved off the calling thread at
all — so a future store this assembly does not know about yet still answers honestly by declaring the
one fact that matters, the same way `IMainThreadOnlyStore` already lets a future main-thread-bound store
get its own protection for free. `CanFlushBlocking` is knowable at composition time, before a single
`MarkDirty` call: this is what turns the obligation above from "remember to build it over the right
store" into something a composition root can assert once and a test can pin — a `SaveScheduler<T>` built
for `OnApplicationPause` should assert `CanFlushBlocking` the same way `ChestsMinigamePrefabTests` asserts
the real prefab composition rather than trusting a comment, so a wrong composition fails at build time
instead of on a player's device the first time the app is backgrounded.

### Disposal and a pending write

`SaveScheduler<T>.Dispose()` stops the loop cleanly — cancelling any coalescing window still counting
down and the token every in-flight or future `SaveAsync` call is linked against — and then tries once,
synchronously, to save whatever is currently pending and not already claimed by a flush in progress.
Over a non-hopping store (the common case, and the only configuration `FlushBlocking` above requires
anyway — `CanFlushBlocking` being true is exactly this condition), that attempt always succeeds,
because everything in that configuration completes synchronously — the same fact `FlushBlocking` relies
on. Over a hopping store, the attempt is not retried and not waited for: `Dispose` must never throw, so
the same `FlushWouldBlock` case that `FlushBlocking` surfaces as an exception is caught here instead,
for the reason `AtomicFileStore`'s own best-effort temp-file cleanup already gives — a caller of
`Dispose` is entitled to assume it never throws, and a write that cannot be completed here is exactly as
lost whether this method throws about it or not.

**Caught is not the same as quiet.** A comment explaining a loss is read by whoever next opens this
file, not by whoever is holding a crash report from a player's device. So `Dispose` calls
`Debug.LogError`, naming the key, in both places a write can be lost — the reason `CurrencyManager`
already logs a failed add or a failed spend rather than swallowing either in silence. A flush already in
flight when `Dispose` runs is not awaited either: `Dispose` is synchronous, and waiting for a
worker-thread hop to finish is the same blocking `FlushBlocking` refuses, so that write continues in the
background to whatever conclusion it reaches, unobserved. If nothing newer arrived behind it, its
outcome is merely unobserved, not necessarily lost. If something newer *did* arrive — a `MarkDirty` call
between the in-flight write starting and `Dispose` running — that newer state is genuinely lost, since
`ScheduleWindowIfNeeded` no-ops once `_disposed` is true and nothing will ever pick it back up; `Dispose`
logs that case by name too, distinctly from the synchronous-attempt-failed case above. Either way, this
is the one place in this phase where a loss is accepted rather than prevented, and it is now reported as
well as documented: call `FlushBlocking` yourself before disposing — or check `CanFlushBlocking` first —
if the guarantee matters more than the convenience of not having to.

`Dispose_WhileACoalescingWindowIsStillCountingDown_CancelsIt_OverANonHoppingComposition` pins that
the countdown really gets cancelled: over a non-hopping composition, `Dispose`'s own best-effort
flush is synchronous, so if the coalescing window had fired instead, `MarkDirty`'s own write would
have produced the same single write regardless of `Dispose`, making the two paths indistinguishable;
asserting exactly one write, attributable to `Dispose` and not to the window, is what pins
`InterruptWindow()` actually running. `Dispose_WithAPendingWriteThatNeverStartedFlushing_OverAHoppingComposition_LogsTheLossAndDoesNotThrow`
and `Dispose_WithAWriteMidHopAndANewerOneQueued_LogsNamingTheKey_AndDoesNotThrow` exercise the two
different ways a hopping composition can lose a write on `Dispose`: the first when the window is
still counting down and `Dispose`'s own synchronous attempt is what has to leave the calling thread,
the second when a flush is already running and a newer write is queued behind it. Both log the key
and neither throws. After the abandoned write lands, nothing about the scheduler reacts to it: no
new coalescing window, no retry, no further write —
`Dispose_WhileAFlushIsGenuinelyInFlight_StopsCleanly_AndNothingKeepsRunningAfterwards` pins that once
disposed, the scheduler stays inert even after the background write it could not wait for finally
completes.

### What was validated without a test

This phase does not add tests — a separate pass gates that. The coalescing state machine
(`MarkDirty`, the throttle window, one-write-in-flight, the failed-write retry, `FlushBlocking`'s
synchronous check, `Dispose`'s best-effort flush) was checked against a scratch harness that ports the
same algorithm over plain `Task`/`TaskCompletionSource`, driven by a hand-advanced fake clock — the
same shape `FakeGameClock` already gives the rest of this codebase, just outside Unity so it could run
without a player loop. `ThreadHoppingStore`'s actual use of `UniTask.RunOnThreadPool` /
`SwitchToMainThread` was instead checked by reading UniTask's own source for what each does with its
`cancellationToken` and `configureAwait` arguments, since a real cross-thread hop is not something a
plain-C# harness outside Unity can faithfully stand in for. Worth testing once a real test pass covers
this phase: the coalescing scenarios above under `PlayModeTest` (an `EditMode` test cannot drive
`ThreadHoppingStore` at all — `SynchronousUniTask` would fail loudly on the very first hop, exactly as
designed), `FlushBlocking` actually throwing when built over a `ThreadHoppingStore`-wrapped store
mid-write, and `CanFlushBlocking`/`CompletesOnCallingThread` answering correctly for every store this
assembly ships plus a `ThreadHoppingStore` wrapping each.

The assembly itself, and `Company.ChestGame.Tests.EditMode` with it, were compiled with `dotnet build`
against the real `UnityEngine`, `UnityEditor`, `UniTask` and `Newtonsoft.Json` assemblies through the
project's own generated `.csproj` files — the same check phases 1 through 4 relied on, not merely the
scratch harness above. That check is what a plain-`Task` port of the algorithm cannot do: it answers
"does this assembly build", not "is the algorithm right", and both matter.

## Currency: the first real caller

Phase 6b's scope is deliberately narrow: persist exactly what `DefaultResourceBankSaveHandle<T>`
already persisted — Coins and Gems, nothing else — through this assembly's pipeline instead of
straight into `PlayerPrefs`. The wider save model, chests progress, and the phase 8 demo panel are
phase 7. What follows is `Company.ChestGame.Currency`'s own adapter, living there rather than here
because it is the one place allowed to know both `CurrencyType` and `ISaveService` — nothing under
`Company.ChestGame.Saving` may know either.

### `IResourceBankSaveHandler<T>` is fully synchronous; `ISaveService` is not

`ResourceBank<T>` calls `Load()` once from its own constructor and `Save()` from inside both
`TryAddResourceAmount` and `TryToSpendResource` — both plain, non-`async` methods returning a value
or `void`, not a `UniTask`. Neither may block on work that genuinely needs to leave the calling
thread, for the same reason `SaveScheduler<T>.FlushBlocking` refuses to: blocking the one thread that
would have to service its own continuation is a deadlock, not a slow path. The two directions are
resolved differently, because the two problems are not the same shape.

**`Save()` never blocks.** `CurrencyResourceBankSaveHandle.Save` hands the state straight to a
`SaveScheduler<CurrencySaveDocument>` via `MarkDirty` and returns immediately — see "Write coalescing"
above for what that buys generally. What it costs specifically for currency: if the process dies
inside the coalescing window (up to `DefaultCoalesceWindowMilliseconds`, 1 second), whatever changed
since the last flush is lost. That window is bounded rather than open-ended for two reasons together,
not one: `MarkDirty` throttles rather than debounces, so a burst of adds during an active minigame
still flushes at most one second after the first of them, never later; and `GameLifetimeScope` force-
flushes the scheduler from both `OnApplicationPause(true)` and `OnApplicationQuit()` — see "What ships,
and where the composition asserts its own constraints" below — which is what closes the window at
exactly the two points mobile and desktop each guarantee the process is still willing to run code at
all. Between those two, the only genuinely open loss window is a hard kill (an OS out-of-memory kill,
a crash, a pulled battery) inside one second of a save that has not yet flushed — the same bound this
assembly's own docs already accept for `SaveScheduler<T>` in general, not a new one currency invented.

**`Load()` blocks, once, on the calling thread — the harder direction, because there is no honest way
to return a `ResourceBankState<T>` from a method with that exact signature without either already
having the value or waiting for it.** Two resolutions were on the table. The first: block only where
`ISaveService.CompletesOnCallingThread` guarantees the wait is not really a wait — the task is already
finished by the time `LoadAsync` returns, because nothing in the composition ever hops off the calling
thread, so `GetAwaiter().GetResult()` reads out a recorded outcome rather than waiting for one. The
second: pre-load during boot, before `CurrencyManager` is ever constructed, and have `Load()` hand back
whatever was already fetched. This phase picks the first. The second would need an async boot step
ahead of `GameLifetimeScope.RegisterCoreServices` — which is registration, not resolution, and runs
before anything is built — restructuring where in boot `ICurrencyManager` can first be resolved, for a
composition that (see below) is already forced to be non-hopping for `FlushBlocking`'s own sake. Paying
the same restriction twice to buy a second implementation of the thing the first already gives for
free was not worth the restructuring.

**What blocking costs: `CurrencyResourceBankSaveHandle`'s constructor refuses any `ISaveService` whose
`CompletesOnCallingThread` answers false — structurally, not by a comment.** A `ThreadHoppingStore`-
backed composition cannot satisfy this handler's contract at all: `Load()` would either have to block
the very thread that would need to run to finish the hop (a deadlock, exactly the one
`SaveScheduler<T>.FlushBlocking` already refuses to risk) or return before the real load finished (a
lie the vendored `ResourceBank<T>` has no way to detect, since its `Load()` contract has no concept of
"not yet"). So the constructor throws `SaveException.SynchronousLoadNeedsNonHoppingStore()` immediately
rather than shipping a composition that would only discover this the first time a player's load
actually raced the hop. **What this forbids, in full: nothing that ever backs `CurrencyResourceBankSaveHandle`
may be wrapped in `ThreadHoppingStore`, ever — not "should not," refused outright at the moment the
handler is constructed.** A future phase wanting the frame-cost relief `ThreadHoppingStore` buys for
currency specifically would have to move to the second resolution (pre-load at boot) instead, not add
the wrapper to this composition.

### The save-then-notify ordering no longer means what it used to

`ResourceBank<T>` — vendored, untouched — calls `Save()` and invokes its own callback in a different
order for each method: `TryAddResourceAmount` invokes `ResourceCollected`/`ResourceAmountChanged`
*before* calling `Save()`; `TryToSpendResource` calls `Save()` *before* invoking
`ResourceSpent`/`ResourceAmountChanged`. That call order is exactly what it always was — pinned now,
by `CurrencyResourceBankSaveHandleTests`, where it never was before — because nothing about this phase
touches the vendored library. What changed is what being on either side of that order *means*.

Before this phase, `Save()` was `DefaultResourceBankSaveHandle.Save`, synchronous `PlayerPrefs.SetString`
I/O that had already happened by the time the call returned. That made the two methods genuinely
asymmetric to an observer: a `ResourceSpent` handler could assume the new balance was already durable,
because `TryToSpendResource` only fires it after `Save()` returns; a `ResourceCollected` handler could
not, because `TryAddResourceAmount` fires it first. Now `Save()` is `CurrencyResourceBankSaveHandle.Save`,
which calls `SaveScheduler<CurrencySaveDocument>.MarkDirty` and returns immediately having persisted
nothing at all — see "`Save()` never blocks" above. Being called before or after a callback no longer
correlates with durability, because neither position was ever durable to begin with: `MarkDirty` only
guarantees a write will happen within the current coalescing window, or at the next pause/quit flush,
not that one already has.

**What an observer can still conclude from either callback, and what it never could:** the in-memory
balance `GetCurrencyAmount` reports is already the new one, in both methods, because `ResourceBank<T>`
mutates its dictionary before calling either `Save()` or the callback — that part was never in question
and is not what changed. **What it can no longer distinguish, and only appeared to be able to before:**
whether that balance has reached disk yet. It never actually could reach that conclusion safely even
under the old ordering — a crash between `TryToSpendResource`'s `Save()` and its callback was already a
narrow enough window nothing exercised it — but the appearance of a guarantee is itself worth retracting
in writing rather than leaving an observer to infer one from call order that no longer supports it. This
is a deliberate, intentional consequence of write coalescing existing at all: restoring the old ordering
would mean making `Save()` synchronous again, which is the entire property this phase exists to remove.
Nothing about `CurrencyManager`'s own public events changed - what changed is what a subscriber is
entitled to assume from them, and this paragraph is that retraction made explicit rather than left to be
discovered by whoever eventually needs the guarantee that no longer holds.

### `CurrencySaveDocument`, and a `new()` constraint the vendored model cannot satisfy

`ISaveService.LoadAsync<T>` (and `SaveService`'s own legacy-import path) both require
`T : class, new()`. `ResourceBankState<T>`'s only constructor is
`ResourceBankState(Dictionary<T, long> resourceAmount = null)` — a constructor with a default
argument, which is a genuine `CS0310` the moment it is asked to stand in for that `T`: a constructor
with an optional parameter is not a parameterless constructor as far as the `new()` constraint is
concerned, confirmed against a real compile rather than assumed while this phase was built.
`ResourceBank<T>` is vendored and not to be touched, so `ResourceBankState<CurrencyType>` can never be
the `T` this assembly saves and loads directly.

`Company.ChestGame.Currency.CurrencySaveDocument` exists instead — the same shape,
`Dictionary<CurrencyType, long> ResourceAmount`, but a type this adapter owns, with a real
parameterless constructor. `Save()` only needs `class`, not `new()`, so it could have kept using
`ResourceBankState<CurrencyType>` directly and left `CurrencySaveDocument` to cover only the `Load()`
side; using it on both sides instead is deliberate, so the JSON this assembly actually persists is
owned by this adapter's own type rather than one direction of it silently tracking whatever shape a
future update to the vendored library's own `ResourceBankState<T>` happens to serialize as.
`CurrencySaveDocument.From` copies the dictionary rather than aliasing it, for the reason given on the
type itself: `ResourceBank<T>` keeps mutating the same dictionary instance for its whole lifetime, and
the state handed to `MarkDirty` is documented elsewhere in this file to be something nothing else holds
a reference to once it is captured — copying is what makes that true here rather than merely assumed.

### The legacy import: `CurrencyLegacyImport`

The concrete `ILegacyImport` "The legacy import" above described and deferred. It reads exactly what
`DefaultResourceBankSaveHandle<CurrencyType>` has always written — a bare `{"ResourceAmount":{...}}`
under `"ResourceBankSaveData_CurrencyType"` in `PlayerPrefs`, no envelope, no version — and because
that shape is already exactly `CurrencySaveDocument`'s own shape, `Import()` does no reshaping at all
beyond `JObject.Parse`: parsing *is* the reshape this key's data needed. The ordering `SaveService`
already enforces is untouched and unweakened here: `LoadAsync` only ever reaches
`CurrencyLegacyImport.Import()` when its own store has nothing under `"currency"`, `SaveAsync` writes
and durably persists the imported document through the real `AtomicFileStore` before anything runs
`Clear()`. Idempotent structurally, per "The legacy import" above, not by a flag this adapter carries.

**Absence is not corruption, and `IsPresent()` is where that distinction actually lives.**
`DefaultResourceBankSaveHandle.Save(null)` — never something `ResourceBank<T>` itself does, but not
something `PlayerPrefs` stops anyone from having written by hand — serializes to the four-byte JSON
literal `"null"`; an empty string is the same absence `PlayerPrefs` cannot otherwise tell apart from
"never written". The old path already treated both as nothing to load:
`JsonConvert.DeserializeObject<ResourceBankState<T>>("null")` returns a C# `null`, and
`ResourceBank.Load`'s own `?? new ResourceBankState<T>()` turned that into booting at zero, silently.
The naive translation of `Import()` into this assembly's contract does not preserve that: `JObject.Parse`
on either value throws, `SaveService.LoadAsync` turns that into `SaveException.PayloadUnreadable`, and
a bootable game becomes an unbootable one on every future launch — a genuine regression this phase
introduced and did not initially catch, because "genuinely corrupt legacy data should stay loud" (true,
and unchanged) is not the same claim as "absent legacy data should stay loud" (false, and the mistake).
`IsPresent()` is where the two get told apart, checked before `Import()` is ever called: it answers
`false` — "nothing to import," the same as the key never existing at all — for an empty or
whitespace-only string and for the literal `"null"`, and answers `true` for everything else, including
JSON that is present but genuinely malformed. A stray unmatched brace still reaches `Import()`, still
throws, and still surfaces as `PayloadUnreadable` — that half of the old design was correct and stays
exactly as loud as it always was.

**`Clear()` renames rather than deletes.** The import is gated on "the store has nothing under
`\"currency\"`" — idempotent against an immediate re-run, because a successful `SaveAsync` makes the
branch unreachable, but not against a *later* one: if the real save and its `.bak` both ever
disappeared afterward (a manual delete, a future bug), `LoadAsync` would fall back to this import
again, and a plain `PlayerPrefs.DeleteKey` would be indistinguishable at that point from "nothing was
ever imported" — reimporting genuinely stale legacy numbers over whatever the player's real balance had
become since, silently. Moving the value to `<key>.migrated` instead of erasing it keeps `IsPresent()`
answering `false` from that point on exactly the way a delete would — re-import still cannot loop — while
keeping the bytes themselves recoverable rather than gone. That recoverability pays for two separate
things at once: a rollback to a build that only knows `DefaultResourceBankSaveHandle` after a migration
has already run finds a marker that explains the zero balance instead of no trace of what happened at
all, and a test run that reaches this method by mistake — see "Sealing the boot path a test cannot pass
arguments through" below for exactly this failure, which happened during this phase's own review —
displaces a developer's real data instead of destroying it. The marker is written before the original
key is deleted, the same write-before-clear ordering `SaveService` itself already enforces one level up
(the new save durable before `Clear()` runs at all): the worst state a failure between those two
`PlayerPrefs` calls can leave behind is both the marker and the original present together, never
neither. The `PlayerPrefs.Save()` that ends `Clear()` is tidiness rather than correctness: by the time
it runs the new save is already durable, so `LoadAsync` never consults `IsPresent()` for this key
again, and it makes no difference whether the rename survives an unclean quit.

A failure inside `Clear()` itself is still caught by `SaveService.ImportLegacyOrFreshAsync` rather than
allowed to fail an otherwise-successful load — that part of the design is unchanged and correct, since
the new save has already succeeded by the time `Clear()` runs. What was missing is that the catch was
empty: a failed `Clear()` was invisible, which is exactly the condition finding 5 above describes as
compounding into the stale-reimport scenario this section already avoids structurally. The catch now
logs the exception, named by key, rather than swallowing it silently — the same reasoning
`SaveScheduler<T>.Dispose()` already follows for its own best-effort flush: a caught failure is not the
same thing as a quiet one, and a comment explaining why a failure is tolerated is read by whoever opens
this file, not by whoever is holding a device log wondering why a legacy entry never went away.

### What ships, and where the composition asserts its own constraints

`GameLifetimeScope.RegisterCoreServices` composes, by hand, from `SaveComponentFactory` rather than
`SaveServiceFactory` — this composition needs an `ILegacyImport`, and `SaveServiceFactory` is
documented above to never grow a parameter for one: **`SaveStorage.AtomicFile`, `SaveCodec.Json`,
`SaveProtection.None`.** Unprotected and file-backed for the reason already given for
`SaveFactoryInputs.Defaults()`'s own keys: this is a showcase with no key-management story to
demonstrate, and `SaveProtection.None` keeps a developer's own save readable and hand-editable rather
than hiding it behind a key that ships in the binary either way. `AtomicFile` over a plain `File` is
the one place this composition spends more than the minimum: `SaveScheduler<T>` means a coalesced
write can now land at any point in the app's lifecycle — mid-minigame, on a background thread's worth
of wall-clock time later, at a pause/quit flush — rather than only inside one synchronous `Save()`
call the way `DefaultResourceBankSaveHandle` always did, so the torn-write protection `AtomicFileStore`
buys over `FileStore` (see `AtomicFileStore` above) is worth its small extra cost precisely because
write timing is no longer fully in this adapter's own hands. `SaveCodec.Json`, not `JsonPretty`: this
document already argues indentation is pure size once nothing is meant to read it in an editor, and an
actual player's save is exactly that case, unlike the phase 8 demo panel's.

Two constraints this composition depends on are asserted at the moment it is wired, in
`GameLifetimeScope.RegisterCoreServices` and `CurrencyResourceBankSaveHandle`'s own constructor, both
reachable by `GameLifetimeScopeTests` building a container from `RegisterCoreServices` exactly as it
already does for every other registration — never left to be discovered on a device the first time a
pause or a load actually depended on either:

- `CurrencyResourceBankSaveHandle`'s constructor throws `SaveException.SynchronousLoadNeedsNonHoppingStore()`
  if the `ISaveService` it was given does not answer `CompletesOnCallingThread == true` — see "`Load()`
  blocks" above for why `Load()` cannot be correct without this.
- The factory registering `SaveScheduler<CurrencySaveDocument>` throws
  `SaveException.SchedulerCannotFlushBlocking(key)` if the scheduler it just built does not answer
  `CanFlushBlocking == true` — because `GameLifetimeScope` calls `FlushBlocking` on it from both
  `OnApplicationPause(true)` and `OnApplicationQuit()`, and a scheduler that could ever answer false
  there would throw on every real pause and quit instead of saving.

`GameLifetimeScope.RegisterCoreServices` hardcodes `SaveStorage.AtomicFile` for the currency store, so
nothing reachable through its own public parameters (`currencySaveInputs`,
`legacyCurrencyPlayerPrefsKey`) can make the `SaveFlushRegistry.Register` call inside it actually throw
`SchedulerCannotFlushBlocking`: the composition can only ever build a non-hopping scheduler, so that
throw site is unreachable from a test without either a production change or a fake standing in for the
real registration. `CurrencyResourceBankSaveHandleTests.SchedulerCannotFlushBlockingException_NamesTheKey_AndMentionsFlushBlocking`
works around that by asserting `SaveException.SchedulerCannotFlushBlocking`'s message contract
directly rather than through the registration path, leaving that path itself unexercised by a test.

Both checks currently prove the same underlying fact twice, once from each consumer's own point of
view, because this composition's scheduler and handler share one `ISaveService` instance — that
redundancy is deliberate rather than an oversight: each consumer of a synchronous guarantee asserts its
own dependency on it locally, the same shape 6a's `SaveComponentFactory.CreateStore`/`CreateProtector`
guard already follows for `NoFactoryInputs`, rather than trusting a guard that happens to sit elsewhere
to still be true if a later change ever gives the two consumers different `ISaveService` instances.

`RegisterCoreServices` takes two further optional parameters for exactly this composition —
`SaveFactoryInputs currencySaveInputs` and `string legacyCurrencyPlayerPrefsKey`, both defaulting to
`null` and, from there, to production's own values (`SaveFactoryInputs.Defaults()` and
`CurrencyLegacyImport.DefaultLegacyKey`) — the same shape `status` already established on this method
for exactly this reason: a production call site changes nothing, and a test asserting against the real
`RegisterCoreServices` gets a seam to redirect through instead of a copy to maintain. See "Redirecting
this composition away from a developer's real save" below for why this exists and what it fixes.

### The pause/quit flush lives on `GameLifetimeScope`

`GameLifetimeScope` calls `FlushAll()` on an `ISaveFlushRegistry` from both `OnApplicationPause(true)`
and `OnApplicationQuit()`, catching and logging rather than letting either Unity callback throw — the
same best-effort reasoning `SaveScheduler<T>.Dispose()` already follows for its own flush attempt,
because "the composition is structurally correct" is not the same guarantee as "the disk write it
triggers cannot fail" (a full disk, a revoked permission). This lives on `GameLifetimeScope` itself
rather than a new dedicated component because it already is the `MonoBehaviour` that survives every
scene load and already owns the one `Awake()` that builds the container everything else here is
resolved from; a separate component would need its own `DontDestroyOnLoad` and its own path to the
same container for no behaviour a second object would add.

`_saveFlushRegistry` is held in a field rather than resolved on demand, because the callbacks that
use it (`OnApplicationPause`, `OnApplicationQuit`) can fire at any time, including while the container
is unavailable. `Awake` calls `base.Awake()` first, deliberately: that is where VContainer builds the
container and dispatches the bootstrapper, so anything the override does afterwards, including
resolving `ISaveFlushRegistry` and the `DontDestroyOnLoad` call, depends on that having already run.
`DontDestroyOnLoad(gameObject)` is necessary because the game scene's own scope descends from this
one, so this scope has to survive the scene load or the game scene's injection has nothing to parent
to. `OnApplicationQuit` exists as its own callback because `OnApplicationPause(true)` does not fire
on most desktop platforms on quit, so `OnApplicationQuit` is the equivalent close for the same window
there.

#### Why the flush stopped being one field

This was originally a single resolved `SaveScheduler<CurrencySaveDocument>` field, flushed by name.
That stops working the moment a second scheduler exists, and phase 7 adds one this assembly cannot
even name: `SaveScheduler<ChestsRunSaveDocument>` lives in `Company.ChestGame.Minigame.Chests`, an
assembly `Company.ChestGame.Core` deliberately does not reference — the same property that lets the
shell start a minigame without knowing its concrete type. No per-type field can be written for a type
this assembly cannot name.

`ISaveFlushable` is the members `SaveScheduler<T>` already had — `CanFlushBlocking` and
`FlushBlocking` — plus `SaveKey`, pulled into a seam, so `SaveScheduler<T> : IDisposable,
ISaveFlushable` needed no new behaviour. `SaveKey` is on the seam rather than left private for one
reason: a registry holding several schedulers has to be able to say *which* one failed. A flush error
on a device that cannot name the save it lost is most of the way to no error at all, and phase 7 is
precisely when the count goes from one to three.

`SaveFlushRegistry.Register` throws `SaveException.SchedulerCannotFlushBlocking(flushable.SaveKey)`
when `CanFlushBlocking` is false. Registering is a declaration that this save wants flushing at
pause/quit, and one that cannot guarantee the flush ever succeeds is a wiring error — the same rule
`GameLifetimeScope`'s currency factory used to assert on its own. That duplicate check is gone: one
rule, enforced once, for every flushable rather than re-derived per composition.

`OnApplicationPause_True_WhenAFlushableThrows_LogsRatherThanPropagating` disposes its scheduler only
after registering it, not before: `Register` asserts `CanFlushBlocking` at the point it runs, and
disposal does not change what `CanFlushBlocking` reports, so a scheduler registered while healthy and
disposed afterward reaches the flush call already broken, and the exception `FlushBlocking` then
throws is `SchedulerDisposed` — a real exception from the real type, not a fake standing in for one.

`FlushAll()` is fault-isolating over a snapshot. One flushable throwing is caught, logged naming its
key, and the loop continues, because this runs from a lifecycle callback where nothing may propagate
and where one bad scheduler must never cost every other one its flush. Snapshotted with `ToArray()`
first so a flush that unregisters something — itself included — cannot invalidate the enumeration it
is running inside. Like `SaveScheduler<T>`, the registry is main-thread-only and takes no locks.

#### An unregistered save looks exactly like a registered one

Registering is a side effect of each scheduler's factory registration, which means a scheduler nothing
ever resolves is a scheduler nothing ever registered — and a save that is never flushed at pause/quit
is indistinguishable, in every other respect, from one that is. It writes on its window like normal,
it survives a clean quit on desktop, and it silently loses up to a window's worth of progress every
time a mobile OS kills a backgrounded app. No assertion anywhere else would notice.

Two things close that. `RegisterCoreServices` carries one `builder.RegisterBuildCallback(resolver =>
resolver.Resolve<SaveScheduler<…>>())` line per scheduler it owns, so registration is a property of
the container rather than of whoever happens to resolve first — and specifically not a line in
`Awake()` that someone has to remember to add alongside the next save. And `ISaveFlushRegistry`
exposes `Registered`, so `GameLifetimeScopeTests` can assert the shipped composition flushes exactly
the set of keys it owns. Adding a save and forgetting to flush it now fails a test instead of shipping.

A scheduler owned by an assembly the composition root cannot reference — the chests one — registers
itself when its owner is injected and unregisters on `Dispose`, so it participates for exactly as long
as it exists.

### Redirecting this composition away from a developer's real save

A save composition is the one seam on this method where "no override, matching every other seam
`RegisterCoreServices` wires" is not an acceptable default, and it is worth saying plainly why this one
is different rather than leaving it to look like an inconsistency. `AddressablesAssetProvider` not
being swappable through this method's signature costs a test nothing — it never touches a resource
that outlives the test process. This composition is not that: without an override, every test that
resolves `ICurrencyManager` writes a real file under the real `Application.persistentDataPath`, and — if
that machine has a legacy `"ResourceBankSaveData_CurrencyType"` PlayerPrefs entry, which any developer
who ever played the game before this phase does — actually performs the real legacy import: it imports
that data and then deletes it, for good, from production code, the first time a test happens to resolve
`ICurrencyManager`. `InMemoryResourceBankSaveHandler`'s own comment already states the rule this would
otherwise break: a test "neither read[s] nor clobber[s] the real editor save." Losing data a developer
cannot get back is worse than the debris a leaked key merely leaves behind, and the fix costs nothing
in production: `RegisterCoreServices(builder, status, currencySaveInputs, legacyCurrencyPlayerPrefsKey)`
takes both as optional parameters, precisely the shape `status` already established on this same method
so a test keeps asserting against the real composition root rather than a copy of it. Production's own
call site — `Configure` — passes neither, so nothing about the shipping game changes.

`SaveFactoryInputs.RootDirectory` redirects the file half on its own — pointing `currencySaveInputs` at
a temp directory keeps `AtomicFileStore` off the real save — but a temp file root does nothing about
`CurrencyLegacyImport`, which reads and clears a PlayerPrefs key with no concept of a root directory at
all. So `legacyCurrencyPlayerPrefsKey` is its own parameter rather than folded into
`SaveFactoryInputs`: that type is `Company.ChestGame.Saving`'s own carrier for what `SaveComponentFactory`
needs to build a store or a protector, and the legacy key is neither — it is `CurrencyLegacyImport`'s
own concern, one `SaveComponentFactory` never touches, so giving `SaveFactoryInputs` a field for it would
mean a `Company.ChestGame.Saving` type carrying a piece of state only `Company.ChestGame.Currency` ever
reads, for a composition that does not even use `SaveFactoryInputs.PlayerPrefsKeyPrefix` today (this
composition stores through `AtomicFile`, not `PlayerPrefs`). Passed straight through to
`CurrencyLegacyImport`'s own constructor, which falls back to `DefaultLegacyKey` — the real
`DefaultResourceBankSaveHandle<CurrencyType>` key — exactly when it is left null, the same
`null`-means-"use the real one" shape `currencySaveInputs` already follows.

**This closes exactly one of the two paths a test can reach this composition through, and saying so
here is what the next section exists to correct.** A test that builds its own `ContainerBuilder` and
calls `RegisterCoreServices` directly — every `GameLifetimeScopeTests` case — can now pass both
parameters and never touch anything real. A test that boots the actual game cannot pass anything
through this method at all, because it never calls this method directly; see "Sealing the boot path a
test cannot pass arguments through" below for the path this section does not cover, and for the real
data that path cost during this phase's own review before it was closed.

### Sealing the boot path a test cannot pass arguments through

`Configure(IContainerBuilder builder)` is Unity's own callback — `LifetimeScope.Awake()` calls it,
with no way for anything to thread an argument through — and its body is
`RegisterCoreServices(builder, _bootStatus != null ? _bootStatus : null)`, passing neither
`currencySaveInputs` nor `legacyCurrencyPlayerPrefsKey`. `GameBootstrapperTests` is a `PlayMode` test
that loads the real Boot scene and lets it run exactly as a player's device would: `Awake()` →
`Configure()` → this exact zero-argument call → `ICurrencyManager` resolved for the game scene's
`CurrencyWatcher` → `ResourceBank<T>`'s constructor → `CurrencyResourceBankSaveHandle.Load()` → the
real `CurrencyLegacyImport` against the real `"ResourceBankSaveData_CurrencyType"` PlayerPrefs entry.
Nothing about the previous section's two optional parameters touches any step of that chain, because
none of it is reachable from outside `Configure()`'s own fixed signature.

**This is not a hypothetical the previous section's fix left theoretically open.** During this phase's
own gate run, this exact path deleted the reviewer's real legacy PlayerPrefs entry (670 Coins, 180
Gems) and wrote a real `currency.sav` under that machine's real `Application.persistentDataPath` —
recovered only because a manual backup happened to exist and the gate independently restored and
verified it. The reason three people missed this in review is worth stating plainly rather than
smoothing over: before this phase, this exact boot-time `Load()` call was harmless, because
`DefaultResourceBankSaveHandle.Load()` only ever *read* `PlayerPrefs` — there was nothing under
`Configure()`'s own call for a test to be careful about, because nothing reachable from it could lose
anything. This phase turned that read into a one-time, irreversible migration without revisiting every
caller of the method that now triggers it, and `Configure()`'s own zero-argument path was the one
nobody re-examined.

The fix could not take the previous section's shape — there is no seam in `Configure()`'s signature to
add a parameter to — so it lives in what `RegisterCoreServices`' own defaulting resolves a left-`null`
argument to, in `BuildCurrencySaveService`'s `DefaultCurrencySaveInputs()` and
`DefaultLegacyCurrencyPlayerPrefsKey()`. `UNITY_INCLUDE_TESTS` is Unity's own answer to "is this
compilation one a test might be running in": a scripting define the Editor sets for the *entire*
compilation whenever test assemblies are part of it — every assembly, production code included, not
only the ones whose own `asmdef` opts in via `defineConstraints` — specifically for the `EditMode`
domain and for the player Unity builds to actually run `PlayMode` tests, and specifically not for a
normal Editor Play session or a shipped build. Under it, `DefaultCurrencySaveInputs()` points
`SaveFactoryInputs.RootDirectory` at `Application.temporaryCachePath` rather than
`Application.persistentDataPath`, and `DefaultLegacyCurrencyPlayerPrefsKey()` returns
`"Tests." + CurrencyLegacyImport.DefaultLegacyKey` rather than the real key — under which
`CurrencyLegacyImport.IsPresent()` finds nothing, so the real legacy entry is never even read, let
alone cleared.

This lives in `RegisterCoreServices`' own defaulting rather than only inside `Configure()`'s call
site, deliberately: it means a hand-built container that forgets to pass either optional parameter —
any test written after this section, not only `GameBootstrapperTests` — is *also* protected, rather
than relying on every future call site remembering to ask for it. `GameLifetimeScopeTests`'s own
calls, whether or not they pass the explicit overrides the previous section added, now redirect either
way.

**What this does not cover, stated plainly rather than left to be discovered.** `UNITY_INCLUDE_TESTS`
answers "was this compiled for a run that might include tests," not "is a test executing right now" —
a `Development Build` with its own "Include Test Assemblies" option checked would also define it,
which would point that real player's save at `Application.temporaryCachePath` too. This project's own
tests never build a player that way — `docs/testing.md` runs both suites through the Editor, in batch
mode — so this is a real gap in principle and not one this project's own pipeline currently exercises;
it is the correct trade-off regardless, because the failure direction is a QA build's save landing
somewhere temporary rather than a real player's save being read from or written to at all under a
condition meant for a different device to have exercised this project's CI. Repeated test runs also
share whatever the temp directory and the `"Tests."`-prefixed `PlayerPrefs` key still hold from a
previous run — neither location is scrubbed by anything this phase adds — which can make a test that
assumes a pristine first run see leftover state from an earlier one. That is a test-isolation
imperfection, not a data-loss risk, and is the trade this phase makes on purpose: recoverable debris
under a clearly test-owned name, never the real thing.

`GameLifetimeScopePauseQuitFlushTests` covers `OnApplicationPause`/`OnApplicationQuit` by keeping its
`GameLifetimeScope` out of this exact path rather than by overriding anything: the `GameObject` it adds
the component to is left inactive for its whole life, and Unity never calls `Awake` on a component
whose `GameObject` has not yet been active, so the container is never built and `Configure()` never
runs. `GuardSetup_NeverRanAwake` pins that directly — a null `Container` is exactly what "`Configure()`
never ran" looks like from outside, so that assertion failing means `Awake()` ran and the rest of the
fixture's isolation claim no longer holds.

With `Awake()` never running, the private `_saveFlushRegistry` field — the only state either callback
touches — is set directly through reflection to a real `SaveFlushRegistry` holding a scheduler built
over an isolated in-memory `FakeSaveStore`, and the private `OnApplicationPause`/`OnApplicationQuit`
methods are likewise invoked through reflection, since nothing in an EditMode test actually pauses or
quits the application to call them.

## Beyond currency: chests and meta

Phase 7 adds two more keys — `"chests"` and `"meta"` — over the one `ISaveService` currency already
registered. Nothing in `ISaveCodec`, `IPayloadProtector`, `ISaveStore`, `SaveService` or
`SaveScheduler<T>` changed to accommodate them, which was the point: adding a save was always meant to
cost a document and a caller.

### `ChestsRunSaveDocument`, and decision #9 made structural

The document carries exactly two members, `ChestCount` and `OpenedChestIndices`, and "exactly" is
load-bearing. There is no `Attempts` field because there is nothing for one to disagree with:
`OpenChest` increments `Attempts` once per chest that finishes opening, so it is always
`OpenedChestIndices.Count`, and two fields that can drift apart are a bug waiting to happen.
`ChestCount` exists only to detect a config change between sessions.

More importantly, **no member here is capable of naming the prize chest, a seed, or a per-chest
state** — and that absence is the enforcement of decision #9, not a restatement of it. `CheckEndGame`
ends the run in the same call that finds the prize, so a mid-run save is only ever taken while every
opened chest is `Open_Empty`, and `BuildCurrentRunDocument` filters on that state explicitly rather
than relying on the timing.

Two tests hold it there. An allow-list test reflects over the serialized member set and fails on a
third member, naming decision #9 in the message — the move `ChestsMinigamePrefabTests` already makes
for the authored pool strategy. And the stronger one: two controllers open the same chests in the
same order, seeded so a *different* chest would win on the next, undrawn attempt in each, and their
persisted documents come out byte-identical through the real `JsonCodec`. **The payload does not move
when the prize does.** Add a seed or a prize index and that test fails immediately, which is the
difference between a decision that is documented and one that is enforced.

### A scheduler the composition root cannot name has to register itself

`Company.ChestGame.Core` does not reference `Company.ChestGame.Minigame.Chests`, so `GameLifetimeScope`
cannot resolve, name, or flush `SaveScheduler<ChestsRunSaveDocument>`. This is the second real caller
for `ISaveFlushRegistry` and the reason it exists: `ChestsMinigameController.Inject` builds its own
scheduler over the injected `ISaveService` and `IGameClock` and registers it with the same singleton
registry `GameLifetimeScope` flushes, then `Dispose` unregisters before disposing. A minigame
participates in the pause/quit flush for exactly as long as it is running.

Three ordering rules inside `Inject`, each closing a real failure rather than a hypothetical one:

- **The guard runs first.** `SaveException.SynchronousLoadNeedsNonHoppingStore()` is thrown before
  anything is built, because the restore load blocks the calling thread exactly the way
  `CurrencyResourceBankSaveHandle.Load()` does. The existing exception already describes "a caller
  that blocks on `LoadAsync`'s result" generically, so this reuses it rather than adding a twin.
- **`Register` runs last.** `MinigameContainer.BeginAsync` destroys the view and releases content
  when injection fails, but it does not `Dispose()` the controller — so a scheduler registered before
  a throw would stay in the singleton registry for the life of the process, flushed at every
  pause/quit, holding a dead controller's state, and accumulating one more per failed start.
- **A corrupt run is discarded, not fatal.** A `SaveException` from the load is logged and answered
  with no pending restore. This is the same call-site policy `GameBootstrapper` applies to meta and it
  rests on the same test: a chests run holds nothing a player earned, because the win pays out through
  currency's own save. Letting it escape would refuse to open the minigame *every time*, with nothing
  in the game that ever clears the file. `NewGame()`'s discard branch then overwrites the unreadable
  document, so the next launch reads cleanly — the save repairs itself. `SaveMigrationException` is
  deliberately not caught: that is a wiring mistake, not a delivery failure.

`Dispose` uses null-conditionals on both new fields. It was idempotent before this phase — every
field it touched was null-safe — and `IDisposable` requires it to stay that way; a second call must
not start throwing because phase 7 added state.

### Restore, discard, and why it lives in `NewGame()`

The load happens once in `Inject`, into a private field. **Restore happens on the first `NewGame()`
call only** — the field is consumed and nulled — so every later call, which is a real restart, takes
the same path a fresh install would.

That placement is what makes the view free. `MinigameContainer.BeginAsync` injects, instantiates the
view, and calls `SetController`, and only then does `GameManager` call `NewGame()` — by which point
the view is subscribed to `OnStateChange`/`OnAttemptsChanged`, and `ChestsMinigameChestElementView.Init`
re-drives itself from `ChestsMinigameChestModel.CurrentState` the moment the pool binds it. Restoring
through the existing `Attempts` setter and `SetOpen` therefore needed **no view code at all**: the view
was already built to re-derive its display from whatever the model holds, for the ordinary case of a
chest opening live. Restore runs after every chest is `SetClosed()`, because `SetOpen` returns early
on an already-open chest.

A saved run is discarded rather than restored under three conditions, each a state a save can
legitimately be in: `ChestCount` differs from the configured count (a server-side config change, which
is the only thing that field exists to catch); an index is out of range or duplicated (a hand-edited
or truncated save); or the opened count already reaches `TotalAttempts` (a run that had ended).
Discarding also clears what is stored, so it is not re-read next launch. **A restart discards
unconditionally too**, even when the saved run was perfectly valid: a player who restarts mid-run must
not find that run resumable later.

The odds survive an interruption exactly. `TryGiveChestPrize` computes
`1 / (Chests.Count - Attempts + 1)` from nothing but the chest count and the attempt number, so a run
restored at `k` opened chests draws on the same odds an uninterrupted run at `k` would. That is a
property of decision #9 rather than a coincidence — a memoryless prize has nothing to restore.

**What this deliberately does not fix:** the attempt budget is re-rollable. Progress is persisted
through `SaveScheduler<T>`, so force-quitting inside the coalescing window refunds the attempt that
was just spent. Save-scumming the *prize* gains nothing — the odds are `1 / (N - k + 1)` and
re-rolling a lower `k` is strictly worse for the player — but the attempts are genuinely exploitable
that way. Closing it means consuming the attempt at click time and flushing before the reveal, which
trades a frame hitch on every chest for an exploit nobody is currently paying for. Written down rather
than designed around.

### `GameMetaSaveDocument`, and a claim that did not survive checking

`Launches`, `FirstLaunchUnixMs`, `LastPlayedUnixMs`, all `long` Unix milliseconds. The reasoning for
that shape needs a correction rather than a restatement.

The assumption going in was that `JsonCodec`'s default `JsonConvert` settings would reinterpret a
date-shaped string the same way "Value-exactness, and where the formatting stops" describes above.
**Checked against the real `Newtonsoft.Json.dll` this project ships, over `JsonCodec`'s exact code
path into a plain `string` property: it does not.** A nine-fractional-digit ISO timestamp, a bare
calendar date, and a UTC-offset timestamp all round-tripped character for character. `DateParseHandling`
applies when materializing into a *generic* member — `SaveEnvelope.Body` is a `JToken`, and the reader
decides whether a string token is a date before anything downstream can say what CLR type it was
headed for. A concretely `string`-typed property is read through a type-directed path that never
consults the setting. That was `SaveEnvelope`'s bug specifically, and it does not generalise.

`long` is still right here, on narrower grounds: it sidesteps timezone and format ambiguity outright
rather than resting on a codec behaviour nobody would re-check the next time the Newtonsoft version
underneath it moves. `IGameClock` has no wall clock, so `GameBootstrapper` reads
`DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()` — the one wall-clock read in this phase.

### A corrupt meta save is recoverable; a corrupt currency save is not

`GameBootstrapper.StartAsync` records the launch first, inside its existing try, so a content failure
afterwards still leaves an accurate count and timestamp behind and a genuine bug still reports to the
boot label. Only the `LoadAsync` call is guarded: a `SaveException` is logged and answered with a
fresh document, and boot continues as though this were a first launch's meta.

This is a call-site policy, not a weakening of `ISaveService`'s contract — `LoadAsync` still throws as
loudly as ever for every other caller. It rests entirely on what meta *is*: nothing in it is something
a player earned or would notice missing. The identical choice for currency — silently resetting a
corrupt balance to zero — would be indefensible, and the contrast is the point. Anything past the load
is deliberately unguarded: a failure in `MarkDirty` is a bug in this phase's own code, not a corrupt
save.

`StartAsync_WhenTheMetaSaveIsCorrupt_LogsAndStillPropagatesTheContentFailure` seeds bytes
`SaveService`'s own pipeline could never have written (the literal string `"not json"`) rather than a
hand-crafted truncated envelope, because any bytes `JObject.Parse` cannot read surface as the same
`SaveException.PayloadUnreadable` a genuinely truncated save would report — the test does not need to
reproduce truncation to exercise the recovery path.

### One `ISaveService`, three keys — and a naming debt

`BuildCurrencySaveService`, `CurrencySaveInputsOverride` and `LegacyCurrencyPlayerPrefsKeyOverride`
keep their currency-flavoured names, but the service they build is what all three keys — `currency`,
`chests` and `meta` — read and write through. That is safe for the reason phase 7a exists:
`ILegacyImport.TargetKey` is checked before `IsPresent()` is asked anything, so `CurrencyLegacyImport`
is never consulted for a `"chests"` or `"meta"` load — both simply read as a first run when nothing is
stored.

## The save inspector

`Company.ChestGame.Saving.Demo` is a leaf assembly referencing only `Company.ChestGame.Saving`,
`Company.ChestGame.Common` and UniTask. Nothing in the game references it and it references nothing in
the game — the arrangement `Company.ChestGame.Pooling.Demo` already settled on, for the documented
reason that a demonstration the game depends on makes "what does this feature actually need" stop
having an honest answer. It follows that the demo owns its own `SaveInspectorDocument` rather than
borrowing `CurrencySaveDocument`.

It is also placed the way the pooling demo is: an instance of
`Assets/_Project/UI/SaveInspector/SaveInspector.prefab` sits at the root of `Game.unity`, so it ships
in the player and a floating **Saving Demo** button opens it from the running game. Nothing in code
refers to it; the scene does.

`SaveInspectorDocument`'s `Nickname` and `Level` fields exist only so a rendered save has more than
one field worth looking at in the inspector UI; `Balance` is the one field that carries meaning, as
the value `SaveTamper` rewrites.

### SaveInspectorPanel, binding, keys and truncation

`SaveInspectorPanel.Start` runs rather than `Awake` because `UIDocument` builds `rootVisualElement`
in `OnEnable`; binding to the tree any earlier would find nothing to query. `BindChrome` sets
`pickingMode = PickingMode.Ignore` on both the chrome root and the toggle root because both
`UIDocument`s fill the whole screen — without opting out of picking, whichever one is on top would
swallow every tap meant for whatever is underneath it, including its own sibling's controls.
`MaxRenderedCharacters` caps the readout at 4000 characters; nothing the shipped demo document
produces comes close to that size, and the cap exists so a much larger payload still leaves the
layout intact rather than to guard against anything currently saved. `ShortNameOf(SaveStorage)`
abbreviates its labels because the four enum names run wider than a segmented button on a narrow
phone; the full name still leads the combo readout once a result comes back, so nothing is lost by
shortening the button.

### Two overlays in one scene, and why the save inspector uses two documents

Each demo is a full-screen overlay behind a floating toggle, and with both in the same scene each
overlay has to cover the other one's toggle while it is open. Placing them side by side naively fails
three ways. Both toggles sat at the same `top: 160px; right: 24px`, so one hid the other. Both panels
sorted at 100, which leaves their draw and hit order undefined. And whichever panel sorts higher keeps
its toggle floating over the other one's open chrome - exactly where that chrome keeps its control
rows. The pooling demo already hides its own toggle while open for that reason, because its toggle's
band runs through a control row.

A single document cannot fix the last one in both directions, because its toggle and its chrome share
a sort order: sort the save inspector above the pooling demo and its toggle floats over the pooling
controls; sort it below and the pooling toggle floats over its own. So the save inspector splits into
two documents with two `PanelSettings`, one each side of the pooling demo's 100:

| Document | `PanelSettings` | Sort order | Effect |
|---|---|---|---|
| `SaveInspectorToggle.uxml` | `SaveInspectorTogglePanelSettings` | 99 | an open pooling demo covers it |
| `SaveInspector.uxml` | `SaveInspectorPanelSettings` | 101 | when open, it covers the pooling toggle |

The toggle itself moves to `top: 272px`, below the pooling toggle's 160-256 band, so the two sit apart
while both are collapsed.

The two toggles also have to read as one stack, and the label is what decides that. Each toggle is as
wide as its label needs above a shared `min-width: 240px`. "Pooling Demo" fits inside that floor;
"Save Inspector" did not, so its button grew to 246 px, left the stack with a ragged left edge, and
squeezed its own padding to almost nothing beside the roomier button above it. The button now reads
**Saving Demo**. That follows the convention the existing toggle already set - the button names a
topic, the panel it opens carries the full title, the same way "Pooling Demo" opens "Object Pooling" -
and it fits the shared floor, so both buttons resolve to exactly 240 x 96 with aligned edges without
the pooling demo changing at all. A future label that outgrows 240 px widens only its own button;
`DemoOverlaysPlayModeTests` fails on exactly that, rather than a reviewer having to notice it. Game UI canvases sort at 0, so 99 still draws above the game. The pooling
demo is untouched: the save inspector carries the asymmetry on its own.

This is a fixed arrangement for exactly two overlays. A third full-screen overlay would need its
toggle below both existing chromes and its chrome above both existing toggles, and past that point
fixed sort numbers stop being a solution. That is the moment to give the overlays a shared notion of
"one is open", rather than a third sort order.

Two Unity details shaped how the prefab is built:

- **The two documents are siblings, and the prefab root carries neither.** A `UIDocument` placed under
  another `UIDocument`'s GameObject becomes a child document: it joins its parent's panel and cannot
  take `PanelSettings` of its own. Nesting them would silently put both back on a single sort order.
  Unity enforces this with an assertion inside `UIDocument.panelSettings`, which is how it surfaced.
- **The generator updates the prefab in place.** `Tools/Saving/Generate Save Inspector Prefab` loads
  an existing prefab's contents, edits them and saves them back, rather than building a fresh
  GameObject over it. A fresh one gets fresh object identities, and `Game.unity` holds references into
  this prefab now, so regenerating the old way would leave that instance pointing at objects that no
  longer exist. It is safe to re-run after editing either `.uxml` or either `PanelSettings`.

Because it now ships, one consequence is worth stating. The panel builds its pipeline from
`SaveFactoryInputs.Defaults()`, and its default selection is the File store, so pressing Save in the
running game writes `save-inspector-demo.sav` and `save-inspector-demo-baseline.sav` into the same
directory the game saves into. The keys never collide with `currency`, `chests` or `meta`, but
nothing cleans them up. Choosing Memory has no side effects.

`DemoOverlayTests` pins the arrangement against the authored assets: `Game.unity` places both demo
prefabs, and the three sort orders keep the save inspector's toggle below the pooling demo and its
chrome above it. `DemoOverlaysPlayModeTests` instantiates both prefabs and asserts the collapsed
toggles do not overlap and form one aligned column: equal width, matching edges, equal height.

Both tests wait two frames after instantiating the two prefabs before reading layout, because a
`UIDocument` binds its visual tree in `Start` and UI Toolkit resolves layout on the panel's own next
update; reading `worldBound` any earlier would see zero-sized elements. The two toggles come from two
different `UIDocument`s, each with its own `PanelSettings`, so comparing their `worldBound` rects
directly is only meaningful because both `PanelSettings` scale to the same reference resolution the
same way — a panel pixel means the same screen area in either document.

### Why the probe builds from `SaveComponentFactory` rather than `SaveServiceFactory`

`SavePipelineProbe.RunAsync` needs the concrete `ISaveStore` back after the write, so it can read the
bytes that actually landed rather than re-encoding the document and displaying something that merely
should match. `SaveServiceFactory` hands back an assembled `ISaveService` and nothing else, so the
probe composes from `CreateStore`/`CreateCodec`/`CreateProtector` — the same reason
`GameLifetimeScope` assembles currency's pipeline by hand. Save, load and the raw read are timed
separately.

### The bytes are always renderable, and that is structural

Rendering looked like it would need a judgment call per combination — text for the readable ones, a
hex dump for the opaque ones. Measured across all fifteen codec/protector pairs, **every combination
this factory can build stores valid UTF-8 end to end.** That is not luck: `SaveEnvelope` is always
plaintext JSON, and a non-text-safe body always travels the envelope's own base64 path rather than
being embedded raw. So the hex fallback in `SavePipelineProbe.Render` is real, exercised code, but
unreachable for anything shipped today, and it is commented as such rather than left looking
load-bearing.

`SavePipelineProbe` decodes with a strict `UTF8Encoding` (`throwOnInvalidBytes: true`) rather than
the default one, which silently replaces a bad byte sequence with U+FFFD instead of reporting it.
Replacing rather than throwing would hide exactly the case `Render` exists to catch: a decode
failure is what routes a combination's output to the hex-dump path instead of text.

What this makes visible on screen is the more interesting thing anyway: the envelope header stays
readable in every single combination while the body stops being readable, which is the design rule
from "The envelope" made literal — a build can always tell which schema it is holding, even one it
cannot decrypt.

The size baseline is the same document through Json + None into the same store, computed under a
different key rather than hard-coded, so a change to the document or the envelope moves it
automatically.

### The tamper button, and why it edits two different ways

`SaveTamper.RunAsync` edits a save the probe already wrote, then reloads it through the same
combination. Which edit it applies is the entire demonstration:

- **`None`, `Base64` and `Xor` are decoded, edited and re-encoded** — the balance rewritten to a new
  value, exactly as a curious player with a decoder would, because the demo, like that player, either
  holds the key or knows there is not one. This is what makes "obfuscation, not security" concrete
  instead of asserted: watching a base64 payload accept a rewritten balance is worth more than any
  sentence in the Axis C table.
- **`Hmac` and `Aes` cannot be reached that way**, so a byte in the protected body is flipped instead
  — the same `FlipLastByte` convention `SaveServiceTamperDetectionTests` already uses.

Measured outcome, matching what the plan predicted before any of it was built: `None`, `Base64` and
`Xor` hand back the tampered balance; `Hmac` and `Aes` reject it. The AES case was repeated to rule
out its random IV producing a lucky pass, and both were checked with gzip in the mix, since a
compressed body puts gzip's magic bytes *inside* the protected region without changing which edit
path applies.

`SaveInspectorPanel.TamperAsync` reads the combination from `_savedStorage`/`_savedCodec`/
`_savedProtection`, captured when Save last ran, rather than from whatever the segmented controls
show at the moment Tamper is pressed — tampering with a combination nothing was saved under would
just fail to parse, which is not the demonstration this button exists for. Its `catch
(SaveInspectorException)` looks unreachable given the button is disabled until `_hasSaved` is true,
but stays reachable if something else clears the key it saved under first. `ShowTamperResult` is the
whole point of the panel made visible: it toggles CSS classes so an accepted edit and a refused one
are styled differently and cannot be mistaken for each other.

### `SaveBenchmark`, and the number the plan got wrong

`SaveBenchmark` walks all fifteen codec/protector pairs over `SaveStorage.InMemory` — never `File`,
`AtomicFile` or `PlayerPrefs`, because this suite must not touch `Application.persistentDataPath` or a
real `PlayerPrefs` table, and the encoded bytes are identical whichever store carries them. It logs
and **asserts on no duration at all**, the rule `PoolBenchmark`'s own header gives: a timing assertion
on a shared machine is the flaky test `docs/testing.md` exists to prevent. The only assertions are
that a combination round-tripped its value and produced a positive byte count.

Each combination is measured 200 times and averaged, not measured once, so one write/read pair's own
timing noise does not read as the combination's cost — `SaveStorage.InMemory`'s operations are
sub-millisecond individually, and repeating them is what makes the average mean something.

It measures **two document sizes**, and that is the whole reason it is worth reading. The plan
estimated gzip at "−70%". Measured:

| | small (126 B baseline) | large (4113 B baseline) |
|---|---|---|
| `Json` + `None` | 126 B — 100% | 4113 B — 100% |
| `Json` + `Aes` | 230 B — 183% | 5542 B — 135% |
| `JsonGzip` + `None` | 176 B — **140%** | 220 B — **5%** |
| `JsonGzip` + `Aes` | 255 B — 202% | 274 B — ~7% |

**At the size this game actually saves, gzip costs 40% more than plaintext.** A gzip stream carries a
header and trailer, its output is not text-safe so the envelope owes it base64 (+33%), and below a few
hundred bytes there is nothing for compression to find that pays for either. At 4 KB the same codec
saves 95%. One size would have answered the wrong question in either direction — measuring only the
small one reads as "gzip is useless", only the large one as "always compress" — so the benchmark
reports both and the crossover is the finding.

Two things follow. `JsonCodec` remains the right default for this game, now on a measurement rather
than an assumption. And the fixed costs are the ones worth knowing: every protector's overhead is
near-constant in bytes, so it is punishing on a small save and negligible on a large one — `Aes` is
+83% of a 126-byte save and +35% of a 4 KB one, for the same handful of bytes of IV and tag.

The large document is a repetitive 4000-character field, labelled as such in the report, because that
is compression's best case rather than a typical payload — the 5% figure is a ceiling, not an
estimate.
