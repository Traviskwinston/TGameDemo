"""
Builds a clean Unity-Humanoid skeleton for a generated A-pose character and skins it.

  blender --background --python _Tools/blender_autorig.py -- \
      --in  _Concept/Ash/generated/ash_raw.glb \
      --out Assets/TGameDemo/Characters/AshAI/Ash.fbx \
      --report _Concept/Ash/generated/rig_report.json

Deliberately not Rigify. Rigify emits hundreds of ORG-/DEF-/MCH- control bones and
Unity's avatar auto-mapper picks the wrong ones out of them. A deform-only skeleton
with standard names maps first time.

Bone positions come from measured mesh landmarks (hand tips, foot clusters, shoulder
width) scaled by human proportion, not from fixed guesses.
"""

import argparse
import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector


def argv_after_ddash():
    return sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def world_verts(objs):
    pts = []
    for o in objs:
        mw = o.matrix_world
        pts.extend([mw @ v.co for v in o.data.vertices])
    return pts


def measure(pts):
    """Finds the landmarks a humanoid skeleton needs, from the geometry itself."""
    xs = [p.x for p in pts]
    zs = [p.z for p in pts]
    ys = [p.y for p in pts]
    lo = Vector((min(xs), min(ys), min(zs)))
    hi = Vector((max(xs), max(ys), max(zs)))
    height = hi.z - lo.z

    m = {"height": height, "lo": list(lo), "hi": hi.z, "centre_x": (lo.x + hi.x) * 0.5}

    # Hand tips: the extreme X verts in the upper half of the body.
    upper = [p for p in pts if p.z > lo.z + height * 0.45]
    if upper:
        m["hand_r"] = list(max(upper, key=lambda p: p.x))
        m["hand_l"] = list(min(upper, key=lambda p: p.x))

    # Shoulder width: X spread of a slice just below the top of the shoulders.
    band = [p for p in pts if lo.z + height * 0.74 < p.z < lo.z + height * 0.80]
    m["shoulder_half"] = (max(p.x for p in band) - min(p.x for p in band)) * 0.5 if band else height * 0.11

    # Foot centres: split the lowest slice into left and right of the midline.
    feet = [p for p in pts if p.z < lo.z + height * 0.06]
    cx = m["centre_x"]
    right = [p for p in feet if p.x > cx]
    left = [p for p in feet if p.x <= cx]
    m["foot_r_x"] = sum(p.x for p in right) / len(right) if right else cx + height * 0.06
    m["foot_l_x"] = sum(p.x for p in left) / len(left) if left else cx - height * 0.06
    m["foot_y"] = sum(p.y for p in feet) / len(feet) if feet else 0.0
    m["toe_y"] = min(p.y for p in feet) if feet else -height * 0.05
    return m


