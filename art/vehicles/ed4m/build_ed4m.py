"""Rebuild the ED4M art master, glTF modules and 8-way transparent sprites.

Blender 4.5 LTS: blender --background --python art/vehicles/ed4m/build_ed4m.py
Model axes: +Z up, -Y cab forward. Dimensions are art proportions in metres.
"""
from pathlib import Path
import bpy, math, json, sys
from mathutils import Vector
from bpy_extras.object_utils import world_to_camera_view

ROOT = Path(__file__).resolve().parents[3]
ART = ROOT / 'art/vehicles/ed4m'
OUT = ROOT / 'game/assets/vehicles/ed4m'
for folder in [ART, OUT/'models', OUT/'sprites']:
    folder.mkdir(parents=True, exist_ok=True)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
for data in bpy.data.materials:
    bpy.data.materials.remove(data)

def material(name, color, metallic=0, roughness=.65, variation=.035):
    mat=bpy.data.materials.new(name)
    mat.diffuse_color=(*color,1)
    mat.use_nodes=True
    nodes=mat.node_tree.nodes
    bsdf=nodes.get('Principled BSDF')
    bsdf.inputs['Base Color'].default_value=(*color,1)
    bsdf.inputs['Metallic'].default_value=metallic
    bsdf.inputs['Roughness'].default_value=roughness
    if variation:
        tex=nodes.new('ShaderNodeTexNoise')
        tex.inputs['Scale'].default_value=17
        tex.inputs['Detail'].default_value=2
        ramp=nodes.new('ShaderNodeValToRGB')
        ramp.color_ramp.elements[0].color=(*[max(0,c-variation) for c in color],1)
        ramp.color_ramp.elements[1].color=(*[min(1,c+variation) for c in color],1)
        mat.node_tree.links.new(tex.outputs['Fac'],ramp.inputs['Fac'])
        mat.node_tree.links.new(ramp.outputs['Color'],bsdf.inputs['Base Color'])
    return mat

# Linear shader colours chosen to keep the reference's muted green under daylight.
M={
 'green':material('01 / faded bottle green',(.10,.16,.09),.16,.7),
 'green_light':material('02 / raised body details',(.105,.165,.094),.12,.68),
 'green_dark':material('03 / door recesses',(.065,.102,.067),.12,.75,.015),
 'cream':material('04 / warm ivory trim',(.60,.57,.44),.1,.68,.025),
 'roof':material('05 / zinc grey roof',(.23,.24,.23),.35,.72,.025),
 'steel':material('06 / underframe steel',(.035,.045,.045),.62,.52,.010),
 'black':material('07 / rubber and gaskets',(.018,.023,.025),.02,.85,.005),
 'glass':material('08 / opaque blue smoke glass',(.045,.073,.085),.15,.19,.003),
 'glass_light':material('09 / glass reflection',(.08,.12,.13),.15,.22,.003),
 'orange':material('10 / vermilion warning paint',(.58,.065,.022),.08,.68,.02),
 'lamp':material('11 / headlight lens',(.78,.72,.49),.1,.25,.005),
 'red':material('12 / tail light lens',(.46,.018,.012),.2,.28,.005),
 'copper':material('13 / pantograph red steel',(.27,.071,.026),.45,.62,.015),
 'insulator':material('14 / glazed brown ceramic',(.18,.095,.052),.15,.35,.01),
 'aluminium':material('15 / window metal frames',(.42,.43,.38),.65,.43,.01),
 'dust':material('16 / brake dust',(.09,.075,.05),.15,.8,.010),
}

ACTIVE=None
exec(compile((ART/'materials_detail.py').read_text(encoding='utf-8'),str(ART/'materials_detail.py'),'exec'),globals())
def finish(obj, name, mat, bevel=0, parent=True):
    obj.name=name
    if mat: obj.data.materials.append(M[mat] if isinstance(mat,str) else mat)
    if parent and ACTIVE: obj.parent=ACTIVE
    if bevel:
        for face in obj.data.polygons:face.use_smooth=True
        mod=obj.modifiers.new('Small manufactured edge','BEVEL')
        mod.width=bevel;mod.segments=3;mod.harden_normals=True
        mod=obj.modifiers.new('Weighted corner normals','WEIGHTED_NORMAL')
        mod.keep_sharp=True
    return obj

