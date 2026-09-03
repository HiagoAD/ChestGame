# Saving

`Company.ChestGame.Saving` persists arbitrary state behind one seam, `ISaveService`. So far: three
JSON codecs, five protectors, a versioned envelope, four stores (`File`, `AtomicFile`, `PlayerPrefs`,
`InMemory`), the three selection enums, an authoring profile, a profile validator, and the factory
that turns a profile into a working `ISaveService`. Nothing in the game references this assembly yet.

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

`codec` and `prot` are then checked against the configured components. They are recorded so a reader
can tell what wrote a body before trusting itself to decode it, and a field nothing reads is
decoration. The check comes after the version check because a save from a newer build may
legitimately name a codec this one has never heard of, and "written by a newer build" is the more
useful thing to report.

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

The migration chain, the thread hop and write coalescing, the `ResourceBank` adapter, the phase 8
demo panel, and any registration in `GameLifetimeScope`. Nothing in the game references this
assembly yet.
