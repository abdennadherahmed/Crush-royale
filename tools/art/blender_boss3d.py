"""Renders a generated boss GLB as the portrait the guild card shows.

    blender.exe -b --python tools/art/blender_boss3d.py -- <glb> <outdir> <name> [--preview] [--yaw <deg>]

Image-to-3D gives no promise about which way the model faces, and it differs from one generator to the next, so
--preview renders a full turnaround and --yaw then states which angle is the front for that particular model.

The model arrives from image-to-3D with an arbitrary scale, origin and orientation, so nothing here may assume
anything about it: the bounding box drives the framing. Lighting is a three-point rig with a cold crystal rim, the
colour the whole guild screen is built around, and the background stays transparent so the render drops onto the
card art instead of carrying a grey box with it.
"""
import math
import sys

import bpy
from mathutils import Vector

WIDTH, HEIGHT = 768, 1536
PREVIEW_WIDTH, PREVIEW_HEIGHT = 640, 800


def argv():
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if len(args) < 3:
        raise SystemExit("usage: blender -b --python blender_boss3d.py -- <glb> <outdir> <name> [--preview] [--yaw <deg>]")
    yaw = 0.0
    if "--yaw" in args:
        yaw = float(args[args.index("--yaw") + 1])
    clip = 0.0
    if "--clip" in args:
        clip = float(args[args.index("--clip") + 1])
    # A bust and a full body need very different distances, and the model decides which one it is, not the script.
    zoom = float(args[args.index("--zoom") + 1]) if "--zoom" in args else 1.0
    return args[0], args[1], args[2], "--preview" in args, yaw, clip, zoom, "--bust" in args


def clear():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def imported_meshes():
    return [o for o in bpy.context.scene.objects if o.type == "MESH"]


def bounds(objects):
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for obj in objects:
        for corner in obj.bound_box:
            world = obj.matrix_world @ Vector(corner)
            lo = Vector((min(lo[i], world[i]) for i in range(3)))
            hi = Vector((max(hi[i], world[i]) for i in range(3)))
    return lo, hi


def normalise(objects):
    """Puts the model on the origin, feet on z = 0, two metres tall, whatever it arrived as."""
    # A rigged export carries an armature whose rest pose can sit far from the mesh; only the visible geometry may
    # decide the framing, or the character ends up small and low in the frame.
    bpy.context.view_layer.update()
    lo, hi = bounds(objects)
    size = hi - lo
    tallest = max(size.x, size.y, size.z)
    if tallest <= 0:
        return 2.0
    scale = 2.0 / tallest
    centre = (lo + hi) * 0.5
    root = bpy.data.objects.new("BossRoot", None)
    bpy.context.scene.collection.objects.link(root)
    for obj in objects:
        if obj.parent is None:
            obj.parent = root
    root.location = -centre * scale
    root.scale = (scale, scale, scale)
    bpy.context.view_layer.update()
    lo, hi = bounds(objects)
    root.location.z -= lo.z
    bpy.context.view_layer.update()
    return (hi - lo).z


def light(name, kind, location, energy, colour, size=2.0):
    data = bpy.data.lights.new(name=name, type=kind)
    data.energy = energy
    data.color = colour
    if kind == "AREA":
        data.size = size
    obj = bpy.data.objects.new(name, data)
    obj.location = location
    bpy.context.scene.collection.objects.link(obj)
    return obj


def aim(obj, target):
    direction = Vector(target) - obj.location
    obj.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()


def build_camera(height, yaw_degrees, preview, clip=0.0, zoom=1.0, bust=False):
    data = bpy.data.cameras.new("BossCam")
    data.lens = 85 if not preview else 70
    camera = bpy.data.objects.new("BossCam", data)
    bpy.context.scene.collection.objects.link(camera)
    bpy.context.scene.camera = camera

    yaw = math.radians(yaw_degrees)
    # A card shows a boss the way a portrait does: head, shoulders and the top of the chest. Framing every model
    # that way is what makes three bosses generated from three different references look like one set.
    distance = height * (0.95 if bust else (2.1 if not preview else 2.4)) * max(0.2, zoom)
    focus = Vector((0.0, 0.0, height * (0.82 if bust else 0.58)))
    camera.location = Vector((math.sin(yaw) * distance, -math.cos(yaw) * distance, height * (0.86 if bust else 0.66)))
    aim(camera, focus)
    if clip > 0:
        # These references are illustrations, so the generator also modelled the cathedral standing behind the boss.
        # It sits well past his cape, and the far clip plane is the one reliable way to drop it: no mesh surgery on a
        # model that comes back as hundreds of loose islands.
        data.clip_end = distance + clip * height
    return camera


def build_lights(height):
    # Key from the front left, a cold crystal rim behind to separate the silhouette from the dark card, and a dim
    # violet fill so the shadow side never goes to pure black.
    key = light("Key", "AREA", (-1.8, -2.4, height * 1.15), 1400, (1.0, 0.98, 0.96), size=2.8)
    aim(key, (0.0, 0.0, height * 0.6))
    # A wide purple rim painted the whole model lavender and turned a green zombie into an amethyst one. It is a
    # narrow edge light now: it separates the silhouette from the dark card without repainting the skin.
    rim = light("Rim", "AREA", (1.9, 2.2, height * 1.05), 260, (0.64, 0.45, 1.0), size=0.6)
    aim(rim, (0.0, 0.0, height * 0.62))
    fill = light("Fill", "AREA", (2.2, -1.6, height * 0.5), 300, (0.85, 0.88, 0.95), size=3.5)
    aim(fill, (0.0, 0.0, height * 0.5))


def configure(width, height_px, samples):
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    try:
        scene.cycles.device = "CPU"
        scene.cycles.samples = samples
        scene.cycles.use_denoising = True
    except AttributeError:
        pass
    scene.render.resolution_x = width
    scene.render.resolution_y = height_px
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = True
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    # AgX pulls every strong colour towards grey, which turned a green zombie and brown leather into one lavender
    # mass. The textures already carry the art direction, so the view transform must not reinterpret them.
    for transform in ("Standard", "Khronos PBR Neutral", "Filmic"):
        try:
            scene.view_settings.view_transform = transform
            break
        except TypeError:
            continue
    try:
        scene.view_settings.look = "None"
    except TypeError:
        pass
    scene.view_settings.exposure = -0.1


def render_to(path):
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    print("wrote", path)


def main():
    glb, outdir, name, preview, yaw, clip, zoom, bust = argv()
    clear()
    bpy.ops.import_scene.gltf(filepath=glb)
    meshes = imported_meshes()
    if not meshes:
        raise SystemExit("no mesh in " + glb)
    height = normalise(meshes)
    build_lights(height)

    if preview:
        # Contact sheet for a human decision: three angles, cheap samples, nothing shipped from here.
        configure(PREVIEW_WIDTH, PREVIEW_HEIGHT, 24)
        for angle in (0, 90, 180, 270):
            build_camera(height, angle, True, clip, zoom, bust)
            render_to(outdir.rstrip("/\\") + "/" + name + "_yaw%03d.png" % angle)
        return

    configure(WIDTH, HEIGHT, 128)
    # Slightly off the front: a dead-on hero shot reads flat, a few degrees of turn gives the armour a lit edge.
    build_camera(height, yaw + 14.0, False, clip, zoom, bust)
    render_to(outdir.rstrip("/\\") + "/" + name + ".png")


main()
