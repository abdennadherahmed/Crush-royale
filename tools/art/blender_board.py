"""Renders board obstacles, bonus overlays and chests as 3D sprites with Blender, headless.

    blender -b --python tools/art/blender_board.py -- <output_dir> <hdri> [only]

Outputs (transparent PNGs):
  stone.png, stone_hard.png   carved rune stone / the same bound with iron (2 hits)
  stone_bands.png             the iron bands alone (overlay for 2-hit stones)
  ice.png                     translucent ice block drawn under a gem
  line_h.png, line_v.png      double-arrow energy rods laid over a line bonus
  bomb.png                    spiked gold ring laid around an area bomb
  chest_<type>.png, chest_open_<type>.png   wood, silver, gold, crystal
  blight.png, egg.png, egg_cracked.png, bomb_badge.png   stage mechanics of stages 201, 301 and 101
"""
import math
import os
import sys

import bpy
import bmesh
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = os.path.abspath(argv[0] if argv else "board3d")
HDRI = os.path.abspath(argv[1]) if len(argv) > 1 else ""
ONLY = argv[2].split(",") if len(argv) > 2 else None
os.makedirs(OUT, exist_ok=True)


# ----------------------------------------------------------------------------- materials

def bsdf_of(mat):
    return next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")


def set_in(node, name, value):
    if name in node.inputs:
        node.inputs[name].default_value = value


def principled(name, color, metallic=0.0, roughness=0.4, coat=0.0, emission=None, strength=0.0, transmission=0.0, ior=1.45, alpha=1.0):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    b = bsdf_of(mat)
    set_in(b, "Base Color", (*color, 1.0))
    set_in(b, "Metallic", metallic)
    set_in(b, "Roughness", roughness)
    set_in(b, "Coat Weight", coat)
    set_in(b, "Coat Roughness", 0.05)
    set_in(b, "Transmission Weight", transmission)
    set_in(b, "IOR", ior)
    set_in(b, "Alpha", alpha)
    if emission is not None:
        set_in(b, "Emission Color", (*emission, 1.0))
        set_in(b, "Emission Strength", strength)
    return mat


def noisy(name, dark, light, scale=6.0, roughness=0.75, bump=0.35, wave=False):
    """Stone or wood: colour ramp driven by noise (or wave bands for planks), with bump."""
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    b = bsdf_of(mat)
    tex = nt.nodes.new("ShaderNodeTexWave" if wave else "ShaderNodeTexNoise")
    if wave:
        tex.inputs["Scale"].default_value = scale
        tex.inputs["Distortion"].default_value = 6.0
        tex.inputs["Detail"].default_value = 3.0
    else:
        tex.inputs["Scale"].default_value = scale
        tex.inputs["Detail"].default_value = 8.0
        tex.inputs["Roughness"].default_value = 0.6
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].color = (*dark, 1)
    ramp.color_ramp.elements[1].color = (*light, 1)
    nt.links.new(tex.outputs["Fac"], ramp.inputs["Fac"])
    nt.links.new(ramp.outputs["Color"], b.inputs["Base Color"])
    bump_node = nt.nodes.new("ShaderNodeBump")
    bump_node.inputs["Strength"].default_value = bump
    nt.links.new(tex.outputs["Fac"], bump_node.inputs["Height"])
    nt.links.new(bump_node.outputs["Normal"], b.inputs["Normal"])
    set_in(b, "Roughness", roughness)
    return mat


# ----------------------------------------------------------------------------- geometry helpers

def box(name, size, location, material, bevel=0.03, segments=3, rotation=(0, 0, 0)):
    bpy.ops.mesh.primitive_cube_add(size=1, location=location, rotation=rotation)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = size
    bpy.ops.object.transform_apply(scale=True)
    if bevel > 0:
        mod = obj.modifiers.new("bevel", "BEVEL")
        mod.width = bevel
        mod.segments = segments
    obj.data.materials.append(material)
    bpy.ops.object.shade_smooth()
    return obj


def cylinder(name, radius, depth, location, material, rotation=(0, 0, 0), vertices=32, bevel=0.0):
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=depth, location=location, rotation=rotation)
    obj = bpy.context.active_object
    obj.name = name
    if bevel > 0:
        mod = obj.modifiers.new("bevel", "BEVEL")
        mod.width = bevel
        mod.segments = 2
    obj.data.materials.append(material)
    bpy.ops.object.shade_smooth()
    return obj


