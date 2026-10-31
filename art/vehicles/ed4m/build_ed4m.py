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
 'green':material('01 / faded bottle green',(.075,.12,.07),.16,.7),
 'green_light':material('02 / raised body details',(.10,.15,.09),.12,.68),
 'green_dark':material('03 / door recesses',(.065,.102,.067),.12,.75,.015),
 'cream':material('04 / warm ivory trim',(.60,.57,.44),.1,.68,.025),
 'roof':material('05 / zinc grey roof',(.18,.20,.19),.35,.72,.025),
 'steel':material('06 / underframe steel',(.085,.097,.095),.48,.73,.015),
 'black':material('07 / rubber and gaskets',(.018,.023,.025),.02,.85,.005),
 'glass':material('08 / opaque blue smoke glass',(.065,.107,.127),.26,.27,.005),
 'glass_light':material('09 / glass reflection',(.145,.207,.222),.2,.33,.005),
 'orange':material('10 / vermilion warning paint',(.58,.065,.022),.08,.68,.02),
 'lamp':material('11 / headlight lens',(.78,.72,.49),.1,.25,.005),
 'red':material('12 / tail light lens',(.46,.018,.012),.2,.28,.005),
 'copper':material('13 / pantograph red steel',(.27,.071,.026),.45,.62,.015),
 'insulator':material('14 / glazed brown ceramic',(.18,.095,.052),.15,.35,.01),
 'aluminium':material('15 / window metal frames',(.42,.43,.38),.65,.43,.01),
 'dust':material('16 / brake dust',(.19,.17,.12),.15,.8,.015),
}

ACTIVE=None
def finish(obj, name, mat, bevel=0, parent=True):
    obj.name=name
    if mat: obj.data.materials.append(M[mat] if isinstance(mat,str) else mat)
    if parent and ACTIVE: obj.parent=ACTIVE
    if bevel:
        mod=obj.modifiers.new('Small manufactured edge','BEVEL')
        mod.width=bevel;mod.segments=2
        mod=obj.modifiers.new('Weighted corner normals','WEIGHTED_NORMAL')
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

def roof():
    # A faceted elliptic crown produces readable highlights without a bevelled toy roof.
    profile=[(-1.77,3.43),(-1.72,3.64),(-1.57,3.83),(-1.27,4.00),(-.80,4.10),(0,4.14),(.80,4.10),(1.27,4),(1.57,3.83),(1.72,3.64),(1.77,3.43)]
    verts=[(x,y,z) for y in [-10.51,10.51] for x,z in profile]
    n=len(profile)
    faces=[(i,i+1,i+1+n,i+n) for i in range(n-1)]+[tuple(range(n-1,-1,-1)),tuple(range(n,2*n)),(0,n,2*n-1,n-1)]
    obj=mesh('Continuous elliptical steel roof',verts,faces,'roof')
    mod=obj.modifiers.new('Roof edge','BEVEL');mod.width=.025;mod.segments=2
    for y in [-9,-6,-3,0,3,6,9]:
        path('Roof sheet seam',[(x,y,z+.012) for x,z in profile[1:-1]],.012,'roof')
    for y in [-8.5,-5.2,-1.9,1.4,4.7,8.0]:
        box('Low rectangular ventilator',(0,y,4.19),(.75,.60,.13),'roof',.025)
        for x in [-.24,0,.24]: box('Vent ribs',(x,y,4.266),(.055,.48,.025),'steel',0)

def bogie(y):
    box('Bogie bolster',(0,y,.72),(2.47,2.8,.40),'steel',.08)
    for side in [-1,1]:
        box('Bogie side frame',(side*1.13,y,.62),(.20,3.55,.25),'steel',.05)
        for ax in [-1.15,1.15]:
            yy=y+ax
            cyl('Wheel tread',(side*.91,yy,.50),(side*1.27,yy,.50),.49,'steel',16)
            cyl('Wheel face',(side*1.275,yy,.50),(side*1.30,yy,.50),.34,'dust',16)
            box('Axlebox',(side*1.34,yy,.52),(.19,.38,.28),'steel',.035)
        for yy in [y-.7,y+.7]:
            for zz in [.64,.72,.80,.88]:
                cyl('Suspension coil',(side*1.14,yy,zz),(side*1.14,yy,zz+.035),.15,'dust',8)
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