# name -> (head, tail, parent). Fractions are of total height, a standard human canon.
def skeleton(m):
    h = m["height"]
    z0 = m["lo"][2]
    cx = m["centre_x"]
    y = m["foot_y"]

    def P(x, zf, yy=None):
        return Vector((x, y if yy is None else yy, z0 + h * zf))

    hip_z, sw = 0.52, m["shoulder_half"]
    hand_r = Vector(m["hand_r"]) if "hand_r" in m else P(cx + sw * 2.4, 0.62)
    hand_l = Vector(m["hand_l"]) if "hand_l" in m else P(cx - sw * 2.4, 0.62)
    sh_r, sh_l = P(cx + sw * 0.42, 0.815), P(cx - sw * 0.42, 0.815)
    arm_r, arm_l = P(cx + sw * 0.92, 0.80), P(cx - sw * 0.92, 0.80)
    elb_r = arm_r + (hand_r - arm_r) * 0.5
    elb_l = arm_l + (hand_l - arm_l) * 0.5

    bones = [
        ("Hips",          P(cx, hip_z),        P(cx, 0.60),        None),
        ("Spine",         P(cx, 0.60),         P(cx, 0.68),        "Hips"),
        ("Chest",         P(cx, 0.68),         P(cx, 0.78),        "Spine"),
        ("UpperChest",    P(cx, 0.78),         P(cx, 0.835),       "Chest"),
        ("Neck",          P(cx, 0.835),        P(cx, 0.885),       "UpperChest"),
        ("Head",          P(cx, 0.885),        P(cx, 0.98),        "Neck"),
        ("LeftShoulder",  P(cx, 0.815),        sh_l,               "UpperChest"),
        ("LeftUpperArm",  arm_l,               elb_l,              "LeftShoulder"),
        ("LeftLowerArm",  elb_l,               hand_l,             "LeftUpperArm"),
        ("LeftHand",      hand_l,              hand_l + (hand_l - elb_l).normalized() * h * 0.055, "LeftLowerArm"),
        ("RightShoulder", P(cx, 0.815),        sh_r,               "UpperChest"),
        ("RightUpperArm", arm_r,               elb_r,              "RightShoulder"),
        ("RightLowerArm", elb_r,               hand_r,             "RightUpperArm"),
        ("RightHand",     hand_r,              hand_r + (hand_r - elb_r).normalized() * h * 0.055, "RightLowerArm"),
        ("LeftUpperLeg",  P(m["foot_l_x"], hip_z),  P(m["foot_l_x"], 0.285), "Hips"),
        ("LeftLowerLeg",  P(m["foot_l_x"], 0.285),  P(m["foot_l_x"], 0.042), "LeftUpperLeg"),
        ("LeftFoot",      P(m["foot_l_x"], 0.042),  P(m["foot_l_x"], 0.012, m["toe_y"]), "LeftLowerLeg"),
        ("RightUpperLeg", P(m["foot_r_x"], hip_z),  P(m["foot_r_x"], 0.285), "Hips"),
        ("RightLowerLeg", P(m["foot_r_x"], 0.285),  P(m["foot_r_x"], 0.042), "RightUpperLeg"),
        ("RightFoot",     P(m["foot_r_x"], 0.042),  P(m["foot_r_x"], 0.012, m["toe_y"]), "RightLowerLeg"),
    ]
    # Shoulders start at the spine midline; nudge them out so they are not zero length.
    fixed = []
    for name, head, tail, parent in bones:
        if (tail - head).length < 1e-4:
            tail = head + Vector((0, 0, h * 0.02))
        fixed.append((name, head, tail, parent))
    return fixed


def build_armature(specs, name="Rig"):
    arm = bpy.data.armatures.new(name)
    obj = bpy.data.objects.new(name, arm)
    bpy.context.scene.collection.objects.link(obj)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    created = {}
    for bname, head, tail, parent in specs:
        eb = arm.edit_bones.new(bname)
        eb.head, eb.tail = head, tail
        created[bname] = eb
    for bname, _, _, parent in specs:
        if parent and parent in created:
            created[bname].parent = created[parent]
            created[bname].use_connect = False
    bpy.ops.object.mode_set(mode="OBJECT")
    return obj


def seg_distance(p, a, b):
    """Distance from p to segment ab, plus where along the segment it lands."""
    ab = b - a
    denom = ab.dot(ab)
    t = 0.0 if denom < 1e-12 else max(0.0, min(1.0, (p - a).dot(ab) / denom))
    return (p - (a + ab * t)).length, t


# A bone may only claim vertices within this fraction of body height. Limbs are thin,
# so without a cap they steal nearby cloth: in an A-pose the cloak hangs right beside
# the arms and between the legs, and gets torn apart when those limbs move.
INFLUENCE_RADIUS = {
    "Hand": 0.075, "LowerArm": 0.075, "UpperArm": 0.085, "Shoulder": 0.10,
    # Foot is deliberately tight: at 0.09 it reached above the ankle and claimed
    # boot-cuff vertices, which then sheared away from the shin when the foot rotated.
    "LowerLeg": 0.10, "UpperLeg": 0.12, "Foot": 0.055,
    "Head": 0.16, "Neck": 0.12, "Hips": 0.40, "Spine": 0.40,
    "Chest": 0.40, "UpperChest": 0.40,
}


def radius_for(bone_name, height):
    for key, frac in INFLUENCE_RADIUS.items():
        if key in bone_name:
            return frac * height
    return 0.25 * height


