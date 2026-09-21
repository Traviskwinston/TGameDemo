"""
Describes the structure of the geometry driven by one bone, to locate defects precisely.

  blender --background --python _Tools/blender_hand_analysis.py -- \
      --in <model.fbx> --bone LeftHand

Reports the digit-like protrusions around the bone and how flat each one is, so a repair
can target measured vertices instead of a guess about where the thumb is.
"""

import argparse
import json
import sys
from pathlib import Path

import bpy
from mathutils import Vector


def argv_after_ddash():
    return sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def flatness(points):
    """Smallest extent divided by largest, along the cluster's own principal axes.

    A proper digit is roughly tubular so the ratio stays well above zero. A
    reconstruction artefact collapsed to a sheet approaches zero.
    """
    if len(points) < 4:
        return None
    centre = Vector((0, 0, 0))
    for p in points:
        centre += p
    centre /= len(points)
    rel = [p - centre for p in points]

    # Power iteration for the dominant axis, then remove it and repeat.
    axes = []
    work = [v.copy() for v in rel]
    for _ in range(3):
        axis = Vector((1, 0.3, 0.2)).normalized()
        for _ in range(24):
            acc = Vector((0, 0, 0))
            for v in work:
                acc += v * v.dot(axis)
            if acc.length < 1e-12:
                break
            axis = acc.normalized()
        axes.append(axis)
        work = [v - axis * v.dot(axis) for v in work]

    spans = []
    for axis in axes:
        proj = [v.dot(axis) for v in rel]
        spans.append(max(proj) - min(proj))
    spans.sort(reverse=True)
    return {"spans": [round(s, 4) for s in spans],
            "flatness": round(spans[2] / spans[0], 4) if spans[0] > 1e-9 else None}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--in", dest="inp", required=True)
    ap.add_argument("--bone", default="LeftHand")
    ap.add_argument("--report", default=None)
    args = ap.parse_args(argv_after_ddash())

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(Path(args.inp).resolve()))

    rig = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
    body = next(o for o in bpy.context.scene.objects if o.type == "MESH")

    gi = {g.name: g.index for g in body.vertex_groups}.get(args.bone)
    if gi is None:
        raise SystemExit(f"no vertex group {args.bone}")

    weight = {}
    for v in body.data.vertices:
        w = next((g.weight for g in v.groups if g.group == gi), 0.0)
        if w > 0.35:
            weight[v.index] = w
    idx = set(weight)
    print(f"{args.bone}: {len(idx)} verts above 0.35")

    bone = rig.data.bones[args.bone]
    root = rig.matrix_world @ bone.head_local
    tip = rig.matrix_world @ bone.tail_local
    along = (tip - root).normalized()

    pos = {i: body.matrix_world @ body.data.vertices[i].co for i in idx}

    # Palm versus digits, split by distance along the bone.
    proj = {i: (pos[i] - root).dot(along) for i in idx}
    span = max(proj.values()) - min(proj.values())
    cut = min(proj.values()) + span * 0.45
    digits = {i for i in idx if proj[i] > cut}
    print(f"  bone length {(tip - root).length:.4f}, vert span along bone {span:.4f}")
    print(f"  {len(digits)} verts in the outer (digit) half")

    # Connected components restricted to the digit verts, using mesh edges.
    adj = {i: set() for i in digits}
    for e in body.data.edges:
        a, b = e.vertices
        if a in digits and b in digits:
            adj[a].add(b)
            adj[b].add(a)

    seen, comps = set(), []
    for i in digits:
        if i in seen:
            continue
        stack, group = [i], []
        seen.add(i)
        while stack:
            cur = stack.pop()
            group.append(cur)
            for j in adj[cur]:
                if j not in seen:
                    seen.add(j)
                    stack.append(j)
        comps.append(group)

    comps.sort(key=len, reverse=True)
    out = []
    print(f"  {len(comps)} connected digit cluster(s):")
    for n, group in enumerate(comps):
        pts = [pos[i] for i in group]
        centre = Vector((0, 0, 0))
        for p in pts:
            centre += p
        centre /= len(pts)
        shape = flatness(pts)
        lateral = (centre - root) - along * (centre - root).dot(along)
        entry = {
            "cluster": n,
            "verts": len(group),
            "centre": [round(c, 4) for c in centre],
            "reach_along_bone": round((centre - root).dot(along), 4),
            "lateral_offset": round(lateral.length, 4),
            "shape": shape,
        }
        out.append(entry)
        f = shape["flatness"] if shape else None
        print(f"    [{n}] {len(group):4d} verts  reach={entry['reach_along_bone']:.4f}  "
              f"lateral={entry['lateral_offset']:.4f}  spans={shape['spans'] if shape else '-'}  "
              f"flatness={f}")

    if args.report:
        Path(args.report).write_text(json.dumps(out, indent=2))


if __name__ == "__main__":
    main()
