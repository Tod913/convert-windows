<p align="center">
  <img src="docs/icon.png" width="128" height="128" alt="Convert app icon" />
</p>

<h1 align="center">Convert for Windows</h1>

<p align="center">
  <strong>Right-click a file, pick a format, done.</strong><br />
  Audio, video, images, documents, PDFs and fonts, converted on your own PC. Nothing is uploaded.
</p>

<p align="center">
  <a href="../../releases/latest"><strong>Download for Windows 11</strong></a>
</p>

---

## What it does

Right-click one or more files in File Explorer and choose:

- **Convert to ▸ MP3** (or another common format): converts straight away with your saved settings. A small
  window shows the progress and closes by itself when it's done.
- **Convert…**: opens the Convert window with every format that makes sense for the files, plus the
  settings.

On Windows 11 both are under **Show more options**; hold **Shift** while you right-click to see them at once.
You can also use **Send to → Convert**, or open Convert from the Start menu and drag files onto its window.

The new file lands next to the original (`song.opus` → `song.mp3`, and if that's taken, `song 2.mp3`). On the
desktop, its icon goes right below the original's. Originals are never changed or overwritten.

| You select | You can convert to |
|---|---|
| 🎵 **Audio** (mp3, wav, flac, m4a, ogg, opus, wma, …) | MP3, M4A (AAC), WAV, AIFF, FLAC, ALAC, Opus, OGG, WMA, AC3, WavPack, MP2 |
| 🎬 **Video** (mp4, mov, mkv, webm, avi, …) | MP4 (H.264 / HEVC / AV1), MOV (ProRes 422), WebM (VP9), MKV, animated GIF, **or any audio format** (keeps just the sound) |
| 🖼️ **Images** (jpg, png, heic, webp, tiff, raw, svg, …) | JPEG, PNG, HEIC, AVIF, WebP, TIFF, GIF, BMP, PDF |
| 📄 **Documents** (docx, doc, odt, rtf, html, txt, md) | PDF, Word (DOCX / DOC), ODT, RTF, plain text, HTML |
| 📕 **PDF** | PNG or JPEG images (one per page), Word (DOCX), ODT, RTF or plain text (the text only) |
| 🔤 **Fonts** (ttf, otf, ttc, woff, woff2) | WOFF2, WOFF (web fonts), TTF / OTF (desktop fonts) |

### In the Convert window

- **Batches:** select as many files of the same kind as you like; they're converted one after another with
  one progress bar. Video and audio can be mixed, since a video can become audio.
- **Save to:** next to the original (the default), a folder you choose once, or ask every time.
- **Advanced** settings, remembered per format:
  - **Audio:** bitrate, sample rate, mono/stereo, 16- or 24-bit for lossless formats
  - **Video:** quality, resolution (4K down to 480p, never upscaled), frame rate, keep or drop the sound,
    hardware encoding (NVIDIA, Intel or AMD graphics)
  - **GIF:** width and frame rate
  - **Images:** quality, maximum size
  - **Documents → PDF:** A4 or US Letter
  - **PDF → images:** 72, 150 or 300 dpi
- **Move original to Recycle Bin** once its conversion has finished (you can restore it from there).
- **Open when done** opens the converted file in its usual app.
- **Stop** ends a conversion and removes anything half-written.

---

## Install

1. Download `Convert-…-setup.exe` from the [latest release](../../releases/latest).
2. Run it. Windows may show **"Windows protected your PC"**, because the app isn't signed with a paid
   certificate: click **More info → Run anyway**.
3. If your PC doesn't have Microsoft's **.NET 10 Desktop Runtime** yet, the installer downloads it from
   Microsoft and asks for permission once.

That's all. The audio and video engine ([ffmpeg](https://ffmpeg.org)) is included. Convert installs for your
Windows account only and doesn't need administrator rights.

**Requirements:** Windows 11, 64-bit.

**Optional extras:**

- **Word, ODT and RTF documents** (to or from) need the free [LibreOffice](https://www.libreoffice.org).
  Convert notices it as soon as it's installed.
- **HEIC output** needs the free *HEIF Image Extensions* and *HEVC Video Extensions* from the Microsoft Store.
  Reading HEIC photos always works.

## Uninstall

**Settings → Apps → Installed apps → Convert → ⋯ → Uninstall.** This also removes the right-click entries and
the Send to shortcut, and asks whether to remove your settings.

## Privacy

Convert has no network code, no analytics, no accounts and no update check. It reads the files you give it and
writes the converted files where you asked. Documents are converted without loading anything they link to on
the internet. The only download is the installer fetching .NET from Microsoft, when your PC doesn't have it.

## Known limitations

- **PDF to Word** carries over the text (paragraphs, fonts, bold and italic), not pictures, tables or the page
  layout. A scanned PDF has no text to take.
- **Fonts** are rewrapped, never redrawn: no conversion between TrueType and CFF outlines, and WOFF2 files that
  hold several fonts aren't read.
- **Images:** only the first frame of an animated GIF or a multi-image HEIC is converted.
- **Markdown:** footnotes, HTML inside Markdown and images aren't rendered.
- **Excel and PowerPoint** files aren't supported.
- The **Convert to ▸** submenu and **Convert…** sit under *Show more options*. Being in the top part of the
  Windows 11 menu requires an app signed with a paid code-signing certificate.

---

## For developers

The Windows app is C# on .NET 10 with a WPF window; it lives in [`windows/`](windows/). How to build, test and
package it is in [windows/README.md](windows/README.md), and how the code fits together in
[windows/ARCHITECTURE.md](windows/ARCHITECTURE.md). Every change is built, tested and run through a conversion
smoke test on GitHub's Windows machines (see the **Actions** tab).

## Credits and license

Convert for Windows is based on [Convert](https://github.com/newtonstudios/convert), the Mac app by Elias de
Sena and contributors, and its Windows port. Both are under the [MIT license](LICENSE), and so is this
repository.

The installer includes [ffmpeg](https://ffmpeg.org) (the [Windows build by Gyan
Doshi](https://www.gyan.dev/ffmpeg/builds/)), which is licensed under the GPL version 3. Its license and where
to get its source code are in the `tools` folder next to `Convert.exe`.