def cone(name, radius, depth, location, material, rotation):
    bpy.ops.mesh.primitive_cone_add(vertices=24, radius1=radius, radius2=0, depth=depth, location=location, rotation=rotation)
    obj = bpy.context.active_object
    obj.name = name
    obj.data.materials.append(material)
    bpy.ops.object.shade_smooth()
    return obj


def half_cylinder(name, radius, length, location, material, scale_y=1.0):
    """Barrel lid: the top half of a cylinder lying along X, closed at the bottom."""
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=48, radius1=radius, radius2=radius, depth=length)
    bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0), matrix=__import__("mathutils").Matrix.Rotation(math.radians(90), 3, "Y"))
    geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
    bmesh.ops.bisect_plane(bm, geom=geom, plane_co=(0, 0, 0), plane_no=(0, 0, 1), clear_inner=True)
    edges = [e for e in bm.edges if e.is_boundary]
    bmesh.ops.edgeloop_fill(bm, edges=edges)
    for v in bm.verts:
        v.co.y *= scale_y
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    obj.location = location
    mod = obj.modifiers.new("bevel", "BEVEL")
    mod.width = 0.025
    mod.segments = 2
    mod.limit_method = "ANGLE"
    obj.data.materials.append(material)
    for p in mesh.polygons:
        p.use_smooth = True
    return obj


def clear_meshes():
    for obj in [o for o in bpy.context.scene.objects if o.type in ("MESH", "CURVE", "EMPTY") or o.name.startswith("glowlight")]:
        bpy.data.objects.remove(obj, do_unlink=True)


# ----------------------------------------------------------------------------- scene

def studio(size, ortho, tilt_deg=19, distance=10.0, transparent_glass=False):
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 80
    scene.cycles.use_denoising = True
    scene.render.film_transparent = True
    scene.cycles.film_transparent_glass = transparent_glass
    scene.cycles.max_bounces = 12
    scene.cycles.transmission_bounces = 10
    scene.render.resolution_x = size
    scene.render.resolution_y = size
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.look = "None"

    if scene.world is None:
        world = bpy.data.worlds.new("studio")
        world.use_nodes = True
        nodes = world.node_tree.nodes
        bg = next(n for n in nodes if n.type == "BACKGROUND")
        if HDRI and os.path.exists(HDRI):
            env = nodes.new("ShaderNodeTexEnvironment")
            env.image = bpy.data.images.load(HDRI)
            world.node_tree.links.new(env.outputs["Color"], bg.inputs["Color"])
        bg.inputs["Strength"].default_value = 1.0
        scene.world = world

        def light(name, energy, location, size_, color=(1, 1, 1)):
            data = bpy.data.lights.new(name, "AREA")
            data.energy = energy
            data.size = size_
            data.color = color
            obj = bpy.data.objects.new(name, data)
            obj.location = location
            obj.rotation_euler = (Vector((0, 0, 0)) - Vector(location)).to_track_quat("-Z", "Y").to_euler()
            scene.collection.objects.link(obj)

        light("key", 220, (-3.5, -3.5, 6.0), 3.0)
        light("rim", 160, (3.5, 3.5, 3.0), 2.0, (0.75, 0.85, 1.0))
        light("fill", 40, (3.0, -4.0, 2.0), 4.0, (1.0, 0.95, 0.9))

    cam = scene.camera
    if cam is None:
        cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
        scene.collection.objects.link(cam)
        scene.camera = cam
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = ortho
    tilt = math.radians(tilt_deg)
    cam.location = (0, -distance * math.sin(tilt), distance * math.cos(tilt))
    cam.rotation_euler = (tilt, 0, 0)


def render(name):
    if ONLY and name not in ONLY and not any(name.startswith(o) for o in ONLY):
        return
    bpy.context.scene.render.filepath = os.path.join(OUT, name + ".png")
    bpy.ops.render.render(write_still=True)
    print("rendered", name)


def wanted(prefix):
    return ONLY is None or any(o.startswith(prefix) or prefix.startswith(o) for o in ONLY)


