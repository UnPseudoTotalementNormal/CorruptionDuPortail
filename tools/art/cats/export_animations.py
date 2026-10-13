"""Exports every animation of the cat (the black and white rig's actions) as takes of one skeleton-only FBX:
Assets/Art/Models/Cat_Animations_v2.fbx. The full Wave comes from the older Cat_Animations.blend (it is empty in the
newer file); Fiery Idle Test (made on the fiery rig, same bone names) is played on this rig.
Usage: blender -b "<Drive>/Animations/Cat_Animations 2.blend" --python export_animations.py -- <fbx>"""
import bpy, sys, os
OUT=sys.argv[-1]
here=os.path.dirname(bpy.data.filepath)
rig=[o for o in bpy.data.objects if o.type=='ARMATURE' and o.name=='BW Rig' and o.library is None and o.animation_data][0]
print("RIG", rig.name, rig.library)
# the full Wave lives in the older file (it is empty in this one)
bpy.data.actions['Wave'].name='Wave (empty)'
with bpy.data.libraries.load(os.path.join(here,'Cat_Animations.blend'), link=False) as (src,dst):
    dst.actions=['Wave']
wave=bpy.data.actions['Wave']
drop={'Wave (empty)','Puts ','BW RigAction','CubeAction'}
for a in list(bpy.data.actions):
    if a.name in drop: bpy.data.actions.remove(a)
# NLA off: each action is exported as its own take
ad=rig.animation_data
for t in list(ad.nla_tracks): ad.nla_tracks.remove(t)
acts=[a for a in bpy.data.actions]
for a in acts:
    a.use_fake_user=True
    ad.action=a
    try:
        if ad.action_slot is None or ad.action_slot.target_id_type!='OBJECT':
            pass
        if ad.action_slot is None:
            s=a.slots.new(id_type='OBJECT', name=rig.name); ad.action_slot=s
    except Exception as e: print("slot", a.name, e)
    print("ACT", a.name, tuple(a.frame_range), "slot", ad.action_slot and ad.action_slot.identifier)
ad.action=None
# The game's Cat_Avatar (from Cat_Animations_v1.fbx) has its BW Rig node here: the rig's own curve must not move the
# cat (this file's rig sits 2.46 lower). Bones are relative to the rig, unchanged. Unity keeps the BW Rig node only with
# "Preserve Hierarchy" (a single-root FBX loses it): CatAnimationsAssetBuilder sets it.
rig.location=(0.0, 0.0207, 0.0139)
for o in bpy.data.objects: o.select_set(False)
rig.select_set(True); bpy.context.view_layer.objects.active=rig
rig.data.pose_position='POSE'
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, object_types={'ARMATURE'}, add_leaf_bones=False,
    use_armature_deform_only=False, bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
    bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0, apply_scale_options='FBX_SCALE_NONE')
print("EXPORTED")
