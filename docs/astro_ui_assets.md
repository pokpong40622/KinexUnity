# AstroStance UI — image-asset contract

Single source of truth for the sprite-based UI redesign. **Both work streams build to this file.**
Stream A renders the PNGs; Stream B writes the Unity code that consumes them. Neither may change
this contract unilaterally — if something here is wrong, say so instead of improvising.

## Goal

Replace the procedurally-drawn Unity UI (flat `Image` + `Round()` colour fills) with designed PNG
sprites, so AstroStance reads as a polished commercial rehab game rather than editor primitives.

## Canvas

- Reference resolution **927 × 1427**, portrait.
- Assets are authored at **~2× density** for crispness, imported at `pixelsPerUnit = 200`.
- Everything transparent (`--default-background-color=00000000` / `omitBackground`).

## Art direction — "sunny park", bright clinical-friendly

Not neon, not sci-fi. Soft, rounded, optimistic. Think a well-made mobile health app.

| role | hex | use |
| --- | --- | --- |
| Cream | `#F0F7E6` | panel/card fill |
| Leaf green | `#8CB866` | card rim, secondary accent |
| Green | `#52B35C` | primary button, success |
| Sky blue | `#4A9EEB` | secondary accent, info |
| Gold | `#FFC233` | stars, treasure, score |
| Coral | `#F27357` | warnings, meteor/dodge |
| Ink | `#24331F` | primary text |
| Plum | `#52337A` | tertiary accent text |

Rules:
- Corner radius ≈ **28px** at 2× (so ~14 in canvas units) on cards; buttons are full pills.
- **Soft** drop shadow only: `0 6px 0` solid darker rim (a "candy" bottom edge) on buttons,
  plus `0 8px 24px rgba(36,51,31,0.18)` ambient on cards. No harsh black.
- Subtle vertical gradient on filled buttons (lighter top → base at bottom). No neon glow.
- 2px inner light rim on cards for a soft embossed edge.
- No text baked into any sprite — all copy stays TMP text in Unity (Thai fonts already wired).

## Asset list

9-slice borders are `left, bottom, right, top` in **source px**. `border 0` = plain sprite.

| file | size px | border | description |
| --- | --- | --- | --- |
| `card_cream.png` | 192×192 | 56,56,56,56 | Main rounded card: cream fill, 3px leaf-green rim, soft ambient shadow. |
| `card_glass.png` | 192×192 | 56,56,56,56 | Translucent white card (~92% white) w/ leaf rim — HUD chips + feed frame backing. |
| `btn_primary.png` | 384×128 | 60,52,60,52 | Green pill, vertical gradient `#6CC96F`→`#52B35C`, 6px darker `#3E8F47` bottom edge. |
| `btn_primary_down.png` | 384×128 | 60,52,60,52 | Pressed: no bottom edge, slightly darker fill, content nudges down 6px. |
| `btn_secondary.png` | 384×128 | 60,52,60,52 | Cream pill w/ 3px leaf-green outline, 6px `#C9D8B4` bottom edge. |
| `btn_round.png` | 128×128 | 0 | Circular cream button w/ leaf rim + bottom edge (preview toggle). |
| `chip.png` | 192×96 | 44,40,44,40 | Small pill, translucent white, leaf rim — score/timer chips. |
| `toast.png` | 256×96 | 44,40,44,40 | Toast pill, solid white 96%, soft shadow — tinted per-message in code. |
| `star_lit.png` | 128×128 | 0 | Gold 5-point star, rounded points, soft inner highlight. |
| `star_dim.png` | 128×128 | 0 | Same star, desaturated `#C9D2C0` at ~35% opacity. |
| `dot_on.png` | 64×64 | 0 | Lane dot active: gold fill + white rim. |
| `dot_off.png` | 64×64 | 0 | Lane dot inactive: `#C9D2C0` fill, no rim. |
| `ring_track.png` | 256×256 | 0 | Timer ring TRACK: full 360° annulus, `#24331F` @ 18%, stroke ~18px. |
| `ring_fill.png` | 256×256 | 0 | Timer ring FILL: identical geometry, gold. Unity sets `Image.type = Filled`, `fillMethod = Radial360`, `fillOrigin = Top`, clockwise. **Geometry must match `ring_track.png` exactly** or the fill will not align. |
| `feed_frame.png` | 192×192 | 56,56,56,56 | Rounded frame for the camera preview: transparent centre, 4px leaf rim, soft shadow. Drawn OVER the RawImage. |
| `icon_treasure.png` | 128×128 | 0 | Gold treasure chest. |
| `icon_kick.png` | 128×128 | 0 | Sky-blue kicking-leg pictogram. |
| `icon_dodge.png` | 128×128 | 0 | Coral side-step/dodge arrows. |
| `icon_sit.png` | 128×128 | 0 | Green sit-to-stand chair pictogram. |
| `icon_step.png` | 128×128 | 0 | Plum left/right footstep pictogram. |

Icons: flat, 2-tone max, thick rounded strokes (~10px at 2×), readable at 48px on screen.

## Output location

`Assets/AstroStance/UI/<name>.png`

Plus `Assets/AstroStance/UI/manifest.json`:

```json
{ "pixelsPerUnit": 200,
  "sprites": [ { "file": "card_cream.png", "border": [56,56,56,56] } ] }
```

`border` is `[left, bottom, right, top]`; `[0,0,0,0]` for plain sprites.

## Unity import rules (Stream B)

- `TextureImporterType.Sprite`, `spriteMode = Single`, `alphaIsTransparency = true`,
  `mipmapEnabled = false`, `wrapMode = Clamp`, `filterMode = Bilinear`,
  `textureCompression = Uncompressed` (UI crispness), `spritePixelsPerUnit = 200`.
- `spriteBorder = new Vector4(left, bottom, right, top)` from the manifest.
- 9-sliced sprites are used with `Image.type = Sliced`. Never `Simple` — it will stretch the corners.

## Invariants — do not break

- Panel GameObject names in `PanelNames` stay **identical** (the builder destroys/rebuilds by name,
  and `AstroStanceSelfTest` asserts them):
  `IntroPanel, FramingPanel, HudPanel, CalibGroup, PausePanel, ResultsPanel, CameraFeedPanel, PreviewToggleButton`
- `CalibGroup` stays a **canvas-level sibling** of `HudPanel`, never nested inside it.
- The scene must keep **exactly one `RawImage`** (the camera feed) — `MediaPipePoseDetector.Start()`
  binds via `FindAnyObjectByType<RawImage>()`. Use `Image` (sprites) for everything else.
- `CameraFeedPanel` stays active at build (the detector finds its RawImage at `Start`).
- All existing director field wiring (`AssignGo`/`Assign`/`AssignArray`) is preserved.
- Thai copy and fonts are unchanged (`FCIconic-Black/SemiBold SDF`, `Montserrat-Black SDF`).
