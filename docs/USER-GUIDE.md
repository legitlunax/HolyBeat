# User Guide

HolyBeat Live Guard is a real-time audio safety processor. It watches the
signal, classifies it into one of the four acoustic failure modes or the Safe
zone, and applies the matching corrective DSP chain before the sound reaches
your ears.

This guide covers the three routing modes, the preset library, the live
detector, and every configuration group with its parameter ranges.

## 1. What the app does in one minute

1. Pick a routing mode (Player is the default and works immediately).
2. Pick a preset, or let the auto-switching detector choose one for you.
3. Listen. The detector shows which failure mode it hears and why.

## 2. The core idea

The presets are not stylistic preferences. Each one is the corrective chain
measured for a specific failure mode found in the 103-track study:

- **Mute** - sidechain pumping, sub-bass swells, scooped mids
- **Noise** - 2.5 - 6 kHz resonance, distorted guitars, shrill leads
- **Dark Noise** - an unstable, sweeping background floor
- **Dual-Layer Split** - treble detached from bass, "two unmixed songs"
- **Safe / Optimal Zone** - the target state

## 3. The three routing modes

### Player (default)

The built-in player is the only source that is processed. Nothing is installed
into the Windows audio stack, so this mode works on any machine with no
drivers and no administrator rights. Use it to audition presets and to compare
the detector's verdict against what you hear.

### System Monitor

The app captures the system output, analyses it and runs the detector, but the
original audio keeps playing untouched - the processed signal is not sent
anywhere. This is the safe way to find out what the app thinks your system
audio is doing before you commit to re-routing it.

### System-wide

The app captures the system output, processes it, and re-routes the result to
the output device you select. This is the mode that behaves like a full
system-wide processor.

System-wide mode needs a loopback path, which on Windows means a virtual audio
cable such as VB-CABLE. **You install the cable yourself** - the app never
downloads or installs a driver. The usual setup is:

1. Install the virtual cable and make it the default playback device.
2. In the app, set the routing mode to **System-wide**.
3. Set the **input device** to the virtual cable and the **output device** to
   your real speakers or headphones.

## 4. The preset library

| Preset | What it does | Tuned against |
| --- | --- | --- |
| **Safe / Optimal Zone** | The reference state: unified full-spectrum cohesion, rich 250 Hz - 2 kHz mid glue, tight non-pumping bass, steady background frequencies, diffused harmonic warmth instead of narrow treble needles | Survivor, Who's The Dreamer?, Half A Heart, Burn Our Bridges Down |
| **Anti-Mute (Sensory Shutdown Guard)** | Upward expansion fills the kick-triggered volume vacuums of heavy sidechains, while 40 - 120 Hz is tightened so bass hits stay punchy instead of swelling into pressure | King of Hearts, Volcano, Redemption, Maestro |
| **Anti-Noise (Ear Pain Guard)** | Dynamic suppression of the 2.8 - 5.5 kHz pain band, a gentle roll-off above 9 kHz, and the warm tail that closes every harsh transient | Maps, Question, Awake and Alive, Mortals |
| **Dark Noise Stabilizer** | A warm harmonic bed plus tape saturation masks the erratic background filter sweeps, and the bright layer is pulled back so nothing floats away from the vocal | Flutter (Weeb Like Me) |
| **Dual-Layer Guard** | Re-joins a dry high-treble layer with the compressed sub-bass bottom: 30% narrower side signal above 4 kHz, mono bass below 120 Hz, warm mid glue | K/DA - VILLAIN, Build A B*tch |
| **Who's The Dreamer? Warm Tail** | The signature discovery: every noisy transient keeps its bite for 12 ms, then the high band closes to 2.2 kHz and a warm tape saturation plus a short dark room catches the tail | Unlike Pluto - Who's The Dreamer? |
| **Safe Reference (Light)** | Minimal intervention for already balanced material: a light de-harsh and the warm tail only, so the energy of the track survives untouched | The Safe benchmark set |

The first six presets build on the last one: **Safe / Optimal Zone** is the
baseline every other preset starts from, and each failure mode switches on the
extra stages it needs.

## 5. The live detector

The detector analyses the incoming audio and returns one of five verdicts -
Safe, Mute, Noise, Dark Noise or Dual-Layer Split - together with a confidence
value and a list of **reasons**.

### Reading the reasons

