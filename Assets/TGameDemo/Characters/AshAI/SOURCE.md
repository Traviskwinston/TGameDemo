# Ash - AI-generated character

Produced by the pipeline in `_Tools` with no paid services:

1. Concept art (front/side/back) - `_Concept/Ash/ash_concept_*.png`
2. Image-to-3D - TRELLIS via HuggingFace Space `trellis-community/TRELLIS`, MIT
   licensed. Multiview reconstruction, 33 seconds.
3. Blender cleanup - `_Tools/blender_inspect_clean.py`. Welds seam duplicates at
   2e-4 (9515 -> 6270 verts) and fills holes up to 10 sides, then decimates to
   12000 tris, recalculates normals, scales to 1.72 m, feet on origin.
4. Rig - `_Tools/blender_autorig.py`, run on the *cleaned* mesh. 20-bone
   deform-only humanoid with Unity bone names. Not Rigify: its ORG-/DEF-/MCH-
   control bones confuse Unity's avatar auto-mapper.

Stage 3 must run before stage 4. Rigging the raw GLB leaves seam duplicates
unwelded, which disconnects the mesh and stops weight smoothing from crossing
seams - that produced 59 isolated-weight vertices instead of 5.

## Known limitations

- Fingers are fused tapered spikes. TRELLIS could not resolve five thin splayed
  fingers from the concept art, and the artefact cannot be trimmed by distance:
  upper-body verts reach 47% of height with no outlier tail separating spike from
  finger. Fixing this needs the concept regenerated with relaxed or closed hands,
  which is the reconstructable pose for image-to-3D.
- Face is a flat smeared plane. Inherent to current image-to-3D, and the reason
  this suits a behind-the-shoulder camera.
- No animation clips. Both this and RogueHooded are Unity Humanoid, so KayKit's 76
  clips can retarget onto this skeleton via Mecanim.
- Cloak is rigidly skinned to the torso. It will not swing; it also will not tear.
- 56 boundary edges remain: the cloak hem and hood opening, intentionally left open
  by the 10-side fill cap.

## Verifying deformation

`_Tools/blender_pose_test.py` poses and renders the rig. It measures facing from the
foot bones and probes each joint in both directions, keeping the sign that bends it
anatomically, because hand-picked rotation axes gave a forward-bending knee and
backward-bending elbows. Do not replace the calibration with fixed angles.

Licence: original design, generated with an MIT-licensed model. No third-party
asset licence applies.
