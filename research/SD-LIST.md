# Flipnote Studio 3D SD catalog (`!!.lst`)

Researched against the Japanese v7.2.0 executable, using Ghidra 12.1.2 and
execution of the original ARM routines under Unicorn 2.1.4. The decompressed
executable SHA-256 is `fa933da9573525ef3dc46e2f1b8437c75149077efff9c5c1ba5ecea57f4de2dd`.
Addresses below use an image base of `0x00100000`.

No personal SD catalogs, author IDs, local animation names or filesystem paths
are included in this repository. The regression vector contains synthetic names.

## Location and purpose

`private/Nintendo 3DS/app/JKZJ/000/!!.lst` is the ordered filename catalog for
one animation folder. The KWZ files live beside it. Studio also discovers existing
`JKZE` and `JKZP` directories and custom-named folders; native analysis was performed
on the Japanese build only. It does not create a game installation or title save.

The catalog contains filenames only. Author IDs, timestamps, thumbnail images,
audio and signatures remain in the KWZ files. There is no RSA signature on this
catalog. Its repeating XOR is obfuscation, not secure encryption.

## Binary layout

All 16-bit values are little-endian. For `N` entries the file is `6 + 68*N` bytes,
with at most 100 entries (6,806 bytes). The six-byte header is unobfuscated:

| Offset | Size | Meaning |
| --- | --- | --- |
| 0 | 2 | High 16 bits of the plaintext payload's standard CRC-32 |
| 2 | 2 | Low 16 bits of that CRC-32 |
| 4 | 2 | UTF-16 LF, `0x000A` |
| 6 | `68*N` | XOR-obfuscated payload |

Decode the payload with `plain[i] = stored[i] XOR table[i % 61]`, starting the
table at payload offset zero. The 61-byte table is:

```text
F7 4C 6A 3A FB 82 A6 37 6E 11 38 CF A0 DD 85 C0
C7 9B C4 D8 DD 28 8A 87 53 20 EE E0 0B EB 43 A0
DB 55 0F 75 36 37 EB 35 6A 34 7F B5 0F 99 F7 EF
43 25 CE A0 29 46 D9 D4 4D BB 04 66 68
```

The plaintext is UTF-16LE, without BOM:

```text
filename-1.kwz + LF
filename-2.kwz + LF
...
filename-N.kwz + LF
N NUL code units
```

Each filename is exactly 32 characters: 28 characters from the KWZ alphabet
`cwmfjordvegbalksnthpyxquiz012345`, then `.kwz`. Each line is 66 bytes.
The `2*N` zero bytes are all at the **end of the stream**; entries are not
individual padded 68-byte records. The writer reserves 34 UTF-16 units per entry
but streams only 33, leaving that final padding in its zero-filled buffer.

CRC-32 uses polynomial `0xEDB88320`, initial register `0xFFFFFFFF`, and final
complement. It covers the **entire decoded payload, including final padding**.
Its two halfwords are stored high-first, each halfword itself little-endian.
Neither a conventional four-byte little-endian CRC nor an encrypted header works.

