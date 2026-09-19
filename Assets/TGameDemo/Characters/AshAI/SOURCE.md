# Ash - AI-generated character

Produced by the pipeline in `_Tools` with no paid services:

1. Concept art (front/side/back) - `_Concept/Ash/ash_concept_*.png`
2. Image-to-3D - TRELLIS via HuggingFace Space `trellis-community/TRELLIS`, MIT
   licensed. Multiview reconstruction, 33 seconds.
3. Blender cleanup - `_Tools/blender_inspect_clean.py`. 12482 -> 11999 tris,
   normals recalculated, scaled to 1.72 m, feet on origin.
4. Rig - `_Tools/blender_autorig.py`. 20-bone deform-only humanoid with Unity
   bone names. Not Rigify: its ORG-/DEF-/MCH- control bones confuse Unity's avatar
   auto-mapper.

## Known limitations

- Face is a flat smeared plane; hands are fingerless stubs. Inherent to current
  image-to-3D, and the reason this suits a behind-the-shoulder camera.
- No animation clips. Both this and RogueHooded are Unity Humanoid, so KayKit's 76
  clips can retarget onto this skeleton via Mecanim.
- Cloak is rigidly skinned to the torso. It will not swing; it also will not tear.
- 55 boundary edges remain, mostly the cloak hem and hood opening.

Licence: original design, generated with an MIT-licensed model. No third-party
asset licence applies.
