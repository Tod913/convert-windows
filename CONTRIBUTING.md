# Contributing to Convert for Windows

Thanks for wanting to help. By taking part you agree to the [Code of Conduct](CODE_OF_CONDUCT.md).

## Ways to help

- **Something broke.** [Open a bug report](../../issues/new/choose). Say what you converted (from which format
  to which), what happened, and which versions of Convert and Windows you use. If the file isn't private,
  attach it: a file that fails is the fastest way to a fix.
- **You miss a format.** [Open a format request](../../issues/new/choose) and say what you'd use it for.
- **You found a security problem.** Don't open a public issue; see the [security policy](SECURITY.md).
- **You want to fix or add something.** Read on.

## Before you write code

Small fixes can go straight to a pull request. For anything bigger, please open an issue first and describe
what you have in mind.

What holds for every change:

- **Originals are never touched.** Convert writes a new file and never overwrites anything. A half-finished
  output is removed when a conversion fails or is stopped.
- **Nothing leaves the PC.** No network code, no analytics, no accounts.
- **Keep it readable.** Comments say why, not what.

## Setting up

You need Windows 11 and the [.NET 10 SDK](https://dotnet.microsoft.com/download)
(`winget install Microsoft.DotNet.SDK.10`), plus ffmpeg for the audio and video tests
(`winget install Gyan.FFmpeg`). Then, in PowerShell:

```powershell
cd windows
.\check.ps1          # formatting, build, unit tests and the conversion smoke test: what CI runs
```

[windows/README.md](windows/README.md) has the details, and [windows/ARCHITECTURE.md](windows/ARCHITECTURE.md)
explains how the code fits together.

## Pull requests

CI (the **Windows** and **Windows setup** workflows) must pass. Say in the pull request what you tried by hand,
especially for anything in the window or the right-click menu, which CI can't see. Update the README when
formats or behaviour change.
