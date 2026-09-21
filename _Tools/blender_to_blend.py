"""
Converts a model into a .blend, headlessly.

  blender --background --python _Tools/blender_to_blend.py -- \
      --in <model.fbx> --out <scene.blend>

Exists because importing FBX inside a GUI session fails: the importer calls
mode_set(mode='EDIT') to build the armature and that poll needs an active object the
GUI startup context does not supply, so Blender opened empty. Background import has
never had the problem, so import there and let the GUI merely open the result.
"""

import argparse
import sys
from pathlib import Path

import bpy


def argv_after_ddash():
    return sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--in", dest="inp", required=True)
    ap.add_argument("--out", required=True)
    args = ap.parse_args(argv_after_ddash())

    src = Path(args.inp).resolve()
    out = Path(args.out).resolve()
    out.parent.mkdir(parents=True, exist_ok=True)

    bpy.ops.wm.read_factory_settings(use_empty=True)
    ext = src.suffix.lower()
    if ext in (".glb", ".gltf"):
        bpy.ops.import_scene.gltf(filepath=str(src))
    elif ext == ".fbx":
        bpy.ops.import_scene.fbx(filepath=str(src))
    else:
        raise SystemExit(f"unsupported input: {ext}")

    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    rigs = [o for o in bpy.context.scene.objects if o.type == "ARMATURE"]
    for m in meshes:
        print(f"[to_blend]   mesh {m.name}: {len(m.data.vertices)} verts, "
              f"{len(m.data.polygons)} faces, {len(m.material_slots)} material slot(s)")
    for r in rigs:
        print(f"[to_blend]   armature {r.name}: {len(r.data.bones)} bones")
    if not meshes:
        raise SystemExit("no mesh imported")

    # Pack textures so the .blend is self-contained and opens with materials intact.
    try:
        bpy.ops.file.pack_all()
    except RuntimeError as exc:
        print(f"[to_blend]   pack_all skipped: {exc}")

    bpy.ops.wm.save_as_mainfile(filepath=str(out))
    print(f"[to_blend] saved {out}")


if __name__ == "__main__":
    main()
