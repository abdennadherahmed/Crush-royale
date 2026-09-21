"""Renders the premium UI kit pieces (glass card frame, header banner, stone button, icon button, tabs, divider).

    blender -b --python tools/art/blender_kit.py -- <output_dir> <hdri>

Same studio as tools/art/blender_buttons.py (orthographic top-down, Cycles, transparent film) so these pieces sit next
to the existing gold/purple buttons and the 3D gems without a style break. Everything is rendered at 2x and the
companion runner downscales it, which is what gives the pieces their clean anti-aliased edges.

Geometry rule for the 9-sliced pieces (card, banner, tabs, buttons): the decorated corner must stay inside the border
share used by UiKit.Sliced, otherwise stretching the middle band smears the ornament.
"""
import os
import sys

import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = os.path.abspath(argv[0] if argv else "kit")
HDRI = os.path.abspath(argv[1]) if len(argv) > 1 else ""
os.makedirs(OUT, exist_ok=True)

# Palette matched to Theme.cs: night violet surfaces, royal gold trim, cold stone for secondary actions.
GOLD = (1.0, 0.74, 0.3)
GOLD_DEEP = (0.55, 0.33, 0.06)
GLASS = (0.055, 0.035, 0.13)
GLASS_RIM = (0.19, 0.14, 0.36)
STONE = (0.150, 0.160, 0.225)
STONE_DEEP = (0.055, 0.060, 0.095)
VIOLET = (0.20, 0.06, 0.46)


def bsdf(mat):
    """Shader nodes are looked up by type: node names are localized on a non-English Blender."""
    return next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")


def principled(name, color, metallic=0.0, roughness=0.25, coat=0.0, emission=0.0, alpha=1.0):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    node = bsdf(mat)
    node.inputs["Base Color"].default_value = (*color, 1.0)
    node.inputs["Metallic"].default_value = metallic
    node.inputs["Roughness"].default_value = roughness
    if "Coat Weight" in node.inputs:
        node.inputs["Coat Weight"].default_value = coat
        node.inputs["Coat Roughness"].default_value = 0.04
    if emission > 0:
        node.inputs["Emission Color"].default_value = (*color, 1.0)
        node.inputs["Emission Strength"].default_value = emission
    if alpha < 1.0:
        node.inputs["Alpha"].default_value = alpha
    return mat


def rounded_slab(name, width, height, radius, thickness, edge, location, material, segments=16):
    """Rounded rectangle extruded, top edges bevelled into a dome: the shape of every button and frame in the kit."""
    bpy.ops.mesh.primitive_plane_add(size=1, location=location)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = (width, height, 1)
    bpy.ops.object.transform_apply(scale=True)
    corners = obj.modifiers.new("corners", "BEVEL")
    corners.affect = "VERTICES"
    corners.width = radius
    corners.segments = segments
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


def rounded_frame(name, width, height, radius, thickness, edge, hole, location, material):
    """Rounded slab with its middle cut out. Needed for the glass card: a full slab under the translucent pane would
    show through it and the card would render opaque grey instead of dark glass."""
    obj = rounded_slab(name, width, height, radius, thickness, edge, location, material)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.convert(target="MESH")
    cutter = rounded_slab(name + "_cut", width - hole * 2, height - hole * 2, max(0.02, radius - hole), thickness * 4,
                          0.0, (location[0], location[1], location[2] - thickness), material)
    bpy.context.view_layer.objects.active = cutter
    bpy.ops.object.convert(target="MESH")
    boolean = obj.modifiers.new("hole", "BOOLEAN")
    boolean.operation = "DIFFERENCE"
    boolean.object = cutter
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier="hole")
    bpy.data.objects.remove(cutter, do_unlink=True)
    return obj


