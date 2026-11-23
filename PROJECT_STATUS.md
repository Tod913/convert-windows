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
- [ ] **Stage 1: Get the cloud build working on your fork.** You switch on GitHub Actions for your fork
      (one click, instructions given in chat). Claude runs the existing build and tests and reports
      honestly what passes and fails.
- [ ] **Stage 2: First installer you can try.** Claude makes the cloud build produce an installer that
      includes ffmpeg (with its licence), so audio/video work with no extra installs. You download it,
      install it on your PC, and try converting from the Start-menu app by dragging files onto it.
- [ ] **Stage 3: Right-click for everyone (option A).** Add a classic "Convert…" right-click entry and
      a "Send to → Convert" entry, installed and removed by the installer, with batches arriving in one
      window. You test it on your PC.
- [ ] **Stage 4: Choose where files are saved.** Add a setting: next to the original (default), a fixed
      folder you pick, or ask each time. Tests added for it.
- [ ] **Stage 5: Fewer clicks (optional).** A "Convert to ▸ MP3 / MP4 / JPEG …" submenu that converts
      immediately with your saved settings, without opening the window first.
- [ ] **Stage 6: Documents.** Decide how to handle LibreOffice (needed for Word/ODT/RTF): the installer
      offers to download it, or we leave it optional. It's too large (~350 MB) to bundle.
- [ ] **Stage 7: Release installer.** Name, icon, version, a clean `Convert-x.y-setup.exe` published on
      your fork's GitHub "Releases" page; optional code signing (option C).

## 5. Current state

- **Done:** Stage 0.
- **Working on:** waiting for you to switch on GitHub Actions for your fork (start of Stage 1).
- **Known problems / open questions:**
  - Nothing has been run on Windows by us yet; everything in section 1 is from the project's own
    documentation and code.
  - Your choice on the right-click approach (section 3). Default plan: option A.
- **Next step:** Stage 1.