def skin(meshes, rig, report, influences=4, sharpness=2.0, smooth_passes=8, height=1.72):
    """
    Nearest-bone-segment weighting.

    Blender's bone-heat solver silently produces no vertex groups on non-manifold
    geometry, and generated meshes are reliably non-manifold, so weights are computed
    directly instead. Distance is to each bone's segment, the closest few bones share
    the vertex by inverse distance, then weights are relaxed across mesh edges.
    """
    bones = [(b.name, b.head_local.copy(), b.tail_local.copy(), radius_for(b.name, height))
             for b in rig.data.bones]
    torso = [b for b in bones if any(k in b[0] for k in ("Hips", "Spine", "Chest"))]

    for m in meshes:
        m.parent = rig
        m.matrix_parent_inverse = rig.matrix_world.inverted()
        if not any(mo.type == "ARMATURE" for mo in m.modifiers):
            mod = m.modifiers.new("Armature", "ARMATURE")
            mod.object = rig

        for g in list(m.vertex_groups):
            m.vertex_groups.remove(g)
        groups = {b[0]: m.vertex_groups.new(name=b[0]) for b in bones}

        to_rig = rig.matrix_world.inverted() @ m.matrix_world
        weights = []
        clipped = 0
        for v in m.data.vertices:
            p = to_rig @ v.co
            eligible, fallback = [], []
            for name, head, tail, radius in bones:
                d, _ = seg_distance(p, head, tail)
                fallback.append((d, name))
                if d <= radius:
                    eligible.append((d, name))

            if eligible:
                pool = sorted(eligible, key=lambda s: s[0])[:influences]
            else:
                # Cloth and hems sit outside every limb radius; hand them to the torso
                # so they follow the body instead of whichever limb happens to be near.
                pool = sorted(
                    [(seg_distance(p, h, t)[0], n) for n, h, t, _ in torso],
                    key=lambda s: s[0],
                )[:influences] or sorted(fallback, key=lambda s: s[0])[:influences]
                clipped += 1

            raw = [(name, 1.0 / ((d + 1e-4) ** sharpness)) for d, name in pool]
            total = sum(w for _, w in raw) or 1.0
            weights.append({name: w / total for name, w in raw})

        adj = [[] for _ in m.data.vertices]
        for e in m.data.edges:
            a, b = e.vertices
            adj[a].append(b)
            adj[b].append(a)

        # A vertex whose strongest bone matches none of its neighbours' is an island.
        # Posed, it shears away from the surface as a spike, which is what showed up
        # at the wrists and fingers. Replace those with the neighbourhood average.
        islands = 0
        for _ in range(2):
            dominant = [max(w, key=w.get) if w else None for w in weights]
            for i, w in enumerate(weights):
                if not adj[i] or dominant[i] is None:
                    continue
                if any(dominant[j] == dominant[i] for j in adj[i]):
                    continue
                acc = {}
                for j in adj[i]:
                    for k, val in weights[j].items():
                        acc[k] = acc.get(k, 0.0) + val
                s = sum(acc.values())
                if s > 0:
                    weights[i] = {k: v / s for k, v in acc.items()}
                    islands += 1

        # Relax across edges so joints bend smoothly instead of creasing.
        if smooth_passes > 0:
            for _ in range(smooth_passes):
                nxt = []
                for i, w in enumerate(weights):
                    acc = dict(w)
                    for j in adj[i]:
                        for k, val in weights[j].items():
                            acc[k] = acc.get(k, 0.0) + val
                    n = len(adj[i]) + 1
                    acc = {k: val / n for k, val in acc.items() if val / n > 1e-3}
                    s = sum(acc.values()) or 1.0
                    nxt.append({k: val / s for k, val in acc.items()})
                weights = nxt

        for i, w in enumerate(weights):
            for name, val in w.items():
                groups[name].add([i], val, "REPLACE")

        assigned = sum(1 for v in m.data.vertices if v.groups)
        report.setdefault("weighting", []).append({
            "mesh": m.name,
            "verts": len(m.data.vertices),
            "verts_weighted": assigned,
            "verts_given_to_torso": clipped,
            "weight_islands_fixed": islands,
            "influences_per_vert": influences,
            "sharpness": sharpness,
            "smooth_passes": smooth_passes,
        })

    report["skinning"] = "nearest_bone_segment"
    report["unweighted"] = [
        {"mesh": m.name,
         "bones_without_weights": [b.name for b in rig.data.bones
                                   if b.name not in {g.name for g in m.vertex_groups}]}
        for m in meshes
        if any(b.name not in {g.name for g in m.vertex_groups} for b in rig.data.bones)
    ]


