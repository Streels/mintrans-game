"""Validate exported ED4M art assets. Requires Pillow, no Blender required."""
from pathlib import Path
import json, math, struct, sys
from PIL import Image

ROOT=Path(__file__).resolve().parents[3]
ASSETS=ROOT/'game/assets/vehicles/ed4m'
meta=json.loads((ASSETS/'ed4m.json').read_text(encoding='utf-8'))
models_only='--models-only' in sys.argv
if not models_only:assert meta['version']==2 and meta['frame_size']==[1024,1024]
assert len(meta['directions'])==8
assert len({tuple(d['grid_delta']) for d in meta['directions']})==8
assert all(0<=v<1024 for v in meta['pivot_px'])
assert set(meta['parts'])=={'head','motor','trailer'}
summary={}
for kind,part in meta['parts'].items():
    path=ASSETS/part['model']
    data=path.read_bytes()
    magic,version,total=struct.unpack_from('<III',data)
    assert magic==0x46546c67 and version==2 and total==len(data),path
    length,chunk=struct.unpack_from('<II',data,12)
    assert chunk==0x4e4f534a
    gltf=json.loads(data[20:20+length])
    assert len(gltf['buffers'])==1 and 'uri' not in gltf['buffers'][0]
    assert not gltf.get('cameras') and not gltf.get('skins')
    count=0
    for mesh in gltf['meshes']:
        for primitive in mesh['primitives']:
            assert primitive.get('mode',4)==4
            assert 'NORMAL' in primitive['attributes'] and 'TANGENT' in primitive['attributes'],path
            acc=gltf['accessors'][primitive['indices']]
            assert acc['count']%3==0
            count+=acc['count']//3
            pos=gltf['accessors'][primitive['attributes']['POSITION']]
            assert all(math.isfinite(v) for v in pos['min']+pos['max'])
    assert 30000<count<300000,(kind,count)
    assert len(gltf['images'])>=2,'Missing embedded colour/normal textures'
    assert all('bufferView' in image for image in gltf['images']),'External image dependency in GLB'
    for mat in gltf['materials']:
        assert 'baseColorTexture' in mat['pbrMetallicRoughness'],'Untextured exported material'
        assert 'normalTexture' in mat,'Missing normal map'
    for map_type in ['color','normal']:
        tex=Image.open(ASSETS/'textures'/f'ed4m_{kind}_{map_type}.png')
        assert tex.size==(2048,2048),(kind,map_type,tex.size)
    if kind=='head':
        # Check the exported geometry itself: the upper forehead must recede
        # behind the shoulder of the nose. This caught the planar first version.
        shoulder=[];forehead=[]
        for gltf_mesh in gltf['meshes']:
            for primitive in gltf_mesh['primitives']:
                name=gltf['materials'][primitive['material']]['name']
                if 'faded bottle green' not in name:continue
                acc=gltf['accessors'][primitive['attributes']['POSITION']]
                view=gltf['bufferViews'][acc['bufferView']]
                start=28+length+view.get('byteOffset',0)+acc.get('byteOffset',0)
                stride=view.get('byteStride',12)
                for i in range(acc['count']):
                    x,height,forward=struct.unpack_from('<fff',data,start+i*stride)
                    if abs(x)>.13 or forward<9.5:continue
                    if abs(height-2.15)<.08:shoulder.append(forward)
                    if abs(height-3.60)<.08:forehead.append(forward)
        assert shoulder and forehead and max(shoulder)-max(forehead)>.5,'Cab front is planar or has lost its backward rake'
    assert len(part['frames'])==8
    assert {f['direction'] for f in part['frames']}=={d['name'] for d in meta['directions']}
    for frame in ([] if models_only else part['frames']):
        image=Image.open(ASSETS/frame['file'])
        assert image.mode=='RGBA' and image.size==(1024,1024),frame
        alpha=image.getchannel('A')
        assert alpha.getextrema()==(0,255),frame
        bounds=alpha.getbbox()
        assert bounds and min(bounds[:2])>=8 and max(bounds[2:])<=1016,(frame,bounds)
        assert sum(alpha.histogram()[1:])>500,frame
    summary[kind]={'triangles':count,'material_groups':sum(len(m['primitives']) for m in gltf['meshes']),'glb_bytes':len(data),'sprite_frames':8}
print(json.dumps({'status':'passed','modules':summary,'sprite_frames':24},indent=2))