This differs from the DSi `.lst`/`.pls` format described in
[jaames's DSi utility](https://gist.github.com/jaames/daaf2531bae31f72cca675df4361c0d4):
the 3DS repeats 61 bytes, uses this CRC header, and does not use that utility's
two MD5 check bytes or fixed DSi catalog sizes.

## Native evidence

| Address | Role / observed behavior |
| --- | --- |
| `0x00207AD0` | In-place XOR; index reduction modulo `0x3D` |
| `0x00374693` | Start of the 61-byte table |
| `0x00222600` | CRC-32, complemented result |
| `0x002A830C` | Ordered-list writer; `(N * 0x22 + 3) * 2` file length |
| `0x002A9840` | Builds a list from discovered/current filenames, capped at 100 |
| `0x002A87FC`, `0x002A9C58` | Load, XOR-decode, combine CRC halfwords and verify |
| `0x002AA11C` | Read lines; validate names; discard duplicates; cap at 100 |
| `0x00207D1C` | Validate 32-character KWZ filenames and allowed alphabet |
| `0x002AA314` | Match catalog entries to files actually present in the folder |

`KwzSdList` deliberately rejects damaged, duplicate or noncanonical catalogs
instead of silently repairing or discarding their contents during SD import.
It reads and writes the observed canonical game format byte-for-byte.

## Import behavior

`KwzSdCard.Import(folder, note)` clones the supplied note and preserves root/parent
filenames, author identity, animation pixels, timing and audio. It updates the
current edit timestamp and corresponding metadata filename until it finds an
unused name. It always renders the selected thumbnail and signs the exported copy.
It never overwrites an existing animation. A full folder is refused.

The original catalog order is retained. Valid KWZ filenames already on disk but
absent from the catalog are included, then the new copy is placed first. An absent
catalog can be created; a corrupt one is refused. Existing files are not renamed.

Writes use temporary files on the same volume, flush and hash-readback checks,
an exclusive Studio lock, and a second check that the directory/catalog did not
change. The original list is backed up as `!!.lst.studio-<random-id>.bak` before
replacement. Backups contain only the small catalog, not animation copies.
The new KWZ is installed before the list. If list replacement fails, the new KWZ
is removed; existing animations and the old list remain. Temporary files are
removed on ordinary success/failure. A committed pair is retained if subsequent
readback fails, with the original backup available.

Close the game before import and safely eject the card afterwards. Studio's lock
coordinates Studio instances, not the console; two filesystem renames cannot form
a power-loss-proof transaction on an SD card. After an interrupted operation,
close the game, retain the `.bak`, and restore it to `!!.lst` if necessary. A newly
added KWZ can remain as an unlisted file without damaging existing animations.

## Verification

### Folder discovery and game icons

Studio stores only the explicitly selected SD root. Every import opens a new
destination chooser and rescans `private/Nintendo 3DS/app/JKZJ`, `JKZE` and `JKZP`.
Children are not restricted to three-digit names: custom names and empty folders
without `!!.lst` are valid. A missing catalog is created only during import.
The chooser refreshes when opened/reactivated, on request, and before acceptance.
It preserves the selected path when present and clears selection if it disappears.
Neither the chooser nor icon decoding writes to the card.

`!.ico` is a signed KIC file, not a Windows icon. `KwzFolderIcon` reads it as:

- An eight-byte `KIC\x03` marker with zero payload size.
- One `KMC\x02` image section with a four-byte CRC-32 followed by compressed layers.
- One `KMI\x05` section containing one 28-byte frame metadata record.
- A 256-byte RSA signature over the preceding file body.

The layers use the ordinary 320×240 KWZ codec, but only the top-left 24×24 pixels
form the icon. They are composited with the frame palette and depth ordering,
then displayed at integer scale without interpolation. Empty layers can have zero
length; other layer lengths must be even and at most `0x4AFF`. The total KMC data
length is the sum of the layer lengths rounded up to four bytes. A missing,
oversized, corrupt or unsupported icon gets a generic folder picture in Studio.

The native KIC reader at `0x0032225C` checks the marker, section sizes, 28-byte KMI,
layer limits and signed body through `0x002CDCF0`. See the existing Ghidra extract
in `ghidra/signature-decompilation.txt`.
[flipnote.js KwzParser](https://github.com/jaames/flipnote.js/blob/master/src/parsers/KwzParser.ts)
independently documents the KIC single-frame variant and 24×24 crop.
Only synthetic icons and folder names are used by committed tests/screenshots.

### Checks

- A 12-entry local game catalog roundtrips byte-for-byte; it is not published.
- An independent Python-generated two-entry vector is embedded in `SdCardTests`.
- The actual ARM XOR and CRC routines accept both catalogs; re-encoding restores
  every original byte. The native filename validator accepts the synthetic KWZ
  name and rejects wrong-length/wrong-extension names.
- Import tests cover signature, all decoded frames, raw audio, metadata filename,
  backups, repeated imports, existing/unlisted files, full and damaged catalogs,
  and rollback after Windows denies catalog replacement.
- Headless Studio tests check flattened text, list registration, and unchanged
  objects/document/undo state, alongside rendering all 20 UI languages.
- KIC tests cover pixel coordinates, palette, depths, transparent/omitted layers,
  signature/CRC failures and invalid section bounds. Chooser tests cover custom
  empty folders, missing/damaged icons, refresh, disappeared destinations, cancel,
  and all 20 languages including right-to-left layout.
- A synthetic KIC with three encoded layers matches flipnote.js 6.3.0 at all
  576 RGBA pixels; its independent pixel hash is a regression vector. The
  zero-length omitted-layer variant is covered separately against native layout
  evidence, since that reference decoder does not handle omitted layers.
- These checks do not constitute an end-to-end import/playback test on a console
  or a running Citra instance. Existing user SD data was inspected read-only.

To repeat the native checks, install `unicorn==2.1.4` in a disposable environment,
then run `python research/verify-sd-list.py <code-decompressed.bin> [catalog.lst ...]`.
Only sample numbers and counts are printed. For the optional private .NET test,
set `KWZ_SD_LIST_SAMPLE` to a local catalog and run `dotnet test test/LibKwz.Test`.
Set `KWZ_TEST_TEMP` to an owned temporary directory to control test disk usage.
