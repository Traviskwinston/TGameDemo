# Ash - AI-generated character

Produced by the pipeline in `_Tools` with no paid services:

1. Concept art (front/side/back) - `_Concept/Ash/v3_refs/`
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

## Arm proportions

`measure` returns the extreme-X vertex per side, which is a *fingertip*, not a wrist.
Treating it as the wrist ran LowerArm from elbow all the way to the fingertip and started
the Hand bone at the fingertip pointing into empty space outside the mesh. No vertex was
weighted above 0.5 to either hand bone, so the hands could not be posed at all.
Shoulder-to-fingertip is now split by human proportion - upper arm to 40%, forearm to
75%, hand the rest - which puts 181 verts under LeftHand with no change to the deform
audit. Verify with `blender_hand_analysis.py` after touching the skeleton.

## Two weighting rules that stop tearing

**No vertex may be driven by both limb chains.** LowerLeg's radius reaches across the gap
between ankles, so boot-sole verts picked up 0.30 RightLowerLeg and 0.27 LeftLowerLeg and
were torn in half the moment the legs scissored: 70x stride stretch. The existing hop
limit does not prevent this because it is anchored on the vertex's *initial* dominant
bone, and a vertex between the ankles starts dominated by Hips, which is only 2 hops from
either leg. `drop_cross_side` keeps the stronger side and discards the other, leaving
centre bones alone.

**Out-of-radius does not mean cloth.** The fallback handed every vertex outside all bone
radii to the torso, which is right for a cloak hem but wrong for boot geometry sitting
just outside the deliberately tight Foot radius of 0.055 - that put Chest and Spine on
verts at ankle height. A vertex within 2.5x the nearest bone's radius is now snapped to
that bone; only genuinely distant verts go to the torso.

Collapse decimation also leaves sub-millimetre edges, and an edge 0.0008 long reports
enormous stretch under any pose because the ratio divides by almost nothing. `weld_slivers`
runs after scale normalisation, at a threshold relative to model height, since the earlier
weld runs before decimation in pre-scale units and cannot catch them.

Together: stride stretch 70.58x -> 3.9x, edges over 4x 24 -> 0.

## Hands: why the concept art poses closed fists

The first two attempts drew Ash with splayed fingers, and both times the thumb came back
as a flat flap rather than a digit. It is a wrong *shape*, not a thin-sheet artefact:
local thickness measured by ray cast (`blender_fix_thin_geometry.py`) found only 17 verts
under 2cm, and thickening 90 of them changed the render not at all. Procedural repair
cannot invent a correct thumb from an incorrect one, and no image-to-3D, retopology or
mesh-repair tool patches specific anatomy.

Five thin splayed digits are the hardest case for image-to-3D. Closed fists are a compact
mass it resolves reliably, and the fist is what a hand holding a weapon or torch needs
anyway. Measured on `blender_hand_analysis.py` flatness, where 1.0 is equidimensional and
0 is a sheet: splayed 0.48, arms-down fists 0.20, A-pose fists 0.83.

Keep both constraints in the prompt. Asking for fists while letting the arms hang against
the body gave the 0.20 result - the arms fused to the cloak, doubling ripped faces to 410
and pushing stride stretch to 4.27x. The reference needs a wide A-pose with visible
background between arm and torso *and* closed fists, stated for every view. A side view
must also say the arm hangs downward, or it gets drawn reaching backward and contradicts
the front.

## Known limitations
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
