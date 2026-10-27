"""Full detail GLB export with preserved split normals and baked 2K PBR maps.
Executed in build_ed4m.py's namespace. No geometry simplification is performed.
"""
def export_glbs():
    scene=bpy.context.scene
    scene.render.engine='CYCLES';scene.cycles.samples=16
    scene.render.bake.use_pass_direct=False;scene.render.bake.use_pass_indirect=False
    scene.render.bake.use_pass_color=True;scene.render.bake.margin=4
    texture_dir=OUT/'textures';texture_dir.mkdir(exist_ok=True)
    for kind,root in parts.items():
        if '--head-update' in sys.argv and kind!='head':continue
        bpy.context.view_layer.update()
        depsgraph=bpy.context.evaluated_depsgraph_get()
        verts=[];faces=[];normals=[];face_mats=[];sources=[]
        for obj in children(root):
            if obj.type not in {'MESH','CURVE','FONT'}:continue
            evaluated=obj.evaluated_get(depsgraph)
            geo=evaluated.to_mesh(preserve_all_data_layers=True,depsgraph=depsgraph)
            if not geo:continue
            source=obj.data.materials[0]
            if source not in sources:sources.append(source)
            mi=sources.index(source);offset=len(verts)
            matrix=obj.matrix_world;normal_matrix=matrix.to_3x3().inverted().transposed()
            verts.extend(tuple(matrix@v.co) for v in geo.vertices)
            for face in geo.polygons:
                faces.append(tuple(offset+v for v in face.vertices));face_mats.append(mi)
                for loop in face.loop_indices:
                    normals.append(tuple((normal_matrix@geo.corner_normals[loop].vector).normalized()))
            evaluated.to_mesh_clear()
        data=bpy.data.meshes.new('ED4M '+kind+' / full detail')
        data.from_pydata(verts,[],faces);data.update()
        for poly,mi in zip(data.polygons,face_mats):poly.material_index=mi;poly.use_smooth=True
        data.normals_split_custom_set(normals)
        obj=bpy.data.objects.new('ED4M_'+kind,data);scene.collection.objects.link(obj)
        mats=[]
        for source in sources:
            mat=source.copy();mat.name=source.name+' / textured game PBR'
            mats.append(mat);data.materials.append(mat)
        bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
        bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.mesh.remove_doubles(threshold=.00001)
        bpy.ops.mesh.quads_convert_to_tris(quad_method='BEAUTY',ngon_method='BEAUTY')
        bpy.ops.uv.smart_project(angle_limit=math.radians(66),island_margin=.002,area_weight=.15,correct_aspect=True,scale_to_bounds=True)
        bpy.ops.object.mode_set(mode='OBJECT')
        saved_visibility={o:o.hide_render for r in parts.values() for o in children(r)}
        for original in saved_visibility:original.hide_render=True
        images=[]
        for map_type in ['color','normal']:
            image=bpy.data.images.new(f'ed4m_{kind}_{map_type}',width=2048,height=2048,alpha=False)
            image.colorspace_settings.name='sRGB' if map_type=='color' else 'Non-Color'
            images.append(image)
            for mat in mats:
                node=mat.node_tree.nodes.new('ShaderNodeTexImage');node.image=image;node.name='Bake target '+map_type
                mat.node_tree.nodes.active=node
            if map_type=='color':
                bpy.ops.object.bake(type='DIFFUSE',pass_filter={'COLOR'},use_clear=True,margin=4)
            else:
                bpy.ops.object.bake(type='NORMAL',normal_space='TANGENT',use_clear=True,margin=4)
            image.filepath_raw=str(texture_dir/f'ed4m_{kind}_{map_type}.png');image.file_format='PNG';image.save()
        for mat in mats:
            nodes=mat.node_tree.nodes;links=mat.node_tree.links;bsdf=nodes.get('Principled BSDF')
            for name in ['Base Color','Normal','Roughness']:
                for link in list(bsdf.inputs[name].links):links.remove(link)
            color=nodes.get('Bake target color');color.interpolation='Linear'
            links.new(color.outputs['Color'],bsdf.inputs['Base Color'])
            bsdf.inputs['Base Color'].default_value=(1,1,1,1)
            normal=nodes.get('Bake target normal');normal.interpolation='Linear'
            normal_map=nodes.new('ShaderNodeNormalMap');links.new(normal.outputs['Color'],normal_map.inputs['Color'])
            links.new(normal_map.outputs['Normal'],bsdf.inputs['Normal'])
        bpy.ops.export_scene.gltf(filepath=str(OUT/'models'/f'ed4m_{kind}.glb'),export_format='GLB',use_selection=True,export_apply=True,export_normals=True,export_tangents=True,export_materials='EXPORT',export_cameras=False,export_lights=False)
        for original,hidden in saved_visibility.items():original.hide_render=hidden
        bpy.data.objects.remove(obj,do_unlink=True)
        for mat in mats:bpy.data.materials.remove(mat)
        for image in images:bpy.data.images.remove(image)
        print('ED4M_FULL_DETAIL_EXPORT',kind,flush=True)
