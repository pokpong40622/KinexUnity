# ADR — Live camera coach for all 9 learn-library poses

Date: 2026-08-02
Status: Accepted (implementation in progress)

## Context

`เรียนรู้` (the learn library) holds 9 rehab poses. Exactly one — `hip_abduction` —
has a live camera coach today (`Assets/Scripts/MotionLab/HipAbductionCoach.cs` +
`lib/screens/learn/pose_coach_page.dart`). The other 8 are read-only wizards.

We are extending the coach to all 9. This ADR is the **wire contract**. Unity and
Flutter are implemented independently and MUST agree on it exactly.

## Division of labour (unchanged from the hip_abduction coach)

- **Unity** owns geometry, rep/hold state machines, and decides which single cue is
  active. It emits cue **IDs only** — no Thai anywhere. These runtime-built Unity
  UIs have no Thai font.
- **Flutter** owns every Thai string, all speech (TTS), and all on-screen UI drawn
  over the camera.

## Pose table (authoritative)

| poseId              | mode | sides       | target        | Unity detector to build on          |
|---------------------|------|-------------|---------------|-------------------------------------|
| `sit_to_stand`      | reps | none        | 15 reps       | `SitStandDetector`                  |
| `seated_knee_lift`  | reps | alternating | 20 reps total | `KneeRaiseDetector`                 |
| `hip_abduction`     | reps | per-side    | 10 / side     | existing `HipAbductionCoach` logic  |
| `hip_extension`     | reps | per-side    | 10 / side     | `HipExtensionDetector`              |
| `narrow_base_stand` | hold | phases      | 10 s / phase  | `TiptoeDetector` (heels, then toes) |
| `tandem_stand`      | hold | per-side    | 10 s / side   | new ankle-line geometry             |
| `single_leg_balance`| hold | per-side    | 10 s / side   | `SingleLegStanceDetector`           |
| `tandem_walk`       | reps | none        | 10 steps      | new heel-to-toe step geometry       |
| `side_walk`         | reps | per-side    | 5 steps / side| `LaneDetector`                      |

- `mode: reps` → Flutter shows an `n/target` counter.
- `mode: hold` → Flutter shows a fill ring driven by the `hold` field.
- `sides: per-side` → first side auto-latched, then `switch_side`.
- `sides: alternating` → the user is expected to alternate every rep; a rep on the
  same side twice in a row emits `switch_side` and is not counted.
- `sides: phases` → `narrow_base_stand` only: phase 0 = weight on heels, phase 1 =
  up on toes. `side` carries `"phase0"` / `"phase1"`.

## Messages — Unity → Flutter

Emitted **only when a field changes** (the change filter that keeps Flutter from
being firehosed). All are single-line JSON.

```
{"type":"coach_ready","pose":"tandem_stand","mode":"hold","target":10,"sides":"per-side"}
{"type":"coach","cue":"hold","side":"left","reps":1,"target":10,"hold":0.62,"angle":0}
{"type":"coach_done","reps":20,"target":20}
```

Field rules:
- `coach_ready` is sent once, from `Start()`, BEFORE any `coach` message. Flutter
  uses it to choose the counter vs ring layout. Flutter must still render sanely if
  it never arrives (fall back to `reps`).
- `cue` — one of the ids below. Never empty.
- `side` — `"left"` / `"right"` / `"phase0"` / `"phase1"` / `""` (unsided).
- `reps` — reps completed **on the current side** (or total when unsided).
- `target` — target **for the current side** (or total when unsided).
- `hold` — 0..1 progress of the current hold. Present in `hold` mode only; Flutter
  must tolerate it being absent.
- `angle` — informational integer, may be 0. Flutter must not depend on it.

Flutter parsing stays deliberately lenient: every field has a fallback, and a
malformed payload is ignored rather than thrown.

## Messages — Flutter → Unity

Before loading the scene, unchanged except the pose id is now any of the 9:

