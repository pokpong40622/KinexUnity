"""Extract BlazePose 33-landmark sequences from the AstroStance test videos.

Runs MediaPipe Pose (same BlazePose model family as the Unity plugin) over each
mp4 in the test-feed folder and writes one JSON per video into
Assets/AstroStance/TestFeeds/ for AstroFeedReplayTest.cs to replay through the
C# detectors in batch mode.

Output schema per file (flat per frame so Unity's JsonUtility can parse it):
  { "name": "goleft", "fps": 30.0, "width": 540, "height": 960,
    "frames": [ {"v": [x,y,z,vis, x,y,z,vis, ... 33*4 floats]} , {"v": []} ... ] }
A frame with no detected pose has an empty "v".

Usage:  python tools/extract_landmarks.py
"""

import json
import os
import sys

import cv2

FEED_DIR = r"C:\Users\Admin\Desktop\AstroStanceTestFeed"
OUT_DIR = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                       "Assets", "AstroStance", "TestFeeds")


MODEL = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                     "Assets", "StreamingAssets", "pose_landmarker_full.bytes")


def make_pose():
    """Tasks API with the SAME model file the Unity plugin ships (.bytes == .task).

    Fresh instance per video — VIDEO mode requires monotonically increasing
    timestamps for the lifetime of the landmarker.
    """
    import mediapipe as mp
    from mediapipe.tasks.python import BaseOptions
    from mediapipe.tasks.python import vision

    options = vision.PoseLandmarkerOptions(
        base_options=BaseOptions(model_asset_path=MODEL),
        running_mode=vision.RunningMode.VIDEO,
        num_poses=1,
    )
    landmarker = vision.PoseLandmarker.create_from_options(options)

    def detect(rgb, ts_ms):
        image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)
        res = landmarker.detect_for_video(image, ts_ms)
        if not res.pose_landmarks:
            return None
        return [[lm.x, lm.y, lm.z, lm.visibility if lm.visibility is not None else 1.0]
                for lm in res.pose_landmarks[0]]

    return detect


def extract(path, detect):
    cap = cv2.VideoCapture(path)
    fps = cap.get(cv2.CAP_PROP_FPS) or 30.0
    width = int(cap.get(cv2.CAP_PROP_FRAME_WIDTH))
    height = int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT))
    frames = []
    idx = 0
    while True:
        ok, bgr = cap.read()
        if not ok:
            break
        rgb = cv2.cvtColor(bgr, cv2.COLOR_BGR2RGB)
        lms = detect(rgb, int(idx * 1000 / fps))
        if lms is None:
            frames.append({"v": []})
        else:
            frames.append({"v": [round(v, 4) for lm in lms for v in lm]})
        idx += 1
    cap.release()
    return {"name": os.path.splitext(os.path.basename(path))[0],
            "fps": fps, "width": width, "height": height, "frames": frames}


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    videos = sorted(f for f in os.listdir(FEED_DIR) if f.lower().endswith(".mp4"))
    if not videos:
        sys.exit(f"no mp4 files in {FEED_DIR}")
    for f in videos:
        data = extract(os.path.join(FEED_DIR, f), make_pose())
        detected = sum(1 for fr in data["frames"] if fr["v"])
        out = os.path.join(OUT_DIR, data["name"] + ".json")
        with open(out, "w") as fh:
            json.dump(data, fh, separators=(",", ":"))
        print(f"{f}: {detected}/{len(data['frames'])} frames with pose -> {out}")


if __name__ == "__main__":
    main()
