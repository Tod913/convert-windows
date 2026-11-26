# Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by
[SignPath Foundation](https://signpath.org).

> Status: the application to SignPath Foundation is pending. Until it is approved, releases are not signed.

## What gets signed

Only the files this repository builds: `Convert.exe` and its own libraries, the package that adds Convert to
Explorer's menu, and the setup program with its uninstaller. They are built by GitHub Actions from the source in
this repository (`.github/workflows/windows-setup.yml`), and only those builds are sent for signing; nothing
built on a personal computer is.

Third-party files that are shipped unchanged (ffmpeg, .NET libraries, Magick.NET) are not signed by this project.

## Team roles

- Committers and reviewers: [Tod913](https://github.com/Tod913)
- Approvers (decide that a release is signed): [Tod913](https://github.com/Tod913)

Every team member uses two-factor authentication on GitHub and SignPath.

## Privacy policy

This program will not transfer any information to other networked systems unless specifically requested by the
user or the person installing or operating it.

The one network connection is made by the **setup program**, and only when the PC doesn't have Microsoft's
.NET 10 Desktop Runtime: it then downloads Microsoft's installer for it from `builds.dotnet.microsoft.com` and
checks it against a fixed SHA-256 fingerprint before running it. Convert itself has no network code.
