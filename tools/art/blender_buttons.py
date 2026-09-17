"""Renders the UI kit buttons (gold, purple, green, red) as chunky 3D game buttons with Blender, headless.

    blender -b --python tools/art/blender_buttons.py -- <output_dir> <hdri>

Rounded pill body with a domed glossy face, a darker lip underneath (the "pressable" thickness), a metallic gold rim
and a soft highlight band. Rendered 2x then downscaled to 468 x 160 (9-slice friendly: corners stay inside the borders).
"""
import math
import os
import sys

import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = os.path.abspath(argv[0] if argv else "buttons")
HDRI = os.path.abspath(argv[1]) if len(argv) > 1 else ""
os.makedirs(OUT, exist_ok=True)

STYLES = {
    # name: (face colour, lip colour)
    "gold": ((1.0, 0.42, 0.0), (0.45, 0.14, 0.0)),
    "purple": ((0.22, 0.05, 0.62), (0.08, 0.01, 0.24)),
    "green": ((0.02, 0.5, 0.08), (0.01, 0.2, 0.03)),
    "red": ((0.75, 0.01, 0.04), (0.3, 0.0, 0.02)),
}

W, H, R = 4.6, 1.52, 0.62


def principled(name, color, metallic=0.0, roughness=0.2, coat=0.0, emission=0.0, alpha=1.0):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = roughness
    if "Coat Weight" in bsdf.inputs:
        bsdf.inputs["Coat Weight"].default_value = coat
        bsdf.inputs["Coat Roughness"].default_value = 0.03
    if emission > 0:
        bsdf.inputs["Emission Color"].default_value = (*color, 1.0)
        bsdf.inputs["Emission Strength"].default_value = emission
    if alpha < 1.0:
        bsdf.inputs["Alpha"].default_value = alpha
        mat.blend_method = "BLEND" if hasattr(mat, "blend_method") else mat.blend_method
    return mat


def rounded_slab(name, width, height, radius, thickness, edge, location, material):
    """Rounded rectangle extruded, its top edges bevelled into a dome."""
    bpy.ops.mesh.primitive_plane_add(size=1, location=location)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = (width, height, 1)
    bpy.ops.object.transform_apply(scale=True)
    corners = obj.modifiers.new("corners", "BEVEL")
    corners.affect = "VERTICES"
    corners.width = radius
    corners.segments = 16
    solid = obj.modifiers.new("solid", "SOLIDIFY")
    solid.thickness = thickness
    solid.offset = 1.0
    dome = obj.modifiers.new("dome", "BEVEL")
    dome.width = edge
    dome.segments = 8
    dome.limit_method = "ANGLE"
    dome.profile = 0.6
    obj.data.materials.append(material)
    bpy.ops.object.shade_smooth()
    return obj


def studio():
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 64
    scene.cycles.use_denoising = True
    scene.render.film_transparent = True
    scene.render.resolution_x = 936
    scene.render.resolution_y = 320
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.view_settings.view_transform = "Standard"

    world = bpy.data.worlds.new("studio")
    world.use_nodes = True
    nodes = world.node_tree.nodes
    if HDRI and os.path.exists(HDRI):
        env = nodes.new("ShaderNodeTexEnvironment")
        env.image = bpy.data.images.load(HDRI)
        world.node_tree.links.new(env.outputs["Color"], nodes["Background"].inputs["Color"])
    nodes["Background"].inputs["Strength"].default_value = 0.35
    scene.world = world

    cam_data = bpy.data.cameras.new("cam")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = 5.2
    cam = bpy.data.objects.new("cam", cam_data)
    cam.location = (0, 0, 10)
    scene.collection.objects.link(cam)
    scene.camera = cam

    key = bpy.data.lights.new("key", "AREA")
    key.energy = 260
    key.size = 6
    key_obj = bpy.data.objects.new("key", key)
    key_obj.location = (-1.5, 2.5, 6)
    key_obj.rotation_euler = (Vector((0, 0, 0)) - Vector(key_obj.location)).to_track_quat("-Z", "Y").to_euler()
    scene.collection.objects.link(key_obj)


bpy.ops.wm.read_factory_settings(use_empty=True)
studio()

gold = principled("gold_rim", (1.0, 0.74, 0.3), metallic=1.0, roughness=0.25)
shine = principled("shine", (1.0, 1.0, 1.0), roughness=0.0, emission=0.25, alpha=0.18)

for style, (face_color, lip_color) in STYLES.items():
    for obj in [o for o in bpy.context.scene.objects if o.type == "MESH"]:
        bpy.data.objects.remove(obj, do_unlink=True)
    rounded_slab("rim", W + 0.2, H + 0.2, R + 0.1, 0.18, 0.06, (0, -0.02, 0.0), gold)
    rounded_slab("lip", W - 0.02, H - 0.02, R - 0.01, 0.22, 0.05, (0, -0.07, 0.12), principled(style + "_lip", lip_color, roughness=0.35))
    rounded_slab("face", W - 0.1, H - 0.2, R - 0.06, 0.26, 0.16, (0, 0.03, 0.2), principled(style + "_face", face_color, roughness=0.18, coat=1.0))
    rounded_slab("shine", W - 0.9, 0.26, 0.13, 0.01, 0.0, (0, 0.36, 0.5), shine)
    bpy.context.scene.render.filepath = os.path.join(OUT, "button_" + style + ".png")
    bpy.ops.render.render(write_still=True)
    print("rendered", style)
