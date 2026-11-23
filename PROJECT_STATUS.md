# Convert for Windows: project status and roadmap

This file is the project's memory. It records what we found, what we decided, what is done, and what
comes next, so the work can be picked up again in a new conversation. Update it at the end of every stage.

Last updated: 2026-11-23

---

## 1. What we found (investigation, Stage 0)

**The original authors have already built a Windows version.** On 2026-11-19 the original project
(`newtonstudios/convert`) added a complete Windows port in the `windows/` folder. Your fork
(`Tod913/convert-windows`) is an exact copy of the original, so you already have it.

What the existing Windows version already does (according to its own documentation and tests; **not yet
verified by us on a real Windows PC**):

| Your wish | Status in the existing code |
|---|---|
| MP4 → MP3, video ↔ video, audio ↔ audio | Done (uses ffmpeg) |
| Images between formats (JPEG, PNG, HEIC, AVIF, WebP, TIFF, GIF, BMP, PDF) | Done (uses Magick.NET) |
| Documents (Markdown, text, HTML → PDF; Word/ODT/RTF) | Done; Word/ODT/RTF need LibreOffice installed |
| PDF → images / Word / text; fonts | Done |
| Batch conversion | Done (select many files) |
| Keep quality and metadata | Done where the formats allow |
| Customise format and quality | Done ("Advanced" settings, remembered per format) |
| **Customise output location** | **Missing**: output always goes next to the original (or Downloads) |
| Progress, errors, Stop button | Done |
| Right-click menu in Windows 11 Explorer | Done, **but only works when the app is signed with a certificate Windows trusts** (see below) |
| Works on Windows 11 | Built for Windows 11 x64 |
| Standalone .exe installer | Mostly done (Inno Setup). **Gaps:** ffmpeg isn't included automatically; without a signing certificate the right-click menu doesn't appear |

**Licence:** MIT, so we may copy, change and redistribute it, as long as we keep the copyright notice.
ffmpeg (if we bundle it) is GPL/LGPL and needs its licence text shipped alongside, which is normal and easy.

**Technology of the Windows version:** C# on Microsoft .NET 10, with a WPF window. About 12,500 lines,
with automated tests and an automatic build on GitHub's own Windows machines ("GitHub Actions").

## 2. Chosen approach

**Reuse the existing Windows port and finish the remaining gaps**, rather than writing anything new.

Alternatives considered:

- *Port the Mac (Swift) code to Windows directly*: not realistic. The Mac app depends on Apple-only parts
  (SwiftUI, ImageIO, PDFKit, Finder Quick Actions) that don't exist on Windows.
- *Rewrite from scratch (Python, Electron, etc.)*: weeks of work to reach what already exists and is tested.
- *Reuse the existing port* (chosen): already does ~90% of the wish list, same author, same behaviour as
  the Mac app, and it stays easy to pull in future improvements from the original project.

**How we build and test without you installing developer tools:** GitHub provides free Windows computers
in the cloud ("GitHub Actions"). The project already contains instructions for them to build the app,
run the tests, and produce the installer. Claude can't run Windows directly, so these cloud machines
are our test PC. Things only a person can check (the right-click menu, how the window looks) are tested
by you on your own PC, with step-by-step instructions.

## 3. The one big obstacle: the right-click menu and signing

Windows 11's main right-click menu only accepts entries from apps signed with a certificate Windows
trusts. Options:

| Option | Cost | Who gets the right-click menu |
|---|---|---|
| A. Classic menu entry (under "Show more options", or Shift + right-click) and/or "Send to → Convert" | Free | Everyone, unsigned. One extra click |
| B. Self-made certificate trusted only on your PC | Free | Only your own PC |
| C. Real code-signing certificate (e.g. Microsoft's Azure Artifact Signing, roughly US$10/month, needs identity verification) | Paid | Everyone, in the top-level Windows 11 menu, fewer security warnings |

**Recommendation:** do A now (free, works for anyone who installs it), and add C later only if you want
to share the app widely. The existing signed top-level menu stays in the code and switches on
automatically once the app is signed.

## 4. Roadmap

Each stage ends with a test and an update of this file. Changes stay small and inside `windows/` where
possible, so updates from the original project can still be merged in.

- [x] **Stage 0: Investigate** the original project and decide the approach (this document).
- [x] **Stage 1: Get the cloud build working on your fork.** Done 2026-11-23 on the unchanged original
      code (`main`): "Windows" workflow passed (format check, build, unit tests, smoke test: 113
      conversions passed, 0 failed, 20 skipped because LibreOffice isn't installed on the runner).
      "Windows setup" workflow passed (built the installer, installed it, tested it, uninstalled it).
- [x] **Stage 2: First installer you can try.** Cloud part done 2026-11-23: the "Windows setup" workflow
      now bundles ffmpeg 9.0.2 (+ licence and source notice) into `tools\` next to `Convert.exe`; the
      installed app's strict smoke test passed (113 passed, 0 failed, 20 skipped for LibreOffice) using
      the bundled ffmpeg. Installer is 128 MB (was 16 MB). Tested by you 2026-11-23 on Windows 11: installs, opens, MP4 → GIF works.
- [x] **Stage 3: Right-click for everyone (option A).** Cloud part done 2026-11-23. When Windows won't
      register the unsigned package, the installer adds a classic "Convert…" entry for each file type
      (HKCU `SystemFileAssociations\.ext\shell\Convert`, MultiSelectModel=Player) and a Send to
      shortcut, both running `Convert.exe --open`. Explorer starts one process per file;
      `SelectionGate` (Convert.Core) gathers them into one window over a per-user named pipe. Passed:
      format, build, unit tests (incl. new `SelectionGateTests`), smoke test, and the setup workflow's
      new checks (entries exist, 3 files via the shell verb land in one window, removed on uninstall).
      Tested by you 2026-11-23: single file, Shift+right-click, multi-select (one window), Send to all work.
- [x] **Stage 4: Choose where files are saved.** Cloud part done 2026-11-23. "Save to" row in the
      window: same folder as the original (default) / a chosen folder (remembered) / "Choose a folder…" /
      "Ask every time". Stored in settings.json (`saveTo`, `saveFolder`, `lastAskedFolder`). Core:
      `OutputFolder.Use(folder)` (AsyncLocal) for the running conversion; a missing or read-only chosen
      folder is an error, not a fallback. `--cli` honours `CONVERT_OUTPUT_DIR`. Passed: new unit tests,
      smoke test `save-to:` section (121 passed, 0 failed). Tested by you 2026-11-23: all modes work.
- [ ] **Stage 5: Fewer clicks (optional).** A "Convert to ▸ MP3 / MP4 / JPEG …" submenu that converts
      immediately with your saved settings, without opening the window first.
- [ ] **Stage 6: Documents.** Decide how to handle LibreOffice (needed for Word/ODT/RTF): the installer
      offers to download it, or we leave it optional. It's too large (~350 MB) to bundle.
- [ ] **Stage 7: Release installer.** Name, icon, version, a clean `Convert-x.y-setup.exe` published on
      your fork's GitHub "Releases" page; optional code signing (option C).

## 5. Current state

- **Done:** Stages 0 and 1, Stage 2's cloud part. Right-click approach: option A (agreed).
  Stage 2 changes are on branch `claude/charming-gates-ppvnla`, not yet merged into `main`.
- **Known problems / open questions:**
  - Verified only on GitHub's cloud Windows machines, not yet on a real PC with a screen.
  - Word/ODT/RTF conversions untested (need LibreOffice).
  - Installer size: the "full" ffmpeg build is large. A smaller build could be chosen in Stage 7.
- **Next step:** choose between Stage 5, 6, 7.
- **Branches:** `main` = tested work up to Stage 3 (merged 2026-11-23). Stage 4 is on `claude/charming-gates-ppvnla`.
- **Ideas noted:** optional "Open file when done" setting.

### How to re-run the cloud checks
Claude starts them with the GitHub tools ("run workflow" on `windows.yml` / `windows-setup.yml`).
Results: https://github.com/Tod913/convert-windows/actions
