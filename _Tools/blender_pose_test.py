"""
Poses a rigged character and renders it, so joint deformation can actually be seen.

  blender --background --python _Tools/blender_pose_test.py -- \
      --in _Concept/Ash/generated/Ash_rigged.fbx --out _Concept/Ash/generated/pose

Bend directions are measured, not assumed. Hand-picking rotation axes produced a
knee bending forwards and elbows bending backwards, because the correct sign depends
on each bone's rest orientation. Each hinge is probed in both directions and the sign
that moves the limb the anatomically correct way is kept.

Character faces -Y after the rig step, so: knees send the ankle backwards (+Y),
elbows send the wrist forwards (-Y).
"""

import argparse
import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector

# joint -> (tip bone we watch, axis to probe, whether the tip should travel forwards)
# A knee sends the ankle backwards; an elbow sends the wrist forwards.
HINGES = {
    "LeftLowerLeg":  ("LeftFoot",  "X", False),
    "RightLowerLeg": ("RightFoot", "X", False),
    "LeftLowerArm":  ("LeftHand",  "X", True),
    "RightLowerArm": ("RightHand", "X", True),
}


def detect_forward(rig):
    """Facing read off the foot bones, which run ankle -> toe.

    Export round-trips flip the model's world orientation, so a hardcoded forward axis
    silently inverts every calibration result. Measuring it keeps this stage correct
    regardless of which way the incoming file happens to face.
    """
    acc = Vector((0.0, 0.0, 0.0))
    for name in ("LeftFoot", "RightFoot"):
        pb = rig.pose.bones.get(name)
        if pb is None:
            continue
        d = (rig.matrix_world @ pb.tail) - (rig.matrix_world @ pb.head)
        acc += Vector((d.x, d.y, 0.0))
    if acc.length < 1e-6:
        return Vector((0.0, -1.0, 0.0))
    return acc.normalized()


def argv_after_ddash():
    return sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def clear_pose(rig):
    for pb in rig.pose.bones:
        pb.rotation_mode = "XYZ"
        pb.rotation_euler = (0, 0, 0)
    bpy.context.view_layer.update()


def tip_world(rig, bone_name):
    pb = rig.pose.bones.get(bone_name)
    if pb is None:
        return None
    return rig.matrix_world @ pb.tail


AXIS_INDEX = {"X": 0, "Y": 1, "Z": 2}


def probe_sign(rig, joint, tip, axis, want, probe=math.radians(35)):
    """Rotates one joint both ways and reports which sign moves `tip` toward `want`."""
    pb = rig.pose.bones.get(joint)
    if pb is None or tip_world(rig, tip) is None:
        return None, {}
    clear_pose(rig)
    base = tip_world(rig, tip).copy()
    results = {}
    for sign in (1, -1):
        clear_pose(rig)
        rot = [0.0, 0.0, 0.0]
        rot[AXIS_INDEX[axis]] = probe * sign
        pb.rotation_euler = rot
        bpy.context.view_layer.update()
        results[sign] = (tip_world(rig, tip) - base).dot(want)
    clear_pose(rig)
    best = max(results, key=results.get)
    return best, {sign: round(v, 4) for sign, v in results.items()}


def calibrate(rig, forward, centre):
    """Measures every rotation direction the poses rely on.

    Covers shoulders as well as hinges: aiming needs the upper arm to swing forwards and
    inwards, and picking those axes by hand is what buried the hands in the cloak.
    """
    side = Vector((-forward.y, forward.x, 0.0))
    signs, detail = {}, {}

    specs = []
    for joint, (tip, axis, wants_forward) in HINGES.items():
        specs.append((joint, tip, axis, forward if wants_forward else -forward, "bend"))

    for joint, tip in (("LeftUpperArm", "LeftHand"), ("RightUpperArm", "RightHand")):
        rest = tip_world(rig, tip)
        if rest is None:
            continue
        specs.append((joint, tip, "X", forward, "swing_forward"))
        # Inward means toward the body midline; which way that is depends on the side
        # the hand actually sits on, so read it off the rest pose.
        toward_mid = -side if (rest - centre).dot(side) > 0 else side
        specs.append((joint, tip, "Z", toward_mid, "swing_inward"))

    for joint, tip, axis, want, label in specs:
        best, scores = probe_sign(rig, joint, tip, axis, want)
        if best is None:
            continue
        signs[(joint, axis)] = best
        detail[f"{joint}.{axis}"] = {"intent": label, "chosen_sign": best,
                                     "score_plus": scores.get(1),
                                     "score_minus": scores.get(-1)}
    return signs, detail