def studio():
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 96
    scene.cycles.use_denoising = True
    scene.render.film_transparent = True
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.view_settings.view_transform = "Standard"

    world = bpy.data.worlds.new("studio")
    world.use_nodes = True
    nodes = world.node_tree.nodes
    background = next(n for n in nodes if n.type == "BACKGROUND")
    if HDRI and os.path.exists(HDRI):
        env = nodes.new("ShaderNodeTexEnvironment")
        env.image = bpy.data.images.load(HDRI)
        world.node_tree.links.new(env.outputs["Color"], background.inputs["Color"])
    background.inputs["Strength"].default_value = 0.35
    scene.world = world

    cam_data = bpy.data.cameras.new("cam")
    cam_data.type = "ORTHO"
    cam = bpy.data.objects.new("cam", cam_data)
    cam.location = (0, 0, 10)
    scene.collection.objects.link(cam)
    scene.camera = cam

    # Key light high and slightly left, like the existing buttons, so the bevel highlight reads on the top edge.
    key = bpy.data.lights.new("key", "AREA")
    key.energy = 320
    key.size = 8
    key_obj = bpy.data.objects.new("key", key)
    key_obj.location = (-1.2, 3.0, 6)
    key_obj.rotation_euler = (Vector((0, 0, 0)) - Vector(key_obj.location)).to_track_quat("-Z", "Y").to_euler()
    scene.collection.objects.link(key_obj)

    # Cold rim light from below keeps the dark glass from going flat black at the bottom.
    rim = bpy.data.lights.new("rim", "AREA")
    rim.energy = 90
    rim.size = 8
    rim.color = (0.55, 0.7, 1.0)
    rim_obj = bpy.data.objects.new("rim", rim)
    rim_obj.location = (1.6, -3.2, 5)
    rim_obj.rotation_euler = (Vector((0, 0, 0)) - Vector(rim_obj.location)).to_track_quat("-Z", "Y").to_euler()
    scene.collection.objects.link(rim_obj)
    return cam_data


def clear_meshes():
    for obj in [o for o in bpy.context.scene.objects if o.type == "MESH"]:
        bpy.data.objects.remove(obj, do_unlink=True)


def render(name, width, height, ortho):
    scene = bpy.context.scene
    scene.render.resolution_x = width
    scene.render.resolution_y = height
    CAMERA.ortho_scale = ortho
    scene.render.filepath = os.path.join(OUT, name + ".png")
    bpy.ops.render.render(write_still=True)
    print("rendered", name)


bpy.ops.wm.read_factory_settings(use_empty=True)
CAMERA = studio()

gold_metal = principled("gold_metal", GOLD, metallic=1.0, roughness=0.22)
gold_deep = principled("gold_deep", GOLD_DEEP, metallic=1.0, roughness=0.45)
shine = principled("shine", (1.0, 1.0, 1.0), roughness=0.0, emission=0.35, alpha=0.20)

# --- Glass card frame -------------------------------------------------------------------------------------------
# A dark translucent pane (alpha < 1 so the backdrop shows through) inside a slim gold hairline and a violet bevel.
# The corner ornament stays within 30% of the side, matching UiKit.Sliced("glass_card", 0.3).
clear_meshes()
CW, CH, CR = 3.1, 3.1, 0.52
rounded_frame("rim", CW + 0.18, CH + 0.18, CR + 0.09, 0.16, 0.05, 0.22, (0, 0, 0.0), gold_metal)
rounded_frame("bevel", CW, CH, CR, 0.22, 0.08, 0.11, (0, 0, 0.10), principled("card_bevel", GLASS_RIM, roughness=0.32))
rounded_slab("pane", CW - 0.10, CH - 0.10, CR - 0.05, 0.10, 0.04,
             (0, 0, 0.06), principled("card_pane", GLASS, roughness=0.24, coat=0.45, alpha=0.60))
# Inner highlight: a thin bright band just under the top edge, the cue that reads as "lit glass".
rounded_slab("inner_top", CW - 0.66, 0.05, 0.025, 0.01, 0.0, (0, CH * 0.5 - 0.30, 0.30), shine)
render("glass_card", 640, 640, 3.7)

# --- Header banner ----------------------------------------------------------------------------------------------
# Wide title plate: violet band, gold top and bottom rails, gem studs parked in the 9-slice side borders.
clear_meshes()
BW, BH = 6.6, 1.5
rounded_slab("banner_rim", BW + 0.14, BH + 0.14, 0.30, 0.16, 0.05, (0, 0, 0.0), gold_metal)
rounded_slab("banner_body", BW, BH, 0.26, 0.22, 0.10, (0, 0, 0.10), principled("banner", VIOLET, roughness=0.20, coat=1.0))
rounded_slab("banner_rail_top", BW - 0.10, 0.10, 0.05, 0.10, 0.03, (0, BH * 0.5 - 0.16, 0.34), gold_metal)
rounded_slab("banner_rail_bottom", BW - 0.10, 0.08, 0.04, 0.08, 0.03, (0, -BH * 0.5 + 0.15, 0.34), gold_deep)
for side in (-1, 1):
    bpy.ops.mesh.primitive_ico_sphere_add(radius=0.17, subdivisions=3, location=(side * (BW * 0.5 - 0.20), 0, 0.30))
    stud = bpy.context.active_object
    stud.scale = (1, 1, 0.55)
    stud.data.materials.append(principled("stud", (0.62, 0.86, 1.0), roughness=0.05, coat=1.0))
    bpy.ops.object.shade_smooth()