# ----------------------------------------------------------------------------- stones & ice

def rock(material):
    bpy.ops.mesh.primitive_cube_add(size=1, location=(0, 0, 0))
    obj = bpy.context.active_object
    obj.scale = (1.62, 1.62, 1.0)
    bpy.ops.object.transform_apply(scale=True)
    bev = obj.modifiers.new("bevel", "BEVEL")
    bev.width = 0.2
    bev.segments = 4
    sub = obj.modifiers.new("sub", "SUBSURF")
    sub.levels = 3
    sub.render_levels = 3
    tex = bpy.data.textures.new("rockdisp", "CLOUDS")
    tex.noise_scale = 0.45
    disp = obj.modifiers.new("disp", "DISPLACE")
    disp.texture = tex
    disp.strength = 0.1
    obj.data.materials.append(material)
    bpy.ops.object.shade_smooth()
    return obj


def rune(material, z):
    """A glowing carved rune on the top face: a stave with two branches."""
    parts = [
        ((0.07, 0.95, 0.04), (-0.12, 0.0, z), (0, 0, 0)),
        ((0.07, 0.5, 0.04), (0.08, 0.22, z), (0, 0, math.radians(-40))),
        ((0.07, 0.5, 0.04), (0.08, -0.12, z), (0, 0, math.radians(40))),
    ]
    for size, loc, rot in parts:
        box("rune", size, loc, material, bevel=0.015, rotation=rot)


def render_stones():
    stone_mat = noisy("stone", (0.05, 0.055, 0.08), (0.24, 0.26, 0.33), scale=7.0, roughness=0.8, bump=0.9)
    glow = principled("rune", (0.2, 0.8, 1.0), emission=(0.25, 0.85, 1.0), strength=9.0)
    iron = principled("iron", (0.18, 0.18, 0.2), metallic=1.0, roughness=0.35)
    rivet = principled("rivet", (0.75, 0.7, 0.6), metallic=1.0, roughness=0.25)

    studio(256, 2.3)
    clear_meshes()
    r = rock(stone_mat)
    rune(glow, 0.5)
    render("stone")

    # Reinforced: two iron bands with rivets across the stone.
    for x in (-0.48, 0.48):
        box("band", (0.2, 1.8, 1.14), (x, 0, 0), iron, bevel=0.03)
        for y in (-0.62, 0.0, 0.62):
            bpy.ops.mesh.primitive_uv_sphere_add(radius=0.055, location=(x, y, 0.58))
            bpy.context.active_object.data.materials.append(rivet)
            bpy.ops.object.shade_smooth()
    render("stone_hard")

    # The bands alone, laid over the painted stone in game (2-hit stones).
    for obj in [o for o in bpy.context.scene.objects if o.type == "MESH" and (o.name.startswith("rune") or o.name.startswith("Cube"))]:
        bpy.data.objects.remove(obj, do_unlink=True)
    render("stone_bands")


def render_ice():
    ice = principled("ice", (0.72, 0.92, 1.0), roughness=0.06, transmission=1.0, ior=1.31, coat=1.0)
    frost = principled("frost", (0.9, 0.97, 1.0), roughness=0.5, alpha=0.35)
    studio(256, 2.3, transparent_glass=True)
    clear_meshes()
    box("ice", (1.8, 1.8, 0.6), (0, 0, -0.2), ice, bevel=0.14, segments=5)
    # Frosted rim and a few inner cracks make it read as ice, not glass.
    box("frostrim", (1.84, 1.84, 0.08), (0, 0, 0.07), frost, bevel=0.03)
    crack = principled("crack", (1, 1, 1), roughness=0.2, emission=(0.85, 0.95, 1.0), strength=1.5)
    for size, loc, rot in [((0.9, 0.025, 0.02), (-0.2, 0.3, 0.12), 25), ((0.6, 0.025, 0.02), (0.35, -0.25, 0.12), -35), ((0.45, 0.025, 0.02), (0.1, 0.05, 0.12), 80)]:
        box("crack", size, loc, crack, bevel=0.0, rotation=(0, 0, math.radians(rot)))
    render("ice")


# ----------------------------------------------------------------------------- bonus overlays

