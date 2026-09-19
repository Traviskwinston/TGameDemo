"""
Renders orthographic turnaround previews of a model so its quality can be judged.

  blender --background --python _Tools/blender_preview.py -- \
      --in _Concept/Ash/generated/ash_raw.glb --out _Concept/Ash/generated/preview
"""

import argparse
import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector

VIEWS = {"front": 0.0, "right": 90.0, "back": 180.0, "left": 270.0}


def argv_after_ddash():
    return sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def bounds(objs):
    lo = Vector((1e9,) * 3)
    hi = Vector((-1e9,) * 3)
    for o in objs:
        for c in o.bound_box:
            p = o.matrix_world @ Vector(c)
            lo = Vector((min(lo.x, p.x), min(lo.y, p.y), min(lo.z, p.z)))
            hi = Vector((max(hi.x, p.x), max(hi.y, p.y), max(hi.z, p.z)))
    return lo, hi


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--in", dest="inp", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--res", type=int, default=560)
    args = ap.parse_args(argv_after_ddash())

    src = Path(args.inp).resolve()
    out = Path(args.out).resolve()
    out.mkdir(parents=True, exist_ok=True)

    bpy.ops.wm.read_factory_settings(use_empty=True)
    ext = src.suffix.lower()
    if ext in (".glb", ".gltf"):
        bpy.ops.import_scene.gltf(filepath=str(src))
    elif ext == ".fbx":
        bpy.ops.import_scene.fbx(filepath=str(src))
    else:
        raise SystemExit(f"unsupported: {ext}")

    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    if not meshes:
        raise SystemExit("no meshes imported")

    lo, hi = bounds(meshes)
    centre = (lo + hi) * 0.5
    height = max(hi.z - lo.z, 1e-3)
    radius = max((hi - lo).length, 1e-3)

    scene = bpy.context.scene
    for engine in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE", "BLENDER_WORKBENCH"):
        try:
            scene.render.engine = engine
            break
        except TypeError:
            continue
    scene.render.resolution_x = args.res
    scene.render.resolution_y = int(args.res * 1.35)
    scene.render.film_transparent = False
    scene.view_settings.view_transform = "Standard"

    world = bpy.data.worlds.new("W")
    scene.world = world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.24, 0.24, 0.26, 1)
    world.node_tree.nodes["Background"].inputs[1].default_value = 1.6

    # Two soft area lights keep the silhouette readable without blowing out texture.
    for name, loc, energy in (("key", (3, -4, 3.5), 900), ("fill", (-3.5, -2.5, 1.6), 350)):
        lamp = bpy.data.lights.new(name, "AREA")
        lamp.energy = energy
        lamp.size = 6.0
        obj = bpy.data.objects.new(name, lamp)
        obj.location = (centre.x + loc[0] * height, centre.y + loc[1] * height, centre.z + loc[2] * height)
        direction = centre - obj.location
        obj.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
        scene.collection.objects.link(obj)

    cam_data = bpy.data.cameras.new("Cam")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = height * 1.18
    cam = bpy.data.objects.new("Cam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam

    dist = radius * 2.5
    written = []
    for name, deg in VIEWS.items():
        rad = math.radians(deg)
        cam.location = (centre.x + math.sin(rad) * dist, centre.y - math.cos(rad) * dist, centre.z)
        cam.rotation_euler = (centre - cam.location).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = str(out / f"{src.stem}_{name}.png")
        bpy.ops.render.render(write_still=True)
        written.append(scene.render.filepath)
        print(f"  rendered {name}")

    print("PREVIEWS:")
    for w in written:
        print(f"  {w}")


if __name__ == "__main__":
    main()
