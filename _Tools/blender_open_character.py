"""
Opens a rigged character in Blender's GUI for interactive inspection.

  blender --python _Tools/blender_open_character.py -- --in <model>

Loads the model, frames it, and switches to material preview so textures show.
Select the Rig and enter Pose Mode to bend joints by hand.
"""

import sys
from pathlib import Path

import bpy


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    src = None
    if "--in" in argv:
        src = Path(argv[argv.index("--in") + 1]).resolve()
    if src is None or not src.exists():
        print(f"model not found: {src}")
        return

    bpy.ops.wm.read_homefile(use_empty=True)
    ext = src.suffix.lower()
    if ext in (".glb", ".gltf"):
        bpy.ops.import_scene.gltf(filepath=str(src))
    elif ext == ".fbx":
        bpy.ops.import_scene.fbx(filepath=str(src))
    else:
        print(f"unsupported: {ext}")
        return

    # Material preview so the generated texture is visible, not grey clay.
    for area in bpy.context.screen.areas:
        if area.type == "VIEW_3D":
            for space in area.spaces:
                if space.type == "VIEW_3D":
                    space.shading.type = "MATERIAL"
                    space.overlay.show_relationship_lines = False

    for o in bpy.context.scene.objects:
        o.select_set(o.type == "MESH")
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    if meshes:
        bpy.context.view_layer.objects.active = meshes[0]
        for area in bpy.context.screen.areas:
            if area.type == "VIEW_3D":
                with bpy.context.temp_override(area=area, region=area.regions[-1]):
                    bpy.ops.view3d.view_selected()

    rigs = [o for o in bpy.context.scene.objects if o.type == "ARMATURE"]
    print(f"loaded {src.name}: {len(meshes)} mesh(es), {len(rigs)} armature(s)")
    if rigs:
        print(f"  bones: {len(rigs[0].data.bones)}")


main()
