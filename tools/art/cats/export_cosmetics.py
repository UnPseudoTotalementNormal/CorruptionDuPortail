"""Exports the parts of the Fiery / Galactic / Mage cats onto the game's skeleton (BW Rig + the Fiery rig's cape bones)
as Assets/Art/Models/Cat_Cosmetics.fbx. Usage: blender -b <Drive>/Corrupted_Cats.blend --python export_cosmetics.py -- <fbx>
Then in Unity: Tools > Cat Cosmetics > Rebuild assets."""
import bpy, mathutils, sys
OUT = sys.argv[-1]
for o in bpy.data.objects:
    if o.animation_data:
        for d in list(o.animation_data.drivers):
            if d.data_path in ('hide_render','hide_viewport'): o.animation_data.drivers.remove(d)
        o.animation_data.action = None
def lcs(lc,out):
    out.append(lc); [lcs(c,out) for c in lc.children]; return out
for lc in lcs(bpy.context.view_layer.layer_collection,[]):
    lc.exclude=False; lc.hide_viewport=False; lc.collection.hide_viewport=False
for o in bpy.data.objects:
    o.hide_set(False); o.hide_viewport=False
    if o.type=='ARMATURE':
        o.data.pose_position='REST'
        for pb in o.pose.bones: pb.matrix_basis=mathutils.Matrix.Identity(4)
bpy.context.view_layer.update()

bw=bpy.data.objects['BW Rig']; fiery=bpy.data.objects['Fiery Rig']
assert (bw.matrix_world.to_translation()-fiery.matrix_world.to_translation()).length<1e-4
# 1) add the cape bones of the Fiery rig to the BW rig (same object transform, so armature space matches)
src={b.name:(b.head_local.copy(), b.tail_local.copy(), b.matrix_local.copy(), b.parent.name if b.parent else None, b.use_deform) for b in fiery.data.bones}
bpy.ops.object.select_all(action='DESELECT')
bpy.context.view_layer.objects.active=bw; bw.select_set(True)
bpy.ops.object.mode_set(mode='EDIT')
eb=bw.data.edit_bones
cape=[n for n in src if n.startswith('Cape')]
# parents first
order=[]
def add(n):
    if n in order or n not in cape: return
    p=src[n][3]
    if p in cape: add(p)
    order.append(n)
for n in cape: add(n)
for n in order:
    h,t,m,p,d=src[n]
    b=eb.new(n); b.head=h; b.tail=t
    # roll from the source matrix: align z axis
    b.align_roll(m.to_3x3() @ mathutils.Vector((0,0,1)))
    b.parent=eb[p]; b.use_deform=d; b.use_connect=False
bpy.ops.object.mode_set(mode='OBJECT')
print("CAPE BONES ADDED", len(order))

parts={
 'Fiery Cape':'arm','Fiery Cape Top':'arm','Fiery Fur Neck':'arm','Fiery Medal':('bone','Cape Master'),'Fiery Metal Decorations':('keep',None),
 'Galactic Bow':'arm','Galactic Bow Mid':('bone','Bow Center'),
 'Mage Cape Bottom':'arm','Mage Cape Top':'arm','Mage Cape Star':('bone','Cape Master'),'Mage Head Star':('bone','Head'),
 'Fiery Eyes Neutral':('bone','Eyes'),'Galactic Eyes.001':('bone','Eyes'),'Galactic Mid':('bone','Eyes'),
 'Mage Eyes':('bone','Eyes'),'Mage Cheek Eyes':('bone','Eyes'),'Mage Top Eyes':('bone','Eyes'),
 'Mouf':('bone','Head'),'Mage Mouf':('bone','Head'),
}
for n,how in parts.items():
    o=bpy.data.objects[n]; mw=o.matrix_world.copy()
    if how=='arm':
        mods=[m for m in o.modifiers if m.type=='ARMATURE']
        if not mods:
            m=o.modifiers.new('Armature','ARMATURE'); mods=[m]
        for m in mods: m.object=bw
        o.parent=bw; o.parent_type='OBJECT'
    elif how[0]=='bone':
        o.parent=bw; o.parent_type='BONE'; o.parent_bone=how[1]
    o.matrix_world=mw
    for m in list(o.modifiers):
        if m.type=='PARTICLE_SYSTEM': o.modifiers.remove(m)
bpy.context.view_layer.update()
# rename so Unity gets clean names
names={'Galactic Eyes.001':'Galactic Eyes','Mouf':'Galactic Mouth','Mage Mouf':'Mage Mouth'}
for a,b in names.items(): bpy.data.objects[a].name=b
sel=[bw]+[bpy.data.objects[names.get(n,n)] for n in parts]
bpy.ops.object.select_all(action='DESELECT')
for o in sel: o.select_set(True)
bpy.context.view_layer.objects.active=bw
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, object_types={'ARMATURE','MESH'}, use_mesh_modifiers=True,
    add_leaf_bones=False, bake_anim=False, use_armature_deform_only=False, apply_scale_options='FBX_SCALE_NONE', mesh_smooth_type='FACE')
print("EXPORTED", OUT)