def render_bonuses():
    gold = principled("gold", (1.0, 0.72, 0.28), metallic=1.0, roughness=0.2)
    energy = principled("energy", (1, 1, 1), emission=(0.55, 0.95, 1.0), strength=6.0)
    fire = principled("fire", (1.0, 0.5, 0.1), emission=(1.0, 0.45, 0.05), strength=7.0)
    studio(256, 2.55, tilt_deg=8)

    for name, angle in (("line_h", 0.0), ("line_v", 90.0)):
        clear_meshes()
        rot = math.radians(angle)
        # Energy rod across the gem with gold arrowheads at both ends.
        cylinder("rod", 0.07, 1.5, (0, 0, 0.9), energy, rotation=(0, math.radians(90), rot))
        cylinder("halo", 0.13, 1.35, (0, 0, 0.85), principled("haze", (0.6, 0.9, 1.0), emission=(0.4, 0.8, 1.0), strength=1.2, alpha=0.25), rotation=(0, math.radians(90), rot))
        for side in (-1, 1):
            d = Vector((math.cos(rot), math.sin(rot), 0)) * side
            tip = d * 0.95 + Vector((0, 0, 0.9))
            # Cone points along +Z by default: aim it outward along d.
            aim = d.to_track_quat("Z", "Y").to_euler()
            cone("arrow", 0.2, 0.34, tip, gold, aim)
            cylinder("collar", 0.11, 0.08, d * 0.74 + Vector((0, 0, 0.9)), gold, rotation=(0, math.radians(90), rot))
        render(name)

    clear_meshes()
    bpy.ops.mesh.primitive_torus_add(major_radius=1.0, minor_radius=0.075, major_segments=64, minor_segments=16, location=(0, 0, 0.6))
    ring = bpy.context.active_object
    ring.data.materials.append(gold)
    bpy.ops.object.shade_smooth()
    for i in range(8):
        a = i * math.pi / 4 + math.pi / 8
        d = Vector((math.cos(a), math.sin(a), 0))
        cone("spike", 0.1, 0.26, d * 1.12 + Vector((0, 0, 0.6)), gold, d.to_track_quat("Z", "Y").to_euler())
        bpy.ops.mesh.primitive_uv_sphere_add(radius=0.075, location=d * 1.0 + Vector((0, 0, 0.7)))
        bpy.context.active_object.data.materials.append(fire)
        bpy.ops.object.shade_smooth()
    render("bomb")


# ----------------------------------------------------------------------------- chests

CHESTS = {
    # body material, trim metal, jewel
    "wood": (("wood", (0.22, 0.1, 0.04), (0.5, 0.27, 0.11)), (0.2, 0.2, 0.22), None),
    "silver": (("wood", (0.12, 0.14, 0.24), (0.3, 0.36, 0.55)), (0.82, 0.85, 0.92), (0.2, 0.55, 1.0)),
    "gold": (("wood", (0.35, 0.02, 0.05), (0.62, 0.06, 0.1)), (1.0, 0.72, 0.28), (1.0, 0.08, 0.15)),
    "crystal": (("glass", (0.45, 0.2, 1.0), None), (1.0, 0.72, 0.28), (0.3, 0.95, 1.0)),
}


