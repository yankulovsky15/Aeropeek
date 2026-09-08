# Aeropeek

A Windows tuning tool for CS2 that **measures instead of promising**.

Most "optimization guides" are never verified. Aeropeek starts from the opposite
premise: it tells you when a tweak does nothing, it records everything it
changes along with the exact prior state, and it can put all of it back.

## What it does

- **Diagnostics** — a dozen checks on your machine: power plan, video output,
  graphics driver age, CPU topology, overlays, hypervisor. Changes nothing.
- **Tweaks** — a catalogue of registry changes, each with its expected gain, what
  it costs you, and whether it is already applied on this machine.
- **Match Mode** — suspends the services and closes the applications that can
  interrupt a game, then puts everything back when you're done.
- **Cleanup** — shader caches, temporary files, crash reports.
- **Benchmark** — captures frame times through PresentMon and compares two runs
  to tell you whether a tweak actually did anything.
- **DNS**, **Utilities**, **System Restore**.

## What it will not do

These are hard limits written into the code, not just guidelines:

- it does not disable your antivirus or any security service;
- it does not touch Windows Update;
- it deletes no system file;
- it does not touch anti-cheat services (FACEIT, Vanguard, EAC, BattlEye);
- it sends nothing over the internet and collects no data. The only outbound
  connections are optional and on request: checking the latest NVIDIA driver
  version, and downloading a third-party utility if you ask for one.

Everything it writes stays in `%LOCALAPPDATA%\Aeropeek`.

## Undoing

Every change is journalled **before** it is applied, together with the previous
value and whether that value existed at all. The Restore tab puts things back,
one tweak at a time or all at once. If the app closes without finishing a
session cleanly, it notices on the next launch and offers to roll back whatever
was left applied.

A setting changed **before** Aeropeek ever saw it cannot be undone: the program
only restores what it wrote itself, and it says so plainly rather than inventing
a default value.

## Requirements

- Windows 10 build 17763 or newer — Windows 11 recommended
- [.NET 10 SDK](https://dotnet.microsoft.com/download) to build from source
- Administrator rights at runtime: the program reads and writes system settings,
  and its manifest requires elevation.

Prefer not to build it yourself? Grab the ready-to-run archive from
[Releases](https://github.com/yankulovsky15/Aeropeek/releases) — the .NET runtime
is bundled, so there is nothing to install.

## Building and running

```
git clone https://github.com/yankulovsky15/Aeropeek.git
cd Aeropeek
dotnet run -c Release
```

To produce a distributable, self-contained build:

```
dotnet publish -c Release -r win-x64 --self-contained true -o dist
```

## Adding a tweak

The catalogue is plain JSON, [`catalogue.json`](catalogue.json) — no rebuild
needed to add one:

```json
{
  "id": "my-tweak",
  "nom": "Title shown in the UI",
  "explication": "What it does, in one sentence.",
  "consequence": "What the user gives up in exchange.",
  "gain": "up to +8% on 1% lows",
  "categorie": "performance",
  "redemarrage": false,
  "applicable": { "buildMin": 19041, "gpu": "nvidia", "portable": "non" },
  "operations": [
    {
      "hive": "HKCU",
      "cle": "Software\\Example",
      "valeur": "ValueName",
      "type": "dword",
      "vers": 0
    }
  ]
}
```

> The JSON field names are French for now — the codebase is being translated to
> English, and renaming them would break every existing catalogue in the wild.
> They will be migrated with a compatibility shim in a later version.

`applicable` is optional. Each of its fields hides the tweak — showing the reason
why — on machines it does not concern: `buildMin` and `buildMax` for the Windows
version, `gpu` for the graphics card vendor, `portable: "non"` to exclude
laptops.

Some registry paths are refused no matter what the catalogue says: Windows
Defender, the security services, `SAM`, `SECURITY` and `Control\Lsa`. A tweak
targeting them shows up as "Blocked".

## A word of warning

This program changes system settings. It journals everything and knows how to
walk it back, but no tool replaces a backup. Create a restore point before your
first run — the Restore tab will do it for you.

## License

[Apache-2.0](LICENSE). See [NOTICE](NOTICE) and
[THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt) for third-party components.

Aeropeek redistributes [Intel PresentMon](https://github.com/GameTechDev/PresentMon)
(MIT licence) for frame-time capture.