def box(name, loc, size, mat, bevel=.015):
    x,y,z=[v/2 for v in size]
    verts=[(-x,-y,-z),(x,-y,-z),(x,y,-z),(-x,y,-z),(-x,-y,z),(x,-y,z),(x,y,z),(-x,y,z)]
    data=bpy.data.meshes.new(name)
    data.from_pydata(verts,[],[(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)])
    data.update()
    obj=bpy.data.objects.new(name,data);bpy.context.collection.objects.link(obj)
    obj.location=loc
    return finish(obj,name,mat,bevel)

def mesh(name, verts, faces, mat):
    data=bpy.data.meshes.new(name)
    data.from_pydata(verts,[],faces);data.update()
    obj=bpy.data.objects.new(name,data)
    bpy.context.collection.objects.link(obj)
    return finish(obj,name,mat)

def poly(name, coords, mat):
    return mesh(name,coords,[tuple(range(len(coords)))],mat)

def cyl(name, a, b, radius, mat, sides=10):
    a,b=Vector(a),Vector(b)
    half=(b-a).length/2
    verts=[(radius*math.cos(i*math.tau/sides),radius*math.sin(i*math.tau/sides),z) for z in [-half,half] for i in range(sides)]
    faces=[(i,(i+1)%sides,(i+1)%sides+sides,i+sides) for i in range(sides)]+[tuple(range(sides-1,-1,-1)),tuple(range(sides,2*sides))]
    data=bpy.data.meshes.new(name);data.from_pydata(verts,[],faces);data.update()
    obj=bpy.data.objects.new(name,data);bpy.context.collection.objects.link(obj)
    obj.location=(a+b)/2
    obj.rotation_euler=(b-a).to_track_quat('Z','Y').to_euler()
    return finish(obj,name,mat,.008)

def path(name, coords, radius, mat):
    for i in range(len(coords)-1): cyl(name+f'.{i}',coords[i],coords[i+1],radius,mat,8)

def coil(name,loc,radius=.13,height=.39):
    curve=bpy.data.curves.new(name,'CURVE');curve.dimensions='3D'
    curve.bevel_depth=.024;curve.bevel_resolution=2
    spline=curve.splines.new('POLY');spline.points.add(79)
    for i,point in enumerate(spline.points):
        t=i/79;angle=t*math.tau*3.5
        point.co=(loc[0]+radius*math.cos(angle),loc[1]+radius*math.sin(angle),loc[2]+height*t,1)
    obj=bpy.data.objects.new(name,curve);bpy.context.collection.objects.link(obj)
    return finish(obj,name,'steel')

def ring_front(name, points, border, y, mat):
    # Flat ring on the X/Z front plane; constant-width-enough art trim.
    cx=sum(p[0] for p in points)/len(points)
    cz=sum(p[1] for p in points)/len(points)
    inner=[]
    for x,z in points:
        vec=Vector((x-cx,z-cz));vec*=max(0,(vec.length-border)/vec.length)
        inner.append((cx+vec.x,cz+vec.y))
    n=len(points)
    return mesh(name,[(x,y,z) for x,z in points+inner],[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)],mat)

def side_rect(name, side, y, z, length, height, mat, depth=.018, bevel=.025):
    return box(name,(side*(1.755+depth/2),y,z),(depth,length,height),mat,bevel)

def text_obj(name, text, loc, size, mat, front=True, side=1):
    curve=bpy.data.curves.new(name,'FONT')
    curve.body=text;curve.size=size;curve.align_x='CENTER';curve.extrude=.001
    obj=bpy.data.objects.new(name,curve);bpy.context.collection.objects.link(obj)
    obj.location=loc
    if front: obj.rotation_euler=(math.pi/2,0,0)
    else: obj.rotation_euler=(math.pi/2,0,side*math.pi/2)
    return finish(obj,name,mat)

