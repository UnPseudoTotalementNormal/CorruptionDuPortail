"""Renders each part of each cat skin alone (other parts held out) from one camera, transparent: the mockups' layered
cat and the customization screen's vignettes (cropped by tools/art/cats/README.md's icon step).
Usage: blender -b <Drive>/Corrupted_Cats.blend --python render_layers.py -- <out dir>"""
import bpy, mathutils, math, os, sys
OUT=sys.argv[-1].rstrip("/")+"/"
sc=bpy.context.scene; vl=bpy.context.view_layer
P={'bw':'','fiery':'Fiery ','galactic':'Galactic ','mage':'Mage '}
BODY=['Arms','Arms Fur','Body','Body Fur','Ears','Ears Fur','Head','Head Fur','Whiskers']
parts={
 'body':{k:[p+n for n in BODY] for k,p in P.items()},
 'outfit':{'bw':['Robe','Ribbon','Bell'],'fiery':['Fiery Cape','Fiery Cape Top','Fiery Fur Neck','Fiery Medal','Fiery Metal Decorations'],
           'galactic':['Galactic Bow','Galactic Bow Mid'],'mage':['Mage Cape Bottom','Mage Cape Top','Mage Cape Star','Mage Head Star'],
           'chef':['Chef Apron','Chef Shirt','Chef Bow Neck','Chef Bow Tie','Chef Sleeves']},
 'eyes':{'bw':['BW Eyes Neutral'],'fiery':['Fiery Eyes Neutral'],'galactic':['Galactic Eyes.001','Galactic Mid'],'mage':['Mage Eyes']},
 'mouth':{'galactic':['Mouf'],'mage':['Mage Mouf']},
}
# make every collection renderable
def lcs(lc,out):
    out.append(lc); [lcs(c,out) for c in lc.children]; return out
for lc in lcs(vl.layer_collection,[]):
    lc.exclude=False; lc.hide_viewport=False; lc.collection.hide_render=False
# Chef parts the file leaves unrigged, bound like export_cosmetics.py does (before the pose)
fr=bpy.data.objects['Fiery Rig']; fr.data.pose_position='REST'; bpy.context.view_layer.update()
def bind(o):
    m=next((m for m in o.modifiers if m.type=='ARMATURE'),None) or o.modifiers.new('Armature','ARMATURE'); m.object=fr
neck=bpy.data.objects['Chef Bow Neck']; bind(neck)
tie=bpy.data.objects['Chef Bow Tie']; mw=tie.matrix_world.copy(); tie.parent=fr; tie.parent_type='BONE'; tie.parent_bone='Spine3'; tie.matrix_world=mw
sl=bpy.data.objects['Chef Sleeves']; arm=[b for b in fr.data.bones if b.name.split('.')[0] in ('Arm1','Arm2','Hand')]
def seg(p,b):
    a=fr.matrix_world @ b.head_local; c=fr.matrix_world @ b.tail_local
    t=max(0.0,min(1.0,(p-a).dot(c-a)/max((c-a).length_squared,1e-9))); return (p-(a+(c-a)*t)).length
for v in sl.data.vertices:
    p=sl.matrix_world @ v.co; side='.L' if p.x>0 else '.R'
    b=min((b for b in arm if b.name.endswith(side)), key=lambda b: seg(p,b))
    (sl.vertex_groups.get(b.name) or sl.vertex_groups.new(name=b.name)).add([v.index],1.0,'REPLACE')
bind(sl); fr.data.pose_position='POSE'
# try an idle pose from the animation file
try:
    with bpy.data.libraries.load(os.path.join(os.path.dirname(bpy.data.filepath), "Animations", "Cat_Animations 2.blend"), link=False) as (src, dst):
        dst.actions=['Idle']
    act=bpy.data.actions['Idle']
    for r in ['BW Rig','Fiery Rig','Galactic Rig','Mage Rig']:
        o=bpy.data.objects[r]; o.animation_data_create(); o.animation_data.action=act
        try:
            if act.slots: o.animation_data.action_slot=act.slots[0]
        except Exception as e: print("slot",e)
    sc.frame_set(30); print("POSE idle ok")
except Exception as e: print("POSE fail",e)
for o in bpy.data.objects:
    if o.animation_data:
        for d in list(o.animation_data.drivers):
            if d.data_path in ('hide_render','hide_viewport'): o.animation_data.drivers.remove(d)
# The mouth cards switch textures on a Value node (the file may preview another mouth): show each card's own (0).
for n in ('Mouf','Mage Mouf'):
    for slot in bpy.data.objects[n].material_slots:
        for node in slot.material.node_tree.nodes:
            if node.type=='VALUE': node.outputs[0].default_value=0.0
allnames={n for d in parts.values() for l in d.values() for n in l}
# camera: perspective 3/4
cam=sc.camera; cam.data.type='PERSP'; cam.data.lens=85; cam.data.clip_end=1000
tgt=mathutils.Vector((0,0,-0.3)); yaw=math.radians(18); dist=19
cam.location=tgt+mathutils.Vector((math.sin(yaw)*dist,-math.cos(yaw)*dist,1.6))
cam.rotation_euler=(tgt-cam.location).to_track_quat('-Z','Y').to_euler()
sc.render.engine='BLENDER_EEVEE'; sc.render.film_transparent=True
sc.render.resolution_x=800; sc.render.resolution_y=1000
sc.render.image_settings.file_format='PNG'; sc.render.image_settings.color_mode='RGBA'
base={'body':'bw','outfit':'bw','eyes':'bw','mouth':None}
holdout_body=[P['bw']+n for n in BODY]
for part,d in parts.items():
    for skin,names in d.items():
        show=set(names); hold=set() if part=='body' else set(holdout_body)
        if part=='body': hold=set()
        for o in bpy.data.objects:
            if o.type not in ('MESH','CURVE'): continue
            o.is_holdout = o.name in hold
            o.hide_render = not (o.name in show or o.name in hold)
        sc.render.filepath=OUT+f"{part}_{skin}.png"
        bpy.ops.render.render(write_still=True)
# a full default composite for checking
for o in bpy.data.objects:
    if o.type=='MESH': o.is_holdout=False; o.hide_render= o.name not in set(parts['body']['bw']+parts['outfit']['bw']+parts['eyes']['bw'])
sc.render.filepath=OUT+"full_bw.png"; bpy.ops.render.render(write_still=True)
