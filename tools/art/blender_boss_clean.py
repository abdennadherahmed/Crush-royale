"""Strips the scenery an image-to-3D generator modelled along with the character.

    blender.exe -b --python tools/art/blender_boss_clean.py -- <in.glb> <out.glb> --axis <x|y|z> --behind <value>
    blender.exe -b --python tools/art/blender_boss_clean.py -- <in.glb> --report

The reference images are illustrations, so the generator happily turns the cathedral behind the boss into geometry.
It comes back as hundreds of loose islands with no clean volume or gap to sort them by, so the only honest rule is
spatial: the card shows the boss from the front, and everything past a plane behind him can go. --report prints the
spread along each axis so that plane can be chosen by looking at a turnaround rather than by guessing.
"""
import sys

import bpy
from mathutils import Vector

AXES = {"x": 0, "y": 1, "z": 2}


def argv():
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if not args:
        raise SystemExit("usage: blender -b --python blender_boss_clean.py -- <in.glb> [out.glb] "
                         "[--report] [--axis x] [--behind -0.05]")
    source = args[0]
    target = args[1] if len(args) > 1 and not args[1].startswith("--") else None
    axis = args[args.index("--axis") + 1] if "--axis" in args else "x"
    behind = float(args[args.index("--behind") + 1]) if "--behind" in args else None
    return source, target, "--report" in args, AXES[axis], behind


def part_bounds(obj):
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for corner in obj.bound_box:
        world = obj.matrix_world @ Vector(corner)
        lo = Vector((min(lo[i], world[i]) for i in range(3)))
        hi = Vector((max(hi[i], world[i]) for i in range(3)))
    return lo, hi


def split_loose():
    for obj in [o for o in bpy.context.scene.objects if o.type == "MESH"]:
        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        bpy.ops.mesh.separate(type="LOOSE")
    return [o for o in bpy.context.scene.objects if o.type == "MESH"]


def main():
    source, target, report, axis, behind = argv()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=source)
    if not [o for o in bpy.context.scene.objects if o.type == "MESH"]:
        raise SystemExit("no mesh in " + source)

    parts = split_loose()
    measured = [(obj,) + part_bounds(obj) for obj in parts]

    if report:
        print("PARTS %d" % len(measured))
        for name, index in (("x", 0), ("y", 1), ("z", 2)):
            centres = sorted((lo[index] + hi[index]) * 0.5 for _, lo, hi in measured)
            print("AXIS %s min %.3f max %.3f median %.3f"
                  % (name, centres[0], centres[-1], centres[len(centres) // 2]))
        return

    if behind is None:
        raise SystemExit("--behind is required when writing a cleaned model")

    removed = 0
    for obj, lo, hi in measured:
        if (lo[axis] + hi[axis]) * 0.5 < behind:
            bpy.data.objects.remove(obj, do_unlink=True)
            removed += 1
    print("removed %d part(s) behind %.3f, kept %d" % (removed, behind, len(measured) - removed))

    if target:
        bpy.ops.object.select_all(action="SELECT")
        bpy.ops.export_scene.gltf(filepath=target, export_format="GLB", use_selection=True)
        print("wrote", target)


main()