rounded_slab("banner_shine", BW - 1.0, 0.16, 0.08, 0.01, 0.0, (0, 0.34, 0.46), shine)
render("header_banner", 1320, 340, 7.3)

# --- Secondary (stone / silver) button --------------------------------------------------------------------------
# Same silhouette as button_gold so the families stack, but brushed stone and no gold rim: visibly the lesser action.
clear_meshes()
SW, SH, SR = 4.6, 1.52, 0.62
rounded_slab("s_rim", SW + 0.2, SH + 0.2, SR + 0.1, 0.18, 0.06, (0, -0.02, 0.0), principled("stone_rim", STONE, metallic=0.25, roughness=0.55))
rounded_slab("s_lip", SW - 0.02, SH - 0.02, SR - 0.01, 0.22, 0.05, (0, -0.07, 0.12), principled("stone_lip", STONE_DEEP, roughness=0.55))
rounded_slab("s_face", SW - 0.1, SH - 0.2, SR - 0.06, 0.26, 0.16, (0, 0.03, 0.2), principled("stone_face", (0.085, 0.092, 0.145), roughness=0.36, coat=0.45))
rounded_slab("s_shine", SW - 0.9, 0.18, 0.09, 0.01, 0.0, (0, 0.36, 0.5), principled("stone_shine", (1.0, 1.0, 1.0), roughness=0.0, emission=0.2, alpha=0.11))
render("button_stone", 936, 320, 5.2)

# --- Small icon button ------------------------------------------------------------------------------------------
# Square gold-rimmed pad for back arrows, settings cogs and hub side buttons; 9-sliced at 0.38.
clear_meshes()
IW, IR = 1.9, 0.48
rounded_slab("i_rim", IW + 0.16, IW + 0.16, IR + 0.08, 0.16, 0.05, (0, -0.01, 0.0), gold_metal)
rounded_slab("i_lip", IW, IW, IR, 0.20, 0.05, (0, -0.05, 0.10), principled("icon_lip", (0.12, 0.07, 0.26), roughness=0.4))
rounded_slab("i_face", IW - 0.12, IW - 0.18, IR - 0.06, 0.24, 0.14, (0, 0.02, 0.18), principled("icon_face", (0.26, 0.15, 0.52), roughness=0.18, coat=1.0))
rounded_slab("i_shine", IW - 0.72, 0.14, 0.07, 0.01, 0.0, (0, 0.52, 0.44), shine)
render("icon_button", 320, 320, 2.4)

# --- Tab strip pieces -------------------------------------------------------------------------------------------
# Flat-bottomed plates (a tab sits on the strip): the active one is gold-lit, the idle one is sunken and desaturated.
for name, face, rim_mat, lit in (("tab_active", (0.55, 0.33, 0.04), gold_metal, True),):
    clear_meshes()
    TW, TH, TR = 2.6, 1.15, 0.26
    rounded_slab(name + "_rim", TW + 0.12, TH + 0.12, TR + 0.06, 0.15, 0.05, (0, 0, 0.0), rim_mat)
    rounded_slab(name + "_face", TW, TH, TR, 0.22, 0.12, (0, 0.03 if lit else -0.02, 0.10),
                 principled(name + "_face_mat", face, roughness=0.20 if lit else 0.42, coat=1.0 if lit else 0.2))
    if lit:
        rounded_slab(name + "_shine", TW - 0.6, 0.12, 0.06, 0.01, 0.0, (0, 0.30, 0.38), shine)
    render(name, 400, 200, 3.0)

# --- Divider ----------------------------------------------------------------------------------------------------
# Thin gold rail, sliced horizontally so it stretches to any width.
clear_meshes()
DW = 6.4
rounded_slab("d_rail", DW, 0.10, 0.05, 0.10, 0.03, (0, 0, 0.0), gold_metal)
rounded_slab("d_rail_shadow", DW, 0.05, 0.025, 0.06, 0.02, (0, -0.09, -0.04), gold_deep)
# No ornament in the middle: a 9-sliced strip stretches its centre, and any centred jewel would smear. UiKit.Divider
# lays the existing round_crystal art over the middle of the rail instead.
render("divider", 1280, 160, 6.8)

print("kit done ->", OUT)