def build_poses(signs):
    """Poses expressed as magnitudes; calibration supplies every direction."""

    def hinge(joint, degrees):
        return (math.radians(degrees) * signs.get((joint, "X"), 1), 0.0, 0.0)

    def shoulder(joint, forward_deg, inward_deg):
        return (math.radians(forward_deg) * signs.get((joint, "X"), 1),
                0.0,
                math.radians(inward_deg) * signs.get((joint, "Z"), 1))

    # Walk-cycle angles, not a lunge. The previous extreme stride exaggerated shear at
    # the ankle and told us little about how the rig behaves in normal locomotion.
    stride = {
        "LeftUpperLeg": (math.radians(-17), 0, 0),
        "RightUpperLeg": (math.radians(14), 0, 0),
        "LeftLowerLeg": hinge("LeftLowerLeg", 8),
        "RightLowerLeg": hinge("RightLowerLeg", 26),
        "LeftUpperArm": (math.radians(12), 0, 0),
        "RightUpperArm": (math.radians(-10), 0, 0),
        "LeftLowerArm": hinge("LeftLowerArm", 16),
        "RightLowerArm": hinge("RightLowerArm", 20),
        "Spine": (math.radians(3), 0, 0),
    }
    # Two-handed aim: both arms up and forward, gun hand leading, support arm folded
    # further across so the hands meet near the centreline.
    aim = {
        "RightUpperArm": shoulder("RightUpperArm", 62, 22),
        "RightLowerArm": hinge("RightLowerArm", 38),
        "LeftUpperArm": shoulder("LeftUpperArm", 48, 40),
        "LeftLowerArm": hinge("LeftLowerArm", 62),
        "Spine": (0, 0, 0),
    }
    return {"rest": {}, "stride": stride, "aim": aim}


def setup_render(scene, centre, height, res):
    for engine in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE", "BLENDER_WORKBENCH"):
        try:
            scene.render.engine = engine
            break
        except TypeError:
            continue
    scene.render.resolution_x = res
    scene.render.resolution_y = int(res * 1.4)
    scene.view_settings.view_transform = "Standard"

    world = bpy.data.worlds.new("W")
    scene.world = world
    bg = world.node_tree.nodes["Background"]
    bg.inputs[0].default_value = (0.3, 0.3, 0.32, 1)
    bg.inputs[1].default_value = 2.0

    for name, loc, energy in (("key", (3, -4, 3.5), 1100), ("fill", (-3.5, -2.5, 1.5), 450)):
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
    return cam


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--in", dest="inp", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--report", default=None)
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
        raise SystemExit(f"need armature + mesh; got {len(rigs)} rigs, {len(meshes)} meshes")
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

    cam = setup_render(bpy.context.scene, centre, height, args.res)
    bpy.context.view_layer.objects.active = rig

    forward = detect_forward(rig)
    print(f"FORWARD (from foot bones): ({forward.x:+.2f}, {forward.y:+.2f})")
    signs, detail = calibrate(rig, forward, centre)
    print("CALIBRATION:")
    for key, d in detail.items():
        print(f"  {key:22} {d['intent']:14} sign={d['chosen_sign']:+d} "
              f"(+{d['score_plus']} / {d['score_minus']})")

    scene = bpy.context.scene
    for pose_name, pose in build_poses(signs).items():
        clear_pose(rig)
        for bone, rot in pose.items():
            pb = rig.pose.bones.get(bone)
            if pb:
                pb.rotation_euler = rot
            else:
                print(f"  WARNING: no bone {bone}")
        bpy.context.view_layer.update()

        # Views defined relative to measured facing, so "front" is always the face.
        side = Vector((-forward.y, forward.x, 0.0))
        for view_name, deg in (("front", 0.0), ("q", 35.0), ("side", 90.0)):
            rad = math.radians(deg)
            offset = (forward * math.cos(rad) + side * math.sin(rad)) * (height * 2.4)
            cam.location = centre + offset
            cam.rotation_euler = (centre - cam.location).to_track_quat("-Z", "Y").to_euler()
            scene.render.filepath = str(out / f"{pose_name}_{view_name}.png")
            bpy.ops.render.render(write_still=True)
            print(f"  rendered {pose_name}_{view_name}")

    if args.report:
        rep = Path(args.report).resolve()
        rep.parent.mkdir(parents=True, exist_ok=True)
        rep.write_text(json.dumps({"calibration": detail}, indent=2))


if __name__ == "__main__":
    main()
