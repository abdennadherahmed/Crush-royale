"""Renders the six board gems as real 3D faceted jewels (pre-rendered 2.5D sprites) with Blender, headless.

    blender -b --python tools/art/blender_gems.py -- <output_dir> [size]

Each colour keeps its own silhouette (colour-blind friendly): round brilliant (red), rhombus (blue), emerald cut (green),
star (yellow), hexagon (purple), triangle (orange). Transparent PNGs, lit by a soft studio (key, rim, fill).
"""
import math
import os
import sys

import bpy
import bmesh
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = os.path.abspath(argv[0] if argv else "gems3d")
SIZE = int(argv[1]) if len(argv) > 1 else 512
HDRI = os.path.abspath(argv[2]) if len(argv) > 2 else os.path.join(os.path.dirname(OUT), "HDRI", "studio_small_09_1k.hdr")
os.makedirs(OUT, exist_ok=True)

GEMS = {
    # name: (glass colour, emission tint) - glass lightens colours, so bases are deep and saturated
    "red": ((0.8, 0.0, 0.03), (1.0, 0.05, 0.1)),
    "blue": ((0.0, 0.12, 0.95), (0.15, 0.35, 1.0)),
    "green": ((0.0, 0.6, 0.1), (0.15, 0.9, 0.3)),
    "yellow": ((1.0, 0.78, 0.0), (1.0, 0.85, 0.05)),
    "purple": ((0.3, 0.0, 0.8), (0.5, 0.15, 1.0)),
    "orange": ((1.0, 0.16, 0.0), (1.0, 0.3, 0.0)),
}


def polygon(name):
    """Girdle outline (x, y) of each cut, roughly within a unit circle."""
    if name == "red":
        return [(math.cos(a), math.sin(a)) for a in (i * 2 * math.pi / 16 for i in range(16))]
    if name == "blue":
        return [(0, 1.08), (0.78, 0), (0, -1.08), (-0.78, 0)]
    if name == "green":
        c = 0.28
        return [(-0.82 + c, 0.95), (0.82 - c, 0.95), (0.82, 0.95 - c), (0.82, -0.95 + c),
                (0.82 - c, -0.95), (-0.82 + c, -0.95), (-0.82, -0.95 + c), (-0.82, 0.95 - c)]
    if name == "yellow":
        pts = []
        for i in range(10):
            a = math.pi / 2 + i * math.pi / 5
            r = 1.05 if i % 2 == 0 else 0.5
            pts.append((r * math.cos(a), r * math.sin(a)))
        return pts
    if name == "purple":
        return [(math.cos(a), math.sin(a)) for a in (math.pi / 2 + i * math.pi / 3 for i in range(6))]
    # orange: triangle with softened corners
    pts = []
    for i in range(3):
        a = math.pi / 2 + i * 2 * math.pi / 3
        for d in (-0.16, 0.16):
            pts.append((1.1 * math.cos(a + d), 1.1 * math.sin(a + d) - 0.1))
    return pts