def roof(kind):
    # A faceted elliptic crown produces readable highlights without a bevelled toy roof.
    profile=[(-1.77,3.43),(-1.72,3.64),(-1.57,3.83),(-1.27,4.00),(-.80,4.10),(0,4.14),(.80,4.10),(1.27,4),(1.57,3.83),(1.72,3.64),(1.77,3.43)]
    front=-9.35 if kind=='head' else -10.51
    verts=[(x,y,z) for y in [front,10.51] for x,z in profile]
    n=len(profile)
    faces=[(i,i+1,i+1+n,i+n) for i in range(n-1)]+[tuple(range(n-1,-1,-1)),tuple(range(n,2*n)),(0,n,2*n-1,n-1)]
    obj=mesh('Continuous elliptical steel roof',verts,faces,'roof')
    for face in obj.data.polygons[:n-1]:face.use_smooth=True
    mod=obj.modifiers.new('Roof edge','BEVEL');mod.width=.025;mod.segments=2
    for y in [-9,-6,-3,0,3,6,9]:
        path('Roof sheet seam',[(x,y,z+.012) for x,z in profile[1:-1]],.012,'roof')
    for y in [-8.5,-5.2,-1.9,1.4,4.7,8.0]:
        box('Low rectangular ventilator',(0,y,4.19),(.75,.60,.13),'roof',.025)
        for x in [-.24,0,.24]: box('Vent ribs',(x,y,4.266),(.055,.48,.025),'steel',0)

def bogie(y):
    box('Bogie bolster',(0,y,.72),(2.47,2.8,.40),'steel',.08)
    for side in [-1,1]:
        box('Cast bogie upper frame',(side*1.53,y,.91),(.20,3.55,.16),'steel',.055)
        box('Cast bogie lower frame',(side*1.53,y,.51),(.19,2.85,.12),'steel',.045)
        for yy in [y-1.15,y,y+1.15]:
            cyl('Cast frame cross brace',(side*1.53,yy-.16,.54),(side*1.53,yy+.12,.91),.085,'steel',16)
        for ax in [-1.15,1.15]:
            yy=y+ax
            cyl('Wheel tread',(side*1.10,yy,.50),(side*1.45,yy,.50),.49,'steel',32)
            cyl('Wheel machined rim',(side*1.455,yy,.50),(side*1.48,yy,.50),.43,'steel',32)
            cyl('Wheel recessed face',(side*1.485,yy,.50),(side*1.50,yy,.50),.33,'dust',32)
            box('Axlebox',(side*1.63,yy,.52),(.20,.39,.29),'steel',.05)
            cyl('Axle bearing cover',(side*1.74,yy,.52),(side*1.78,yy,.52),.105,'steel',24)
        for yy in [y-.7,y+.7]:
            coil('Helical primary suspension',(side*1.57,yy,.57))
            cyl('Suspension upper spring seat',(side*1.57,yy,.96),(side*1.57,yy,.99),.18,'steel',20)
            cyl('Suspension lower spring seat',(side*1.57,yy,.54),(side*1.57,yy,.57),.18,'steel',20)
        cyl('Bogie damper',(side*1.30,y-.33,.56),(side*1.30,y-.14,1.0),.065,'steel',16)
        for yy in [y-1.45,y+1.45]:
            box('Brake shoe carrier',(side*1.12,yy,.51),(.14,.16,.37),'steel',.025)
        path('Brake rigging',[(side*1.24,y-1.45,.56),(side*1.24,y,.39),(side*1.24,y+1.45,.56)],.033,'steel')
        for yy in [y-1.5,y-.9,y-.3,y+.3,y+.9,y+1.5]:
            cyl('Bogie frame fastener',(side*1.64,yy,.91),(side*1.68,yy,.91),.040,'dust',12)
    for yy in [y-1.15,y+1.15]: cyl('Axle',(-1.1,yy,.5),(1.1,yy,.5),.095,'steel')

def gangway(y):
    sign=1 if y>0 else -1
    box('End panel',(0,y,2.41),(3.22,.11,2.5),'green',.13)
    box('End connection dark opening',(0,y+sign*.09,2.30),(1.18,.12,2.05),'black',.07)
    for i in range(5):
        yy=y+sign*(.10+i*.065)
        for x in [-.68,.68]: box('Bellows side pleat',(x,yy,2.35),(.12,.05,2.13),'black',.015)
        box('Bellows top pleat',(0,yy,3.42),(1.44,.05,.13),'black',.025)
    box('Gangway footplate',(0,y+sign*.24,1.25),(1.22,.45,.09),'steel',.025)
    coupler(y+sign*.35,sign)

