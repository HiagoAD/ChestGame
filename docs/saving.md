# Saving

`Company.ChestGame.Saving` persists arbitrary state behind one seam, `ISaveService`. So far: three
JSON codecs, five protectors, a versioned envelope, four stores (`File`, `AtomicFile`, `PlayerPrefs`,
`InMemory`), the three selection enums, an authoring profile, a profile validator, the factory that
turns a profile into a working `ISaveService`, a migration chain, the seam a pre-envelope legacy
import plugs into, a store decorator that hops encoded, protected bytes onto a worker thread and back
(`ThreadHoppingStore`), and a write-coalescing scheduler (`SaveScheduler<T>`) built on top of
`ISaveService` rather than inside it. Nothing in the game references this assembly yet.

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

### `ISaveMigration` and `SaveMigrator`

`ISaveMigration` is one step: `FromVersion` and `JObject Apply(JObject document)`, from `FromVersion`
to `FromVersion + 1` and never further. `SaveMigrator` is what walks a chain of these from a document's
stored version up to a target, strictly ascending and one version at a time — never skipping a step,
never applying two at once. It takes no Unity type anywhere in its surface, so it is exercised by a
plain NUnit test with no player loop, the same reason `SaveMigrator` and `SaveEnvelope` both stay free
of `UnityEngine`.

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
leftover legacy entry is inert, not lost.

## The golden corpus

`Assets/Tests/EditMode/SaveCorpus/` holds one committed envelope file per historical schema version —
`v1.json` today, the only one that exists while `CurrentSchemaVersion` stays at `1`. These are real
bytes a build actually produced through the real pipeline (`SaveService` over `JsonCodec` and
`NoProtection`), frozen the moment they were generated, not JSON typed by hand to look plausible.

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
adding a schema version takes".

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
  `1.5`.
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
`FileStore`'s root and `PlayerPrefsStore`'s prefix follow: a test supplies its own and never touches
whatever the factory would otherwise default to. `SaveServiceFactory` supplies a fixed default key
per protector when it builds one from a `SaveProfileSO`; a test wanting a specific key constructs the
protector directly instead of going through the factory, since nothing about `Create` or `CreateFrom`
needs to expose key material the way `playerPrefsKeyPrefix` exposes a namespace.

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
profile picking it for privacy rather than tamper-evidence is told what it actually got.

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
goes on the end of whichever enum it belongs to — phase 3 appended `JsonPretty` and `JsonGzip` to
`SaveCodec` and `Base64`, `Xor`, `Hmac` and `Aes` to `SaveProtection`, in that order, after the
member each enum already had.

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

`CreateCodec` and `CreateProtector` listed `SaveCodec.Json` and `SaveProtection.None` as explicit arms
*alongside* the discard from the start, back when each enum had only that one member and the discard
alone would have returned the same thing. That was deliberate rather than premature: an enum with one
member makes the redundancy easy to "clean up" into just the discard, which is exactly the shape of
the mistake `PoolFactory.Create` warns about — skip the arm for a new member and the switch still
compiles, quietly keeping every profile on `JsonCodec` or `NoProtection` regardless of what its
dropdown says. Phase 3 is the proof the arm was worth keeping: `JsonPretty`, `JsonGzip`, `Base64`,
`Xor`, `Hmac` and `Aes` each landed as their own case, not folded into the discard, so nothing about
adding them required restructuring either switch. `SaveService`'s codec/protector id check on load —
see "Loading, and the three ways a version goes wrong" — is a second line of defence if a case is ever
missed anyway, since a save written by one codec and loaded through another fails as
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
`SaveServiceFactory` or any composition root — that is out of this phase's scope, along with every
other piece of integration listed under "Not built yet" below — so today it is available but inert,
proven by the scratch harness this phase's own review used rather than by a shipped call site.

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
from a genuinely broken store (a full disk, a revoked permission) still surfaces to whoever is awaiting
`FlushAsync` at the time; nothing here hides a real failure, it only makes sure the *data* that failed
to save is not silently dropped on the floor because the retry that would have picked it up was never
given anything to notice. If the underlying cause does not go away — a disk that stays full stays
full — this repeats once per coalescing window rather than spinning: each retry is exactly one more
`SaveAsync` call, gated by the same throttle as any other write, never a tight loop hammering a store
that is already failing.

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

## Not built yet

Wiring `ThreadHoppingStore` into `SaveServiceFactory` or any composition root, and constructing a real
`SaveScheduler<T>` anywhere — both are integration, and this phase is mechanism only. The concrete
`ILegacyImport` adapter for `ResourceBank` and `CurrencyType`, the actual save model, the phase 8 demo
panel, and any registration in `GameLifetimeScope`. The migration chain and the legacy-import seam
exist as of phase 4; nothing has a real migration or a real legacy adapter to run yet, because both
need a save model that does not exist until phase 7. Nothing in the game references this assembly yet.
