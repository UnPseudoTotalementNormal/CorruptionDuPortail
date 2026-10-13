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
           'galactic':['Galactic Bow','Galactic Bow Mid'],'mage':['Mage Cape Bottom','Mage Cape Top','Mage Cape Star','Mage Head Star']},
 'eyes':{'bw':['BW Eyes Neutral'],'fiery':['Fiery Eyes Neutral'],'galactic':['Galactic Eyes.001','Galactic Mid'],'mage':['Mage Eyes','Mage Cheek Eyes','Mage Top Eyes']},
 'mouth':{'galactic':['Mouf'],'mage':['Mage Mouf']},
}
# make every collection renderable
def lcs(lc,out):
    out.append(lc); [lcs(c,out) for c in lc.children]; return out
for lc in lcs(vl.layer_collection,[]):
    lc.exclude=False; lc.hide_viewport=False; lc.collection.hide_render=False
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