def coupler(y,sign):
    box('Coupling shank',(0,y+sign*.22,.89),(.20,.66,.18),'steel',.025)
    box('SA3 knuckle',(0,y+sign*.52,.90),(.40,.27,.34),'steel',.045)
    box('SA3 jaw',(sign*.15,y+sign*.64,.92),(.17,.20,.28),'steel',.02)

def door(side,y):
    side_rect('Vestibule dark recess',side,y,2.17,1.57,2.20,'green_dark',.02,.035)
    side_rect('Double passenger door',side,y,2.18,1.43,2.10,'green_light',.044,.025)
    for dy in [-.36,.36]:
        side_rect('Door window gasket',side,y+dy,2.76,.47,.74,'black',.058,.035)
        side_rect('Door glass',side,y+dy,2.76,.38,.65,'glass',.068,.025)
        side_rect('Door kick panel',side,y+dy,1.55,.59,.50,'green',.066,.01)
        side_rect('Door grab handle',side,y+dy+(.18 if dy<0 else -.18),2.12,.035,.24,'aluminium',.086,.008)
    side_rect('Door centre seam',side,y,2.18,.025,2.08,'green_dark',.08,0)
    for yy in [y-.84,y+.84]:
        cyl('Vestibule handrail',(side*1.84,yy,1.34),(side*1.84,yy,2.4),.022,'aluminium',8)
    for level,reach in [(1.13,.13),(.91,.26),(.69,.38)]:
        box('Boarding stair',(side*(1.75+reach/2),y,level),(reach,1.47,.085),'steel',.015)

def window(side,y):
    side_rect('Window rubber outer',side,y,2.87,1.27,.99,'black',.028,.06)
    side_rect('Window aluminium frame',side,y,2.87,1.20,.92,'aluminium',.042,.05)
    side_rect('Window glass',side,y,2.87,1.10,.82,'glass',.054,.04)
    side_rect('Window upper reflection',side,y,3.12,1.08,.19,'glass_light',.060,.015)
    side_rect('Opening light mullion',side,y,3.08,1.12,.028,'aluminium',.065,0)
    side_rect('Rubber sill',side,y,2.38,1.29,.035,'green_dark',.055,0)

def pantograph():
    for x in [-.66,.66]:
        for y in [.9,2.7]:
            cyl('Pantograph insulator',(x,y,4.15),(x,y,4.43),.11,'insulator',10)
            for z in [4.21,4.29,4.37]: cyl('Insulator skirt',(x,y,z),(x,y,z+.035),.155,'insulator',10)
    box('Pantograph base',(0,1.8,4.48),(1.55,2.15,.14),'steel',.025)
    # Two parallel rhombi, spring and transverse contact head.
    for x in [-.42,.42]:
        for a,b in [((x,1.8,4.58),(x,.58,5.39)),((x,.58,5.39),(x,1.8,6.15)),((x,1.8,6.15),(x,3.02,5.39)),((x,3.02,5.39),(x,1.8,4.58))]:
            cyl('Diamond pantograph arm',a,b,.047,'copper',8)
        cyl('Pantograph spring',(x,1.04,4.65),(x,2.56,4.65),.065,'steel',10)
    for y,z in [(.58,5.39),(3.02,5.39),(1.8,6.15)]: cyl('Pantograph crossbar',(-.48,y,z),(.48,y,z),.045,'steel',8)
    path('Contact bow',[(-1.15,1.8,6.11),(-.9,1.8,6.29),(.9,1.8,6.29),(1.15,1.8,6.11)],.045,'steel')
    cyl('Carbon contact strip',(-.94,1.8,6.31),(.94,1.8,6.31),.035,'black',8)
    box('Roof equipment box',(0,-2.6,4.30),(1.30,1.75,.30),'roof',.035)
    for y in [-3.18,-2.96,-2.74,-2.52,-2.30,-2.08]: box('Resistor grille',(0,y,4.47),(1.12,.075,.025),'steel',0)
    for y in [-4.5,4.8,6]:
        cyl('HV ceramic column',(.73,y,4.16),(.73,y,4.57),.10,'insulator')
        for z in [4.25,4.36,4.47]: cyl('HV column skirt',(.73,y,z),(.73,y,z+.03),.145,'insulator')
    path('Roof HV bus',[(.73,-4.5,4.58),(.73,4.8,4.58),(.73,6,4.58)],.025,'copper')

