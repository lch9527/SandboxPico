"""Car assembly part art kit (concept: output/pdf/Car-Parts-and-VFX-Art-Concept.pdf).

Run headless:
  blender --background --factory-startup --python BuildCarPartsArt.py

Writes, relative to this file's parent (Assets/MetaHackthone/Art/CarParts):
  Models/<Piece>.fbx          one FBX per visual piece, objects <Piece>_LOD0..2
  Textures/CarArt_WoodGrain.png
  Source~/CarParts.blend      editable source, laid out for browsing
  Source~/CarPartsManifest.json  triangle counts and bounds per LOD

All geometry is written in Unity part space (X right, Y up, Z forward, metres at
GeometryScale 1) and converted on the way into Blender, so numbers here can be
compared directly with DesignerSandboxParts.cs and SandboxDrill.cs.
The speed bump modules are authored for a roller of Diameter 1 lying along X.
"""
import bpy, bmesh, json, math, sys
from pathlib import Path
from mathutils import Vector, Matrix
import numpy as np

HERE = Path(__file__).resolve().parent
KIT = HERE.parent
MODELS = KIT / 'Models'
TEXTURES = KIT / 'Textures'
MODELS.mkdir(parents=True, exist_ok=True)
TEXTURES.mkdir(parents=True, exist_ok=True)

U = 2.6184137 * .2 / 6          # BuildingGeometry.Unit
DRILL_BASE = .3 * U * 2.79507 - .12865   # SandboxDrill.BaseCenter z
DRILL_TIP = U * 2.79507 - .12865         # SandboxDrill.Tip z
WHEEL_WIDTH = .23 / .38 * U
SMALL_RADIUS = .30 / .38 * U
LARGE_RADIUS = .44 / .38 * U

PALETTE = {
    'CarArt_Wood': (0.839, 0.604, 0.302),
    'CarArt_WoodEdge': (0.561, 0.345, 0.133),
    'CarArt_Enamel': (0.161, 0.420, 0.471),
    'CarArt_Rubber': (0.204, 0.239, 0.267),
    'CarArt_Brass': (0.722, 0.541, 0.314),
    'CarArt_Energy': (0.380, 0.812, 0.780),
    'CarArt_Cream': (0.929, 0.890, 0.800),
    'CarArt_Steel': (0.682, 0.714, 0.737),
    'CarArt_BumpPaint': (0.949, 0.714, 0.196),
    'CarArt_BumpBase': (0.204, 0.239, 0.267),
    'CarArt_Sandstone': (0.847, 0.682, 0.416),
    'CarArt_SandstoneEdge': (0.631, 0.482, 0.275),
}


def to_blender(p):
    """Unity (x, y, z) -> Blender (x, y, z). The FBX exporter and Unity's importer undo this."""
    return Vector((-p[0], -p[2], p[1]))


def to_unity(p):
    return Vector((-p[0], p[2], -p[1]))


# ---------------------------------------------------------------- scene helpers
def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.unit_settings.system = 'METRIC'
    scene.unit_settings.scale_length = 1
    return scene


def material(name):
    mat = bpy.data.materials.get(name)
    if mat:
        return mat
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get('Principled BSDF')
    r, g, b = PALETTE[name]
    lin = lambda c: c / 12.92 if c <= .04045 else ((c + .055) / 1.055) ** 2.4
    bsdf.inputs['Base Color'].default_value = (lin(r), lin(g), lin(b), 1)
    bsdf.inputs['Roughness'].default_value = {'CarArt_Brass': .35, 'CarArt_Steel': .35, 'CarArt_Enamel': .4}.get(name, .7)
    bsdf.inputs['Metallic'].default_value = 1 if name in ('CarArt_Brass', 'CarArt_Steel') else 0
    if name == 'CarArt_Energy':
        bsdf.inputs['Emission Color'].default_value = (lin(r), lin(g), lin(b), 1)
        bsdf.inputs['Emission Strength'].default_value = .45
    mat.diffuse_color = (r, g, b, 1)
    return mat