| Reason | What it means |
| --- | --- |
| "Sub-bass pressure at 40-80 Hz takes N% of the spectrum." / "Prolonged sub-bass swells risk the Mute (sensory shutdown) response." | Low-end energy is high enough to trigger the Mute response |
| "Mid range (250 Hz - 1.5 kHz) is scooped out by N%." | The mid range that should glue the mix together is missing |
| "Kick-triggered pumping dips of N dB at about R Hz." / "Sidechain volume vacuums are deep enough to trigger the Mute response." | Classic sidechain compression pumping |
| "Narrow-band resonance of N dB inside the 2.5-6 kHz pain band." | The frequency region most likely to cause headaches |
| "Spectral shape keeps moving - background filter sweeps detected." | Dark Noise: the background will not hold still |
| "High-frequency side/mid ratio of R - the bright layer is detached from the bass." | Dual-Layer Split: treble detached from bass |
| "Unified full-spectrum cohesion - inside the Safe / Optimal Zone." | Nothing objectionable detected |

The detector recommends the matching preset for the verdict it reaches. With
**auto-switch profile** enabled the app applies that recommendation by itself;
with it disabled the recommendation is only a suggestion you can accept
manually. The Safe zone wins whenever no failure signature is clearly present,
so a clean track is never "corrected" needlessly.

## 6. Configuration groups

Every preset exposes the same parameter groups. The ranges below are what the
sliders enforce; the defaults are the values measured in the study.

### 6.1 Safety EQ

| Parameter | Range | Default | What it does |
| --- | --- | --- | --- |
| High-pass frequency | 20 - 80 Hz | **35 Hz** | Steep cut of non-musical sub-rumble and pressure swells (4th order) |
| De-harsh band low | 1000 - 8000 Hz | **2800 Hz** | Lower edge of the ear-canal amplification band that causes headaches |
| De-harsh band high | band low + 200 - 12000 Hz | **5500 Hz** | Upper edge of that band |
| De-harsh threshold | around -22 dB (presets -22 to -30) | **-22 dB** | Level above which the dynamic de-harsh engages |
| De-harsh ratio | 2:1 - 4:1 | **3:1** | How hard the resonance is pulled down |
| De-harsh max reduction | up to -6 dB (presets to -7.5 dB) | **-6 dB** | Hard ceiling on the correction |
| De-harsh attack / release | 1.5 - 60 ms | **2 / 60 ms** | Reaction speed of the correction |
| High-shelf gain | -2 to -4 dB | **-3 dB** | Removes digital air and Nightcore aliasing above 9 kHz |
| Mid-glue gain | +1.2 to +3 dB | **+2 dB** | Reconnects bass and vocal layers |
| Mid-glue frequency | 100 - 2000 Hz (tuned 400 - 800 Hz) | **600 Hz** | Centre of the glue |
| Mid-glue Q | 0.8 - 1.2 | **0.9** | Wide and warm, never a honky bell |

### 6.2 Anti-pumping dynamics

| Parameter | Range | Default | What it does |
| --- | --- | --- | --- |
| Expansion band | 40 - 6000 Hz (tuned 100 Hz - 1.5 kHz) | **100 Hz - 1.5 kHz** | Band where the volume floor is lifted |
| Expansion attack | 5 - 15 ms | **8 ms** | How fast a dip is caught |
| Expansion release | 120 - 200 ms | **150 ms** | How gently the floor is restored |
| Expansion slow release | around 1200 ms | **1200 ms** | Keeps longer musical phrases from sagging |
| Expansion max boost | 0 - 6 dB (presets 4.0 - 5.5 dB) | **4.5 dB** | Ceiling of the upward correction |
| Expansion sensitivity | 0 - 1 | **0.5** | How close the level has to fall to trigger it |
| Sub band | 20 - 400 Hz (tuned 40 - 120 Hz) | **40 - 120 Hz** | Band of the sub-bass envelope follower |
| Sub attack | around 10 ms | **10 ms** | Keeps bass hits punchy |
| Sub release | 60 - 80 ms | **70 ms** | Stops bass from lingering as pressure swells |
| Sub ratio / threshold | 3:1, around -24 to -26 dB | **3:1 / -24 dB** | How hard the sub band is tightened |
| Sub max reduction | 0 - 8 dB | **-6 dB** | Ceiling of the sub correction |

### 6.3 Stereo field

| Parameter | Range | Default | What it does |
| --- | --- | --- | --- |
| Mono bass frequency | 60 - 240 Hz | **120 Hz** | Everything below is summed to mono, removing disorienting stereo sub-bass |
| Narrowing frequency | 2000 - 9000 Hz | **4000 Hz** | Where the stereo image starts to be narrowed |
| High-frequency width | 0.2 - 1.0 | **0.7** | 1.0 is untouched, 0.7 is the 30% narrowing used by Dual-Layer Guard |
| Warm bed level | around -18 to -15 dB | **-18 dB** | Level of the warm harmonic bed that masks erratic sweeps (250 Hz - 1.2 kHz) |

