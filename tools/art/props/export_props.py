# Exports the artist's props ("Corrupted - Assets.blend") as one FBX per object, ready for Unity.
#
#   blender -b --disable-autoexec "Corrupted - Assets.blend" --python export_props.py -- <repo>/Assets/Art/Models/Props
#
# Run it again when the artist updates the file: same names, so Unity keeps every reference (the FBX guid does not move).
# The chain is exported as one link (without its array): CatChains lays the links itself.
# Each prop is moved to the origin (centre of its footprint, on the ground) and keeps the file's real size, so the table,
# the chair and the small objects stay at the artist's relative scale. A prop's front (Blender -Y) comes out facing Unity -Z,
# the player, with an identity root (the half turn below). Modifiers (bevels, solidify, the chain's array) are applied.
import math
import os
import sys

import bpy
import mathutils

OUT = sys.argv[sys.argv.index("--") + 1]

# With these axes Blender -Y lands on Unity +Z: half a turn puts the prop's front towards the player.
FACE_THE_PLAYER = mathutils.Matrix.Rotation(math.pi, 4, "Z")

# FBX name -> root object in the file; its children come along.
PROPS = {
    "Table": "Table",
    "Chair": "Seat",
    "Timer": "Timer",
    "Candle": "Candle Base",
    "CrystalBall": "Crystal Ball Base",
    "PurpleHand": "Purple Hand",
    "Bell": "Bell Base",
    "CrystalSkull": "Red Crystal Skull",
    "WoodenBoard": "Board",
    "Sniper": "Sniper",
    "Corruptor": "Corruptor Body",
    "ChainPile": "Chain Pile",
    "ChainLink": "Individual Chain",
    "TeddyBear": "Teddy Bear Head",
    "LeafHeart": "Leaf Heart",
    "ArcaneCube": "Arcane Cube",
    "BeaconRobot": "Robot Body",
    "Totem": "Totem",
}


ONE_LINK = "ChainLink"


def tree(root):
    return [root] + list(root.children_recursive)


def bounds(objects):
    depsgraph = bpy.context.evaluated_depsgraph_get()
    low = mathutils.Vector((1e9, 1e9, 1e9))
    high = -low
    for obj in objects:
        if obj.type != "MESH":
            continue
        evaluated = obj.evaluated_get(depsgraph)
        mesh = evaluated.to_mesh()
        for vertex in mesh.vertices:
            world = evaluated.matrix_world @ vertex.co
            for axis in range(3):
                low[axis] = min(low[axis], world[axis])
                high[axis] = max(high[axis], world[axis])
        evaluated.to_mesh_clear()
    return low, high


os.makedirs(OUT, exist_ok=True)
for name, root_name in PROPS.items():
    root = bpy.data.objects[root_name]
    objects = tree(root)
    if name == ONE_LINK:
        for modifier in root.modifiers:
            modifier.show_viewport = False
            modifier.show_render = False
        bpy.context.view_layer.update()
    low, high = bounds(objects)
    pivot = mathutils.Vector(((low.x + high.x) / 2, (low.y + high.y) / 2, low.z))
    # Flat, every part at the root: the export's baked axes misplace nested parts (the clock's ring and centre). Unity
    # still finds a part by name (the clock's hands).
    moved = FACE_THE_PLAYER @ mathutils.Matrix.Translation(-pivot)
    saved = {obj: obj.matrix_world.copy() for obj in objects}
    for obj in objects:
        obj.parent = None
    for obj, world in saved.items():
        obj.matrix_world = moved @ world
    bpy.context.view_layer.update()

    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.hide_set(False)
        obj.hide_viewport = False
        obj.select_set(True)
    bpy.context.view_layer.objects.active = root

    path = os.path.join(OUT, name + ".fbx")
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        object_types={"MESH"},
        use_mesh_modifiers=True,
        mesh_smooth_type="FACE",
        add_leaf_bones=False,
        bake_anim=False,
        apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z",
        axis_up="Y",
        bake_space_transform=True,
        path_mode="STRIP",
    )
    print("EXPORTED", name, "size (m)", [round(v, 3) for v in (high - low)])

