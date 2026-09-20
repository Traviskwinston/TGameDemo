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
   control bones confuse Unity's avatar auto-mapper. Also rips fused surfaces
   (below) before weighting.

Stage 3 must run before stage 4. Rigging the raw GLB leaves seam duplicates
unwelded, which disconnects the mesh and stops weight smoothing from crossing
seams - that produced 59 isolated-weight vertices instead of 5.

## Fused surfaces

Image-to-3D returns one watertight shell, so surfaces that merely touch come back
fused. Here the hands and sleeves were joined to the cloak and hips. No weighting can
satisfy a single edge whose ends belong to an arm and to the hips; weight smoothing only
hid it by dragging Hips weight out into the arms, which then stayed behind when the
shoulder swung and stretched into long flesh-coloured spikes.

`rip_fused_surfaces` deletes faces spanning regions at least `--rip-hops` apart, culls
the orphan shards that leaves, then closes the resulting boundary loops. Measured on the
aim pose, worst edge stretch: 45x before, 3.0x after, with no edge above 4x in any pose.

Two details are load-bearing:

- Faces must be *deleted*, not edge-split. Splitting duplicates vertices but leaves
  every resulting edge still spanning both regions, so the stretch survives it.
- Region assignment must use the same out-of-radius fallback as `skin`. Resolving it to
  the nearest segment instead labelled cloak verts beside a sleeve as "arm", so they
  looked like same-region neighbours and escaped ripping while `skin` weighted them to
  Hips. That single inconsistency left the audit at 26x.

`rip_hops=3` measured best. 4 leaves the arm-to-chest fusion in place; 5 is far worse.
A pure hop count cannot separate chest-to-upper-arm fusion from the hood's legitimate
join at the shoulders, since both are 3 hops, so ripping additionally requires one side
to be a limb rather than core body.

## Known limitations

- Hands are small and the fingers barely separated. TRELLIS struggled to resolve five
  thin splayed fingers from the concept art. Regenerating the concept with relaxed or
  lightly closed hands would reconstruct better. This is cosmetic, not a deform bug -
  the spikes that looked like finger damage were the fusion issue above.
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

`_Tools/blender_deform_audit.py` reports worst edge stretch per pose and names the bones
weighting each end. Renders show that something is wrong; only this attributes it to a
bone, which is what turned "strange flesh things on the arms" into a specific finding
that arm verts carried 33% Hips weight. Run it after any change to weighting or ripping;
treat any edge above 4x as a regression.

Licence: original design, generated with an MIT-licensed model. No third-party
asset licence applies.
