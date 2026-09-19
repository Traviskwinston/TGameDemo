"""
Blender stage of the 2D -> 3D pipeline. Run headless:

  blender --background --python _Tools/blender_inspect_clean.py -- \
      --in  _Concept/Ash/generated/ash_raw_pbr.glb \
      --out Assets/TGameDemo/Characters/AshAI/Ash.fbx \
      --report _Concept/Ash/generated/blender_report.json \
      --target-tris 12000 --height 1.72

Inspects the generated asset, repairs what is safely repairable, optimises it for
real-time use, and exports FBX for Unity. It reports problems it cannot fix rather
than rebuilding geometry, because the generated mesh is the asset.
"""

import argparse
import json
import sys
from pathlib import Path

import bmesh
import bpy
from mathutils import Vector


def argv_after_ddash():
    return sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def do_import(path: Path):
    ext = path.suffix.lower()
    if ext in (".glb", ".gltf"):
        bpy.ops.import_scene.gltf(filepath=str(path))
    elif ext == ".fbx":
        bpy.ops.import_scene.fbx(filepath=str(path))
    elif ext == ".obj":
        bpy.ops.wm.obj_import(filepath=str(path))
    else:
        raise SystemExit(f"unsupported input: {ext}")
    return [o for o in bpy.context.scene.objects if o.type == "MESH"]


def mesh_stats(obj):
    """Geometry health for one mesh, measured on an evaluated copy."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.verts.ensure_lookup_table()
    bm.edges.ensure_lookup_table()

    non_manifold = sum(1 for e in bm.edges if not e.is_manifold)
    boundary = sum(1 for e in bm.edges if e.is_boundary)
    loose_verts = sum(1 for v in bm.verts if not v.link_edges)
    tris = sum(len(f.verts) - 2 for f in bm.faces)
    ngons = sum(1 for f in bm.faces if len(f.verts) > 4)
    quads = sum(1 for f in bm.faces if len(f.verts) == 4)
    stats = {
        "name": obj.name,
        "verts": len(bm.verts),
        "faces": len(bm.faces),
        "tris": tris,
        "quads": quads,
        "ngons": ngons,
        "non_manifold_edges": non_manifold,
        "boundary_edges": boundary,
        "loose_verts": loose_verts,
        "uv_layers": [l.name for l in obj.data.uv_layers],
        "materials": [m.name for m in obj.data.materials if m],
        "shape_keys": bool(obj.data.shape_keys),
        "has_armature_deform": any(m.type == "ARMATURE" for m in obj.modifiers),
        "vertex_groups": len(obj.vertex_groups),
    }
    bm.free()
    return stats


def clean(obj, report):
    """Repairs that never change the silhouette: dedupe, normals, loose geometry."""
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)

    before = len(obj.data.vertices)
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_edges], context="VERTS")
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()

    report.setdefault("cleaned", []).append({
        "name": obj.name,
        "verts_before": before,
        "verts_after": len(obj.data.vertices),
        "normals_recalculated": True,
    })


def decimate(obj, target_tris, report):
    current = sum(len(p.vertices) - 2 for p in obj.data.polygons)
    if target_tris <= 0 or current <= target_tris:
        return
    ratio = max(0.02, target_tris / float(current))
    mod = obj.modifiers.new("GameDecimate", "DECIMATE")
    mod.decimate_type = "COLLAPSE"
    mod.ratio = ratio
    mod.use_collapse_triangulate = True
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=mod.name)
    after = sum(len(p.vertices) - 2 for p in obj.data.polygons)
    report.setdefault("decimated", []).append(
        {"name": obj.name, "from": current, "to": after, "ratio": round(ratio, 4)})


def ensure_uvs(obj, report):
    if obj.data.uv_layers:
        return
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=1.15, island_margin=0.02)
    bpy.ops.object.mode_set(mode="OBJECT")
    report.setdefault("uv_generated", []).append(obj.name)


def normalise_scale(meshes, target_height, report):
    """Puts the character at game scale, feet on the origin, facing -Y (Unity +Z)."""
    if not meshes or target_height <= 0:
        return

    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for obj in meshes:
        for corner in obj.bound_box:
            p = obj.matrix_world @ Vector(corner)
            lo = Vector((min(lo.x, p.x), min(lo.y, p.y), min(lo.z, p.z)))
            hi = Vector((max(hi.x, p.x), max(hi.y, p.y), max(hi.z, p.z)))

    height = hi.z - lo.z
    if height < 1e-4:
        return

    factor = target_height / height
    root = bpy.data.objects.new("AshRoot", None)
    bpy.context.scene.collection.objects.link(root)
    for obj in meshes:
        if obj.parent is None:
            obj.parent = root
    root.scale = (factor, factor, factor)
    root.location = (-(lo.x + hi.x) * 0.5 * factor, -(lo.y + hi.y) * 0.5 * factor, -lo.z * factor)

    report["scale"] = {
        "source_height": round(height, 4),
        "target_height": target_height,
        "factor": round(factor, 4),
    }


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--in", dest="inp", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--report", required=True)
    ap.add_argument("--target-tris", type=int, default=12000)
    ap.add_argument("--height", type=float, default=1.72)
    args = ap.parse_args(argv_after_ddash())

    src = Path(args.inp).resolve()
    if not src.exists():
        raise SystemExit(f"input not found: {src}")

    reset_scene()
    meshes = do_import(src)
    if not meshes:
        raise SystemExit("import produced no mesh objects")

    report = {
        "input": str(src),
        "blender": bpy.app.version_string,
        "before": [mesh_stats(o) for o in meshes],
        "armatures": [o.name for o in bpy.context.scene.objects if o.type == "ARMATURE"],
    }

    bpy.ops.object.select_all(action="DESELECT")
    for obj in meshes:
        clean(obj, report)
        ensure_uvs(obj, report)
        decimate(obj, args.target_tris, report)

    normalise_scale(meshes, args.height, report)
    report["after"] = [mesh_stats(o) for o in meshes]

    # Problems worth a human eye: these are not safely auto-fixable.
    warnings = []
    for s in report["after"]:
        if s["non_manifold_edges"]:
            warnings.append(f"{s['name']}: {s['non_manifold_edges']} non-manifold edges")
        if s["boundary_edges"]:
            warnings.append(f"{s['name']}: {s['boundary_edges']} boundary edges (holes or intended openings)")
        if not s["uv_layers"]:
            warnings.append(f"{s['name']}: no UVs")
        if not s["materials"]:
            warnings.append(f"{s['name']}: no material")
    if not report["armatures"]:
        warnings.append("no armature: needs rigging before it can animate")
    report["warnings"] = warnings

    out = Path(args.out).resolve()
    out.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.fbx(
        filepath=str(out),
        use_selection=True,
        apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z",
        axis_up="Y",
        mesh_smooth_type="FACE",
        add_leaf_bones=False,
        bake_anim=bool(report["armatures"]),
        path_mode="COPY",
        embed_textures=True,
    )
    report["output"] = str(out)

    rep = Path(args.report).resolve()
    rep.parent.mkdir(parents=True, exist_ok=True)
    rep.write_text(json.dumps(report, indent=2))

    print("=== BLENDER REPORT ===")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