def cab():
    y=-10.67
    box('Rounded ED4M front',(0,y,2.48),(3.45,.31,2.85),'green',.23)
    # Signature pale continuous border around the two front windows.
    outline=[(-1.39,1.64),(-1.60,1.85),(-1.60,3.48),(-1.47,3.75),(-1.15,3.90),(1.15,3.90),(1.47,3.75),(1.60,3.48),(1.60,1.85),(1.39,1.64)]
    ring_front('Cream wraparound cab border',outline,.14,-10.843,'cream')
    poly('Cream lower front band',[(-1.48,-10.85,1.61),(1.48,-10.85,1.61),(1.48,-10.85,1.97),(-1.48,-10.85,1.97)],'cream')
    for side in [-1,1]:
        # Two distinct windscreens with raised/slanted inner corners, early ED4M.
        points=[(.12,2.45),(1.36,2.45),(1.36,3.36),(1.23,3.58),(.43,3.67),(.12,3.43)]
        points=[(x*side,z) for x,z in points]
        poly('Windscreen dark gasket',[(x,-10.87,z) for x,z in points],'black')
        ring_front('Windscreen metal rim',points,.045,-10.885,'aluminium')
        cx=sum(x for x,z in points)/len(points);cz=sum(z for x,z in points)/len(points)
        glass=[(cx+(x-cx)*.91,cz+(z-cz)*.91) for x,z in points]
        poly('Cab windscreen',[(x,-10.889,z) for x,z in glass],'glass')
        poly('Windshield subtle reflection',[(side*.20,-10.894,3.30),(side*1.28,-10.894,3.30),(side*1.25,-10.894,3.47),(side*.43,-10.894,3.57)],'glass_light')
        box('Destination display',(side*.74,-10.902,3.39),(.87,.015,.12),'black',.015)
        path('Windscreen wiper',[(side*.39,-10.93,2.49),(side*.63,-10.93,2.85),(side*.69,-10.93,3.03)],.018,'black')
        box('Low marker light housing',(side*1.15,-10.93,2.15),(.47,.16,.25),'cream',.055)
        for dx,mat in [(-.105,'lamp'),(.105,'red')]:
            cyl('White or red marker lens',(side*1.15+dx,-11.025,2.15),(side*1.15+dx,-11.055,2.15),.070,mat,12)
        for x in [side*1.0,side*1.17,side*1.30]:
            path('Marker light guard',[(x,-11.07,2.04),(x,-11.09,2.27)],.011,'steel')
        box('Cab side window gasket',(side*1.783,-9.90,2.99),(.045,.77,.88),'black',.065)
        box('Cab side glass',(side*1.812,-9.90,2.99),(.018,.65,.75),'glass',.04)
        side_rect('Cab side cream accent',side,-9.88,2.17,.81,.12,'cream',.06,.01)
        cyl('Side mirror stem',(side*1.74,-10.3,3.30),(side*2.02,-10.42,3.29),.02,'steel')
        box('Side mirror',(side*2.04,-10.43,3.28),(.10,.08,.22),'steel',.025)
    tri=[(-.19,3.70),(0,3.48),(.19,3.70)]
    poly('Upper triangular headlight recess',[(x,-10.9,z) for x,z in tri],'black')
    cyl('Top centre headlamp',(0,-10.91,3.635),(0,-10.95,3.635),.086,'lamp',12)
    # Front apron with recognisable red/orange V warning chevrons.
    poly('Front pilot',[(-1.48,-10.87,.57),(1.48,-10.87,.57),(1.65,-10.87,1.45),(-1.65,-10.87,1.45)],'green')
    for side in [-1,1]:
        for off in [0,.58]:
            pts=[(.10+off,.64),(.43+off,.64),(1.28+off,1.43),(.95+off,1.43)]
            pts=[(side*min(x,1.60),-10.89,z) for x,z in pts]
            poly('Vermilion warning chevron',pts,'orange')
    coupler(-10.88,-1)
    for side in [-1,1]:
        path('Brake hose',[(side*.30,-11.04,1.11),(side*.48,-11.17,.85),(side*.40,-11.10,.62),(side*.19,-11.10,.66)],.035,'black')
    box('Front central equipment hatch',(0,-10.91,1.48),(.70,.13,.28),'steel',.02)
    text_obj('Front series marking','ЭД4М',(-.94,-10.91,1.74),.16,'green_dark')
    text_obj('Fleet number','0138',(.96,-10.91,1.74),.16,'green_dark')
    # Low roof equipment, no pantograph on a driving trailer.
    box('Cab roof ventilation pack',(0,-8.8,4.31),(1.2,1.1,.25),'roof',.035)
    for x in [-.4,-.2,0,.2,.4]: box('Cab roof intake ribs',(x,-8.8,4.445),(.065,.87,.025),'steel',0)
    cyl('Cab roof radio aerial',(0,-10,4.06),(0,-10,4.53),.018,'steel',8)