def deform_test(rig, meshes, report):
    """Poses a few joints and confirms the mesh actually moves."""
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="POSE")
    tests = {"LeftUpperLeg": (-0.7, 0, 0), "RightLowerArm": (0, 0, -0.8), "Head": (0.4, 0, 0)}
    applied = []
    for bname, rot in tests.items():
        pb = rig.pose.bones.get(bname)
        if pb:
            pb.rotation_mode = "XYZ"
            pb.rotation_euler = rot
            applied.append(bname)
    bpy.context.view_layer.update()

    moved = {}
    depsgraph = bpy.context.evaluated_depsgraph_get()
    depsgraph.update()
    for m in meshes:
        # The evaluated object's own data carries the modifier result; m.data is rest pose.
        ev = m.evaluated_get(depsgraph)
        base = [v.co.copy() for v in m.data.vertices]
        posed = [v.co.copy() for v in ev.data.vertices]
        delta = max((a - b).length for a, b in zip(posed, base)) if base else 0.0
        moved[m.name] = round(delta, 5)
    report["deform_test"] = {"posed_bones": applied, "max_vertex_displacement": moved}

    for pb in rig.pose.bones:
        pb.rotation_euler = (0, 0, 0)
    bpy.ops.object.mode_set(mode="OBJECT")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--in", dest="inp", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--report", required=True)
    ap.add_argument("--height", type=float, default=1.72)
    ap.add_argument("--face-forward", default="+Y",
                    help="which axis the source model faces; rotated to -Y for Blender/Unity")
    args = ap.parse_args(argv_after_ddash())

    src = Path(args.inp).resolve()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    ext = src.suffix.lower()
    if ext in (".glb", ".gltf"):
        bpy.ops.import_scene.gltf(filepath=str(src))
    elif ext == ".fbx":
        bpy.ops.import_scene.fbx(filepath=str(src))
    else:
        raise SystemExit(f"unsupported input: {ext}")

    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    if not meshes:
        raise SystemExit("no meshes imported")

    report = {"input": str(src), "blender": bpy.app.version_string}

    # Join into one mesh so a single armature drives everything predictably.
    bpy.ops.object.select_all(action="DESELECT")
    for m in meshes:
        m.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    body = bpy.context.view_layer.objects.active
    body.name = "Ash_Body"
    meshes = [body]

    # TRELLIS orients the character facing +Y; Blender and the FBX export expect -Y.
    if args.face_forward.upper() in ("+Y", "Y"):
        body.rotation_euler = (0, 0, math.pi)
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
        report["rotated_180_z"] = True

    # Normalise height and drop feet to the origin before measuring for bones.
    pts = world_verts(meshes)
    zs = [p.z for p in pts]
    xs = [p.x for p in pts]
    cur_h = max(zs) - min(zs)
    factor = args.height / cur_h if cur_h > 1e-6 else 1.0
    body.scale = (factor, factor, factor)
    body.location = (-(sum(xs) / len(xs)) * factor, body.location.y, -min(zs) * factor)
    bpy.ops.object.transform_apply(location=True, rotation=False, scale=True)
    report["scale_factor"] = round(factor, 4)

    m = measure(world_verts(meshes))
    report["landmarks"] = {k: (round(v, 4) if isinstance(v, float) else v) for k, v in m.items()}

    rig = build_armature(skeleton(m))
    report["bones"] = [b.name for b in rig.data.bones]

    skin(meshes, rig, report, height=args.height)
    deform_test(rig, meshes, report)

    out = Path(args.out).resolve()
    out.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.fbx(
        filepath=str(out),
        use_selection=True,
        apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z",
        axis_up="Y",
        add_leaf_bones=False,
        bake_anim=False,
        mesh_smooth_type="FACE",
        path_mode="COPY",
        embed_textures=True,
        primary_bone_axis="Y",
        secondary_bone_axis="X",
    )
    report["output"] = str(out)

    rep = Path(args.report).resolve()
    rep.parent.mkdir(parents=True, exist_ok=True)
    rep.write_text(json.dumps(report, indent=2))
    print("=== RIG REPORT ===")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