def chest(kind, open_):
    (body_kind, dark, light), trim_color, jewel_color = CHESTS[kind]
    if body_kind == "glass":
        body = principled("crystalbody", dark, roughness=0.05, transmission=0.85, ior=1.5, coat=1.0, emission=(0.5, 0.3, 1.0), strength=0.6)
    else:
        body = noisy("planks_" + kind, dark, light, scale=3.0, roughness=0.6, bump=0.25, wave=True)
    trim = principled("trim_" + kind, trim_color, metallic=1.0, roughness=0.22 if kind != "wood" else 0.45)
    jewel = principled("jewel_" + kind, jewel_color, roughness=0.02, transmission=0.6, ior=1.8, coat=1.0, emission=jewel_color, strength=1.2) if jewel_color else None

    W, D, H, R = 1.7, 1.15, 0.85, 0.58
    parts = []
    parts.append(box("body", (W, D, H), (0, 0, H / 2), body, bevel=0.05))
    # Metal corners and straps.
    for x in (-W / 2 + 0.06, W / 2 - 0.06):
        parts.append(box("corner", (0.16, D + 0.04, H + 0.02), (x, 0, H / 2), trim, bevel=0.03))
    for x in (-0.42, 0.42):
        parts.append(box("strap", (0.14, D + 0.05, H + 0.01), (x, 0, H / 2), trim, bevel=0.02))
    parts.append(box("rim", (W + 0.04, D + 0.06, 0.1), (0, 0, H), trim, bevel=0.02))

    # Lid, hinged on the back top edge.
    hinge = bpy.data.objects.new("hinge", None)
    bpy.context.scene.collection.objects.link(hinge)
    hinge.location = (0, D / 2, H)
    lid_parts = [half_cylinder("lid", R, W, (0, 0, H), body, scale_y=D / 2 / R)]
    for x in (-0.42, 0.42, -W / 2 + 0.06, W / 2 - 0.06):
        lid_parts.append(half_cylinder("lidstrap", R + 0.03, 0.14 if abs(x) < 0.5 else 0.16, (x, 0, H), trim, scale_y=(D / 2 + 0.03) / (R + 0.03)))
    # Lock plate on the front (closed: on the lid edge; open: stays on the body).
    lock = box("lock", (0.34, 0.08, 0.36), (0, -D / 2 - 0.04, H - 0.08), trim, bevel=0.03)
    parts.append(lock)
    if jewel:
        bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=2, radius=0.1, location=(0, -D / 2 - 0.1, H - 0.02))
        gem = bpy.context.active_object
        gem.data.materials.append(jewel)
        lid_parts.append(gem)
        for x in (-0.42, 0.42):
            bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=2, radius=0.07, location=(x, -D / 2 - 0.05, H * 0.5))
            bpy.context.active_object.data.materials.append(jewel)
    keyhole = principled("keyhole", (0.02, 0.01, 0.02), roughness=0.9)
    box("keyhole", (0.06, 0.02, 0.12), (0, -D / 2 - 0.085, H - 0.14), keyhole, bevel=0.0)

    for p in lid_parts:
        p.parent = hinge
        p.matrix_parent_inverse = hinge.matrix_world.inverted()

    if open_:
        hinge.rotation_euler = (math.radians(-105), 0, 0)
        # Treasure: glowing gold pile and a warm light rising from the chest.
        coin = principled("coin", (1.0, 0.75, 0.2), metallic=1.0, roughness=0.18, emission=(1.0, 0.6, 0.1), strength=0.4)
        glowmat = principled("glow", (1.0, 0.85, 0.4), emission=(1.0, 0.75, 0.3), strength=4.0)
        box("glowfloor", (W - 0.2, D - 0.2, 0.05), (0, 0, H - 0.05), glowmat, bevel=0.0)
        import random
        rnd = random.Random(7)
        for i in range(46):
            x = rnd.uniform(-W / 2 + 0.2, W / 2 - 0.2)
            y = rnd.uniform(-D / 2 + 0.18, D / 2 - 0.2)
            z = H + 0.04 + 0.22 * math.exp(-(x * x + y * y) * 2.5) + rnd.uniform(0, 0.05)
            cylinder("coin", 0.11, 0.03, (x, y, z), coin, rotation=(rnd.uniform(-0.6, 0.6), rnd.uniform(-0.6, 0.6), 0), vertices=20)
        light = bpy.data.lights.new("glowlight", "POINT")
        light.energy = 120
        light.color = (1.0, 0.8, 0.45)
        lobj = bpy.data.objects.new("glowlight", light)
        lobj.location = (0, -0.1, H + 0.6)
        bpy.context.scene.collection.objects.link(lobj)

    # 3/4 view: turn the whole chest.
    pivot = bpy.data.objects.new("pivot", None)
    bpy.context.scene.collection.objects.link(pivot)
    for obj in list(bpy.context.scene.objects):
        if obj.parent is None and obj.type in ("MESH", "EMPTY") and obj is not pivot:
            obj.parent = pivot
    pivot.rotation_euler = (0, 0, math.radians(-22))
    pivot.location = (0, 0, -0.75)


