"""
Sets up the viewport for inspecting an already-open character.

  blender <scene.blend> --python _Tools/blender_open_character.py

Frames the model and switches to material preview so textures show, rather than grey
clay. Select the Rig and enter Pose Mode to bend joints by hand.

This deliberately does no importing. Importing FBX from a GUI session fails, because the
importer calls mode_set(mode='EDIT') to build the armature and that poll needs an active
object the GUI context does not supply, which left Blender open but empty. Use
blender_to_blend.py to import headlessly first.

Viewport work runs from a timer: at the moment a --python script executes the window
still reports no 3D viewport areas, so doing it inline silently does nothing.
"""

import bpy
from mathutils import Vector


def mesh_bounds(meshes):
    lo = Vector((1e9,) * 3)
    hi = Vector((-1e9,) * 3)
    for m in meshes:
        for corner in m.bound_box:
            p = m.matrix_world @ Vector(corner)
            lo = Vector((min(lo.x, p.x), min(lo.y, p.y), min(lo.z, p.z)))
            hi = Vector((max(hi.x, p.x), max(hi.y, p.y), max(hi.z, p.z)))
    return (lo + hi) * 0.5, max(hi.z - lo.z, 1e-3)


def viewports():
    found = []
    for window in bpy.context.window_manager.windows:
        for area in window.screen.areas:
            if area.type != "VIEW_3D":
                continue
            region = next((r for r in area.regions if r.type == "WINDOW"), None)
            if region:
                found.append((window, area, region))
    return found


def setup():
    views = viewports()
    if not views:
        return 0.25  # UI not ready yet; the timer will call back.

    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    rigs = [o for o in bpy.context.scene.objects if o.type == "ARMATURE"]

    # Bones draw over the mesh and hide exactly the surface being judged. Unhide the Rig
    # in the outliner to pose it.
    for r in rigs:
        r.hide_viewport = True

    if meshes:
        bpy.context.view_layer.objects.active = meshes[0]
        for o in bpy.context.scene.objects:
            o.select_set(True)

    for window, area, region in views:
        for space in area.spaces:
            if space.type == "VIEW_3D":
                space.shading.type = "MATERIAL"
                space.overlay.show_relationship_lines = False
                space.overlay.show_floor = False
                space.overlay.show_axis_x = False
                space.overlay.show_axis_y = False
                space.overlay.show_cursor = False
                space.clip_start = 0.01
                space.clip_end = 1000.0
        with bpy.context.temp_override(window=window, area=area, region=region):
            bpy.ops.view3d.view_axis(type="FRONT")

        # Framed by measurement, not view_all: that left the character a couple of
        # hundred pixels tall in a 2500px viewport, too small to judge anything.
        centre, height = mesh_bounds(meshes)
        for space in area.spaces:
            if space.type == "VIEW_3D":
                rv3d = space.region_3d
                rv3d.view_location = centre
                rv3d.view_distance = height * 1.45

    # Deselect so no orange outline obscures the silhouette.
    for o in bpy.context.scene.objects:
        o.select_set(False)

    for m in meshes:
        print(f"[open_character] mesh {m.name}: {len(m.data.vertices)} verts, "
              f"{len(m.data.polygons)} faces")
    for r in rigs:
        print(f"[open_character] armature {r.name}: {len(r.data.bones)} bones")
    print(f"[open_character] framed {len(meshes)} mesh(es) in {len(views)} viewport(s)")
    return None


bpy.app.timers.register(setup, first_interval=0.3)
