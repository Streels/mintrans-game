"""Procedural paint grain, lower-body dust, subtle wear and local crevice shading.
Executed in the build script namespace; these colours are baked into the GLB.
"""
for key in ['green','green_light','green_dark','cream','roof','steel','copper','dust']:
    mat=M[key];nodes=mat.node_tree.nodes;links=mat.node_tree.links
    bsdf=nodes.get('Principled BSDF');base=bsdf.inputs['Base Color']
    old=base.links[0].from_socket if base.is_linked else None
    geo=nodes.new('ShaderNodeNewGeometry')
    for noise in [n for n in nodes if n.type=='TEX_NOISE']:
        links.new(geo.outputs['Position'],noise.inputs['Vector'])
        noise.inputs['Scale'].default_value=14
    for ramp in [n for n in nodes if n.type=='VALTORGB']:
        color=mat.diffuse_color[:3]
        ramp.color_ramp.elements[0].color=(*[c*.91 for c in color],1)
        ramp.color_ramp.elements[1].color=(*[c*1.07 for c in color],1)
    if old:
        macro=nodes.new('ShaderNodeTexNoise');macro.inputs['Scale'].default_value=.65;macro.inputs['Detail'].default_value=3
        links.new(geo.outputs['Position'],macro.inputs['Vector'])
        ramp=nodes.new('ShaderNodeValToRGB')
        ramp.color_ramp.elements[0].color=(.58,.60,.59,1)
        ramp.color_ramp.elements[1].color=(1.12,1.10,1.07,1)
        links.new(macro.outputs['Fac'],ramp.inputs['Fac'])
        mix=nodes.new('ShaderNodeMixRGB');mix.blend_type='MULTIPLY';mix.inputs[0].default_value=.8
        links.new(old,mix.inputs[1]);links.new(ramp.outputs['Color'],mix.inputs[2])
        current=mix.outputs['Color']
        if key in ['green','green_light','cream']:
            sep=nodes.new('ShaderNodeSeparateXYZ');links.new(geo.outputs['Position'],sep.inputs[0])
            low=nodes.new('ShaderNodeMapRange');low.clamp=True
            low.inputs['From Min'].default_value=1.05;low.inputs['From Max'].default_value=1.95
            low.inputs['To Min'].default_value=.19;low.inputs['To Max'].default_value=0
            links.new(sep.outputs['Z'],low.inputs['Value'])
            dusty=nodes.new('ShaderNodeMixRGB');dusty.inputs[2].default_value=(.14,.12,.079,1)
            links.new(low.outputs[0],dusty.inputs[0]);links.new(current,dusty.inputs[1]);current=dusty.outputs[0]
        ao=nodes.new('ShaderNodeAmbientOcclusion');ao.inputs['Distance'].default_value=.22;ao.samples=4
        ao.inputs['Color'].default_value=(1,1,1,1)
        contact=nodes.new('ShaderNodeMixRGB');contact.blend_type='MULTIPLY';contact.inputs[0].default_value=.28
        links.new(current,contact.inputs[1]);links.new(ao.outputs['Color'],contact.inputs[2]);links.new(contact.outputs[0],base)
    grain=nodes.new('ShaderNodeTexNoise');grain.inputs['Scale'].default_value=145;grain.inputs['Detail'].default_value=2
    links.new(geo.outputs['Position'],grain.inputs['Vector'])
    bump=nodes.new('ShaderNodeBump');bump.inputs['Strength'].default_value=.12;bump.inputs['Distance'].default_value=.005
    links.new(grain.outputs['Fac'],bump.inputs['Height']);links.new(bump.outputs['Normal'],bsdf.inputs['Normal'])
    rough=nodes.new('ShaderNodeMapRange');rough.inputs['From Min'].default_value=0;rough.inputs['From Max'].default_value=1
    rough.inputs['To Min'].default_value=max(.25,bsdf.inputs['Roughness'].default_value-.11)
    rough.inputs['To Max'].default_value=min(.96,bsdf.inputs['Roughness'].default_value+.09)
    links.new(grain.outputs['Fac'],rough.inputs['Value']);links.new(rough.outputs[0],bsdf.inputs['Roughness'])
