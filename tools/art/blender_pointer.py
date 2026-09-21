"""Renders the daily wheel pointer: a gold arrow head pointing DOWN into the wheel.

    blender -b --python tools/art/blender_pointer.py -- <output_dir> <hdri>

The previous pointer was a procedural triangle rotated by 180 degrees, and it ended up pointing the wrong way on the
device. Rendering it with the tip explicitly at the bottom of the frame removes the guesswork: the sprite is used with
no rotation at all.
"""
import math
import os
import sys

import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = os.path.abspath(argv[0] if argv else "pointer")
HDRI = os.path.abspath(argv[1]) if len(argv) > 1 else ""
os.makedirs(OUT, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.render.engine = "CYCLES"
scene.cycles.samples = 96
scene.cycles.use_denoising = True
scene.render.film_transparent = True
scene.render.resolution_x = 192
scene.render.resolution_y = 256
scene.render.image_settings.file_format = "PNG"
scene.render.image_settings.color_mode = "RGBA"
scene.view_settings.view_transform = "Standard"

world = bpy.data.worlds.new("w")
world.use_nodes = True
bg = next(n for n in world.node_tree.nodes if n.type == "BACKGROUND")
if HDRI and os.path.exists(HDRI):
    env = world.node_tree.nodes.new("ShaderNodeTexEnvironment")
    env.image = bpy.data.images.load(HDRI)
    world.node_tree.links.new(env.outputs["Color"], bg.inputs["Color"])
bg.inputs["Strength"].default_value = 1.0
scene.world = world


def material(name, color, metallic, roughness):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    b = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    b.inputs["Base Color"].default_value = (*color, 1.0)
    b.inputs["Metallic"].default_value = metallic
    b.inputs["Roughness"].default_value = roughness
    return mat


gold = material("gold", (1.0, 0.72, 0.28), 1.0, 0.18)
ruby = material("ruby", (0.75, 0.02, 0.1), 0.0, 0.08)

# Cone pointing down (-Z), tip at the bottom of the frame.
bpy.ops.mesh.primitive_cone_add(vertices=4, radius1=0.62, radius2=0, depth=1.15, location=(0, 0, 0.1), rotation=(math.radians(180), 0, math.radians(45)))
tip = bpy.context.active_object
tip.data.materials.append(gold)
bevel = tip.modifiers.new("bevel", "BEVEL")
bevel.width = 0.04
bevel.segments = 3
bpy.ops.object.shade_smooth()

# Collar and a ruby cabochon on top, so it reads as a jewelled pointer and not a plain triangle.
bpy.ops.mesh.primitive_cylinder_add(vertices=32, radius=0.42, depth=0.22, location=(0, 0, 0.72))
collar = bpy.context.active_object
collar.data.materials.append(gold)
bpy.ops.object.shade_smooth()
bpy.ops.mesh.primitive_uv_sphere_add(segments=32, ring_count=16, radius=0.26, location=(0, 0, 0.86))
gem = bpy.context.active_object
gem.scale = (1, 1, 0.75)
gem.data.materials.append(ruby)
bpy.ops.object.shade_smooth()

cam_data = bpy.data.cameras.new("cam")
cam_data.type = "ORTHO"
cam_data.ortho_scale = 1.6
cam = bpy.data.objects.new("cam", cam_data)
cam.location = (0, -6, 0.25)
cam.rotation_euler = (math.radians(90), 0, 0)
scene.collection.objects.link(cam)
scene.camera = cam

for name, energy, location in (("key", 220, (-2.5, -3.0, 3.0)), ("rim", 140, (2.5, -2.0, 2.0)), ("fill", 60, (0, -4.0, -2.0))):
    data = bpy.data.lights.new(name, "AREA")
    data.energy = energy
    data.size = 3.0
    obj = bpy.data.objects.new(name, data)
    obj.location = location
    obj.rotation_euler = (Vector((0, 0, 0)) - Vector(location)).to_track_quat("-Z", "Y").to_euler()
    scene.collection.objects.link(obj)

scene.render.filepath = os.path.join(OUT, "wheel_pointer.png")
bpy.ops.render.render(write_still=True)
print("rendered wheel_pointer")
