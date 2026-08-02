# Dasher Art Staging Manifest

Staged for the "Quake Escape"-style sunny park look. Nothing in this folder has touched the Unity `Assets/` folder — it is pure staging for manual/scripted import later.

Budget: **24 MB hard cap.** Actual total: **18,281,849 bytes ≈ 17.43 MB (74% of budget, ~6.9 MB headroom).**

All files verified: GLB files checked for `glTF` magic bytes (first 4 bytes `67 6c 54 46`); images checked with `file` to confirm real JPEG data (not HTML error pages). Nothing failed verification / nothing was deleted.

## ground/ (4,946,467 bytes ≈ 4.72 MB)

Tiled seamless PBR, 1K JPG only (diffuse + normal, DirectX/GL variant noted). Source: Poly Haven API, CC0.

| File | What | Source | Licence | Bytes |
|---|---|---|---|---|
| `leafy_grass_diff_1k.jpg` | Green lawn/grass — base colour, 1024x1024 | https://polyhaven.com/a/leafy_grass | CC0 | 1,195,394 |
| `leafy_grass_nor_1k.jpg` | Green lawn/grass — normal map (GL/+Y), 1024x1024 | https://polyhaven.com/a/leafy_grass | CC0 | 1,466,049 |
| `park_dirt_diff_1k.jpg` | Dirt/sand landing patch — base colour, 1024x1024 | https://polyhaven.com/a/park_dirt | CC0 | 1,146,359 |
| `park_dirt_nor_1k.jpg` | Dirt/sand landing patch — normal map (GL/+Y), 1024x1024 | https://polyhaven.com/a/park_dirt | CC0 | 1,138,665 |

Notes: `park_dirt` was chosen specifically because it's a literal park-pathway/dirt texture (not desert sand), fits the "tree-lined avenue with dirt landing patches" brief. No AO/roughness maps were pulled to save budget — both surfaces have fairly uniform micro-roughness, base colour + normal covers 90% of the visual read. A third "mown-grass lane strip" variant (candidates: `grass_path_2`, `sparse_grass`) was scouted but **not downloaded** — skipped to keep headroom; can be added later within budget if lanes need a distinct look.

## foliage/ (8,719,092 bytes ≈ 8.31 MB)

GLB, embedded compressed textures, CC0. Source: poly.pizza mirror of Quaternius "Ultimate Stylized Nature Pack" (bundle: https://poly.pizza/bundle/Ultimate-Stylized-Nature-Pack-zyIyYd9yGr). Each GLB is a multi-mesh pack (several named mesh variants sharing one small texture atlas), verified by parsing the glTF JSON chunk.

| File | Contains | Source | Licence | Bytes |
|---|---|---|---|---|
| `Trees_Quaternius.glb` | 5 broadleaf tree variants: NormalTree_1..5 | https://poly.pizza/m/etFGNvsiFv | CC0 | 3,408,276 |
| `MapleTrees_Quaternius.glb` | 5 maple tree variants: MapleTree_1..5 | https://poly.pizza/m/iGFtQd0PJO | CC0 | 3,355,280 |
| `Bushes_Quaternius.glb` | 3 shrub meshes: Plant_1, Bush, Bush_Flowers | https://poly.pizza/m/J2h3HrO356 | CC0 | 472,900 |
| `FlowerBushes_Quaternius.glb` | 3 flowering-shrub meshes: Petals_1, Plant_2, Plant_Flowers | https://poly.pizza/m/1X06RgvSr6 | CC0 | 342,312 |
| `Flowers_Quaternius.glb` | 7 small flower/flower-clump meshes | https://poly.pizza/m/NBUxHir6FJ | CC0 | 372,664 |
| `Grass_Quaternius.glb` | 2 grass-tuft meshes: Grass_Large_Extruded, Grass_Small | https://poly.pizza/m/UGTOzcO3P2 | CC0 | 767,660 |

Total: **10 tree variants across 2 species** (generic broadleaf + maple) + shrubs/flowers/grass tufts. All textured (each pack ships its own diffuse texture baked into the GLB), not flat vertex colour.

Scouted but deliberately NOT downloaded (budget / brief fit):
- `BirchTrees_Quaternius` (3rd species, 4.19 MB) — would have pushed total close to the cap for marginal extra variety; skip unless requested.
- `DeadTrees` (x2 variants in the bundle) — rejected, doesn't fit a bright sunny park.
- `PalmTrees` — rejected, wrong biome for a temperate park.
- `PineTrees` — rejected per brief ("not pine-only" implies avoid pine-heavy set; 2 broadleaf species already covers variety without it).

## rocks/ (3,152,056 bytes ≈ 3.01 MB)

| File | Contains | Source | Licence | Bytes |
|---|---|---|---|---|
| `Rocks_Quaternius.glb` | 5 stone meshes: Rock_1..5 | https://poly.pizza/m/gYhoEOKItJ | CC0 | 3,152,056 |

No log models were found in this pack; only stones. Not pursued further to stay comfortably under budget — flag if logs are a hard requirement, a separate small search would be needed.

## sky/ (1,435,119 bytes ≈ 1.37 MB)

| File | What | Source | Licence | Bytes |
|---|---|---|---|---|
| `kloofendal_48d_partly_cloudy_puresky_1k.hdr` | Daylight sky panorama, partly cloudy, midday, high contrast, "pure sky" variant (sky dome only, no ground/scene baked in) | https://polyhaven.com/a/kloofendal_48d_partly_cloudy_puresky | CC0 | 1,435,119 |

Note on format: Poly Haven does **not** publish a JPG option for HDRI panoramas at any resolution — only `.hdr` (Radiance) and `.exr`. `.exr` was explicitly avoided per the budget rules; `.hdr` at 1K (1.37 MB) was used instead as the smallest legitimate option — it is not a resolution/format cheat, it's the only non-EXR format Poly Haven offers for this asset type. Unity's texture importer reads `.hdr` natively. If a flat JPG cloud texture for a billboard is preferred instead, that would need a separate source (not found on Poly Haven under `textures`).

## props/

Empty. Nothing in the brief's "props" bucket was distinct from what's already covered by rocks/ (stones) — no additional small-prop search was run. Flag if benches/lamp-posts/fences etc. are wanted; not attempted here.

---

## TOTAL: 18,281,849 bytes ≈ 17.43 MB / 24 MB budget (68% headroom used, 31% remaining)

| Folder | Bytes | MB |
|---|---|---|
| ground | 4,946,467 | 4.72 |
| foliage | 8,719,092 | 8.31 |
| rocks | 3,152,056 | 3.01 |
| sky | 1,435,119 | 1.37 |
| props | 0 | 0 |
| **Total** | **18,281,849** | **17.43** |