### 6.4 Warm tail and texture

| Parameter | Range | Default | What it does |
| --- | --- | --- | --- |
| Warm tail hold | 10 - 14 ms | **12 ms** | How long the transient keeps its full brightness |
| Warm tail close | 14 - 18 ms | **15 ms** | How long the high band takes to close |
| Warm tail reopen | around 120 ms | **120 ms** | How long the band stays closed before the next hit |
| Warm tail closed frequency | 1200 - 5000 Hz | **2200 Hz** | Where the high band settles during the tail |
| Warm tail threshold | around -34 dB | **-34 dB** | Level above which the tail filter engages |
| Warm tail onset ratio | around 1.8 | **1.8** | How much of a sudden transient is needed to trigger it |
| Saturation drive | 1.0 - 4.0 | **1.35** | Amount of tape-style soft clipping |
| Saturation mix | 0 - 1 | **0.6** | Blend between the clean and the saturated signal |
| Room mix | 0 - 0.4 | **0.08** | Level of the short dark room that grounds the tail |
| Room decay | around 120 - 130 ms | **120 ms** | Length of that room |

### 6.5 Loudness and headroom

| Parameter | Range | Default | What it does |
| --- | --- | --- | --- |
| Target loudness | research target -13 to -11 LUFS (slider -18 to -8) | **-12 LUFS** | Integrated loudness the output is corrected towards, leaving 6 - 9 dB of natural crest factor |
| True-peak ceiling | -3.0 to -0.1 dBTP | **-1.0 dBTP** | Hard ceiling so the master never bricks |
| Output gain | -12 to +12 dB | **0 dB** | Trim after processing |
| Bypass | on / off | **off** | Passes the signal through untouched while you compare |

### 6.6 Routing

| Setting | Options | Default |
| --- | --- | --- |
| Mode | Player / System Monitor / System-wide | **Player** |
| Input device | any capture endpoint | none selected |
| Output device | any playback endpoint | system default |
| Master volume | 0 - 100% | **85%** |
| Prefer virtual cable | on / off | **on** (stored in config.json) |

### 6.7 Interface

| Setting | Where | Options | Default |
| --- | --- | --- | --- |
| Auto-switch profile | Left panel | on / off | **off** |
| Start with Windows | Left panel | on / off | **off** |
| Bypass (A/B) | Header bar | on / off | **off** |
| Close to tray | Stored in config.json | on / off | **on** |

The window itself is a single three column layout: the preset list on the left,
the live spectrum, meters and detector in the middle, and the full parameter
configuration on the right. The routing group sits along the bottom bar.

## 7. Keyboard-free workflow

Everything is reachable with the mouse alone, so you can keep your hands on the
game or the keyboard while the app runs:

1. **Launch** from the Start Menu or the Desktop shortcut.
2. **Choose a mode** in the routing group - Player is the safe default.
3. **Pick a preset** from the preset list. The active preset is highlighted.
4. **Read the detector panel** to see the current verdict, the confidence and
   the reasons behind it.
5. **Adjust a group** by dragging a slider; the current value is shown beside
   it. Changes are applied immediately and are audible straight away.
6. **Compare** by enabling Bypass and toggling back.
7. **Let it run.** The app can stay in the tray and keep processing while you
   work; closing the window keeps it running.

## 8. Troubleshooting

| Symptom | What to check |
| --- | --- |
| No sound | The output device selector must point at the device you are listening on; check that Bypass is off and that Windows is not muting the device |
| System-wide mode is silent | System-wide needs a virtual cable you installed yourself, set as the input device |
| The detector always says Safe | System Monitor analyses the system audio but leaves the playback untouched - switch to System-wide to hear the correction |
| The app window vanished | It went to the tray; it can also be closed to the tray by default |
| Sound is dull | The de-harsh is working hard on a bright track - try Safe Reference (Light) or raise the de-harsh threshold |
| Bass feels thin | The sub band has been tightened; reduce the sub max reduction or lower the mono-bass frequency |
| The app stutters | Close other audio tools, and prefer the default 48 kHz shared-mode device |

## 9. Privacy

Everything runs locally on your machine. HolyBeat Live Guard makes no network
calls, sends no telemetry and has no account, no update check and no cloud
component. The only file it writes is `%APPDATA%\HolyBeat\config.json`,
which holds your preset, routing and interface settings. Delete that file to
return the app to its factory defaults.
