# Design system — hybrid Windows 11 + macOS

FileWheel is a Windows application: it keeps the cues that make people feel at home (accent color, icons, font, window buttons), and borrows from macOS its restraint, grouped surfaces and "soft" controls. Everything is defined in `src/FileWheel.App/Ui/Theme.cs`.

## What comes from each system

| Element | Windows 11 | macOS | FileWheel choice |
|---|---|---|---|
| Accent color | System accent | System blue | **Windows accent**, lightened in dark mode |
| Icons | Segoe Fluent Icons | SF Symbols | **Segoe Fluent Icons** |
| Font | Segoe UI Variable | SF Pro | **Segoe UI Variable** (Display for titles) |
| Window buttons | Minimize / Maximize / Close on the right | Traffic lights on the left | **Windows** (don't disturb habits) |
| Window background | Mica | Neutral grey | **macOS neutral grey** (#F5F5F7 / #1F1F21) |
| Settings sidebar | List + accent pill | Tinted bar, rounded selection | **macOS tinted bar + Windows accent pill** |
| Setting groups | "Settings" cards | Rounded groups | **11 px rounded cards**, thin separators between rows |
| Page titles | 28 px Semibold | Large title | **26 px Segoe UI Variable Display** |
| Setting checkboxes | Toggle switch | Toggle switch | **Toggle switch** (label on the left, switch on the right) |
| Short choices | Drop-down list | Segmented control | **Pill segmented control** (2 to 5 choices) |
| Buttons | Flat, 4 px corners | Slightly raised "push" | **7 px corners, light gradient and bottom edge**; flat on hover |
| Primary button | Accent | Accent | **Gradient accent**, white or black text depending on contrast |
| Dialog button order | Primary on the left | Primary on the right | **Primary on the right**, Cancel on the left, everywhere |
| Dialog footer | Tinted band | — | **Tinted band** with separator |
| Keyboard focus ring | Double outline | Accent halo | **3 px accent halo** |
| Fields, lists, scrolling | WPF Fluent theme | — | **WPF Fluent theme** |
| Wheel | — | Frosted glass, soft shadow | **"Glass" disc** (vertical gradient, highlight on the top edge, radial shadow), **active sector in Windows accent** with gradient |

## Palette

| Token | Light | Dark |
|---|---|---|
| Window background | #F5F5F7 | #1F1F21 |
| Sidebar | #EAEAEE | #29292C |
| Surface (cards) | #FFFFFF | #2B2B2E |
| Primary text | #1D1D1F | #F5F5F7 |
| Secondary text | #6E6E73 | #98989F |
| Switch off | #E1E1E6 | #48484D |
| Danger | #D72C1F | #FF453A |
| Accent | Windows accent | Windows accent + 22 % white |

## Rules

- **Automatic light/dark** and accent changes applied **live** (listening to WM_SETTINGCHANGE / WM_DWMCOLORIZATIONCOLORCHANGED), without restarting.
- **No bitmap effect on text**: shadows are separate layers, to keep ClearType smoothing.
- **Radii**: controls 7 px, cards 11 px, switch and segments pill-shaped.
- **Spacing**: page margins 24 px, cards 14–16 px, setting rows ≥ 30 px high.
- **Accessibility**: text contrast on the accent is computed (black text on a light accent), keyboard focus always visible, Esc closes dialogs, Enter confirms (except for a destructive action, where Enter = Cancel).

## Logo and icons (0.2.0)

- **Logo**: "squircle" tile (the shape of macOS icons) in Windows accent blue with a light gradient, white 6-sector wheel whose top sector is light blue (the hovered sector). Below 32 px, the tile fills the square and the wheel gets thicker. Sources: `assets/filewheel.svg`, `assets/logo_gen.py`, `assets/logo_icon.py`.
- **Notification icon**: monochrome like Windows 11 system icons (white on a dark taskbar, black on a light taskbar), drawn on the fly: greyed out when the wheel is disabled, sectors filling up during a task, green / red dot when a task ends.
- **Installer**: side banner with the logo's blue gradient.

## Express wheel (0.2.0)

- Target formats written **in large type** (bold, size ≈ diameter / 16.5), fitted to the sector width.
- Tools and actions: Fluent icon + **short uppercase label** (≈ diameter / 33), can be turned off (*Paramètres › Roue*).
- Sectors as **glass tiles** separated by a thin, more transparent gap; hovered sector in an accent gradient.
- Center at rest: file size (bold) and name.
