# KINEX — Change Log

Each version is a safe restore point. To revert, tell Claude: "revert to v[X]" and I'll undo the changes listed in that version.

---

## v4 — 2026-05-31
### Camera aspect ratio fix (stretch → correct proportions)
**Files changed:** `Assets/Scripts/PoseDetector.cs`
- Added fields: `rawImage`, `cropScale`, `cropOffset`, `cropReady`
- Added `ComputeCrop()` method — detects webcam aspect ratio, computes center-crop UV rect
- `Graphics.Blit` now uses `cropScale/cropOffset` so inference input is correctly proportioned
- `rawImage.uvRect` is set so the preview also shows correct proportions (no slim/stretched look)
- Fixed deprecation warnings: `FindFirstObjectByType` → `FindAnyObjectByType`

---

## v3 — 2026-05-31
### Head/neck tracking added
**Files changed:** `Assets/Scripts/PoseDetector.cs`
- Added keypoint constants: `NOSE = 0`, `L_EAR = 3`, `R_EAR = 4`
- Added `TryDriveMid()` helper (drives bone from average of two keypoints → one target keypoint)
- Added `Neck` bone driven by shoulder-midpoint → nose direction (nod + tilt, no yaw)
- Added `CacheDir(Neck, Head)` in `CacheTpose()` so T-pose direction is cached for neck

---

## v2 — 2026-05-31
### Compile error fix + PoseDetector wired to scene
**Files changed:** `Assets/Scripts/PoseDetector.cs`, Scene `SampleScene`
- Fixed CS0136: renamed inner loop variable `conf` → `detConf` (line 79)
- Added `PoseDetector` component to `KinexUserModel` GameObject
- Assigned `Assets/Models/yolov8n-pose.onnx` to `PoseDetector.modelAsset` field
- Scene saved

---

## v1 — [Previous session]
### Namespace + API migration (Sentis → InferenceEngine)
**Files changed:** `Assets/Scripts/PoseDetector.cs`
- `using Unity.Sentis` → `using Unity.InferenceEngine`
- `ModelAsset`, `Worker`, `BackendType` — all now from `Unity.InferenceEngine`
- `output.MakeReadable()` removed → `tensor.ReadbackAndClone()` for sync CPU read
- `TextureConverter.ToTensor(texture, w, h, ch)` signature unchanged

---

## v0 — [Session 1] — Initial project setup
### What exists at baseline
- Unity 6 project at `D:\Unity project\Kinex`
- Portrait 9:16 Game View configured
- `KinexUserModel` — Mixamo character, Humanoid rig, in scene
- Camera preview RawImage in bottom-right corner of screen
- `Assets/Models/yolov8n-pose.onnx` — 13 MB, Input: `[1,3,320,320]`, Output: `[1,56,2100]`
- `com.unity.ai.inference` v2.5.0 installed
- `Assets/Scripts/CameraBackground.cs` — WebCamTexture manager
- `Assets/Scripts/PoseDetector.cs` — ONNX loader + bone driver (8 limb bones)
- unity-mcp connected