# Override the first prototype's planar front with the photographic profile.
exec(compile((ART/'cab_geometry.py').read_text(encoding='utf-8'),str(ART/'cab_geometry.py'),'exec'),globals())

def build(kind):
    global ACTIVE
    ACTIVE=bpy.data.objects.new('ED4M_'+kind,None)
    ACTIVE['vehicle']='ED4M';ACTIVE['module']=kind
    ACTIVE['forward']='-Y';ACTIVE['units']='metres';ACTIVE['art_length_m']=21.5
    bpy.context.collection.objects.link(ACTIVE)
    box('Underframe sill',(0,0,1.08),(3.36,21.1,.27),'steel',.065)
    front=-9.35 if kind=='head' else -10.5
    box('Ribbed steel body',(0,(front+10.5)/2,2.42),(3.50,10.5-front,2.55),'green',.10)
    roof(kind)
    for side in [-1,1]:
        center=(front+10.4)/2;span=10.4-front
        side_rect('Cream window belt',side,center,2.93,span,1.13,'cream',.012,.025)
        side_rect('Roof side drip rail',side,center,3.51,span,.065,'green_dark',.045,.01)
        side_rect('Lower cream keyline',side,center,2.29,span,.055,'cream',.028,0)
        # Driving trailer has a short cab zone and the near passenger vestibule set back.
        doors=[-8.05,8.78] if kind=='head' else [-8.78,8.78]
        for y in doors: door(side,y)
        for y in [-6.53,-4.91,-3.29,-1.67,-.05,1.57,3.19,4.81,6.43]: window(side,y)
        for z in [1.36,1.52,1.68,1.84,2.00,2.13]:
            for a,b in [(-7.30,7.86),(-10.35,-9.62),(9.65,10.35)]:
                side_rect('Pressed horizontal body rib',side,(a+b)/2,z,b-a,.043,'green_light',.036,.008)
        for y in [-6.8,-3.2,3.4,6.9]:
            side_rect('Underframe access panel',side,y,.91,1.40,.24,'steel',.04,.015)
        side_rect('Small fleet plate',side,0,1.78,.67,.19,'cream',.05,.01)
        for yy in [-.17,0,.17]: side_rect('Fleet plate print',side,yy,1.78,.065,.10,'green_dark',.065,0)
    for y in [-7.55,7.55]: bogie(y)
    for y,size in [(-3.8,2.1),(-.7,2.9),(2.8,2.3)]:
        box('Underfloor equipment cabinet',(0,y,.63),(2.24,size,.58),'steel',.04)
        for side in [-1,1]:
            for yy in [y-size/2+.18,y+size/2-.18]: box('Equipment cabinet latch',(side*1.14,yy,.64),(.035,.08,.17),'dust',.005)
    cyl('Air reservoir',(-.73,4.65,.63),(.73,4.65,.63),.29,'steel',12)
    if kind=='head': cab()
    else: gangway(-10.54)
    gangway(10.54)
    if kind=='motor': pantograph()
    result=ACTIVE
    ACTIVE=None
    return result

parts={k:build(k) for k in ['head','motor','trailer']}

def children(root): return [root,*root.children_recursive]
def show_only(kind):
    for k,root in parts.items():
        root.location=(0,0,0);root.rotation_euler=(0,0,0)
        for obj in children(root): obj.hide_render=k!=kind

exec(compile((ART/'export_detail.py').read_text(encoding='utf-8'),str(ART/'export_detail.py'),'exec'),globals())
if '--review-only' not in sys.argv:
    export_glbs()

