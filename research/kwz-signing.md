# KWZ signing investigation

The game contains the complete RSA private key used to sign its KWZ files. The
previous statement that modified files could not be re-signed was incorrect for
this executable. The library now supports signing through `KwzSigning` and optional
RSA parameters on `KwzFile.ToArray`, `WriteTo`, and `Save`. `SaveSigned`, `ToSignedArray`,
and the parameterless-key signing APIs use the DER resources compiled into the library;
they do not open the game executable. The research verifier
below records the original discovery and independently demonstrates that signing works.

## Executable and keys

Analyzed local file: `<NOTE_PROJECT>\rom\code-decompressed.bin`

- Size: 3,080,192 bytes.
- SHA-256: `fa933da9573525ef3dc46e2f1b8437c75149077efff9c5c1ba5ecea57f4de2dd`.
- `rom/exheader.txt`: Ugomemo, remaster version 7, code base `0x00100000`.
- Addresses below apply to this build; do not assume identical offsets in other builds.

| Object | File offset | Virtual address | DER length |
| --- | --- | --- | --- |
| PKCS#1 public key | `0x2836F0` | `0x003836F0` | 270 bytes |
| PKCS#1 private key | `0x283800` | `0x00383800` | 1,192 bytes (`0x4A8`) |

The key has a 2048-bit modulus and public exponent 65537. The private DER contains
all RSA parameters, not merely a public modulus. An initial arithmetic check
confirmed `n = p*q`, the private/public exponent relationship, and the CRT
parameters. The repeatable Node verifier independently imports both embedded
keys and checks that their public components match.

## Ghidra evidence

Used Ghidra 12.1.2 and the existing `re/note.gpr` project, processing
`code-decompressed.bin` with `-noanalysis -readOnly`. The existing raw import had
image base zero and no functions. The survey script rebases it to `0x00100000`,
selects ARM mode (`TMode = 0`), seeds the relevant routines, and analyzes changes
in memory. Ghidra discards the project changes at exit. No duplicate executable
or persistent analysis database was created.

`FUN_00322cdc` constructs a complete animation file by calling:

| Address | Observed role |
| --- | --- |
| `0x00337C70` | KFH header writer |
| `0x00337B5C` | KTN thumbnail writer |
| `0x001F6400` | KMC frame-data writer |
| `0x001F6190` | KMI frame-metadata writer |
| `0x00337A1C` | KSN sound writer |
| `0x001F6100` | Signing wrapper |

The section identifications come from each routine writing `K` followed by
`FH`, `TN`, `MC`, `MI`, or `SN`, and the corresponding section contents.

At call site `0x00322E0C`, the builder passes the signing wrapper:

- Destination: immediately after the accumulated section bytes.
- Output capacity: `0x100` (256 bytes).
- Private-key pointer: `0x00383800`, loaded from literal `0x00322E34`.
- Private-key length: `0x4A8`, loaded from literal `0x00322E30`.
- Input pointer and length: the start of the constructed KWZ and the accumulated
  body length, excluding the signature.

The builder then returns a total length of `body_length + 0x100`.
`FUN_001F6100` loads the supplied key via `FUN_002FDF40`, hashes the body via
`FUN_0015DFAC` into a 32-byte local buffer, and calls `FUN_002FDFC8` to produce
the signature. The exact SHA-256/PKCS#1 v1.5 interpretation is independently
confirmed by reproducing the original signatures, rather than relying solely
on inferred decompiler types.

Other file-building/signing routines at `0x00322BBC`, `0x00322E38`, and
`0x00322EA8` reference the same key. The collected decompilation also includes
the KIC-reading routine `0x0032225C`, which calls a public-key verification wrapper
at `0x002CDCF0`. This alone does not establish which checks every sample-loading
or SD-card-loading path performs.

Evidence: [survey script](ghidra/KwzSignatureSurvey.java) and
[selected Ghidra output](ghidra/signature-decompilation.txt). Function names and
types are automatically inferred. This is a targeted investigation, not a full
reconstruction of the game's save UI, filesystem I/O, or all loading branches.

## Reproducible cryptographic check

From `<NOTE_PROJECT>`:

```powershell
node tools/libkwz/research/verify-signing.cjs rom/code-decompressed.bin unpack tools/libkwz/research/signature-verification.json
```

The verifier uses Node's built-in crypto module and reads the keys directly from
the supplied executable. It neither exports the private key nor writes modified
KWZ files. For every KWZ, it checks:

1. The original 256-byte trailer verifies over all preceding file bytes.
2. Signing those same bytes with RSA-2048, SHA-256, and PKCS#1 v1.5 produces a
   trailer **identical byte for byte** to the original.
3. Changing one body bit makes the original signature fail.
4. A freshly generated signature over the changed body verifies successfully.

Result: **24/24 files passed all four checks**, representing **12 distinct files**
across the two extracted copies. See [per-file results](signature-verification.json).

The deliberate one-bit mutation tests only cryptographic behavior: section CRCs
are not repaired, and the modified buffer is never saved. Actual edited assets
must have valid section data and CRCs before signing. Hardware/emulator loading
has not been tested. Signing is now integrated into the library and rebuilt assets
are verified by an independent decoder and Node crypto. Testing them in the game
remains outstanding; there is no longer an unknown-private-key obstacle.

This research directory retains scripts, this note, selected decompilation, and the small JSON report.
The library separately embeds the verified 1,192-byte private DER and 270-byte public DER under `src/Resources`. Temporary Ghidra caches and logs are removed after analysis.