def render_chests():
    studio(320, 3.0, tilt_deg=28)
    for kind in CHESTS:
        for open_ in (False, True):
            name = ("chest_open_" if open_ else "chest_") + kind
            if ONLY and not any(name.startswith(o) for o in ONLY):
                continue
            clear_meshes()
            chest(kind, open_)
            render(name)


# ----------------------------------------------------------------------------- stage mechanics (101 / 201 / 301)

def crystal_spike(name, length, radius, location, tilt, spin, material):
    """Hexagonal crystal: prism with a pointed tip, tilted outward from the cluster centre."""
    bpy.ops.mesh.primitive_cone_add(vertices=6, radius1=radius, radius2=radius * 0.85, depth=length, location=(0, 0, 0))
    body = bpy.context.active_object
    bpy.ops.mesh.primitive_cone_add(vertices=6, radius1=radius * 0.85, radius2=0, depth=length * 0.35, location=(0, 0, length / 2 + length * 0.175))
    tip = bpy.context.active_object
    body.select_set(True)
    tip.select_set(True)
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.join()
    body.name = name
    body.data.materials.append(material)
    body.rotation_euler = (tilt, 0, spin)
    body.location = location
    return body


def render_mechanics():
    studio(256, 2.4)
    # Blight: dark corrupted crystal cluster with magenta glow on a rocky base.
    clear_meshes()
    shard = principled("blight", (0.16, 0.0, 0.22), metallic=0.3, roughness=0.12, coat=1.0, emission=(0.7, 0.0, 0.85), strength=0.9)
    base = noisy("blightbase", (0.01, 0.0, 0.02), (0.09, 0.03, 0.12), scale=6.0, roughness=0.85, bump=1.0)
    # Dark rock the crystals grow from (flattened, displaced so it reads as stone, not glass).
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=4, radius=0.75, location=(0, 0, -0.55))
    rock_obj = bpy.context.active_object
    rock_obj.scale = (1.1, 0.95, 0.4)
    rock_tex = bpy.data.textures.new("blightrock", "CLOUDS")
    rock_tex.noise_scale = 0.3
    rock_disp = rock_obj.modifiers.new("disp", "DISPLACE")
    rock_disp.texture = rock_tex
    rock_disp.strength = 0.18
    rock_obj.data.materials.append(principled("blightrock", (0.035, 0.02, 0.05), roughness=0.9))
    bpy.ops.object.shade_smooth()
    spikes = [(1.1, 0.2, (0, 0, -0.1), 0.0, 0.0), (0.8, 0.16, (-0.35, 0.1, -0.2), 0.45, 1.2), (0.85, 0.16, (0.38, -0.05, -0.2), 0.5, -1.9),
              (0.6, 0.13, (0.1, 0.38, -0.25), 0.55, 3.0), (0.55, 0.12, (-0.1, -0.4, -0.25), 0.6, 0.2), (0.45, 0.1, (0.55, 0.3, -0.3), 0.8, -2.6),
              (0.45, 0.1, (-0.6, -0.25, -0.3), 0.8, 0.9)]
    for i, (length, radius, loc, tilt, spin) in enumerate(spikes):
        crystal_spike("spike%d" % i, length, radius, (loc[0], loc[1], loc[2] + length / 2), tilt, spin, shard)
    glow = bpy.data.lights.new("glowlight_blight", "POINT")
    glow.energy = 40
    glow.color = (0.9, 0.2, 1.0)
    gobj = bpy.data.objects.new("glowlight_blight", glow)
    gobj.location = (0, -0.6, 0.6)
    bpy.context.scene.collection.objects.link(gobj)
    render("blight")

    # Dragon egg: scaled shell with voronoi scales, gold speckles; cracked version glows from inside.
    for cracked in (False, True):
        clear_meshes()
        mat = bpy.data.materials.new("eggshell")
        mat.use_nodes = True
        nt = mat.node_tree
        b = bsdf_of(mat)
        vor = nt.nodes.new("ShaderNodeTexVoronoi")
        vor.inputs["Scale"].default_value = 9.0
        ramp = nt.nodes.new("ShaderNodeValToRGB")
        ramp.color_ramp.elements[0].color = (0.02, 0.35, 0.3, 1)
        ramp.color_ramp.elements[1].color = (0.35, 0.9, 0.7, 1)
        nt.links.new(vor.outputs["Distance"], ramp.inputs["Fac"])
        nt.links.new(ramp.outputs["Color"], b.inputs["Base Color"])
        bump = nt.nodes.new("ShaderNodeBump")
        bump.inputs["Strength"].default_value = 0.6
        nt.links.new(vor.outputs["Distance"], bump.inputs["Height"])
        nt.links.new(bump.outputs["Normal"], b.inputs["Normal"])
        set_in(b, "Roughness", 0.3)
        set_in(b, "Coat Weight", 0.6)
        bpy.ops.mesh.primitive_uv_sphere_add(segments=64, ring_count=32, radius=0.72, location=(0, 0, 0))
        egg = bpy.context.active_object
        egg.scale = (0.86, 0.86, 1.15)
        for v in egg.data.vertices:
            if v.co.z > 0:
                v.co.x *= 1 - 0.18 * v.co.z / 0.72
                v.co.y *= 1 - 0.18 * v.co.z / 0.72
        egg.data.materials.append(mat)
        bpy.ops.object.shade_smooth()
        # Lay the egg's long axis along the picture's vertical (the camera looks down).
        pivot = bpy.data.objects.new("eggpivot", None)
        bpy.context.scene.collection.objects.link(pivot)
        egg.parent = pivot
        gold = principled("eggband", (1.0, 0.72, 0.28), metallic=1.0, roughness=0.22)
        bpy.ops.mesh.primitive_torus_add(major_radius=0.64, minor_radius=0.04, location=(0, 0, -0.05))
        band = bpy.context.active_object
        band.data.materials.append(gold)
        band.parent = pivot
        bpy.ops.object.shade_smooth()
        if cracked:
            lava = principled("crack", (1.0, 0.6, 0.1), emission=(1.0, 0.55, 0.1), strength=8.0)
            # Glowing cracks on the side facing the camera (the egg's -Y side before the pivot tilt).
            for size, loc, rot in [((0.6, 0.05, 0.04), (-0.05, -0.6, 0.25), 20), ((0.4, 0.05, 0.04), (0.2, -0.58, 0.0), -40),
                                   ((0.35, 0.05, 0.04), (-0.22, -0.55, 0.5), 70), ((0.3, 0.05, 0.04), (0.08, -0.6, -0.25), -15)]:
                crack = box("crack", size, loc, lava, bevel=0.0, rotation=(0, math.radians(rot), 0))
                crack.parent = pivot
        pivot.rotation_euler = (math.radians(-72), 0, math.radians(12))
        render("egg_cracked" if cracked else "egg")

    # Countdown bomb badge: black iron bomb, gold ring, lit fuse.
    clear_meshes()
    iron = principled("bombiron", (0.03, 0.03, 0.05), metallic=0.6, roughness=0.25, coat=1.0)
    gold = principled("bombgold", (1.0, 0.72, 0.28), metallic=1.0, roughness=0.2)
    spark = principled("spark", (1.0, 0.8, 0.3), emission=(1.0, 0.6, 0.1), strength=18.0)
    bpy.ops.mesh.primitive_uv_sphere_add(segments=48, ring_count=24, radius=0.75, location=(0, 0, -0.1))
    bpy.context.active_object.data.materials.append(iron)
    bpy.ops.object.shade_smooth()
    cylinder("cap", 0.2, 0.22, (0.35, 0.35, 0.55), gold, rotation=(math.radians(-30), math.radians(30), 0))
    cylinder("fuse", 0.045, 0.45, (0.5, 0.5, 0.8), principled("rope", (0.55, 0.4, 0.2), roughness=0.9), rotation=(math.radians(-40), math.radians(40), 0))
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=2, radius=0.12, location=(0.64, 0.64, 1.0))
    bpy.context.active_object.data.materials.append(spark)
    render("bomb_badge")


bpy.ops.wm.read_factory_settings(use_empty=True)
if wanted("stone"):
    render_stones()
if wanted("ice"):
    render_ice()
if wanted("line") or wanted("bomb"):
    render_bonuses()
if wanted("chest"):
    render_chests()
if wanted("blight") or wanted("egg") or wanted("bomb_badge"):
    render_mechanics()
