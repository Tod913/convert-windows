# Security Policy

## Supported versions

Only the [latest release](../../releases/latest) gets fixes. If you're on an older version, please update first
and check whether the problem is still there.

## Reporting a vulnerability

**Please don't open a public issue for a security problem.** Report it privately instead: go to this
repository's **Security** tab and click **Report a vulnerability**. Only the maintainers can read it.

It helps to include the versions of Convert and Windows, what you did step by step, the file that triggers the
problem (if it isn't private), and what you think someone could do with it.

## What counts

Convert opens files that may come from anywhere, so the interesting problems are the ones a file can cause.
For example, a file that makes Convert:

- write, overwrite or delete something outside the folder it's meant to write to
- run a command or a program it shouldn't
- reach the network
- crash, or use up all memory or disk space

Problems in ffmpeg, LibreOffice, Magick.NET or WebView2 themselves are best reported to those projects; tell us
too if Convert should guard against them.
