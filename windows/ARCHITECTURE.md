# How Convert for Windows is built

Notes for people changing the code. What the app does for its users is in the [main README](../README.md)
and in [README.md](README.md) next to this file.

The Mac app ([newtonstudios/convert](https://github.com/newtonstudios/convert), `Sources/Convert/`; not in this
repository) is the original and the specification: target IDs, labels, defaults, option ranges, error texts and
the order of things come from there, and the "Mac counterpart" files below and in code comments are in that
repository. The two apps share behaviour, not code. Where Windows can't do something the Mac does, the format is hidden at runtime rather than
offered and failing, and the difference is listed in the README.

## The projects

| Project | What's in it |
|---|---|
| `src/Convert.Core` | Every conversion, with no window: formats, sniffing, ffmpeg arguments, images, documents, PDF, fonts |
| `src/Convert` | The window (WPF, Fluent theme) and the `--cli` mode |
| `src/Convert.Shell` | The **Convert…** entry in Explorer's menu (`IExplorerCommand`) |
| `packaging/` | The package manifest template, logos, and what `build.ps1` and `uninstall.ps1` share |
| `tests/Convert.Core.Tests` | xUnit tests; most run real conversions on files they generate |

| File in `src/Convert.Core` | Mac counterpart | What's in it |
|---|---|---|
| `Formats.cs`, `MediaKinds.cs` | `Formats.swift` | Accepted extensions, output formats, per-format options, settings |
| `Sniff.cs` | `Sniff.swift` | Looks inside a file when its name can't be trusted |
| `Converter.cs`, `Tools.cs`, `Paths.cs` | `Converter.swift`, `Process.swift` | Runs one conversion; external tools, Stop, where output goes |
| `FFmpeg.cs` | `FFmpeg.swift` | ffmpeg arguments, and which GPU encoder works |
| `ImageConv.cs` | `ImageConv.swift` | Images, through Magick.NET |
| `DocConv.cs` | `DocConv.swift`, `Markdown.swift` | Documents: Markdig, WebView2 for PDF, LibreOffice for Word formats |
| `PdfConv.cs`, `PdfDocConv.cs`, `PdfText*.cs`, `DocWriter*.cs` | `DocConv.swift`, `PDFText.swift` | PDF → images, text, Word/ODT/RTF |
| `Fonts/` | `FontConv.swift`, `SFNT.swift`, `WOFF.swift`, `WOFF2.swift` | Font tables and the two web font wrappers |
| `FileOps.cs` | (in `ConvertModel.swift`) | The Recycle Bin |
| `SelectionList.cs` | — | The list file a long selection travels in |

## One conversion

`Converter.ConvertAsync` is the only way in, for the window and for `--cli`:

1. The file's contents are checked against its name (`Sniff.Verify`), so audio renamed to `.jpg` gets a
   sentence about that instead of a decoder's error.
2. The output name is chosen: next to the original, or in Downloads when that folder can't be written
   to (`OutputFolder`). "Can't be written to" is found out by creating a file there; attributes and
   permissions don't know about write-protected media or what a server allows.
3. The converter for the target's group runs.

Three rules hold in every converter:

- **Nothing is overwritten.** `Paths.UniqueOutput` picks `name.ext`, `name 2.ext`, …, and in-process
  writers create the file with `Paths.WriteNew`, which fails rather than replace a file that appeared
  in the meantime. ffmpeg runs without `-y` for the same reason.
- **Nothing half-written stays.** A failed or stopped conversion deletes what it began: the file, or
  the folder of pages or fonts.
- **Stop reaches everything.** `Runner` carries it: it kills the external tool that's running
  (`Runner.RunAsync`, `Proc.RunSync` with a runner; the whole process tree, since `soffice.exe` only
  starts `soffice.bin`), and work done in-process calls `runner.CheckCancelled()` between steps or
  watches `runner.Token`. A stopped conversion throws `StoppedException`; the window shows "Stopped"
  and no error, and never recycles the original.

Closing the window during a conversion is Stop. The window hides and only closes once the batch has
ended, because the app exiting first would leave the partial output behind.

## Tools

`Tools` looks for `ffmpeg.exe`, `ffprobe.exe` and `soffice.exe` on every use, so installing one while
Convert is open takes effect without a restart. The folders, in order: `tools\` next to `Convert.exe`
(a bundled ffmpeg), the PATH the app was started with, the PATH as it is in the registry now (winget
adds ffmpeg's folder to the user PATH, which a running program doesn't see), winget's link folder, and
for LibreOffice its usual place in Program Files. Relative PATH entries are skipped: they would mean the
folder of the files being converted.

What is found out about a tool (its encoders, whether a GPU encoder works) is kept per installed copy:
path, size and date. A different ffmpeg is asked again.

A tool's error output is read as it comes and only the last 64 KB are kept; the last line is the
message shown.

## Audio and video

Same arguments as the Mac, with these encoders: ffmpeg's own `aac` (Apple's `aac_at` doesn't exist
here), `libfdk_aac` for HE-AAC (only some ffmpeg builds have it; hidden otherwise), and for hardware
H.264/HEVC whichever of NVENC, Quick Sync or AMF passes a three-frame test encode
(`HardwareEncoder`). ProRes is always `prores_ks`: it has no hardware encoder on Windows, so it has no
"Hardware encoding" option. If a hardware encode fails on the real file, it is tried once more in
software (`Converter.WithSoftwareFallback`).

## Images

Magick.NET, with ImageMagick kept on a short lead (`ImageConv.Setup`):

- It is always told which decoder to use (by the file's contents when they're a well-known format,
  otherwise by its extension, from a fixed list). Left alone it would also consider MSL and MVG, which
  are scripts, and URL, which fetches.
- Files are read and written through streams. ImageMagick gives meaning to parts of a file name
  (`photo[1].jpg`, `%d`), and a name is just a name.
- A policy denies the scripting and fetching coders, external delegates and `@file` reads, and makes
  PostScript and PDF write-only. Memory, disk and dimensions have ceilings, so a small file claiming to
  be an enormous picture is refused.
- SVG is read by librsvg, which reaches neither the network nor files outside the drawing's folder
  (tested in `ImageSafetyTests`).

HEIC is written by Windows' own HEIF encoder (`WindowsHeif`), which needs the HEIF and HEVC extensions
from the Microsoft Store; a probe encode decides whether HEIC is offered.

## Documents

Markdown is rendered by Markdig; Markdown, plain text and HTML are printed to PDF by WebView2, in a
hidden window on a thread of its own (`DocBrowser.cs`). WebView2 rather than Edge: Edge can be
uninstalled, WebView2 can't. Word, ODT and RTF, in or out, go through `soffice --headless` with a
throwaway profile (`DocOffice.cs`).

A document must not make Convert talk to anyone, and a browser engine is built to do just that. What
keeps it quiet, each layer tested on its own in `DocNetworkTests`:

- Every request the page makes passes a filter (`WebResourceRequested`, which does fire for `file:`
  URLs) that only lets through files in the document's own folder and that conversion's temp folder.
  Before each conversion a probe page checks that the filter is really in place.
- The filter doesn't see connections the engine opens ahead of time (preconnect, a frame it is about
  to load), so the engine also gets a proxy that doesn't exist, a name resolver that knows no names,
  and its own background requests and reputation checks switched off.
- Navigations after the first, new windows, downloads and links to other programs are cancelled.
- LibreOffice gets a proxy that doesn't exist in its profile, and HTML and Markdown only reach it after
  WebView2 has loaded them and local pictures have been embedded as data, so nothing stays linked.
- Output files never contain the original's folder: the `<base>` that makes relative pictures work is
  only in the temporary copy that is printed, and links to local files are removed.

WebView2 treats a window nobody sees as a background page and slows it to a crawl on a busy PC; the
switches that turn that off are what keep a one-page PDF at a second or two. Its data folder is named
after the startup options (`%LOCALAPPDATA%\Convert\WebView2\<hash>`), because a running WebView2 refuses
a second user of the same folder with different options.

Outputs are made in the temp folder and copied into place when complete.

## PDF

`PdfDocConv` and `PdfText*.cs` turn a PDF's text into paragraphs for Word, ODT and RTF: a port of
`PDFText.swift`'s rules (line spacing per text size, size changes, centred lines, list items, indents,
a line ending early). The Mac gets its lines from PDFKit; here they are built from PdfPig's letters in
the order the PDF draws them. `DocWriter*.cs` write the three formats directly (RTF as text, DOCX and ODT
as zip packages), so this needs neither LibreOffice nor a library.

Pages are rendered by `Windows.Data.Pdf` (part of Windows), from a stream Convert opens itself, since
Windows' own way of opening a file by path stops at 260 characters. It renders larger than asked on
scaled displays, so each page is resized to the exact size and the dpi setting means the same everywhere.
Text comes from PdfPig.

## Fonts

`Fonts/` is a port of the Mac's font code: TTF, OTF, collections, WOFF and WOFF2 hold the same tables
in different wrappers, and converting moves the tables from one to the other. Every read is
bounds-checked and every size a file claims is capped against what is really there, so a damaged or
hostile font is an error message, never a crash or a full disk. zlib and Brotli come with .NET; WOFF2
is compressed at Brotli's highest setting.

## Ending the process

Convert ends itself with `AppExit.Now` once its work is done, instead of returning from `Main`. A
process that has drawn anything has the graphics driver loaded, and on PCs with Intel graphics that
driver's shutdown code can take seconds or never return, leaving an invisible process spinning for
good. The test host ends the same way (`TestSetup`).

## The Recycle Bin

"Move original to Recycle Bin" must never delete for good, and Windows' "recycle" means "or delete, if
it can't be recycled": on network shares, on drives without a bin, for files over the bin's size limit.
`FileOps.Recycle` therefore: skips files over the limit Explorer has recorded for the drive; asks
Windows what it is about to do (`IFileOperationProgressSink.PreDeleteItem`) and refuses unless that is a
move to the bin; and holds a hard link to the file for the duration, to put it back under its name if
Windows deleted it after all. An original is only recycled when its finished output sits next to it
(`FileOps.MayRecycle`).

## The Explorer menu

- Windows 11's top-level context menu only shows `IExplorerCommand` handlers from apps with *package
  identity*. Convert gets that through a sparse package: a small signed MSIX that holds only the
  manifest and logos and points at the normal install folder (`Add-AppxPackage -ExternalLocation`).
- The manifest registers `Convert.Shell.comhost.dll` as a COM surrogate server (it runs in
  `dllhost.exe`, not in Explorer) and one `ItemType` per accepted extension, so the handler is only
  loaded for files Convert can handle. The extension list comes from `Convert.exe --list-extensions`;
  `MediaKinds.cs` is the one source.
- `Convert.Shell` stays tiny: it shares only `MediaKinds.cs` and `SelectionList.cs` with Core (as linked
  source) and never loads Magick.NET or WPF. It decides by extension alone, so it answers at once for
  any selection; the app looks inside the files afterwards (`Sniff`).
- A selection too long for a command line (about 32,000 characters) is written to a list file in the
  temp folder and passed as `--files <list>`. Convert deletes the list after reading it, but only a file
  with the name the menu gives it, in the temp folder.
- The publisher in the package manifest and in `src/Convert/app.manifest` (`<msix>`) must match. For a
  release it is the subject of the signing certificate; `build.ps1 -Setup` builds the app with a copy of
  the manifest that says so.
- `build.ps1 -Install` registers the package with PowerShell. The setup program
  (`installer/Convert.iss`) has no PowerShell to lean on, so the app does it itself:
  `Convert.exe --register-menu` registers the `Convert.msix` next to it with its own folder as the
  external location, and `--unregister-menu` removes it and ends the `dllhost.exe` that holds the files
  (`ShellMenu.cs`).
- `Convert.Shell` is a .NET comhost, so the .NET 10 runtime must be installed.

## The classic menu, for an unsigned Convert

Windows only registers the package when it trusts the signature. When it doesn't, the setup program runs
`Convert.exe --register-classic-menu` (`ClassicMenu.cs`), which writes, for this user only and for every
extension in `MediaKinds.cs`, `HKCU\Software\Classes\SystemFileAssociations\.<ext>\shell\`:

- `Convert` ("Convert…"): `Convert.exe --open "%1"`, the window.
- `ConvertTo` ("Convert to ▸"): a submenu (`SubCommands` and a `shell` key) with the few formats
  `QuickTargets.cs` lists for that kind of file, minus the one it is already in; each runs
  `Convert.exe --quick <id> "%1"`.

Both use `MultiSelectModel=Player`, so a selection of any size is offered, but Explorer starts Convert once
per file. `SelectionGate` (Core) gathers them: the first process creates a named pipe (only it can, through
`FirstPipeInstance`; only this user can connect, through `CurrentUserOnly`) and opens the window; the others
send their paths and end. A quick conversion uses a gate of its own per format, waits a second for the rest of
the selection, converts with the saved settings and closes its window three seconds after a clean finish,
not while the pointer is on it. What it decided goes to `%LOCALAPPDATA%\Convert\convert.log`.
Send to → Convert is a shortcut that runs `Convert.exe --open` with all the files at once.

The uninstaller removes every entry that starts this `Convert.exe` (in Pascal, so it works even when .NET
is gone), and a signed install removes them in favour of the package.

## Where outputs go

"Save to" in the window is next to the original, a chosen folder, or a folder asked for at each
conversion. `OutputFolder.Use(folder)` sets it for the conversion that's running (an `AsyncLocal`, so
nothing else needs to know); `OutputFolder.For` then returns that folder, and an output there doesn't count
as a fallback. A chosen folder that's gone or read-only is an error, never a silent fallback. `--cli` takes
`CONVERT_OUTPUT_DIR`.

When an original and its output are both on the desktop, `DesktopIcons.cs` moves the new icon to the first
free place below the original's, through the desktop's `IFolderView` (found via `IShellWindows`), unless the
desktop arranges its icons by itself.
