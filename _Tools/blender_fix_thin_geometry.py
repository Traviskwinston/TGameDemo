"""
Finds and thickens degenerate thin-sheet geometry inside chosen vertex groups.

  blender --background --python _Tools/blender_fix_thin_geometry.py -- \
      --in <model.fbx> --groups LeftHand RightHand --report <json>          # measure only
  ... --out <fixed.fbx> --min-thickness 0.012                               # and repair

Image-to-3D collapsed Ash's thumbs into blades with almost no thickness. Local thickness
is measured by casting a ray from each vertex back through the surface along its inward
normal; a solid digit returns a real distance, a sheet returns almost nothing.

Restricted to named groups on purpose. The cloak is legitimately a thin sheet and must
not be inflated.
"""

import argparse
import json
import statistics
import sys
from pathlib import Path

import bpy
from mathutils import Vector


def argv_after_ddash():
    return sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def region_verts(body, groups, threshold=0.35):
    wanted = {g.index for g in body.vertex_groups if g.name in groups}
    if not wanted:
        raise SystemExit(f"no vertex groups matching {groups}")
    out = set()
    for v in body.data.vertices:
        if any(g.group in wanted and g.weight > threshold for g in v.groups):
            out.add(v.index)
    return out


def measure_thickness(body, idx, max_depth):
    """Local thickness per vertex, via a ray cast inward along the vertex normal."""
    eps = max_depth * 0.02
    result = {}
    for i in idx:
        v = body.data.vertices[i]
        n = v.normal
        if n.length < 1e-6:
            continue
        origin = v.co - n * eps
        hit, loc, _, _ = body.ray_cast(origin, -n, distance=max_depth)
        if hit:
            result[i] = (loc - v.co).length
    return result


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--in", dest="inp", required=True)
    ap.add_argument("--out", default=None)
    ap.add_argument("--groups", nargs="+", default=["LeftHand", "RightHand"])
    ap.add_argument("--min-thickness", type=float, default=0.0,
                    help="target thickness as a fraction of model height; 0 = measure only")
    ap.add_argument("--report", default=None)
    ap.add_argument("--smooth-passes", type=int, default=2)
    args = ap.parse_args(argv_after_ddash())

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(Path(args.inp).resolve()))

    body = next(o for o in bpy.context.scene.objects if o.type == "MESH")
    zs = [(body.matrix_world @ v.co).z for v in body.data.vertices]
    height = max(zs) - min(zs)

    idx = region_verts(body, set(args.groups))
    max_depth = height * 0.12
    thick = measure_thickness(body, idx, max_depth)
    if not thick:
        raise SystemExit("no ray hits; normals may be inconsistent")

    vals = sorted(thick.values())
    report = {
        "model_height": round(height, 4),
        "groups": args.groups,
        "verts_in_region": len(idx),
        "verts_measured": len(vals),
        "thickness_fraction_of_height": {
            "min": round(vals[0] / height, 5),
            "p10": round(vals[len(vals) // 10] / height, 5),
            "median": round(statistics.median(vals) / height, 5),
            "p90": round(vals[len(vals) * 9 // 10] / height, 5),
            "max": round(vals[-1] / height, 5),
        },
    }
    target = args.min_thickness * height
    if target > 0:
        report["verts_below_target"] = sum(1 for v in vals if v < target)
    print(json.dumps(report, indent=2))

    if args.out and target > 0:
        # Push each too-thin vertex out along its own normal by half the shortfall. Both
        # faces of a sheet move outward along opposing normals, so the sheet gains volume
        # without shifting its centre line.
        moved = 0
        offset = {}
        for i, t in thick.items():
            if t < target:
                offset[i] = (target - t) * 0.5
                moved += 1

        # Feather into neighbours so the thickened area does not end in a hard step.
        if args.smooth_passes > 0:
            adj = {i: [] for i in idx}
            for e in body.data.edges:
                a, b = e.vertices
                if a in adj and b in adj:
                    adj[a].append(b)
                    adj[b].append(a)
            for _ in range(args.smooth_passes):
                nxt = dict(offset)
                for i in adj:
                    vals2 = [offset.get(j, 0.0) for j in adj[i]]
                    if vals2:
                        blended = (offset.get(i, 0.0) + sum(vals2) / len(vals2)) * 0.5
                        if blended > 1e-9:
                            nxt[i] = blended
                offset = nxt

        for i, d in offset.items():
            v = body.data.vertices[i]
            v.co = v.co + v.normal * d
        body.data.update()
        report["verts_thickened"] = moved
        report["verts_touched_after_feather"] = len(offset)
        print(f"thickened {moved} verts (feathered to {len(offset)})")

        out = Path(args.out).resolve()
        bpy.ops.object.select_all(action="SELECT")
        bpy.ops.export_scene.fbx(
            filepath=str(out), use_selection=True,
            apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
            add_leaf_bones=False, bake_anim=False, mesh_smooth_type="FACE",
            path_mode="COPY", embed_textures=True,
            primary_bone_axis="Y", secondary_bone_axis="X",
        )
        report["output"] = str(out)

    if args.report:
        Path(args.report).write_text(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
