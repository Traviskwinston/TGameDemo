"""
Finds skinning failures by measuring edge stretch per pose, and names the guilty bones.

  blender --background --python _Tools/blender_deform_audit.py -- \
      --in _Concept/Ash/generated/Ash_rigged.fbx --report .../deform_audit.json

A spike is an edge that grows enormously while its neighbours do not, because one of
its vertices is weighted to a bone that moves differently from the surrounding surface.
Eyeballing renders cannot attribute that to a bone; this can, so fixes stop being
guesswork.
"""

import argparse
import json
import sys
from pathlib import Path

import bpy
from mathutils import Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
from blender_pose_test import build_poses, calibrate, clear_pose, detect_forward  # noqa: E402


def argv_after_ddash():
    return sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def rest_positions(obj):
    return [v.co.copy() for v in obj.data.vertices]


def posed_positions(obj):
    dg = bpy.context.evaluated_depsgraph_get()
    ev = obj.evaluated_get(dg)
    mesh = ev.to_mesh()
    pts = [v.co.copy() for v in mesh.vertices]
    ev.to_mesh_clear()
    return pts


def dominant_groups(obj, vert_index, limit=3):
    groups = sorted(obj.data.vertices[vert_index].groups,
                    key=lambda g: g.weight, reverse=True)[:limit]
    out = []
    for g in groups:
        try:
            out.append((obj.vertex_groups[g.group].name, round(g.weight, 3)))
        except (IndexError, KeyError):
            continue
    return out


def audit_pose(obj, rest, lo_z, height, top=12):
    posed = posed_positions(obj)
    if len(posed) != len(rest):
        return {"error": f"vert count changed {len(rest)} -> {len(posed)}"}

    worst = []
    for e in obj.data.edges:
        a, b = e.vertices
        r = (rest[a] - rest[b]).length
        if r < 1e-6:
            continue
        p = (posed[a] - posed[b]).length
        worst.append((p / r, a, b, r, p))
    worst.sort(reverse=True)

    # Displacement relative to the local surface: how far a vert moved compared with
    # the average of its edge-neighbours is what actually reads as a spike.
    stretched = []
    for ratio, a, b, r, p in worst[:top]:
        mid = (posed[a] + posed[b]) * 0.5
        stretched.append({
            "stretch_ratio": round(ratio, 2),
            "rest_len": round(r, 4),
            "posed_len": round(p, 4),
            "height_frac": round((mid.z - lo_z) / height, 3),
            "vert_a_bones": dominant_groups(obj, a),
            "vert_b_bones": dominant_groups(obj, b),
        })

    ratios = [w[0] for w in worst]
    over2 = sum(1 for r in ratios if r > 2.0)
    over4 = sum(1 for r in ratios if r > 4.0)
    return {
        "edges": len(ratios),
        "max_stretch": round(ratios[0], 2) if ratios else 0,
        "edges_over_2x": over2,
        "edges_over_4x": over4,
        "worst": stretched,
    }


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--in", dest="inp", required=True)
    ap.add_argument("--report", required=True)
    args = ap.parse_args(argv_after_ddash())

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(Path(args.inp).resolve()))

    rig = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
    obj = next(o for o in bpy.context.scene.objects if o.type == "MESH")

    pts = [obj.matrix_world @ v.co for v in obj.data.vertices]
    lo_z = min(p.z for p in pts)
    height = max(p.z for p in pts) - lo_z
    centre = Vector((0.0, 0.0, 0.0))
    for p in pts:
        centre += p
    centre /= len(pts)

    bpy.context.view_layer.objects.active = rig
    forward = detect_forward(rig)
    signs, _ = calibrate(rig, forward, centre)

    rest = rest_positions(obj)
    out = {}
    for name, pose in build_poses(signs).items():
        clear_pose(rig)
        for bone, rot in pose.items():
            pb = rig.pose.bones.get(bone)
            if pb:
                pb.rotation_euler = rot
        bpy.context.view_layer.update()
        out[name] = audit_pose(obj, rest, lo_z, height)

        r = out[name]
        print(f"\n[{name}] max_stretch={r['max_stretch']}x  "
              f">2x={r['edges_over_2x']}  >4x={r['edges_over_4x']}")
        for w in r["worst"][:6]:
            print(f"   {w['stretch_ratio']:6.1f}x  h={w['height_frac']:.2f}  "
                  f"{w['rest_len']:.4f}->{w['posed_len']:.4f}  "
                  f"A={w['vert_a_bones']}  B={w['vert_b_bones']}")

    rep = Path(args.report).resolve()
    rep.parent.mkdir(parents=True, exist_ok=True)
    rep.write_text(json.dumps(out, indent=2))


if __name__ == "__main__":
    main()
