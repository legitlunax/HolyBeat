# HolyBeat Live Guard

A real-time acoustic safety processor for Windows.

HolyBeat Live Guard sits between your audio sources and your ears and applies a
deterministic DSP chain in the same spirit as DTS Sound Unbound or Dolby Atmos
for Headphones - it is an independent project and is not affiliated with,
derived from, or endorsed by either of them.

The rules are not taste. They come from a 103-track acoustic study that
classified every track as one of four physically unpleasant failure modes (or
as safe), measured what caused the problem, and derived the corrective DSP from
the measurements.

## The four acoustic failure modes

| Mode | What you feel | Signal cause |
| --- | --- | --- |
| **Mute** | Sensory shutdown, auditory gain lockup | Aggressive sidechain compression (kick-triggered volume pumping), prolonged 40-80 Hz sub-bass swells, scooped 250 Hz - 1.5 kHz mid range |
| **Noise** | Tension, vascular headaches, ear fatigue | Narrow-band harmonic spikes at 2.5 - 6 kHz, distorted guitars and cymbals, clipped bass boosts, shrill sawtooth leads |
| **Dark Noise** | Frequency disorientation, unstable background | Rapid spectral modulation: sweeping low-pass and band-pass filters, LFO pitch warble, a background floor that will not hold still |
| **Dual-Layer Split** | Headache *and* sensory shutdown at once ("two unmixed songs") | Extreme mid-side widening plus a scooped mid range, detaching a dry treble vocal layer from a booming sub-bass layer |

The **Safe / Optimal Zone** is the target state: unified full-spectrum cohesion
with a rich 250 Hz - 2 kHz mid glue, tight non-pumping bass, steady background
frequencies, and diffused harmonic warmth instead of narrow treble needles.

## The six-stage DSP chain

| Stage | Purpose | Live? |
| --- | --- | --- |
| 1. Pitch and time-stretch normalization | Restore Nightcore-shifted formants, calm frantic tempos | Offline only - a live processor cannot slow a track down |
| 2. Surgical EQ and de-harshing | 35 Hz high-pass, dynamic 2.8 - 5.5 kHz de-harsh, -3 dB shelf above 9 kHz, +2 dB mid glue at 400 - 800 Hz | Yes |
| 3. Anti-pumping restoration | Upward expansion over 100 Hz - 1.5 kHz, sub tightening over 40 - 120 Hz | Yes |
| 4. Spectral stabilization and stereo glue | Mono bass below 120 Hz, 20 - 30% narrowing above 4 kHz, warm harmonic bed | Yes |
| 5. Warm tail and vocal anchor | 12 ms transient hold then close to 2.2 kHz over 15 ms, tape saturation, short dark room | Yes |
| 6. Healthy headroom mastering | -13 to -11 LUFS target, -1.0 dBTP true-peak ceiling | Yes |

Stage 5 is the signature finding: in *Who's The Dreamer?* every noisy transient
keeps its bite for a moment and then closes down to a warm low-mid tail. The
live filter reproduces that envelope so the material stays energetic without
the sting.

## Quick start

```powershell
# 1. build and package (needs the .NET 10 SDK)
powershell -ExecutionPolicy Bypass -File .\build.ps1

# 2. install for the current user (no admin rights, no driver)
powershell -ExecutionPolicy Bypass -File .\install\install.ps1

# 3. run it
& "$env:LOCALAPPDATA\Programs\HolyBeatLiveGuard\HolyBeat.LiveGuard.exe"
```

Add `-EnableAutoStart` to the install command to have the app start with
Windows. Use `install\uninstall.ps1` to remove it again.

The app works in **Player** mode straight away: nothing is installed into the
Windows audio stack and no administrator rights are needed.

## Project layout

```
E:\HolyBeat\
  build.ps1                     build + test + publish + package
  HolyBeat.slnx                 solution
  src\
    HolyBeat.LiveGuard.Core\    DSP core: profiles, detector, DSP chain
    HolyBeat.LiveGuard.App\     WPF application (HolyBeat.LiveGuard.exe)
  tests\
    HolyBeat.LiveGuard.Tests\   xUnit test project
  install\
    install.ps1                  per-user installer
    uninstall.ps1                per-user uninstaller
  docs\
    INSTALL.md                   installation and troubleshooting
    USER-GUIDE.md                how to use the app
  dist\                         created by build.ps1 (publish output and zip)
```

## Requirements

- Windows 10 / 11 (x64 or arm64)
- .NET 10 SDK to build; the published app is self-contained and needs no runtime
- About 250 MB of free space for the published output

## Where the settings live

`%APPDATA%\HolyBeat\config.json` is created on first launch and holds the
active preset, routing and UI settings. Delete the file to reset the app to
its factory defaults.

## Documentation

- [docs/INSTALL.md](docs/INSTALL.md) - build, install, uninstall, troubleshooting
- [docs/USER-GUIDE.md](docs/USER-GUIDE.md) - routing modes, presets, detector, every parameter

## Privacy

Everything runs locally. The app makes no network calls, collects no telemetry
and sends nothing to a server. The only file it writes is the configuration
JSON in your user profile.

## License

HolyBeat is published under the custom source-available terms in [LICENSE](LICENSE).
The source may be inspected and used for personal, non-commercial purposes.
Commercial sale and other commercial activity are reserved to the copyright
holder and require prior written permission and a USD $1,000,000 license fee.
This is **not an open-source license**: it does not grant general commercial
use or resale rights. Copyright ownership is not transferred by access to this
public repository. Third-party materials, if any, remain under their own terms.

The local build output and reference audio are not included in this repository.
