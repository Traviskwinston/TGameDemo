"""
Renders close-ups orbiting a named bone, for inspecting one body part.

  blender --background --python _Tools/blender_closeup.py -- \
      --in <model.fbx> --out <dir> --bone LeftHand --views 6

Full-body renders are too small to judge hands or faces. This frames a single bone so
defects are actually visible before anything is changed.
"""

import argparse
import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
from blender_pose_test import setup_render  # noqa: E402


def argv_after_ddash():
    return sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--in", dest="inp", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--bone", default="LeftHand")
    ap.add_argument("--views", type=int, default=6)
    ap.add_argument("--span", type=float, default=0.0,
                    help="width of framed region; 0 derives it from the bone's vertices")
    ap.add_argument("--res", type=int, default=520)
    ap.add_argument("--wire", action="store_true", help="also render a wireframe pass")
    args = ap.parse_args(argv_after_ddash())

    out = Path(args.out).resolve()
    out.mkdir(parents=True, exist_ok=True)

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(Path(args.inp).resolve()))

    rigs = [o for o in bpy.context.scene.objects if o.type == "ARMATURE"]
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    if not rigs or not meshes:
        raise SystemExit("need an armature and a mesh")
    rig = rigs[0]

    if args.bone not in {b.name for b in rig.data.bones}:
        raise SystemExit(f"no bone {args.bone}; have {[b.name for b in rig.data.bones]}")

    # Framed on the vertices actually weighted to the bone, not the bone itself. Aiming at
    # the bone put the hand in the corner of frame, because the bone sits at the wrist and
    # the geometry it drives extends well past it.
    cluster = []
    for m in meshes:
        gi = {g.name: g.index for g in m.vertex_groups}.get(args.bone)
        if gi is None:
            continue
        for v in m.data.vertices:
            w = next((g.weight for g in v.groups if g.group == gi), 0.0)
            if w > 0.5:
                cluster.append(m.matrix_world @ v.co)
    if not cluster:
        raise SystemExit(f"no vertices weighted above 0.5 to {args.bone}")

    lo = Vector((min(p.x for p in cluster), min(p.y for p in cluster), min(p.z for p in cluster)))
    hi = Vector((max(p.x for p in cluster), max(p.y for p in cluster), max(p.z for p in cluster)))
    target = (lo + hi) * 0.5
    extent = max((hi - lo).x, (hi - lo).y, (hi - lo).z)
    print(f"  {args.bone}: {len(cluster)} verts, extent {extent:.4f}")

    zs = [(m.matrix_world @ v.co).z for m in meshes for v in m.data.vertices]
    height = max(zs) - min(zs)

    scene = bpy.context.scene
    cam = setup_render(scene, target, height, args.res)
    scene.render.resolution_y = args.res
    cam.data.ortho_scale = args.span if args.span > 0 else extent * 2.2

    for i in range(args.views):
        ang = math.tau * i / args.views
        d = height * 2.0
        cam.location = (target.x + math.sin(ang) * d,
                        target.y - math.cos(ang) * d,
                        target.z + height * 0.05)
        cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = str(out / f"{args.bone}_{i:02d}.png")
        bpy.ops.render.render(write_still=True)
        print(f"  {args.bone}_{i:02d}")

    if args.wire:
        scene.render.engine = "BLENDER_WORKBENCH"
        scene.display.shading.show_xray = False
        scene.display.shading.type = "SOLID"
        scene.display.shading.color_type = "SINGLE"
        scene.display.shading.single_color = (0.55, 0.55, 0.58)
        for m in meshes:
            m.show_wire = True
            m.show_all_edges = True
        for i in range(args.views):
            ang = math.tau * i / args.views
            d = height * 2.0
            cam.location = (target.x + math.sin(ang) * d,
                            target.y - math.cos(ang) * d,
                            target.z + height * 0.05)
            cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
            scene.render.filepath = str(out / f"{args.bone}_wire_{i:02d}.png")
            bpy.ops.render.render(write_still=True)
            print(f"  {args.bone}_wire_{i:02d}")


if __name__ == "__main__":
    main()
