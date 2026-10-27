"""Curved, inclined early ED4M front. Executed in the build script namespace.

The profile is an art reconstruction from side and three-quarter photographs,
not a factory dimensioned drawing. The shoulder is ahead of the windscreen roof.
"""
from mathutils.geometry import tessellate_polygon

CAB_PROFILE=[(.55,-10.68),(1.12,-10.84),(1.65,-11.08),(2.15,-11.22),(2.38,-11.18),(2.60,-11.06),(3.2,-10.72),(3.68,-10.44),(3.88,-10.27),(4.04,-10.04),(4.14,-9.72)]
CAB_WIDTH=[(1.12,1.52),(1.32,1.70),(1.55,1.75),(3.43,1.75),(3.63,1.70),(3.82,1.54),(3.99,1.20),(4.1,.65),(4.14,.05)]

def interp_profile(points,z):
    if z<=points[0][0]: return points[0][1]
    if z>=points[-1][0]: return points[-1][1]
    for (za,va),(zb,vb) in zip(points,points[1:]):
        if za<=z<=zb:
            t=(z-za)/(zb-za)
            i=points.index((za,va));delta=(vb-va)/(zb-za)
            before=(va-points[i-1][1])/(za-points[i-1][0]) if i else delta
            after=(points[i+2][1]-vb)/(points[i+2][0]-zb) if i+2<len(points) else delta
            ma=0 if before*delta<=0 else 2*before*delta/(before+delta)
            mb=0 if after*delta<=0 else 2*after*delta/(after+delta)
            span=zb-za
            return (2*t**3-3*t*t+1)*va+(t**3-2*t*t+t)*span*ma+(-2*t**3+3*t*t)*vb+(t**3-t*t)*span*mb

def front_point(x,z,offset=0):
    ax=abs(x)
    # Broad shallow bow, then the rounded return to the side of the cab.
    corner=.085*(ax/1.45)**2 if ax<=1.45 else .085+.51*((ax-1.45)/.30)**2
    return (x,interp_profile(CAB_PROFILE,z)+corner-offset,z)

def front_normal(x,z):
    step=.0005
    dx=Vector(front_point(x+step,z))-Vector(front_point(x-step,z))
    dz=Vector(front_point(x,z+step))-Vector(front_point(x,z-step))
    return tuple(dx.cross(dz).normalized())

def front_poly(name,points,mat,offset=.025,subdivisions=7):
    source=[Vector((x,z,0)) for x,z in points]
    tris=tessellate_polygon([source])
    vertices=[];faces=[]
    n=subdivisions
    for triangle in tris:
        a,b,c=[source[v] if isinstance(v,int) else v for v in triangle]
        ids={}
        for row in range(n+1):
            for col in range(n+1-row):
                p=a+(b-a)*(row/n)+(c-a)*(col/n)
                ids[row,col]=len(vertices)
                vertices.append(front_point(p.x,p.y,offset))
        for row in range(n):
            for col in range(n-row):
                faces.append((ids[row,col],ids[row+1,col],ids[row,col+1]))
                if row+col<n-1:
                    faces.append((ids[row+1,col],ids[row+1,col+1],ids[row,col+1]))
    # Weld the tessellated paint/glass patches before smooth shading.
    remap={};unique=[];indices=[]
    for vertex in vertices:
        key=tuple(round(v,7) for v in vertex)
        if key not in remap:remap[key]=len(unique);unique.append(vertex)
        indices.append(remap[key])
    mapped=[]
    for face in faces:
        face=tuple(indices[i] for i in face)
        a,b,c=[Vector(unique[i]) for i in face[:3]]
        normal=Vector(front_normal(a.x,a.z))
        if (b-a).cross(c-a).dot(normal)<0:face=tuple(reversed(face))
        mapped.append(face)
    obj=mesh(name,unique,mapped,mat)
    for face in obj.data.polygons:face.use_smooth=True
    obj.data.normals_split_custom_set_from_vertices([front_normal(v[0],v[2]) for v in unique])
    return obj

def front_text(name,body,x,z,size,mat):
    font=bpy.data.curves.new(name,'FONT');font.body=body;font.size=size;font.align_x='CENTER';font.extrude=.0005
    temp=bpy.data.objects.new(name+' / temporary font',font);bpy.context.collection.objects.link(temp)
    bpy.context.view_layer.update()
    evaluated=temp.evaluated_get(bpy.context.evaluated_depsgraph_get());geo=evaluated.to_mesh()
    vertices=[front_point(x+v.co.x,z+v.co.y,.028+v.co.z) for v in geo.vertices]
    faces=[tuple(p.vertices) for p in geo.polygons]
    obj=mesh(name,vertices,faces,mat)
    evaluated.to_mesh_clear();bpy.data.objects.remove(temp,do_unlink=True);bpy.data.curves.remove(font)
    return obj