scene=bpy.context.scene
scene.render.engine='CYCLES'
scene.cycles.samples=64
scene.cycles.use_denoising=True
scene.cycles.max_bounces=6
scene.cycles.diffuse_bounces=3
scene.cycles.glossy_bounces=3
scene.render.image_settings.file_format='PNG'
scene.render.image_settings.color_mode='RGBA'
scene.render.film_transparent=True
scene.render.resolution_percentage=100
scene.world.color=(.28,.28,.28)
scene.view_settings.view_transform='AgX'
scene.view_settings.look='AgX - Medium High Contrast'
scene.view_settings.exposure=0

def light(name,loc,energy,size):
    data=bpy.data.lights.new(name,'AREA');data.energy=energy;data.shape='DISK';data.size=size
    obj=bpy.data.objects.new(name,data);scene.collection.objects.link(obj);obj.location=loc
    obj.rotation_euler=(Vector((0,0,1.7))-obj.location).to_track_quat('-Z','Y').to_euler()
    return obj

key=light('Large soft northwest daylight',(24,-32,36),12000,18)
fill=light('Cool soft fill',(-20,12,22),10000,22)
sun_data=bpy.data.lights.new('Diffuse daylight sun','SUN');sun_data.energy=2.0;sun_data.angle=.22
sun=bpy.data.objects.new('Diffuse daylight sun',sun_data);scene.collection.objects.link(sun)
sun.rotation_euler=(math.radians(30),math.radians(25),math.radians(15))
cam_data=bpy.data.cameras.new('2 to 1 isometric camera');cam_data.type='ORTHO'
camera=bpy.data.objects.new('2 to 1 isometric camera',cam_data);scene.collection.objects.link(camera);scene.camera=camera

def set_camera(target,scale):
    target=Vector(target)
    camera.location=target+Vector((40,-40,math.sqrt(2)*40/math.sqrt(3)))
    camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler()
    camera.data.ortho_scale=scale

if '--review-only' in sys.argv or '--review-and-build' in sys.argv:
    show_only('head')
    floor_mat=material('Review warm grey ground',(.38,.36,.32),0,.85,.004)
    floor=box('Review ground',(0,0,-.22),(1000,1000,.2),floor_mat,.01)
    scene.render.film_transparent=False
    scene.render.resolution_x=2560;scene.render.resolution_y=1440
    scene.cycles.samples=32 if '--head-update' in sys.argv else 64
    set_camera((0,-.3,2),23)
    scene.render.filepath=str(ART/'ed4m-v02-full.png')
    bpy.ops.render.render(write_still=True)
    scene.render.resolution_x=2048;scene.render.resolution_y=1536
    scene.cycles.samples=64
    target=Vector((0,-8.7,2.1));camera.location=target+Vector((18,-24,14))
    camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.ortho_scale=8.3
    scene.render.filepath=str(ART/'ed4m-v02-cab.png')
    bpy.ops.render.render(write_still=True)
    target=Vector((0,-8.3,2.15));camera.location=target+Vector((30,0,0))
    camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.ortho_scale=8.3
    scene.render.filepath=str(ART/'ed4m-v02-profile.png')
    scene.cycles.samples=32
    bpy.ops.render.render(write_still=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(ART/'ed4m-v02-review.blend'))
    print('ED4M_REVIEW_COMPLETE',flush=True)
    if '--review-only' in sys.argv:
        sys.exit(0)
    floor.hide_render=True
    scene.render.film_transparent=True
    scene.cycles.samples=64

