# Design spec (shared by macOS and Windows)

Both apps must look the same. The macOS app is the reference; Windows copies it, Mac look included.
Change a value here and in both apps together.

## Colours

| Token | Dark | Light |
|---|---|---|
| Green bar (0 – 60%) | `#30D158` | `#34C759` |
| Yellow bar (60 – 75%) | `#FFD60A` | `#FFCC00` |
| Red bar (75 – 100%) | `#FF453A` | `#FF3B30` |
| % label, green | primary text | primary text |
| % label, yellow | `#FFD60A` | `#B38000` |
| % label, red | `#FF453A` | `#FF3B30` |
| Primary text | white 85% | black 85% |
| Secondary text | white 55% | black 50% |
| Tertiary text | white 25% | black 26% |
| Bar track | primary at 10% | primary at 10% |
| Threshold ticks (60%, 75%) | black 28%, 1 px | black 28%, 1 px |
| Divider | white 10% | black 10% |
| Dashboard surface | macOS menu material (Windows: `#323236` at 97%) | menu material (Windows: `#F2F2F4` at 97%) |
| Widget surface | Liquid Glass, black tint 32%, always dark (Windows: `#232326` at 92%) | — |
| Hairline edge | white 14% | black 10% |

Claude badge: `#D97857` square, white `✳`. Codex badge: white square, black `❯_`, black 15% hairline.

## Type

System font: SF Pro on macOS, Segoe UI Variable on Windows. Tabular digits wherever numbers change.

| Use | Size | Weight |
|---|---|---|
| Provider name | 12 | semibold |
| Row label ("5 hours", "Weekly") | 12 | regular, secondary |
| Row % | 13 | semibold |
| Reset line | 11 | regular, tertiary |
| Footer "Updated 11:53" | 11 | regular, tertiary |
| Small widget big % | 30 | bold, tracking −0.6 |
| Widget captions | 10.5 | regular |

## Geometry

- **Menu-bar / tray icon:** two bars, Claude on top and Codex below. Each 18 × 4 pt (16 × 4 px on Windows at 100%), 3 pt apart, fully rounded, filled to the 5-hour %.
- **Dashboard:** 300 wide, padding 10, corner radius 12. Rows: label column 56, gap 8, bar (6 tall), % column 40 right-aligned.
  The reset line is indented to the bar (4 + 56 + 8). Sections are separated by dividers, and the footer holds "Updated", ↻ and ⋯.
- **Medium widget:** 360 × 170, radius 22, padding 16 × 14, two columns 18 apart.
- **Small widget:** 170 × 170, radius 22.
- **Bars:** capsule, minimum visible fill = bar height, ticks at 60% and 75%.

## Text

English only. All strings come from the shared formatting code and are pinned by `fixtures/expectations.json`:
`Resets 14:30 (3h 06m)`, `Resets Thu 8 Oct, 10:00`, `Weekly · Sat 11:04`, `Codex · 5-hour limit at 75%`, and so on.

## Motion

Bar fills animate with a critically damped spring (macOS) or 0.35 s ease-out (Windows), and both respect reduced motion.
The dashboard fades in over 0.12 s and out over 0.1 s.
