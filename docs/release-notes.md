## Install

**macOS** (Apple Silicon or Intel, macOS 14+). Run this in Terminal:

```sh
curl -fsSL https://raw.githubusercontent.com/OWNER/REPO/main/macos/scripts/install-release.sh | zsh
```

Or download `AI-Usage-macOS.zip`, unzip it, and move **AI Usage** to Applications. The first time you open it, macOS blocks it:
go to System Settings → Privacy & Security and click **Open Anyway** (the app isn't notarized).

**Windows** (10 or 11, x64). Run this in PowerShell:

```powershell
irm https://raw.githubusercontent.com/OWNER/REPO/main/windows/install.ps1 | iex
```

Or download `AI-Usage-Windows-x64.exe` and run it. If SmartScreen appears, click **More info → Run anyway** (the app isn't code-signed).

You need Claude Code and/or the Codex CLI installed and signed in on the same computer. See the README for details.

> AI Usage reads usage from undocumented Anthropic and OpenAI endpoints. They can change without notice, so a release may stop showing data until it's updated.