def build(kind):
    global ACTIVE
    ACTIVE=bpy.data.objects.new('ED4M_'+kind,None)
    ACTIVE['vehicle']='ED4M';ACTIVE['module']=kind
    ACTIVE['forward']='-Y';ACTIVE['units']='metres';ACTIVE['art_length_m']=21.5
    bpy.context.collection.objects.link(ACTIVE)
    box('Underframe sill',(0,0,1.08),(3.36,21.1,.27),'steel',.065)
    box('Ribbed steel body',(0,0,2.42),(3.50,21.0,2.55),'green',.10)
    roof()
    for side in [-1,1]:
        side_rect('Cream window belt',side,0,2.93,20.8,1.13,'cream',.012,.025)
        side_rect('Roof side drip rail',side,0,3.51,20.9,.065,'green_dark',.045,.01)
        side_rect('Lower cream keyline',side,0,2.29,20.8,.055,'cream',.028,0)
        # Driving trailer has a short cab zone and the near passenger vestibule set back.
        doors=[-8.20,8.78] if kind=='head' else [-8.78,8.78]
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

# Solid PBR values are exported to glTF; subtle procedural paint noise is render-only.
def export_glbs():
    for k,root in parts.items():
        bpy.context.view_layer.update()
        depsgraph=bpy.context.evaluated_depsgraph_get()
        groups={}
        for obj in children(root):
            if obj.type not in {'MESH','FONT'}: continue
            use_modifiers=obj.type=='FONT' or obj.name.startswith(('Ribbed steel body','Rounded ED4M front','Continuous elliptical steel roof'))
            evaluated=obj.evaluated_get(depsgraph) if use_modifiers else None
            geo=evaluated.to_mesh() if evaluated else obj.data
            if geo is None: continue
            mat=obj.data.materials[0]
            verts,faces=groups.setdefault(mat.name,([],[]))
            offset=len(verts)
            verts.extend(tuple(obj.matrix_world @ v.co) for v in geo.vertices)
            faces.extend(tuple(offset+i for i in face.vertices) for face in geo.polygons)
            if evaluated: evaluated.to_mesh_clear()
        combined=[]
        export_materials=[]
        for mat_name,(verts,faces) in groups.items():
            source_mat=bpy.data.materials[mat_name]
            game_mat=source_mat.copy();game_mat.name=mat_name+' / game PBR'
            base=game_mat.node_tree.nodes.get('Principled BSDF').inputs['Base Color']
            for link in list(base.links): game_mat.node_tree.links.remove(link)
            base.default_value=source_mat.diffuse_color
            export_materials.append(game_mat)
            data=bpy.data.meshes.new(k+' / '+mat_name)
            data.from_pydata(verts,[],faces);data.materials.append(game_mat);data.update()
            obj=bpy.data.objects.new(k+' / '+mat_name,data)
            bpy.context.collection.objects.link(obj)
            combined.append(obj)
        bpy.ops.object.select_all(action='DESELECT')
        for obj in combined: obj.select_set(True)
        bpy.context.view_layer.objects.active=combined[0]
        bpy.ops.export_scene.gltf(filepath=str(OUT/'models'/f'ed4m_{k}.glb'),export_format='GLB',use_selection=True,export_apply=True,export_materials='EXPORT',export_cameras=False,export_lights=False)
        for obj in combined: bpy.data.objects.remove(obj,do_unlink=True)
        for mat in export_materials: bpy.data.materials.remove(mat)

export_glbs()

scene=bpy.context.scene
scene.render.engine='CYCLES'
scene.cycles.samples=32
scene.cycles.use_denoising=True
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

set_camera((0,0,2.1),27)
scene.render.resolution_x=512;scene.render.resolution_y=512
bpy.context.view_layer.update()
p=world_to_camera_view(scene,camera,Vector((0,0,0)))
pivot=[round(p.x*512,3),round((1-p.y)*512,3)]
directions=[('s',0,1),('sw',-1,1),('w',-1,0),('nw',-1,-1),('n',0,-1),('ne',1,-1),('e',1,0),('se',1,1)]
manifest={
 'id':'ed4m','version':1,'source':'art/vehicles/ed4m/ed4m.blend',
 'projection':{'type':'orthographic','tile_ratio':[2,1],'elevation_degrees':30},
 'frame_size':[512,512],'pivot_px':pivot,'alpha':'straight','pixels_per_metre':512/27,
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
        if '--model-only' not in sys.argv:
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
if '--model-only' not in sys.argv:
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
stats={k:{'objects':len(children(root)),'mesh_triangles':sum(len(o.data.polygons)*2 for o in children(root) if o.type=='MESH')} for k,root in parts.items()}
(ART/'build-stats.json').write_text(json.dumps(stats,indent=2),encoding='utf-8')
print('ED4M_BUILD_COMPLETE',json.dumps(stats),flush=True)
