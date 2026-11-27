Right-click a file in File Explorer, pick a format, done. Everything is converted on your own PC.

**Install:** download `Convert-…-setup.exe` below and run it. Windows 11, 64-bit. If Windows says
"Windows protected your PC", click **More info → Run anyway**: this release isn't code-signed yet.
`.sha256` is the file's SHA-256 fingerprint, to check the download. Installing over an earlier version
updates it.

### What's new in this version

Smaller and faster, measured against 0.5.0 on the same PC:

- **Download 45 MB instead of 128 MB**, and 265 MB on disk instead of 499 MB. The included ffmpeg is now
  the "essentials" build, which has every encoder Convert uses.
- **Opens about a third faster** for audio and video, and faster for pictures: Convert no longer checks
  formats that don't apply to the file, and remembers what it found out about ffmpeg and the graphics card.
- **Selecting several files** in the classic menu: each extra file now takes about 0.1 s instead of 0.3 s.
- **Video conversions start sooner**: the graphics-card check isn't repeated every time.
- **MP4 (AV1)** now uses the libaom encoder: files come out smaller, and it takes about twice as long.

Code signing: free code signing provided by [SignPath.io](https://about.signpath.io), certificate by
[SignPath Foundation](https://signpath.org) ([policy](https://github.com/Tod913/convert-windows/blob/main/CODE_SIGNING_POLICY.md)).

Based on [Convert](https://github.com/newtonstudios/convert) 0.4 by Elias de Sena and contributors.