def front_ring(name,points,border,mat,offset=.035):
    cx=sum(x for x,z in points)/len(points);cz=sum(z for x,z in points)/len(points)
    inner=[]
    for x,z in points:
        vec=Vector((x-cx,z-cz));vec*=max(0,(vec.length-border)/vec.length)
        inner.append((cx+vec.x,cz+vec.y))
    for i in range(len(points)):
        j=(i+1)%len(points)
        front_poly(name, [points[i],points[j],inner[j],inner[i]],mat,offset,4)

def rounded_outline(points,radius=.07,segments=5):
    result=[]
    for i,pair in enumerate(points):
        p=Vector(pair);a=Vector(points[i-1]);b=Vector(points[(i+1)%len(points)])
        incoming=p+(a-p).normalized()*min(radius,(a-p).length*.25)
        outgoing=p+(b-p).normalized()*min(radius,(b-p).length*.25)
        for j in range(segments+1):
            t=j/segments;q=(1-t)**2*incoming+2*(1-t)*t*p+t*t*outgoing
            result.append(tuple(q))
    return result

def side_panel(name,side,points,mat,depth):
    return poly(name,[(side*(1.75+depth),y,z) for y,z in points],mat)

def cab():
    # Closed loft: all front surfaces, including trim and glazing, follow its profile.
    zs=sorted(set([1.12+i*(4.14-1.12)/60 for i in range(61)]+[p[0] for p in CAB_PROFILE if p[0]>=1.12]+[p[0] for p in CAB_WIDTH]))
    steps=64;vertices=[]
    for z in zs:
        width=interp_profile(CAB_WIDTH,z)
        for i in range(steps+1):
            x=width*(i/steps*2-1)
            vertices.append(front_point(x,z))
    faces=[]
    for row in range(len(zs)-1):
        for col in range(steps):
            i=row*(steps+1)+col
            faces.append((i,i+1,i+1+steps+1,i+steps+1))
    front=mesh('ED4M shaped inclined front shell',vertices,faces,'green')
    for face in front.data.polygons:face.use_smooth=True
    front.data.normals_split_custom_set_from_vertices([front_normal(v[0],v[2]) for v in vertices])
    for side,index in [(-1,0),(1,steps)]:
        sideverts=[]
        for row,z in enumerate(zs):
            p=vertices[row*(steps+1)+index]
            sideverts.extend([p,(p[0],-9.35,z)])
        sidefaces=[(i,i+1,i+3,i+2) for i in range(0,len(sideverts)-2,2)]
        obj=mesh('Smooth cab side return',sideverts,sidefaces,'green')
        for face in obj.data.polygons:face.use_smooth=True

    outline=rounded_outline([(-1.37,1.63),(-1.60,1.80),(-1.61,3.46),(-1.47,3.73),(-1.14,3.90),(0,3.97),(1.14,3.90),(1.47,3.73),(1.61,3.46),(1.60,1.80),(1.37,1.63)],.10,4)
    front_ring('Continuous ivory front surround',outline,.135,'cream',.025)
    front_poly('Ivory lower face band',[(-1.50,1.64),(1.50,1.64),(1.56,1.97),(-1.56,1.97)],'cream',.0255)
    for side in [-1,1]:
        raw=[(.115,2.39),(1.37,2.39),(1.37,3.38),(1.23,3.57),(.43,3.69),(.115,3.44)]
        points=rounded_outline([(side*x,z) for x,z in raw],.065,5)
        front_poly('Recessed windscreen seal',points,'black',.04)
        front_ring('Windscreen aluminium rim',points,.037,'aluminium',.054)
        cx=sum(x for x,z in points)/len(points);cz=sum(z for x,z in points)/len(points)
        glass=[(cx+(x-cx)*.91,cz+(z-cz)*.91) for x,z in points]
        front_poly('Inclined dark windscreen',glass,'glass',.056)
        front_poly('Subtle reflected sky at glass top',[(side*.20,3.29),(side*1.27,3.29),(side*1.22,3.48),(side*.43,3.58)],'glass_light',.059)
        display=[(side*.25,3.34),(side*1.18,3.34),(side*1.16,3.46),(side*.40,3.56)]
        front_poly('Destination sign inset',display,'black',.064)
        for xx in [.39,.48,.58,.68,.78,.88,.98]:
            front_poly('Small route display characters',[(side*xx,3.38),(side*(xx+.038),3.38),(side*(xx+.038),3.42),(side*xx,3.42)],'cream',.067,2)
        wiper=[front_point(side*x,z,.10) for x,z in [(.35,2.40),(.58,2.80),(.70,3.10)]]
        path('Windshield wiper arm',wiper,.013,'steel')
        path('Wiper rubber blade',[front_point(side*.70,z,.115) for z in [2.94,3.14]],.017,'black')
        # Buffer lamps sit on the projecting shoulder of the nose.
        x=side*1.14;z=2.14;y=front_point(x,z,.10)[1]
        box('Rounded marker lamp casing',(x,y,z),(.48,.18,.26),'cream',.045)
        box('Marker black optical recess',(x,y-.098,z),(.40,.025,.19),'black',.025)
        for dx,r,mat in [(-.105,.078,'lamp'),(.105,.061,'red')]:
            cyl('Marker lamp reflector',(x+dx,y-.12,z),(x+dx,y-.135,z),r+.013,'aluminium',24)
            cyl('Marker lamp glass',(x+dx,y-.137,z),(x+dx,y-.15,z),r,mat,24)
        for xx in [-.17,-.085,0,.085,.17]:
            path('Marker cage vertical',[(x+xx,y-.18,z-.105),(x+xx,y-.21,z),(x+xx,y-.18,z+.105)],.007,'aluminium')
        for zz in [-.07,0,.07]:
            path('Marker cage horizontal',[(x-.205,y-.18,z+zz),(x,y-.21,z+zz),(x+.205,y-.18,z+zz)],.007,'aluminium')

        sideglass=[(-10.36,2.56),(-9.46,2.56),(-9.46,3.40),(-9.96,3.40)]
        side_panel('Cab side window black gasket',side,sideglass,'black',.026)
        side_panel('Cab side glazing',side,[(-10.28,2.61),(-9.51,2.61),(-9.51,3.35),(-9.94,3.35)],'glass',.041)
        for yy in [-9.78]:
            cyl('Cab side window mullion',(side*1.802,yy,2.59),(side*1.802,yy,3.37),.018,'aluminium',12)
        side_rect('Crew door seal',side,-9.03,2.20,.48,2.16,'green_dark',.022,.04)
        side_rect('Crew access door',side,-9.03,2.21,.42,2.09,'green',.044,.035)
        side_rect('Crew door louvre',side,-9.03,2.94,.30,.53,'black',.054,.02)
        for zz in [2.73,2.82,2.91,3.00,3.09,3.18]:
            side_rect('Crew door louvre blade',side,-9.03,zz,.30,.032,'steel',.067,.005)
        cyl('Crew door handle',(side*1.84,-8.9,2.03),(side*1.84,-8.9,2.22),.018,'aluminium',12)
        for zz,reach in [(1.16,.12),(.94,.25),(.72,.38)]:
            box('Crew door step',(side*(1.75+reach/2),-9.03,zz),(reach,.54,.07),'steel',.015)
        cyl('Mirror support',(side*1.73,-10.0,3.26),(side*2.04,-10.15,3.24),.019,'steel',12)
        box('Side rear-view mirror',(side*2.055,-10.16,3.25),(.12,.065,.25),'steel',.024)
        side_rect('Cab lower ivory keyline',side,-9.85,2.14,.78,.075,'cream',.045,.01)

    triangle=rounded_outline([(-.21,3.74),(0,3.48),(.21,3.74)],.04,5)
    front_poly('Triangular central headlamp recess',triangle,'black',.04)
    p=front_point(0,3.64,.10)
    cyl('Headlamp metal bezel',p,(p[0],p[1]-.045,p[2]),.105,'aluminium',32)
    cyl('Central headlight lens',(p[0],p[1]-.05,p[2]),(p[0],p[1]-.07,p[2]),.084,'lamp',32)
    # The tapered lower apron returns beneath the projecting shoulder.
    front_poly('Curved front pilot',[(-1.48,.58),(1.48,.58),(1.61,1.45),(-1.61,1.45)],'green',.045)
    for side in [-1,1]:
        for off in [0,.58]:
            pts=[(.10+off,.64),(.43+off,.64),(1.28+off,1.43),(.95+off,1.43)]
            front_poly('Orange pilot warning chevron',[(side*min(x,1.59),z) for x,z in pts],'orange',.055)
    coupler(-11.12,-1)
    for side in [-1,1]:
        path('Sagging front brake hose',[(side*.30,-11.36,1.12),(side*.48,-11.54,.83),(side*.40,-11.51,.62),(side*.19,-11.44,.66)],.034,'black')
        cyl('Brake hose end ferrule',(side*.19,-11.44,.66),(side*.16,-11.42,.74),.047,'steel',12)
    py=front_point(0,1.48,.12)[1]
    box('Central front equipment hatch',(0,py,1.48),(.72,.18,.28),'steel',.035)
    for side in [-1,1]:
        front_text('Series and running number','ЭД4М' if side<0 else '0138',side*.94,1.75,.165,'green_dark')
    # A small raised factory crest under the windscreens.
    crest=[(-.16,2.25),(-.04,2.30),(.14,2.28),(.04,2.20),(-.12,2.18)]
    path('Small factory emblem',[front_point(x,z,.075) for x,z in crest+[crest[0]]],.014,'aluminium')
    box('Cab roof air intake',(0,-8.8,4.31),(1.20,1.1,.25),'roof',.035)
    for x in [-.4,-.2,0,.2,.4]:box('Roof intake grille rib',(x,-8.8,4.445),(.055,.88,.025),'steel',.003)
    cyl('Cab radio aerial',(0,-9.8,4.13),(0,-9.8,4.56),.016,'steel',12)