def build_gem(name):
    outline = [Vector((x, y, 0.0)) for x, y in polygon(name)]
    n = len(outline)
    center = sum(outline, Vector()) / n
    mesh = bpy.data.meshes.new(name)
    bm = bmesh.new()
    girdle_top = [bm.verts.new(v + Vector((0, 0, 0.06))) for v in outline]
    girdle_bottom = [bm.verts.new(v - Vector((0, 0, 0.06))) for v in outline]
    table_scale = 0.3 if name == "yellow" else 0.56
    table = [bm.verts.new(center + (v - center) * table_scale + Vector((0, 0, 0.42))) for v in outline]
    # Crown mid ring (star facets) makes the top sparkle more.
    mid = []
    for i in range(n):
        a, b = outline[i], outline[(i + 1) % n]
        m = (a + b) / 2
        mid.append(bm.verts.new(center + (m - center) * 0.8 + Vector((0, 0, 0.26))))
    lower = [bm.verts.new(center + (v - center) * 0.45 + Vector((0, 0, -0.42))) for v in outline]
    culet = bm.verts.new(center + Vector((0, 0, -0.8)))

    bm.faces.new(table)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((girdle_top[i], girdle_top[j], mid[i]))
        bm.faces.new((girdle_top[i], mid[i], table[i]))
        bm.faces.new((girdle_top[j], table[j], mid[i]))
        bm.faces.new((table[i], mid[i], table[j]))
        bm.faces.new((girdle_bottom[j], girdle_bottom[i], girdle_top[i], girdle_top[j]))
        bm.faces.new((lower[j], lower[i], girdle_bottom[i], girdle_bottom[j]))
        bm.faces.new((culet, lower[i], lower[j]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    for poly in mesh.polygons:
        poly.use_smooth = False
    return obj


def material(name, base, glow):
    """Coloured glass: refraction and total internal reflection make the facets sparkle (opaque on transparent film)."""
    mat = bpy.data.materials.new(name + "_jewel")
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")

    def set_input(label, value):
        if label in bsdf.inputs:
            bsdf.inputs[label].default_value = value

    set_input("Base Color", (*base, 1.0))
    set_input("Roughness", 0.02)
    set_input("IOR", 1.9)
    set_input("Transmission Weight", 1.0)
    set_input("Specular IOR Level", 1.0)
    set_input("Coat Weight", 0.4)
    set_input("Coat Roughness", 0.0)
    set_input("Emission Color", (*glow, 1.0))
    set_input("Emission Strength", 0.25)
    return mat


def gold():
    mat = bpy.data.materials.new("gold")
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (1.0, 0.72, 0.28, 1.0)
    bsdf.inputs["Metallic"].default_value = 1.0
    bsdf.inputs["Roughness"].default_value = 0.22
    return mat


def build_bezel(name):
    """Gold setting hugging the girdle, like the painted gems' frames."""
    outline = [Vector((x, y, 0.0)) for x, y in polygon(name)]
    n = len(outline)
    center = sum(outline, Vector()) / n
    bm = bmesh.new()
    inner_top = [bm.verts.new(center + (v - center) * 0.99 + Vector((0, 0, 0.12))) for v in outline]
    outer_top = [bm.verts.new(center + (v - center) * 1.13 + Vector((0, 0, 0.08))) for v in outline]
    outer_bottom = [bm.verts.new(center + (v - center) * 1.13 + Vector((0, 0, -0.16))) for v in outline]
    inner_bottom = [bm.verts.new(center + (v - center) * 0.99 + Vector((0, 0, -0.16))) for v in outline]
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((inner_top[i], outer_top[i], outer_top[j], inner_top[j]))
        bm.faces.new((outer_top[i], outer_bottom[i], outer_bottom[j], outer_top[j]))
        bm.faces.new((inner_bottom[i], inner_top[i], inner_top[j], inner_bottom[j]))
        bm.faces.new((outer_bottom[i], inner_bottom[i], inner_bottom[j], outer_bottom[j]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    mesh = bpy.data.meshes.new(name + "_bezel")
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name + "_bezel", mesh)
    bpy.context.scene.collection.objects.link(obj)
    bevel = obj.modifiers.new("bevel", "BEVEL")
    bevel.width = 0.03
    bevel.segments = 3
    for poly in mesh.polygons:
        poly.use_smooth = True
    obj.data.materials.append(gold())
    return obj


def studio():
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 96
    scene.cycles.use_denoising = True
    scene.render.film_transparent = True
    scene.render.resolution_x = SIZE
    scene.render.resolution_y = SIZE
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.look = "None"
    scene.view_settings.exposure = 0.0

    scene.cycles.film_transparent_glass = False
    scene.cycles.max_bounces = 16
    scene.cycles.transmission_bounces = 12
    world = bpy.data.worlds.new("studio")
    world.use_nodes = True
    nodes = world.node_tree.nodes
    env = nodes.new("ShaderNodeTexEnvironment")
    env.image = bpy.data.images.load(HDRI)
    world.node_tree.links.new(env.outputs["Color"], nodes["Background"].inputs["Color"])
    nodes["Background"].inputs["Strength"].default_value = 1.2
    scene.world = world

    cam_data = bpy.data.cameras.new("cam")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = 2.55
    cam = bpy.data.objects.new("cam", cam_data)
    cam.location = (0, -3.2, 9.4)
    cam.rotation_euler = (math.radians(19), 0, 0)
    scene.collection.objects.link(cam)
    scene.camera = cam

    def light(name, kind, energy, location, size, color=(1, 1, 1)):
        data = bpy.data.lights.new(name, kind)
        data.energy = energy
        data.color = color
        if kind == "AREA":
            data.size = size
        obj = bpy.data.objects.new(name, data)
        obj.location = location
        direction = Vector((0, 0, 0)) - Vector(location)
        obj.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
        scene.collection.objects.link(obj)

    light("key", "AREA", 120, (-3.5, -3.0, 6.0), 3.0)
    light("rim", "AREA", 90, (3.5, 3.5, 3.0), 2.0, (0.8, 0.9, 1.0))
    light("fill", "AREA", 20, (3.0, -4.0, 2.0), 4.0, (1.0, 0.95, 0.9))
    light("spark", "POINT", 60, (-1.2, -1.5, 2.2), 0.1)


for obj in list(bpy.data.objects):
    bpy.data.objects.remove(obj, do_unlink=True)
studio()

for gem_name, (base_color, glow_color) in GEMS.items():
    gem = build_gem(gem_name)
    gem.data.materials.append(material(gem_name, base_color, glow_color))
    bezel = build_bezel(gem_name)
    for obj in (gem, bezel):
        obj.rotation_euler = (math.radians(-6), math.radians(8), 0)
    bpy.context.scene.render.filepath = os.path.join(OUT, gem_name + ".png")
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(gem, do_unlink=True)
    bpy.data.objects.remove(bezel, do_unlink=True)
    print("rendered", gem_name)
