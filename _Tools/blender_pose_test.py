"""
Poses a rigged character and renders it, so joint deformation can actually be seen.

  blender --background --python _Tools/blender_pose_test.py -- \
      --in _Concept/Ash/generated/Ash_rigged.fbx --out _Concept/Ash/generated/pose

Numbers proving vertices move do not prove they move correctly. This renders a
stride and an arm raise to expose collapsing shoulders, knees or hips.
"""

import argparse
import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector

# Rough mid-stride and a raised weapon arm: enough to expose bad weights.
POSES = {
    "stride": {
        "LeftUpperLeg": (math.radians(-28), 0, 0),
        "LeftLowerLeg": (math.radians(18), 0, 0),
        "RightUpperLeg": (math.radians(24), 0, 0),
        "RightLowerLeg": (math.radians(-34), 0, 0),
        "LeftUpperArm": (math.radians(18), 0, 0),
        "RightUpperArm": (math.radians(-16), 0, 0),
        "Spine": (math.radians(5), 0, 0),
    },
    "aim": {
        "RightUpperArm": (0, 0, math.radians(58)),
        "RightLowerArm": (math.radians(-38), 0, 0),
        "LeftUpperArm": (0, 0, math.radians(-42)),
        "LeftLowerArm": (math.radians(-52), 0, 0),
        "Chest": (0, 0, math.radians(-10)),
        "Head": (0, 0, math.radians(-6)),
    },
}


def argv_after_ddash():
    return sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--in", dest="inp", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--res", type=int, default=520)
    args = ap.parse_args(argv_after_ddash())

    src = Path(args.inp).resolve()
    out = Path(args.out).resolve()
    out.mkdir(parents=True, exist_ok=True)

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(src))

    rigs = [o for o in bpy.context.scene.objects if o.type == "ARMATURE"]
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    if not rigs or not meshes:
        raise SystemExit(f"need an armature and a mesh; got {len(rigs)} rigs, {len(meshes)} meshes")
    rig = rigs[0]

    lo = Vector((1e9,) * 3)
    hi = Vector((-1e9,) * 3)
    for m in meshes:
        for c in m.bound_box:
            p = m.matrix_world @ Vector(c)
            lo = Vector((min(lo.x, p.x), min(lo.y, p.y), min(lo.z, p.z)))
            hi = Vector((max(hi.x, p.x), max(hi.y, p.y), max(hi.z, p.z)))
    centre = (lo + hi) * 0.5
    height = max(hi.z - lo.z, 1e-3)

    scene = bpy.context.scene
    for engine in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE", "BLENDER_WORKBENCH"):
        try:
            scene.render.engine = engine
            break
        except TypeError:
            continue
    scene.render.resolution_x = args.res
    scene.render.resolution_y = int(args.res * 1.4)
    scene.view_settings.view_transform = "Standard"

    world = bpy.data.worlds.new("W")
    scene.world = world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.26, 0.26, 0.28, 1)
    world.node_tree.nodes["Background"].inputs[1].default_value = 1.7

    for name, loc, energy in (("key", (3, -4, 3.5), 1000), ("fill", (-3.5, -2.5, 1.5), 380)):
        lamp = bpy.data.lights.new(name, "AREA")
        lamp.energy = energy
        lamp.size = 6.0
        o = bpy.data.objects.new(name, lamp)
        o.location = (centre.x + loc[0] * height, centre.y + loc[1] * height, centre.z + loc[2] * height)
        o.rotation_euler = (centre - o.location).to_track_quat("-Z", "Y").to_euler()
        scene.collection.objects.link(o)

    cam_data = bpy.data.cameras.new("Cam")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = height * 1.2
    cam = bpy.data.objects.new("Cam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam

    bpy.context.view_layer.objects.active = rig
    for pb in rig.pose.bones:
        pb.rotation_mode = "XYZ"

    # Three-quarter view shows limb separation better than a flat orthographic front.
    for pose_name, pose in POSES.items():
        for pb in rig.pose.bones:
            pb.rotation_euler = (0, 0, 0)
        applied = []
        for bone, rot in pose.items():
            pb = rig.pose.bones.get(bone)
            if pb:
                pb.rotation_euler = rot
                applied.append(bone)
            else:
                print(f"  WARNING: no bone named {bone}")
        bpy.context.view_layer.update()

        for view_name, deg in (("q", 215.0), ("side", 270.0)):
            rad = math.radians(deg)
            d = height * 2.4
            cam.location = (centre.x + math.sin(rad) * d, centre.y - math.cos(rad) * d, centre.z)
            cam.rotation_euler = (centre - cam.location).to_track_quat("-Z", "Y").to_euler()
            scene.render.filepath = str(out / f"{pose_name}_{view_name}.png")
            bpy.ops.render.render(write_still=True)
            print(f"  rendered {pose_name}_{view_name}  (bones: {len(applied)})")


if __name__ == "__main__":
    main()