```
sendToUnity('SceneRouter', 'SetCoach', '<poseId>')
sendToUnity('SceneRouter', 'LoadGame', 'motionlab')
```

On leaving the coach screen, Flutter clears it: `SetCoach` with `''`.

## Cue ids (authoritative — Unity emits ONLY these)

**Global** (any pose may emit these):

| id             | meaning                                   |
|----------------|-------------------------------------------|
| `get_ready`    | nothing judged yet / warming up           |
| `not_in_frame` | body missing or too low-confidence to judge|
| `stand_tall`   | trunk leaning; straighten up              |
| `hold`         | currently correct — keep it               |
| `steady`       | wobbling too much (hold poses)            |
| `go_slower`    | moving too fast to judge or to be safe    |
| `rep_good`     | one rep / step banked                     |
| `switch_side`  | change to the other leg / direction       |
| `done`         | session finished                          |

**Pose-specific:**

| poseId              | extra cue ids                                                   |
|---------------------|-----------------------------------------------------------------|
| `sit_to_stand`      | `sit_first`, `stand_up`, `sit_down`, `knees_behind_toes`         |
| `seated_knee_lift`  | `lift_knee`, `lift_more`, `lower_slow`                           |
| `hip_abduction`     | `knee_straight`, `lift_more`, `too_high`, `lower_slow`           |
| `hip_extension`     | `extend_more`, `too_high`, `no_arch`, `knee_straight`, `lower_slow` |
| `narrow_base_stand` | `on_heels`, `on_toes`, `phase_done`                              |
| `tandem_stand`      | `feet_in_line`                                                   |
| `single_leg_balance`| `lift_foot`                                                      |
| `tandem_walk`       | `heel_to_toe`, `walk_forward`, `step_good`                       |
| `side_walk`         | `step_side`, `feet_together`, `step_good`                        |

Flutter MUST have a Thai line for every id above. An id with no Thai line falls
back to the `get_ready` text — never blank, never the raw id.

## Implementation notes (added 2026-08-02, after both sides were built)

- **`hold` is strictly linear in time**: `elapsedHoldSeconds / targetSeconds`, clamped,
  computed in one place (`PoseCoachBase.TickHold`). No easing, no quality weighting.
  Flutter relies on this to derive displayed seconds as `round(hold * target)`.
- **Broken hold**: the timer FREEZES for `holdGraceSeconds` (0.75 s), then HARD-RESETS
  to 0. It does not decay. The ring visibly stalls, then snaps to zero.
- `hold` is quantised to 50 steps on the wire so the change filter cannot firehose
  (≤5 messages/sec).
- In `hold` mode, `reps` counts **completed holds**. Flutter draws the ring from
  `hold` and ignores `reps`.

### Reserved-but-never-emitted cues

`knees_behind_toes` (sit_to_stand) and `no_arch` (hip_extension) are **not emitted**.
Both are sagittal-plane faults and are invisible to a head-on camera; emitting them
would be guessing at the user. The ids stay reserved and Flutter keeps Thai lines for
them, so a future side-camera or depth signal can turn them on with no contract change.

### Reduced scope

`tandem_walk` counts steps and checks that the user stays on the line (ankle X spread).
It does **not** verify heel-to-toe contact. Its `heel_to_toe` cue therefore means
"you left the line", not "your foot placement was wrong" — the Thai wording must suit
both readings.

## Consequences

- Adding a pose later = one Unity coach class + one Thai cue block + one table row.
- `tandem_walk` is the least reliable of the nine (gait from a single fixed camera).
  It is built last and may ship as "counts steps, does not judge foot placement".
- Every threshold stays a serialized field. These are educated guesses and have to
  be tuned on-device with a real user.

## Alternatives rejected

- **One Thai string table in Unity.** Rejected: no Thai font in the runtime-built
  Unity UIs, and it would split wording ownership across two repos.
- **One giant coach class with a `switch` on poseId.** Rejected: nine unrelated
  state machines in one file. One class per pose, one shared base for the plumbing
  (confidence gate, cue debounce, change filter, emit).
