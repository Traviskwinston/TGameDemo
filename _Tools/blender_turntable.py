"""
Renders a 360 turntable of a model, as stills and as an mp4.

  blender --background --python _Tools/blender_turntable.py -- \
      --in _Concept/Ash/generated/Ash_rigged.fbx --out _Concept/Ash/generated/turntable

Still frames from fixed angles keep prompting the question of whether the asset is
actually 3D. A continuous orbit answers it.
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
    ap.add_argument("--frames", type=int, default=36)
    ap.add_argument("--res", type=int, default=440)
    ap.add_argument("--stills", type=int, default=12,
                    help="how many evenly spaced frames to also save as stills")
    ap.add_argument("--no-video", action="store_true")
    args = ap.parse_args(argv_after_ddash())

    out = Path(args.out).resolve()
    out.mkdir(parents=True, exist_ok=True)

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(Path(args.inp).resolve()))

    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    if not meshes:
        raise SystemExit("no mesh imported")

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
    cam = setup_render(scene, centre, height, args.res)

    # Camera rides a pivot at the model's centre; spinning the pivot orbits the camera.
    pivot = bpy.data.objects.new("Pivot", None)
    scene.collection.objects.link(pivot)
    pivot.location = centre
    cam.parent = pivot
    cam.location = (0.0, -height * 2.4, 0.0)
    cam.rotation_euler = (math.radians(90), 0.0, 0.0)

    # A rim light plus more ambient, because this character's back is an almost black
    # cloak and the default front-lit setup renders half the orbit as a silhouette.
    scene.world.node_tree.nodes["Background"].inputs[1].default_value = 4.0
    rim = bpy.data.lights.new("rim", "AREA")
    rim.energy = 900
    rim.size = 5.0
    rim_obj = bpy.data.objects.new("rim", rim)
    rim_obj.location = (centre.x, centre.y + height * 3.0, centre.z + height * 1.4)
    rim_obj.rotation_euler = (centre - rim_obj.location).to_track_quat("-Z", "Y").to_euler()
    scene.collection.objects.link(rim_obj)

    # Lights ride the pivot, so every angle is lit the same way.
    for obj in list(scene.objects):
        if obj.type == "LIGHT":
            obj.matrix_parent_inverse = pivot.matrix_world.inverted()
            obj.parent = pivot

    scene.frame_start = 1
    scene.frame_end = args.frames
    pivot.rotation_mode = "XYZ"

    # Driven by a frame handler rather than keyframes: Blender 5.x moved actions to
    # slots and channelbags, so action.fcurves no longer exists to set interpolation.
    def spin(sc, _=None):
        t = (sc.frame_current - 1) / float(args.frames)
        pivot.rotation_euler = (0.0, 0.0, math.tau * t)

    bpy.app.handlers.frame_change_pre.append(spin)

    step = max(1, args.frames // max(1, args.stills))
    for frame in range(1, args.frames + 1, step):
        scene.frame_set(frame)
        spin(scene)
        scene.render.filepath = str(out / f"turn_{frame:03d}.png")
        bpy.ops.render.render(write_still=True)
        print(f"  still {frame:03d}")

    # Not every Blender build ships FFMPEG; this one offers only still formats, so fall
    # back to the frame sequence rather than failing after the stills are already done.
    formats = scene.render.image_settings.bl_rna.properties["file_format"].enum_items.keys()
    if not args.no_video and "FFMPEG" not in formats:
        print("  no FFMPEG in this Blender build; stills only")
        args.no_video = True

    if not args.no_video:
        scene.render.image_settings.file_format = "FFMPEG"
        scene.render.ffmpeg.format = "MPEG4"
        scene.render.ffmpeg.codec = "H264"
        scene.render.ffmpeg.constant_rate_factor = "HIGH"
        scene.render.fps = 24
        scene.render.filepath = str(out / "turntable.mp4")
        bpy.ops.render.render(animation=True)
        print(f"  video {out / 'turntable.mp4'}")


if __name__ == "__main__":
    main()