def new_object(name, verts, faces, mats, face_mats=None):
    """verts in Unity space; faces as index lists; face_mats one material index per face."""
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata([to_blender(v) for v in verts], [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    for m in mats:
        mesh.materials.append(material(m))
    if face_mats:
        for poly, index in zip(mesh.polygons, face_mats):
            poly.material_index = index
    clean(obj)
    return obj


def activate(obj):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def clean(obj):
    """Weld duplicates and make every closed shell face outwards."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()


def join(name, objects):
    activate(objects[0])
    for o in objects:
        o.select_set(True)
    bpy.ops.object.join()
    result = bpy.context.view_layer.objects.active
    result.name = name
    result.data.name = name
    return result


def cut(target, cutters):
    """Boolean difference; cutter faces keep their own material index (index based)."""
    cutter = join(target.name + '_cutter', cutters) if len(cutters) > 1 else cutters[0]
    mod = target.modifiers.new('Recess', 'BOOLEAN')
    mod.operation = 'DIFFERENCE'
    mod.solver = 'EXACT'
    mod.object = cutter
    if hasattr(mod, 'material_mode'):
        mod.material_mode = 'INDEX'
    activate(target)
    bpy.ops.object.modifier_apply(modifier=mod.name)
    bpy.data.objects.remove(cutter, do_unlink=True)
    clean(target)


def finish(obj, sharp_degrees=42, uv_along=2, grain=(5 * U, 3 * U)):
    """Triangulate, smooth with face-weighted normals, box-project UVs (grain along `uv_along`)."""
    mesh = obj.data
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.dissolve_degenerate(bm, edges=bm.edges, dist=1e-7)
    bmesh.ops.triangulate(bm, faces=bm.faces, quad_method='BEAUTY', ngon_method='BEAUTY')
    limit = math.radians(sharp_degrees)
    for face in bm.faces:
        face.smooth = True
    for edge in bm.edges:
        edge.smooth = not (len(edge.link_faces) == 2 and edge.calc_face_angle(0) > limit)
    uv = bm.loops.layers.uv.verify()
    others = [a for a in range(3) if a != uv_along]
    for face in bm.faces:
        n = to_unity(face.normal)
        axis = max(range(3), key=lambda a: abs(n[a]))
        for loop in face.loops:
            p = to_unity(loop.vert.co)
            if axis == uv_along:      # end grain: both axes across the grain
                u, v = p[others[0]], p[others[1]]
            else:
                u, v = p[uv_along], p[[a for a in others if a != axis][0]]
            loop[uv].uv = (u / grain[0] + .37, v / grain[1] + .5)
    # Unity orders submeshes by first use, so keep faces in slot order: every LOD then shares one slot layout.
    bm.faces.sort(key=lambda f: f.material_index)
    bm.faces.index_update()
    bm.to_mesh(mesh)
    bm.free()
    mod = obj.modifiers.new('Weighted normals', 'WEIGHTED_NORMAL')
    mod.keep_sharp = True
    mod.weight = 60
    activate(obj)
    bpy.ops.object.modifier_apply(modifier=mod.name)
    return obj


# ---------------------------------------------------------------- primitive builders
def beveled_box(name, size, center, bevel, segments, mats):
    """Axis-aligned box in Unity space, edges cut inwards so the outer size never grows."""
    sx, sy, sz = size
    c = Vector(center)
    verts = [c + Vector((x * sx / 2, y * sy / 2, z * sz / 2)) for x in (-1, 1) for y in (-1, 1) for z in (-1, 1)]
    faces = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
    obj = new_object(name, verts, faces, mats)
    bevel_edges(obj, bevel, segments)
    return obj


def bevel_edges(obj, width, segments):
    if segments <= 0 or width <= 0:
        return
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.bevel(bm, geom=list(bm.edges), offset=width, offset_type='OFFSET', segments=segments,
                    profile=.5, affect='EDGES', clamp_overlap=True)
    bm.to_mesh(obj.data)
    bm.free()
    clean(obj)


def frame(normal):
    n = Vector(normal).normalized()
    helper = Vector((0, 1, 0)) if abs(n.y) < .9 else Vector((1, 0, 0))
    a = n.cross(helper).normalized()
    return n, a, n.cross(a)


def disc_cutter(name, center, normal, radius, depth, sides, mats, floor_mat=0, wall_mat=0):
    """Cylinder that starts just outside the surface and ends `depth` below it."""
    n, a, b = frame(normal)
    c = Vector(center)
    top, bottom = c + n * radius * .5, c - n * depth
    verts, faces, face_mats = [], [], []
    for i in range(sides):
        t = math.tau * (i + .5) / sides
        offset = (a * math.cos(t) + b * math.sin(t)) * radius
        verts += [top + offset, bottom + offset]
    for i in range(sides):
        j = (i + 1) % sides
        faces.append((2 * i, 2 * j, 2 * j + 1, 2 * i + 1)); face_mats.append(wall_mat)
    faces.append([2 * i for i in range(sides)]); face_mats.append(wall_mat)
    faces.append([2 * i + 1 for i in range(sides)]); face_mats.append(floor_mat)
    return new_object(name, verts, faces, mats, face_mats)


def panel_cutter(name, center, normal, along, half_along, half_across, depth, corner, mats, floor_mat):
    """Rounded-rectangle pocket; the pocket floor takes `floor_mat`."""
    n = Vector(normal).normalized()
    a = Vector(along).normalized()
    b = n.cross(a)
    c = Vector(center)
    outline = []
    for sx, sy, start in ((1, 1, 0), (-1, 1, 90), (-1, -1, 180), (1, -1, 270)):
        for step in range(3):
            t = math.radians(start + step * 45)
            outline.append(a * (sx * (half_along - corner) + math.cos(t) * corner) + b * (sy * (half_across - corner) + math.sin(t) * corner))
    count = len(outline)
    verts, faces, face_mats = [], [], []
    for o in outline:
        verts += [c + n * depth * 2 + o, c - n * depth + o]
    for i in range(count):
        j = (i + 1) % count
        faces.append((2 * i, 2 * j, 2 * j + 1, 2 * i + 1)); face_mats.append(0)
    faces.append([2 * i for i in range(count)]); face_mats.append(0)
    faces.append([2 * i + 1 for i in range(count)]); face_mats.append(floor_mat)
    return new_object(name, verts, faces, mats, face_mats)


def lathe(name, rings, sides, axis, mats, ring_mats=None, closed=False):
    """rings: list of functions/tuples giving (axial, radius) for side j.
    axis 'x' spins around Unity X (wheels); 'z' around Unity Z (drill).
    Open profiles must start and end on the axis (radius 0) or are capped flat."""
    def point(k, j):
        ring = rings[k]
        axial, radius = ring(j) if callable(ring) else ring
        t = math.tau * j / sides
        if axis == 'x':
            return Vector((axial, math.cos(t) * radius, math.sin(t) * radius))
        if axis == 'y':
            return Vector((math.cos(t) * radius, axial, math.sin(t) * radius))
        return Vector((math.cos(t) * radius, math.sin(t) * radius, axial))
    verts, index = [], {}
    for k in range(len(rings)):
        on_axis = all(abs((rings[k](j) if callable(rings[k]) else rings[k])[1]) < 1e-9 for j in range(sides))
        for j in range(sides):
            if on_axis and j > 0:
                index[k, j] = index[k, 0]
                continue
            index[k, j] = len(verts)
            verts.append(point(k, j))
    faces, face_mats = [], []
    count = len(rings)
    for k in range(count if closed else count - 1):
        k2 = (k + 1) % count
        for j in range(sides):
            j2 = (j + 1) % sides
            quad = [index[k, j], index[k2, j], index[k2, j2], index[k, j2]]
            quad = [v for n, v in enumerate(quad) if v not in quad[:n]]
            if len(quad) >= 3:
                faces.append(quad)
                face_mats.append(ring_mats[k] if ring_mats else 0)
    if not closed:
        for k in (0, count - 1):
            loop = [index[k, j] for j in range(sides)]
            if len(set(loop)) >= 3:
                faces.append(loop)
                face_mats.append(ring_mats[min(k, count - 2)] if ring_mats else 0)
    return new_object(name, verts, faces, mats, face_mats)


def loft(name, sections, mats, strip_mats, cap_mat):
    """sections: list of (x, [(y, z), ...]) closed outlines with equal point counts, lofted along Unity X."""
    count = len(sections[0][1])
    verts = [Vector((x, y, z)) for x, outline in sections for (y, z) in outline]
    faces, face_mats = [], []
    for s in range(len(sections) - 1):
        for i in range(count):
            j = (i + 1) % count
            faces.append((s * count + i, s * count + j, (s + 1) * count + j, (s + 1) * count + i))
            face_mats.append(strip_mats[i])
    faces.append([i for i in range(count)]); face_mats.append(cap_mat)
    faces.append([(len(sections) - 1) * count + i for i in range(count)]); face_mats.append(cap_mat)
    return new_object(name, verts, faces, mats, face_mats)


# ---------------------------------------------------------------- parts
def unit_centres(size_units):
    """Mirror of BuildingGeometry.BoxUnitCenters: one socket per unit cell on every face."""
    out = []
    dims = [d * U for d in size_units]
    for axis in range(3):
        a, b = [i for i in range(3) if i != axis]
        for sign in (-1, 1):
            for i in range(size_units[a]):
                for j in range(size_units[b]):
                    centre = [0, 0, 0]
                    centre[axis] = sign * dims[axis] / 2
                    centre[a] = (i - (size_units[a] - 1) / 2) * U
                    centre[b] = (j - (size_units[b] - 1) / 2) * U
                    normal = [0, 0, 0]
                    normal[axis] = sign
                    out.append((centre, normal))
    return out


WOOD = ['CarArt_Wood', 'CarArt_WoodEdge']


def darken_bevels(obj, keep_normals):
    """Faces that are not one of the block's original flat faces (the chamfers) take the edge colour."""
    keep = [to_blender(n).normalized() for n in keep_normals]
    for poly in obj.data.polygons:
        if not any(poly.normal.dot(k) > .9995 for k in keep):
            poly.material_index = 1


def wood_block(name, size_units, lod, bevel=.07, single_dot=0):
    """Every LOD keeps a dark chamfer, so the seam between two joined blocks survives at distance.
    single_dot > 0 replaces the per-socket dimples with one large dimple of that radius (in units) per face:
    four dots on the big cube read as a die."""
    size = [d * U for d in size_units]
    obj = beveled_box(f'{name}_LOD{lod}', size, (0, 0, 0), bevel * U, (2, 1, 1)[lod], WOOD)
    darken_bevels(obj, [(1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1)])
    if lod < 2:
        sides = (16, 8)[lod]
        if single_dot:
            faces = [([sign * size[axis] / 2 if i == axis else 0 for i in range(3)], [sign if i == axis else 0 for i in range(3)]) for axis in range(3) for sign in (-1, 1)]
            cut(obj, [disc_cutter(f'c{n}', c, nrm, single_dot * U, .05 * U, sides * 2, WOOD, floor_mat=1, wall_mat=1) for n, (c, nrm) in enumerate(faces)])
        else:
            cut(obj, [disc_cutter(f'c{n}', c, nrm, .27 * U, .04 * U, sides, WOOD, floor_mat=1, wall_mat=1) for n, (c, nrm) in enumerate(unit_centres(size_units))])
    return finish(obj)


def core(lod):
    mats = ['CarArt_Enamel', 'CarArt_Energy']
    name = f'Core_LOD{lod}'
    if lod == 2:
        # Each face is a 3x3 grid whose centre cell carries the energy colour: no coplanar decals.
        h, k = U / 2, U * .27
        verts, faces, face_mats = [], [], []
        for axis in range(3):
            a, b = [i for i in range(3) if i != axis]
            for sign in (-1, 1):
                base = len(verts)
                for va in (-h, -k, k, h):
                    for vb in (-h, -k, k, h):
                        p = [0, 0, 0]; p[axis] = sign * h; p[a] = va; p[b] = vb
                        verts.append(p)
                for i in range(3):
                    for j in range(3):
                        faces.append((base + i * 4 + j, base + i * 4 + j + 1, base + (i + 1) * 4 + j + 1, base + (i + 1) * 4 + j))
                        face_mats.append(1 if i == 1 and j == 1 else 0)
        return finish(new_object(name, verts, faces, mats, face_mats))
    obj = beveled_box(name, (U, U, U), (0, 0, 0), .085 * U, (3, 1)[lod], mats)
    sides = (24, 10)[lod]
    cut(obj, [disc_cutter(f'c{n}', c, nrm, .30 * U, .035 * U, sides, mats, floor_mat=1) for n, (c, nrm) in enumerate(unit_centres((1, 1, 1)))])
    return finish(obj)


def triangle(lod):
    """Right prism along X; outline (z, y): (-u,-u/2) (u,-u/2) (-u,u/2). Real slope kept flat."""
    h = U / 2
    outline = [(-U, -h), (U, -h), (-U, h)]      # (z, y)
    verts = [(side * h, y, z) for side in (-1, 1) for (z, y) in outline]
    faces = [(0, 1, 2), (3, 5, 4), (0, 3, 4, 1), (1, 4, 5, 2), (2, 5, 3, 0)]
    obj = new_object(f'Triangle_LOD{lod}', verts, faces, WOOD)
    slope = (0, 2, 1)
    bevel_edges(obj, .06 * U, (2, 1, 1)[lod])
    darken_bevels(obj, [(1, 0, 0), (-1, 0, 0), (0, -1, 0), (0, 0, -1), slope])
    if lod < 2:
        sides = (16, 8)[lod]
        sockets = [((-h, -U / 6, -U / 3), (-1, 0, 0), .2), ((h, -U / 6, -U / 3), (1, 0, 0), .2), ((0, 0, -U), (0, 0, -1), .27)]
        for z in (-h, h):
            sockets.append(((0, -h, z), (0, -1, 0), .27))
            sockets.append(((0, -z * .5, z), slope, .27))
        cut(obj, [disc_cutter(f'c{n}', c, nrm, r * U, .04 * U, sides, WOOD, floor_mat=1, wall_mat=1) for n, (c, nrm, r) in enumerate(sockets)])
    return finish(obj)


def tire(name, radius, lod):
    """Chunky chevron tread. Stays inside Ring(radius, .39 radius, width): the collider outline."""
    w = WHEEL_WIDTH
    bore = .385 * radius
    lugs = 12
    if lod == 2:
        sides = 12
        outer = radius * .985
        rings = [(-w / 2, bore), (-w / 2, outer), (w / 2, outer), (w / 2, bore)]
        return finish(lathe(f'{name}_LOD2', rings, sides, 'x', ['CarArt_Rubber'], closed=True), sharp_degrees=50)
    sides = lugs * (4, 2)[lod]
    period = (4, 2)[lod]
    columns = ([-.5, -.27, 0, .27, .5], [-.5, 0, .5])[lod]
    shift = ([0, 1, 2, 1, 0], [0, 1, 0])[lod]
    groove = (.915, .94)[lod]

    def tread(col):
        x = columns[col] * w
        edge = abs(columns[col]) == .5

        def ring(j):
            lug = (j + shift[col]) % period < period / 2
            r = radius * (1 if lug else groove)
            if edge:    # rounded shoulder: lugs wrap over it, grooves sit lower
                r -= radius * .045
            return (x * (.93 if edge else 1), r)
        return ring
    rings = [(-w * .44, bore), (-w / 2, radius * .50), (-w / 2, radius * .80)]
    rings += [tread(c) for c in range(len(columns))]
    rings += [(w / 2, radius * .80), (w / 2, radius * .50), (w * .44, bore)]
    return finish(lathe(f'{name}_LOD{lod}', rings, sides, 'x', ['CarArt_Rubber'], closed=True), sharp_degrees=50)


def hub(name, radius, lod):
    """Brass bearing with flat mount faces at +-0.48 width and a shallow socket dimple, never a plug."""
    w = WHEEL_WIDTH
    r = .37 * radius
    face = w * .48
    if lod == 2:
        rings = [(-face, 0), (-face, r), (face, r), (face, 0)]
        return finish(lathe(f'{name}_LOD2', rings, 8, 'x', ['CarArt_Brass']), sharp_degrees=50)
    sides = (24, 12)[lod]
    dimple, depth = r * .58, .03 * U
    half = [(face - depth, 0), (face - depth, dimple * .92), (face, dimple), (face, r * .86), (face - r * .14, r)]
    rings = [(-a, rad) for a, rad in half] + [(a, rad) for a, rad in reversed(half)]
    return finish(lathe(f'{name}_LOD{lod}', rings, sides, 'x', ['CarArt_Brass']), sharp_degrees=38)


HOUSING_DEPTH = .70 * U
HOUSING_SIDE = .72 * U        # narrower than a cube face: the drill has one socket, at its base
COLLAR_END = .84 * U
BIT_START = .80 * U
BIT_RADIUS = .42 * U


def drill_housing(lod):
    """Module shell inside SandboxDrill's one-cube collider (base at DRILL_BASE, faces +Z)."""
    mats = ['CarArt_Cream', 'CarArt_Enamel', 'CarArt_Brass']
    name = f'DrillHousing_LOD{lod}'
    side = HOUSING_SIDE
    centre = (0, 0, DRILL_BASE + HOUSING_DEPTH / 2)
    z0, z1 = DRILL_BASE + HOUSING_DEPTH - .02 * U, DRILL_BASE + COLLAR_END
    if lod == 2:
        h, k = side / 2, side * .3
        za, zb = DRILL_BASE, DRILL_BASE + HOUSING_DEPTH
        zk0, zk1 = za + HOUSING_DEPTH * .2, zb - HOUSING_DEPTH * .2
        verts, faces, face_mats = [], [], []
        for axis in (0, 1):
            other = 1 - axis
            for sign in (-1, 1):
                base = len(verts)
                for va in (-h, -k, k, h):
                    for z in (za, zk0, zk1, zb):
                        p = [0, 0, z]; p[axis] = sign * h; p[other] = va
                        verts.append(p)
                for i in range(3):
                    for j in range(3):
                        faces.append((base + i * 4 + j, base + i * 4 + j + 1, base + (i + 1) * 4 + j + 1, base + (i + 1) * 4 + j))
                        face_mats.append(1 if i == 1 and j == 1 else 0)
        for z in (za, zb):
            base = len(verts)
            verts += [(va, vb, z) for va in (-h, -k, k, h) for vb in (-h, -k, k, h)]
            for i in range(3):
                for j in range(3):
                    faces.append((base + i * 4 + j, base + i * 4 + j + 1, base + (i + 1) * 4 + j + 1, base + (i + 1) * 4 + j))
                    face_mats.append(0)
        shell = new_object(name, verts, faces, mats, face_mats)
        collar = lathe('collar', [(z0, 0), (z0, .46 * U), (z1, .46 * U), (z1, 0)], 8, 'z', mats, ring_mats=[2, 2, 2])
        return finish(join(name, [shell, collar]), sharp_degrees=50)
    shell = beveled_box(name, (side, side, HOUSING_DEPTH), centre, .075 * U, (2, 1)[lod], mats)
    cutters = [disc_cutter('socket', (0, 0, DRILL_BASE), (0, 0, -1), .27 * U, .03 * U, (16, 8)[lod], mats)]
    for n, normal in enumerate(((1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0))):
        face_centre = Vector(normal) * side / 2 + Vector((0, 0, centre[2]))
        cutters.append(panel_cutter(f'panel{n}', face_centre, normal, (0, 0, 1), HOUSING_DEPTH * .33, side * .31, .028 * U, .07 * U, mats, 1))
    cut(shell, cutters)
    sides = (24, 12)[lod]
    rings = [(z0, 0), (z0, .44 * U), (z0 + .03 * U, .47 * U), (z1 - .03 * U, .47 * U), (z1, .43 * U), (z1, 0)]
    collar = lathe('collar', rings, sides, 'z', mats, ring_mats=[2] * 5)
    return finish(join(name, [shell, collar]))


def drill_bit(lod):
    """Cone with three wide spiral flutes, centred on Z so SandboxDrill can spin it in place.
    The grid columns follow the flutes, so their edges stay crisp at a low triangle count."""
    start, tip = DRILL_BASE + BIT_START, DRILL_TIP
    rows, per_flute = ((12, 6), (5, 3), (2, 2))[lod]
    flutes, twist, depth = 3, 1.4, (.24, .24, 0)[lod]
    sides = flutes * per_flute
    verts, faces = [Vector((0, 0, start))], []
    for k in range(rows):
        t = k / rows
        radius = BIT_RADIUS * (1 - t) ** .8
        for j in range(sides):
            ramp = (j % per_flute) / per_flute          # 0 at the cutting edge, falling away behind it
            angle = math.tau * (j / sides + twist * t)
            r = radius * (1 - depth * ramp)
            verts.append(Vector((math.cos(angle) * r, math.sin(angle) * r, start + (tip - start) * t)))
    verts.append(Vector((0, 0, tip)))
    last = len(verts) - 1
    for j in range(sides):
        j2 = (j + 1) % sides
        faces.append((0, 1 + j2, 1 + j))
        for k in range(rows - 1):
            a, b = 1 + k * sides, 1 + (k + 1) * sides
            faces.append((a + j, a + j2, b + j2, b + j))
        faces.append((1 + (rows - 1) * sides + j, 1 + (rows - 1) * sides + j2, last))
    return finish(new_object(f'DrillBit_LOD{lod}', verts, faces, ['CarArt_Steel']), sharp_degrees=(38, 38, 40)[lod])


# ---------------------------------------------------------------- speed bump (roller Diameter = 1)
def bump_outline(lod, inset=1.0, squash=1.0):
    """(y, z) outline and per-edge material. Arch fills the roller's circle and its undercut."""
    arc = (12, 6, 4)[lod]
    pts = []
    if lod < 2:
        pts += [(0, -.58), (.045, -.58), (.09, -.52), (.14, -.5)]
    else:
        pts += [(0, -.5)]
    pts.append((.5, -.5))
    for i in range(1, arc):
        t = math.pi * (1 - i / arc)
        pts.append((.5 + .5 * math.sin(t), .5 * math.cos(t)))
    pts.append((.5, .5))
    if lod < 2:
        pts += [(.14, .5), (.09, .52), (.045, .58), (0, .58)]
    else:
        pts += [(0, .5)]
    flange = 3 if lod < 2 else 0
    count = len(pts)
    mats = [1 if i < flange or i >= count - 1 - flange else 0 for i in range(count)]
    out = []
    for i, (y, z) in enumerate(pts):
        arch = flange < i < count - 1 - flange or lod == 2
        if arch and inset != 1:
            y, z = .3 + (y - .3) * inset if y > .14 else y, z * inset
        out.append((y * squash, z * (squash if squash == 1 else (1 + squash) / 2)))
    return out, mats


def bump_mid(lod):
    mats = ['CarArt_BumpPaint', 'CarArt_BumpBase']
    full, strip = bump_outline(lod)
    if lod == 0:
        tucked, _ = bump_outline(lod, inset=.9)
        sections = [(-.4, tucked), (-.36, full), (.36, full), (.4, tucked)]
    else:
        sections = [(-.4, full), (.4, full)]
    return finish(loft(f'SpeedBumpMid_LOD{lod}', sections, mats, strip, 1), sharp_degrees=40, uv_along=0, grain=(1, 1))


def bump_end(lod):
    """Blunt end cap, joint at x = 0 and the outer end at x = +0.3."""
    mats = ['CarArt_BumpPaint', 'CarArt_BumpBase']
    full, strip = bump_outline(lod)
    if lod == 0:
        tucked, _ = bump_outline(lod, inset=.9)
        sections = [(0, tucked), (.04, full), (.2, full), (.265, bump_outline(lod, squash=.94)[0]), (.3, bump_outline(lod, squash=.8)[0])]
    elif lod == 1:
        sections = [(0, full), (.22, full), (.3, bump_outline(lod, squash=.82)[0])]
    else:
        sections = [(0, full), (.3, full)]
    return finish(loft(f'SpeedBumpEnd_LOD{lod}', sections, mats, strip, 1), sharp_degrees=40, uv_along=0, grain=(1, 1))


# ---------------------------------------------------------------- build turntable handwheel
WHEEL_RADIUS = .17      # DownhillRotationWheel.Radius: where the hand closes on it


def handwheel(lod):
    """Ship's-wheel ring lying in XZ: an enamel rim, eight wooden handles and inlaid turn arrows.
    It replaces nothing physical; the wheel's trigger grips stay on the same circle."""
    mats = ['CarArt_Enamel', 'CarArt_Wood', 'CarArt_Energy']
    name = f'Handwheel_LOD{lod}'
    a, b, c = .016, .011, .005          # rim half width, half height, chamfer
    R = WHEEL_RADIUS
    if lod == 2:
        rim_rings = [(-b, R - a), (-b, R + a), (b, R + a), (b, R - a)]
    else:
        rim_rings = [(-b, R - a + c), (-b, R + a - c), (-b + c, R + a), (b - c, R + a), (b, R + a - c), (b, R - a + c), (b - c, R - a), (-b + c, R - a)]
    rim = lathe('rim', rim_rings, (48, 24, 16)[lod], 'y', mats, closed=True)
    parts = [rim]
    # Handles: turned pegs pointing outwards, fat at the tip like a ship's wheel.
    profile = ([(0, 0), (0, .0100), (.007, .0110), (.019, .0092), (.027, .0138), (.034, .0120), (.038, 0)],
               [(0, 0), (0, .0102), (.019, .0092), (.027, .0138), (.038, 0)],
               [(0, 0), (0, .0105), (.027, .0135), (.038, 0)])[lod]
    sides = (10, 6, 4)[lod]
    for k in range(8):
        angle = math.tau * k / 8
        out = Vector((math.cos(angle), 0, math.sin(angle)))
        tangent = Vector((-math.sin(angle), 0, math.cos(angle)))
        verts, faces = [], []
        for (axial, radius) in profile:
            if radius == 0:
                verts.append(out * (R + a - .003 + axial))
            else:
                for j in range(sides):
                    t = math.tau * j / sides
                    verts.append(out * (R + a - .003 + axial) + tangent * math.cos(t) * radius + Vector((0, 1, 0)) * math.sin(t) * radius)
        first, last = 0, len(verts) - 1
        rings = len(profile) - 2
        for j in range(sides):
            j2 = (j + 1) % sides
            faces.append((first, 1 + j, 1 + j2))
            for ring in range(rings - 1):
                r0, r1 = 1 + ring * sides, 1 + (ring + 1) * sides
                faces.append((r0 + j, r1 + j, r1 + j2, r0 + j2))
            faces.append((1 + (rings - 1) * sides + j, last, 1 + (rings - 1) * sides + j2))
        parts.append(new_object(f'handle{k}', verts, faces, mats, [1] * len(faces)))
    # Double-headed arc arrows inlaid in the rim's top between the handles: it turns both ways.
    steps = (6, 3, 2)[lod]
    top, bottom = b + .0012, b - .002
    for k in range(8):
        start, end = math.radians(k * 45 + 9), math.radians(k * 45 + 36)
        head = math.radians(7)
        def at(angle, radius, y):
            return Vector((math.cos(angle) * radius, y, math.sin(angle) * radius))
        ring = [(start, R)] + [(start + head, R + .0105), (start + head, R + .0045)]
        ring += [(start + head + (end - start - 2 * head) * i / steps, R + .0045) for i in range(1, steps)]
        ring += [(end - head, R + .0045), (end - head, R + .0105), (end, R), (end - head, R - .0105), (end - head, R - .0045)]
        ring += [(end - head - (end - start - 2 * head) * i / steps, R - .0045) for i in range(1, steps)]
        ring += [(start + head, R - .0045), (start + head, R - .0105)]
        count = len(ring)
        verts = [at(angle, radius, top) for angle, radius in ring] + [at(angle, radius, bottom) for angle, radius in ring]
        faces = [list(range(count)), list(range(count, 2 * count))]
        faces += [(i, (i + 1) % count, count + (i + 1) % count, count + i) for i in range(count)]
        parts.append(new_object(f'arrow{k}', verts, faces, mats, [2] * len(faces)))
    return finish(join(name, parts), sharp_degrees=40)


# ---------------------------------------------------------------- level scenery props (metres, origin on the ground)
# Each campaign level gets objects of its own. Level 3 is the wheelwright's level: stacked tyres and a
# hanging wheel sign. Level 4 is the quarry: cut sandstone blocks and an ore cart.
def place(obj, position=(0, 0, 0), yaw=0, tilt=0, roll=0):
    """Move a mesh in Unity terms: yaw about Y (up), tilt about X, roll about Z, then translate."""
    m = Matrix.Translation(to_blender(position)) @ Matrix.Rotation(math.radians(yaw), 4, 'Z') \
        @ Matrix.Rotation(math.radians(-tilt), 4, 'X') @ Matrix.Rotation(math.radians(roll), 4, 'Y')
    obj.data.transform(m)
    obj.data.update()
    return obj


def reslot(obj, mats, shift):
    """Give a sub-part the whole prop's material list, moving its own slots up by `shift`.
    Clearing the slots resets every face to slot 0, so the indices are read first."""
    indices = [poly.material_index for poly in obj.data.polygons]
    obj.data.materials.clear()
    for name in mats:
        obj.data.materials.append(material(name))
    for poly, index in zip(obj.data.polygons, indices):
        poly.material_index = index + shift
    return obj


def prop_tyre(mats, lod, radius, height, axis='y'):
    """A plain round tyre with a brass hub, as stock for stacks and signs. mats: rubber first, brass second."""
    r, h, bore = radius, height / 2, radius * .42
    if lod == 2:
        rings = [(-h, bore), (-h, r), (h, r), (h, bore)]
    else:
        rings = [(-h, bore), (-h, r * .8), (-h * .55, r), (h * .55, r), (h, r * .8), (h, bore)]
    sides = (24, 14, 8)[lod]
    tyre = lathe('tyre', rings, sides, axis, mats, ring_mats=[0] * len(rings), closed=True)
    hub = lathe('hub', [(-h * .6, 0), (-h * .6, bore * .96), (h * .6, bore * .96), (h * .6, 0)], sides, axis, mats, ring_mats=[1, 1, 1])
    return join('wheel', [tyre, hub])


def prop_tyre_stack(lod):
    mats = ['CarArt_Rubber', 'CarArt_Brass']
    parts = []
    for n, (dx, dz, yaw) in enumerate(((0, 0, 0), (.02, -.015, 40), (-.015, .02, 95))):
        parts.append(place(prop_tyre(mats, lod, .23, .115), (dx, .0575 + n * .115, dz), yaw))
    # A fourth leans against the stack, so it reads as a working pile and not a column.
    parts.append(place(prop_tyre(mats, lod, .2, .1), (.33, .19, .03), 12, roll=68))
    return finish(join(f'PropTyreStack_LOD{lod}', parts), sharp_degrees=45)


def prop_wheel_sign(lod):
    mats = ['CarArt_Wood', 'CarArt_WoodEdge', 'CarArt_Rubber', 'CarArt_Brass', 'CarArt_Enamel']
    segs = (2, 1, 1)[lod]
    axes = [(1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1)]
    def timber(size, centre):
        box = beveled_box('timber', size, centre, .012, segs, mats)
        darken_bevels(box, axes)
        return box
    parts = [timber((.08, 1.0, .08), (0, .5, 0)), timber((.62, .07, .07), (.23, .93, 0)), timber((.16, .05, .16), (0, .025, 0))]
    # The wheel hangs face-on under the arm on two straps.
    wheel_mats = ['CarArt_Rubber', 'CarArt_Brass']
    wheel = place(prop_tyre(wheel_mats, lod, .2, .09, axis='z'), (.34, .6, 0))
    parts.append(reslot(wheel, mats, 2))
    for x in (.26, .42):
        strap = beveled_box('strap', (.016, .12, .016), (x, .84, 0), 0, 0, mats)
        for poly in strap.data.polygons:
            poly.material_index = 3
        parts.append(strap)
    plate = beveled_box('plate', (.2, .12, .022), (0, .62, .05), .008, 1, mats)
    for poly in plate.data.polygons:
        poly.material_index = 4
    parts.append(plate)
    return finish(join(f'PropWheelSign_LOD{lod}', parts), sharp_degrees=42, uv_along=1)


def prop_stone_blocks(lod):
    mats = ['CarArt_Sandstone', 'CarArt_SandstoneEdge']
    segs = (2, 1, 1)[lod]
    axes = [(1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1)]
    parts = []
    for size, position, yaw in (((.44, .3, .36), (-.12, .15, 0), 8), ((.36, .26, .3), (.3, .13, .06), -24), ((.32, .24, .28), (-.04, .42, .02), 31)):
        block = beveled_box('block', size, (0, 0, 0), .03, segs, mats)
        darken_bevels(block, axes)
        if lod < 2:
            # One sawn groove across the top: the mark of a cut block rather than a boulder.
            cut(block, [panel_cutter('groove', (0, size[1] / 2, 0), (0, 1, 0), (1, 0, 0), size[0] * .5 + .05, .012, .012, .004, mats, 1)])
        parts.append(place(block, position, yaw))
    return finish(join(f'PropStoneBlocks_LOD{lod}', parts), sharp_degrees=42)


def prop_ore_cart(lod):
    mats = ['CarArt_Wood', 'CarArt_WoodEdge', 'CarArt_Rubber', 'CarArt_Brass', 'CarArt_Sandstone', 'CarArt_SandstoneEdge']
    segs = (2, 1, 1)[lod]
    axes = [(1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1)]
    tub = beveled_box('tub', (.52, .2, .32), (0, .24, 0), .02, segs, mats)
    darken_bevels(tub, axes)
    parts = [tub]
    wheel_mats = ['CarArt_Rubber', 'CarArt_Brass']
    for x in (-.17, .17):
        for z in (-.18, .18):
            wheel = place(prop_tyre(wheel_mats, lod, .075, .045, axis='z'), (x, .075, z))
            parts.append(reslot(wheel, mats, 2))
    # A heap of rough-cut stone over the rim.
    for size, position, yaw, tilt in (((.2, .13, .18), (-.12, .38, -.03), 15, 8), ((.18, .14, .17), (.1, .39, .04), -30, -6), ((.15, .11, .14), (0, .47, 0), 50, 10)):
        lump = beveled_box('lump', size, (0, 0, 0), .022, segs, mats)
        darken_bevels(lump, axes)
        for poly in lump.data.polygons:
            poly.material_index += 4
        parts.append(place(lump, position, yaw, tilt))
    return finish(join(f'PropOreCart_LOD{lod}', parts), sharp_degrees=42, uv_along=0)


# ---------------------------------------------------------------- texture
def wood_texture(path, size=512):
    """Tileable, low-contrast broad grain. Stored as a light multiplier: the material tint carries the hue."""
    v, u = np.meshgrid(np.linspace(0, 1, size, endpoint=False), np.linspace(0, 1, size, endpoint=False), indexing='ij')
    tau = math.tau
    warp = .35 * np.sin(tau * u) + .18 * np.sin(tau * (2 * u + .3))
    tone = .5 + .30 * np.sin(tau * (3 * v + warp)) + .14 * np.sin(tau * (7 * v + .6 * np.sin(tau * u + 1))) + .05 * np.sin(tau * (17 * v + .3 * np.sin(tau * 2 * u)))
    tone = np.clip(tone, 0, 1) ** .8
    dark, light = np.array([.86, .82, .76]), np.array([1, 1, 1])
    rgb = dark + (light - dark) * tone[..., None]
    pixels = np.concatenate([rgb, np.ones((size, size, 1))], axis=2).astype(np.float32)
    image = bpy.data.images.new('CarArt_WoodGrain', size, size, alpha=False)
    image.pixels.foreach_set(pixels.ravel())
    image.filepath_raw = str(path)
    image.file_format = 'PNG'
    image.save()
    return image


# ---------------------------------------------------------------- build, export, preview
PIECES = [
    ('SmallWheelTire', lambda lod: tire('SmallWheelTire', SMALL_RADIUS, lod)),
    ('SmallWheelHub', lambda lod: hub('SmallWheelHub', SMALL_RADIUS, lod)),
    ('LargeWheelTire', lambda lod: tire('LargeWheelTire', LARGE_RADIUS, lod)),
    ('LargeWheelHub', lambda lod: hub('LargeWheelHub', LARGE_RADIUS, lod)),
    ('Core', core),
    ('Cube', lambda lod: wood_block('Cube', (2, 2, 2), lod, bevel=.085, single_dot=.56)),
    ('ShortBlock', lambda lod: wood_block('ShortBlock', (1, 1, 2), lod)),
    ('LongBlock', lambda lod: wood_block('LongBlock', (1, 1, 3), lod)),
    ('Triangle', triangle),
    ('DrillHousing', drill_housing),
    ('DrillBit', drill_bit),
    ('SpeedBumpMid', bump_mid),
    ('SpeedBumpEnd', bump_end),
    ('Handwheel', handwheel),
    ('PropTyreStack', prop_tyre_stack),
    ('PropWheelSign', prop_wheel_sign),
    ('PropStoneBlocks', prop_stone_blocks),
    ('PropOreCart', prop_ore_cart),
]


def export(name, objects):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objects:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.export_scene.fbx(filepath=str(MODELS / f'{name}.fbx'), use_selection=True, object_types={'MESH'},
                             apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS', bake_space_transform=True,
                             axis_forward='-Z', axis_up='Y', use_mesh_modifiers=True, mesh_smooth_type='OFF',
                             use_tspace=False, add_leaf_bones=False, bake_anim=False, path_mode='STRIP')


def describe(obj):
    mesh = obj.data
    points = [to_unity(v.co) for v in mesh.vertices]
    lo = [min(p[a] for p in points) for a in range(3)]
    hi = [max(p[a] for p in points) for a in range(3)]
    bm = bmesh.new(); bm.from_mesh(mesh)
    closed = all(len(e.link_faces) == 2 for e in bm.edges)
    bm.free()
    used = sorted({p.material_index for p in mesh.polygons})
    assert used == list(range(len(mesh.materials))), (obj.name, used)
    return dict(triangles=len(mesh.polygons), vertices=len(mesh.vertices), closed=closed,
                min=[round(x, 6) for x in lo], max=[round(x, 6) for x in hi], materials=[m.name for m in mesh.materials])


def main():
    scene = reset_scene()
    manifest = dict(unit=U, drill_base=DRILL_BASE, drill_tip=DRILL_TIP, pieces={})
    built = {}
    for name, make in PIECES:
        lods = [make(lod) for lod in range(3)]
        for lod, obj in enumerate(lods):
            obj.name = obj.data.name = f'{name}_LOD{lod}'
        export(name, lods)
        manifest['pieces'][name] = [describe(o) for o in lods]
        built[name] = lods
        print(name, [d['triangles'] for d in manifest['pieces'][name]], [d['closed'] for d in manifest['pieces'][name]])
    (HERE / 'CarPartsManifest.json').write_text(json.dumps(manifest, indent=1), encoding='utf-8')

    image = wood_texture(TEXTURES / 'CarArt_WoodGrain.png')
    wood = material('CarArt_Wood')
    nodes, links = wood.node_tree.nodes, wood.node_tree.links
    tex = nodes.new('ShaderNodeTexImage'); tex.image = image
    mix = nodes.new('ShaderNodeMixRGB'); mix.blend_type = 'MULTIPLY'; mix.inputs['Fac'].default_value = 1
    bsdf = nodes['Principled BSDF']
    mix.inputs['Color1'].default_value = bsdf.inputs['Base Color'].default_value
    links.new(tex.outputs['Color'], mix.inputs['Color2'])
    links.new(mix.outputs['Color'], bsdf.inputs['Base Color'])

    # Lay the kit out for the .blend and the preview: assembled groups along the camera's right vector, LODs receding.
    groups = [('SmallWheelTire', 'SmallWheelHub'), ('LargeWheelTire', 'LargeWheelHub'), ('Core',), ('Cube',), ('ShortBlock',),
              ('LongBlock',), ('Triangle',), ('DrillHousing', 'DrillBit'), ('SpeedBumpEnd', 'SpeedBumpMid'), ('Handwheel',), ('PropTyreStack',), ('PropWheelSign',), ('PropStoneBlocks',), ('PropOreCart',)]
    eye = Vector((.9, -2.2, 1.7))
    right = Vector((-eye.y, eye.x, 0)).normalized()
    away = Vector((-eye.x, -eye.y, 0)).normalized()
    wide = ('LargeWheelTire', 'Cube', 'LongBlock', 'DrillHousing', 'SpeedBumpEnd')
    props = ('PropTyreStack', 'PropWheelSign', 'PropStoneBlocks', 'PropOreCart')
    x, stops = 0, []
    for group in groups:
        bump = group[0].startswith('SpeedBump')
        scale = .16 if bump else .55 if group[0] == 'Handwheel' else .32 if group[0] in props else 1
        x += (.62 if bump else .13) if group[0] in wide else .16 if group[0] == 'Handwheel' or group[0] in props else .1
        stops.append(x)
        for lod in range(3):
            for name in group:
                obj = built[name][lod]
                obj.scale = (scale,) * 3
                offset = Vector((0, 0, 0))
                if name == 'SpeedBumpMid':
                    offset = Vector((-.4 * scale, 0, 0))
                if name == 'SpeedBumpEnd':
                    obj.rotation_euler = (0, 0, math.pi)
                obj.location = right * x + away * lod * .3 + Vector((0, 0, 0 if bump or name in props else U * 1.2)) + offset
        x += .13 if group[0] in wide else .16 if group[0] == 'Handwheel' or group[0] in props else .1
    bpy.ops.wm.save_as_mainfile(filepath=str(HERE / 'CarParts.blend'))

    if '--no-preview' in sys.argv:
        return
    try:
        scene.render.engine = 'BLENDER_EEVEE_NEXT'
        scene.view_settings.view_transform = 'Standard'
        dark = material('CarArt_BumpBase')
        for lod in range(3):
            mid, end = built['SpeedBumpMid'][lod], built['SpeedBumpEnd'][lod]
            for n in range(1, 4):
                copy = mid.copy(); copy.data = mid.data.copy(); scene.collection.objects.link(copy)
                copy.location = mid.location - Vector((.8 * .16 * n, 0, 0))
                if n % 2:
                    copy.data.materials[0] = dark
            far = end.copy(); scene.collection.objects.link(far)
            far.rotation_euler = (0, 0, 0); far.location = end.location - Vector((.8 * .16 * 4, 0, 0))
        world = bpy.data.worlds.new('World'); scene.world = world
        world.use_nodes = True
        world.node_tree.nodes['Background'].inputs['Color'].default_value = (.85, .82, .74, 1)
        world.node_tree.nodes['Background'].inputs['Strength'].default_value = .9
        sun = bpy.data.objects.new('Sun', bpy.data.lights.new('Sun', 'SUN'))
        sun.data.energy = 2.4; sun.data.angle = math.radians(6); sun.rotation_euler = (math.radians(52), math.radians(4), math.radians(-18))
        scene.collection.objects.link(sun)
        ground = bpy.data.meshes.new('Ground'); ground.from_pydata([(-6, -6, 0), (6, -6, 0), (6, 6, 0), (-6, 6, 0)], [], [(0, 1, 2, 3)])
        floor = bpy.data.objects.new('Ground', ground); scene.collection.objects.link(floor)
        floor_mat = bpy.data.materials.new('Preview ground'); floor_mat.use_nodes = True
        floor_mat.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (.78, .73, .62, 1)
        ground.materials.append(floor_mat)
        cam = bpy.data.objects.new('Camera', bpy.data.cameras.new('Camera'))
        scene.collection.objects.link(cam); scene.camera = cam
        cam.data.type = 'ORTHO'
        out = KIT.parents[3] / 'Docs' / 'Images'
        out.mkdir(parents=True, exist_ok=True)
        split = (stops[4] + stops[5]) / 2
        shots = [('CarPartsArt-Blender-LOD0-A', 0, split, 0, (2400, 900)), ('CarPartsArt-Blender-LOD0-B', split, x, 0, (2400, 900)),
                 ('CarPartsArt-Blender-AllLODs', 0, x, .3, (2400, 1100))]
        for label, x0, x1, depth, size in shots:
            cam.data.ortho_scale = (x1 - x0) + .04
            target = right * (x0 + x1) / 2 + away * depth + Vector((0, 0, .09))
            cam.location = target + eye
            cam.rotation_euler = (target - cam.location).to_track_quat('-Z', 'Y').to_euler()
            scene.render.resolution_x, scene.render.resolution_y = size
            scene.render.filepath = str(out / f'{label}.png')
            bpy.ops.render.render(write_still=True)
            print('Preview', scene.render.filepath)
    except Exception as error:      # preview is optional; the kit is already exported
        print('Preview skipped:', error)


main()