set_camera((0,0,2.1),27)
SPRITE_SIZE=1024
scene.render.resolution_x=SPRITE_SIZE;scene.render.resolution_y=SPRITE_SIZE
bpy.context.view_layer.update()
p=world_to_camera_view(scene,camera,Vector((0,0,0)))
pivot=[round(p.x*SPRITE_SIZE,3),round((1-p.y)*SPRITE_SIZE,3)]
directions=[('s',0,1),('sw',-1,1),('w',-1,0),('nw',-1,-1),('n',0,-1),('ne',1,-1),('e',1,0),('se',1,1)]
manifest={
 'id':'ed4m','version':2,'source':'art/vehicles/ed4m/ed4m.blend',
 'projection':{'type':'orthographic','tile_ratio':[2,1],'elevation_degrees':30},
 'frame_size':[SPRITE_SIZE,SPRITE_SIZE],'pivot_px':pivot,'alpha':'straight','pixels_per_metre':SPRITE_SIZE/27,
 'length_m':21.5,'coupling_pitch_m':22.35,
 'directions':[{'name':n,'grid_delta':[dx,dy]} for n,dx,dy in directions],
 'parts':{}
}
for kind,root in parts.items():
    show_only(kind)
    frames=[]
    for name,dx,dy in ([] if '--preview-only' in sys.argv else directions):
        root.rotation_euler.z=math.atan2(dx,dy)
        path_out=OUT/'sprites'/f'ed4m_{kind}_{name}.png'
        scene.render.filepath=str(path_out)
        if '--model-only' not in sys.argv and not ('--head-update' in sys.argv and kind!='head'):
            bpy.ops.render.render(write_still=True)
        frames.append({'direction':name,'file':f'sprites/{path_out.name}'})
    manifest['parts'][kind]={'model':f'models/ed4m_{kind}.glb','frames':frames}
    root.rotation_euler.z=0
(OUT/'ed4m.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')

# Neutral beauty view of an assembled four-car set, for visual inspection.
for k,root in parts.items():
    for obj in children(root): obj.hide_render=False
parts['head'].location=(0,-33.525,0)
parts['motor'].location=(0,-11.175,0)
parts['trailer'].location=(0,11.175,0)
rear=bpy.data.objects.new('ED4M_rear_head',None);scene.collection.objects.link(rear)
rear.location=(0,33.525,0);rear.rotation_euler.z=math.pi
for obj in parts['head'].children_recursive:
    copy=obj.copy()
    if obj.data: copy.data=obj.data
    scene.collection.objects.link(copy)
    copy.parent=rear

ground_mat=material('Presentation ground',(.36,.35,.31),0,.85,.006)
ground=box('Presentation floor',(0,0,-.22),(1000,1000,.2),ground_mat,.02)
ACTIVE=None
for yy in [i*.65-47 for i in range(145)]: box('Track sleeper',(0,yy,-.035),(2.8,.22,.16),'steel',.025)
for x in [-.76,.76]:
    box('Rail web',(x,0,.04),(.075,95,.14),'steel',.008)
    box('Rail running head',(x,0,.13),(.11,95,.055),'aluminium',.01)
scene.render.film_transparent=False
scene.render.resolution_x=1920;scene.render.resolution_y=1080
scene.cycles.samples=64
set_camera((0,-1,1.9),87)
scene.render.filepath=str(ART/'ed4m-preview.png')
if '--model-only' not in sys.argv and '--skip-hero' not in sys.argv:
    bpy.ops.render.render(write_still=True)

# Save a usable, arranged master. Presentation objects live in their own collection.
presentation=bpy.data.collections.new('Presentation / toggle for isolated models')
scene.collection.children.link(presentation)
model_objects=set()
for root in parts.values(): model_objects.update(children(root))
for obj in list(scene.objects):
    if obj not in model_objects:
        for coll in list(obj.users_collection): coll.objects.unlink(obj)
        presentation.objects.link(obj)
presentation.hide_viewport=True
for obj in children(rear):
    obj.hide_render=True
for obj in presentation.objects:
    if obj.name.startswith(('Track sleeper','Rail web','Rail running head')):
        obj.hide_render=True
for i,(kind,root) in enumerate(parts.items()):
    root.location=((i-1)*6,0,0);root.rotation_euler=(0,0,0)
set_camera((0,0,2),34)
bpy.ops.object.select_all(action='DESELECT')
parts['head'].select_set(True);bpy.context.view_layer.objects.active=parts['head']
for area in bpy.context.screen.areas:
    if area.type=='VIEW_3D':
        area.spaces.active.region_3d.view_distance=34
        area.spaces.active.region_3d.view_location=(0,0,2)
        area.spaces.active.region_3d.view_rotation=camera.rotation_euler.to_quaternion()
        area.spaces.active.shading.color_type='MATERIAL'
        area.spaces.active.clip_end=1000
bpy.ops.wm.save_as_mainfile(filepath=str(ART/'ed4m.blend'))
print('ED4M_BUILD_COMPLETE',flush=True)
