# Engine version

The project is pinned to **Unity 6000.3.27f2** (revision `22188f149405`, 6.3 LTS) in
`ProjectSettings/ProjectVersion.txt`. `ci/run-tests.sh` and `ci/build-addressables.sh` read the version
from that file to find the editor binary, so the pin is the only place it is set.

## Why 6000.3.27f2

On 2026-10-30 Unity announced two industry-wide vulnerabilities that reach Unity projects:

- **Brotli decoder, CVE-2026-111497.** An out-of-bounds write in Google's Brotli library, which ships in
  the .NET class libraries bundled with the Unity runtime. UnityWebRequest and AssetBundles do not use it.
- **eMudhra certificate authority, CVE-2026-111943.** A flawed CA whose distrust has to reach the trust
  store the editor and player rely on.

Unity's [Brotli advisory](https://unity.com/security/unity-brotli-decoder-vulnerability) lists every 6.3 LTS
release from 6000.3.0a1 to 6000.3.27f1 as affected and 6000.3.27f2 as the patched build. The project was on
6000.3.11f1. Unity also assesses the severity in its ecosystem as low, medium at worst, with no known
exploitation.

The fix sits in the runtime libraries, so it reaches players only through a build made with the patched
editor. A package built with an older editor keeps the vulnerable libraries.

## The project's exposure

Unity's advisory says platforms other than WebGL are affected only when the application calls
`BrotliStream`, `BrotliDecoder`, or `HttpClient` with Brotli automatic decompression on untrusted data.
The project's code does not reference any of them, and it has no scoped registries in
`Packages/manifest.json`. `GzipJsonCodec` compresses saves with `GZipStream`, not Brotli. Remote
Addressables content is fetched through Unity's own loading APIs, which per the advisory do not use the
Brotli decoder. Those fetches use TLS, though, so the certificate trust fixed with the eMudhra issue
applies to them.

Unity relies on the device platform for certificate security, so players should still keep their
devices updated.
