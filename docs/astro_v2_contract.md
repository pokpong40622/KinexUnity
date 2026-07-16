# AstroStance v2 — shared contract (LOGIC agent ↔ UI agent)

Two Sonnet subagents build to THIS contract on NON-overlapping files. Neither runs Unity/MCP or the
scene builder — they only write + self-review C#. The orchestrator (Claude) does ALL Unity verification
(compile, feed-injection swap test, `Kinex/Build AstroStance Scene`, screenshots, device build).

## File ownership (do NOT edit outside your set)
- **LOGIC agent** owns: `Assets/Scripts/AstroStance/AstroStanceDirector.cs`,
  `Assets/Scripts/AstroStance/AstroSpawner.cs`, a NEW `Assets/Scripts/AstroStance/AstroDifficulty.cs`,
  `Assets/Scripts/MediaPipePoseDetector.cs` (careful — shared by all games), and ONLY the
  `ConfigureDetector(...)` method of `Assets/Editor/AstroStanceSceneBuilder.cs`.
- **UI agent** owns: `Assets/Editor/AstroStanceUIBuilder.cs`, `Assets/Editor/AstroUISpriteImporter.cs`,
  and PNG assets under `Assets/AstroStance/UI/`.
- Neither touches the other's files. If you think you need to, STOP and note it in your final report.

## Contract 1 — Difficulty (LOGIC declares, UI calls)
- LOGIC: `public enum AstroDifficulty { Easy, Normal, Hard }` (in AstroDifficulty.cs, namespace `Kinex.AstroStance`).
- LOGIC on `AstroStanceDirector`:
  - `public void SetDifficulty(AstroDifficulty d)` — stores it; safe to call anytime (e.g. on the intro
    screen); applied when a run starts. Default = `Normal`.
  - `public AstroDifficulty CurrentDifficulty { get; }` — for UI selected-state highlight.
- UI: three buttons on the start page (Easy / Normal / Hard, Thai labels ง่าย / ปกติ / ยาก). Each calls
  `director.SetDifficulty(...)` and highlights the chosen one; Normal highlighted by default.

## Contract 2 — Body-loss warning (LOGIC owns state, UI provides elements)
- LOGIC on `AstroStanceDirector`:
  - `public UnityEngine.UI.Button bodyLostIgnoreButton;`  — assigned by UI (button on the lock/framing
    overlay). LOGIC controls its visibility (`.gameObject.SetActive`), hidden by default; only shown on a
    MID-GAME body-loss lock, NEVER on the initial pre-Start framing.
  - `public GameObject bodyLostToast;` — a small warning popup (Thai: "ขยับให้เห็นทั้งตัว"), assigned by UI,
    hidden by default. LOGIC shows it ~3s when body is lost AFTER the player pressed ignore.
  - `public void OnBodyLostIgnorePressed();` — UI wires `bodyLostIgnoreButton.onClick` → this (via
    `UnityEditor.Events.UnityEventTools.AddPersistentListener`, same as other buttons). Sets an internal
    `_bodyWarnIgnored=true` session flag, hides the lock, resumes play.
  - Behavior: during Play/Countdown when body is lost past the existing threshold —
    - if `!_bodyWarnIgnored` → do the CURRENT lock (bounce to Framing) but with the Ignore button visible;
    - if `_bodyWarnIgnored` → do NOT bounce; show `bodyLostToast` for ~3s (throttled), keep playing.
- UI just creates + assigns `bodyLostIgnoreButton` (on the framing/lock panel, hidden) and `bodyLostToast`
  (hidden), and wires the onClick. LOGIC drives all visibility/timing.

## Contract 3 — Start-page art (UI only)
Copy these 6 PNGs from `C:\Users\Admin\Desktop\AstroStanceTestFeed\GameStartUI` into `Assets/AstroStance/UI/`
and import as Sprite (keep the null-safe `Ui(name)` loader working):
- `astrostancestartpagebackground.png` → full-bleed intro background (stretch to fill).
- `astrostancelogo.png` → logo near the top (replaces the text title + star).
- `sidewalkcard.png`, `sitcard.png`, `kickcard.png` → the three how-to cards (replace the procedural cards).
- `startbutton.png` → the Start ("เริ่มภารกิจ") button graphic.
Keep the existing `Ui()` fallback so a missing PNG degrades gracefully, not crash.

## Conventions (both)
- Namespaces `Kinex.*`, one class per file, Karpathy simplicity (fewest layers), match surrounding style.
- Null-safe everywhere (the UI builder already warns-once + falls back; keep that).
- Do NOT run `Kinex/Build AstroStance Scene`, do NOT save scenes, do NOT call Unity MCP. Just write code.
