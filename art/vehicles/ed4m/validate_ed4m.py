"""Validate exported ED4M art assets. Requires Pillow, no Blender required."""
from pathlib import Path
import json, math, struct
from PIL import Image

ROOT=Path(__file__).resolve().parents[3]
ASSETS=ROOT/'game/assets/vehicles/ed4m'
meta=json.loads((ASSETS/'ed4m.json').read_text(encoding='utf-8'))
assert meta['frame_size']==[512,512]
assert len(meta['directions'])==8
assert len({tuple(d['grid_delta']) for d in meta['directions']})==8
assert all(0<=v<512 for v in meta['pivot_px'])
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
            acc=gltf['accessors'][primitive['indices']]
            assert acc['count']%3==0
            count+=acc['count']//3
            pos=gltf['accessors'][primitive['attributes']['POSITION']]
            assert all(math.isfinite(v) for v in pos['min']+pos['max'])
    assert 4000<count<15000,(kind,count)
    colours=[m['pbrMetallicRoughness'].get('baseColorFactor',[1,1,1,1]) for m in gltf['materials']]
    assert any(c[1]>c[0]*1.3 and c[1]>c[2]*1.3 for c in colours),'Missing green paint'
    assert len(part['frames'])==8
    assert {f['direction'] for f in part['frames']}=={d['name'] for d in meta['directions']}
    for frame in part['frames']:
        image=Image.open(ASSETS/frame['file'])
        assert image.mode=='RGBA' and image.size==(512,512),frame
        alpha=image.getchannel('A')
        assert alpha.getextrema()==(0,255),frame
        bounds=alpha.getbbox()
        assert bounds and min(bounds[:2])>=4 and max(bounds[2:])<=508,(frame,bounds)
        assert sum(alpha.histogram()[1:])>500,frame
    summary[kind]={'triangles':count,'mesh_groups':len(gltf['meshes']),'glb_bytes':len(data),'sprite_frames':8}
print(json.dumps({'status':'passed','modules':summary,'sprite_frames':24},indent=2))
