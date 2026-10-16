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

# --- Black and white eyes: the blink mesh, and a copy of the open eyes keeping the "Angry" shape key (the game's
# model lost it). Both hang on the Eyes bone.
closed=bpy.data.objects['BW Eyes Closed']; closed.hide_render=False; closed.hide_set(False)
expressive=bpy.data.objects['BW Eyes Neutral'].copy(); expressive.data=bpy.data.objects['BW Eyes Neutral'].data.copy()
expressive.name='BW Eyes Expressive'; bpy.context.scene.collection.objects.link(expressive)
for o in (closed, expressive):
    mw=o.matrix_world.copy(); o.parent=bw; o.parent_type='BONE'; o.parent_bone='Eyes'; o.matrix_world=mw
    o.data.materials.clear()
closed.data.materials.append(bpy.data.materials.new('BW Eyes Closed'))
expressive.data.materials.append(bpy.data.materials.new('BW Eyes Expressive'))
parts['BW Eyes Closed']=('keep',None); parts['BW Eyes Expressive']=('keep',None)

# --- Universal eyes (flat shapes at the origin in the file): one per side, placed and sized on the fiery cat's
# two plain eyes (centre, height, front surface of each eye), mirrored on the right, joined in one object on the
# Eyes bone. The object's own rotation / scale is kept (its mesh data is not at unit size).
src=bpy.data.objects['Fiery Eyes Neutral']
vs=[src.matrix_world @ v.co for v in src.data.vertices]
def side(pred):
    p=[v for v in vs if pred(v.x)]
    xs=[v.x for v in p]; ys=[v.y for v in p]; zs=[v.z for v in p]
    return mathutils.Vector(((min(xs)+max(xs))/2, min(ys), (min(zs)+max(zs))/2)), max(zs)-min(zs)
eyes_lr={'L':side(lambda x:x>0), 'R':side(lambda x:x<0)}
for name in ['Simple','Bored','Full Button']:
    o=bpy.data.objects[name]; copies=[]
    for key,(centre,height) in eyes_lr.items():
        c=o.copy(); c.data=o.data.copy(); bpy.context.scene.collection.objects.link(c)
        k=height*1.0/max(o.dimensions.z,1e-4)
        own=o.matrix_world.copy(); own.translation=mathutils.Vector((0,0,0))
        c.matrix_world=mathutils.Matrix.Translation(centre+mathutils.Vector((0,-0.01,0))) @ mathutils.Matrix.Diagonal((k if key=='L' else -k,k,k,1)) @ own
        copies.append(c)
    bpy.ops.object.select_all(action='DESELECT')
    for c in copies: c.select_set(True)
    bpy.context.view_layer.objects.active=copies[0]
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    bpy.ops.object.join()
    j=bpy.context.view_layer.objects.active; j.name=name+' Eyes'
    # mirrored copy: flip its normals back
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT'); bpy.ops.mesh.normals_make_consistent(inside=False); bpy.ops.object.mode_set(mode='OBJECT')
    mw=j.matrix_world.copy(); j.parent=bw; j.parent_type='BONE'; j.parent_bone='Eyes'; j.matrix_world=mw
    parts[j.name]=('keep',None)

# --- Universal mouths: the mage mouth card with each expression texture (Choked, Drunk, Sad).
mm=bpy.data.objects['Mage Mouf']
for name in ['Choked','Drunk','Sad']:
    c=mm.copy(); c.data=mm.data.copy(); c.name=name+' Mouth'; bpy.context.scene.collection.objects.link(c)
    c.data.materials.clear(); c.data.materials.append(bpy.data.materials.new(name+' Mouth'))
    c.parent=mm.parent; c.parent_type=mm.parent_type; c.parent_bone=mm.parent_bone; c.matrix_world=mm.matrix_world.copy()
    parts[c.name]=('keep',None)
# --- Tears (Crying): the emitter on the Head bone and the tear card, from the animation file (same rig, rest pose).
import os
anim_path=os.path.join(os.path.dirname(bpy.data.filepath),'Animations','Cat_Animations 2.blend')
with bpy.data.libraries.load(anim_path, link=False) as (src_lib, dst_lib):
    dst_lib.objects=['Tear Emitter','Tear Particle']
for o in dst_lib.objects:
    bpy.context.scene.collection.objects.link(o)
emitter=bpy.data.objects['Tear Emitter']; tear=bpy.data.objects['Tear Particle']
anim_rig=emitter.parent
if anim_rig is not None and anim_rig.type=='ARMATURE':
    anim_rig.data.pose_position='REST'; bpy.context.view_layer.update()
    rel=(anim_rig.matrix_world @ anim_rig.data.bones['Head'].matrix_local).inverted() @ emitter.matrix_world
    world=bw.matrix_world @ bw.data.bones['Head'].matrix_local @ rel
else:
    world=emitter.matrix_world.copy()
for m in list(emitter.modifiers): emitter.modifiers.remove(m)
emitter.parent=bw; emitter.parent_type='BONE'; emitter.parent_bone='Head'; emitter.matrix_world=world
tear.parent=None; tear.matrix_world=mathutils.Matrix.Identity(4)
parts['Tear Emitter']=('keep',None); parts['Tear Particle']=('keep',None)
bpy.context.view_layer.update()
# rename so Unity gets clean names
names={'Galactic Eyes.001':'Galactic Eyes','Mouf':'Galactic Mouth','Mage Mouf':'Mage Mouth'}
for a,b in names.items(): bpy.data.objects[a].name=b
sel=[bw]+[bpy.data.objects[names.get(n,n)] for n in parts]
# shape keys only survive without modifiers on that mesh (BW Eyes Expressive has none)
bpy.ops.object.select_all(action='DESELECT')
for o in sel: o.select_set(True)
bpy.context.view_layer.objects.active=bw
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, object_types={'ARMATURE','MESH'}, use_mesh_modifiers=True,
    add_leaf_bones=False, bake_anim=False, use_armature_deform_only=False, apply_scale_options='FBX_SCALE_NONE', mesh_smooth_type='FACE')
print("EXPORTED", OUT)
