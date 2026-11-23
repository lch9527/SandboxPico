"""Encode a folder of frameNNNNN.jpg images to an H.264 MP4 with Blender's built-in ffmpeg.
Run:  blender -b --factory-startup --python encode.py -- <frames folder> <output.mp4> [fps]
Written to work across the 4.x and 5.x Python APIs: it checks which names exist instead of assuming."""
import bpy, os, sys, glob

args = sys.argv[sys.argv.index("--") + 1:]
folder, output = args[0], args[1]
fps = int(args[2]) if len(args) > 2 else 30
files = sorted(f for f in os.listdir(folder) if f.lower().endswith(".jpg"))
if not files:
    raise SystemExit("No frames in " + folder)
print("BLENDER", bpy.app.version_string, "frames", len(files))

def choose(owner, name, wanted):
    """Set an enum property to the first wanted value it actually offers; report what it offers otherwise."""
    items = [i.identifier for i in owner.bl_rna.properties[name].enum_items]
    for value in wanted:
        if value in items:
            setattr(owner, name, value)
            return value
    print("ENUM", name, "offers", items)
    return None

scene = bpy.context.scene
render = scene.render
render.resolution_x, render.resolution_y, render.resolution_percentage = 1280, 720, 100
render.fps, render.fps_base = fps, 1
scene.frame_start, scene.frame_end = 1, len(files)
# Pass the frames through untouched: no filmic tone curve on top of what Unity rendered.
# These two enums are filled in at run time, so they are set directly rather than through choose().
scene.view_settings.view_transform = 'Standard'
try:
    scene.view_settings.look = 'None'
except TypeError as error:
    print("LOOK", error)
print("VIEW", scene.view_settings.view_transform, scene.view_settings.look, scene.display_settings.display_device)

editor = scene.sequence_editor_create()
strips = editor.strips if hasattr(editor, "strips") else editor.sequences
strip = strips.new_image("frames", os.path.join(folder, files[0]), 1, 1)
for name in files[1:]:
    strip.elements.append(name)

settings = render.image_settings
if hasattr(settings, "media_type"):
    choose(settings, "media_type", ["VIDEO"])
if choose(settings, "file_format", ["FFMPEG"]) is None:
    raise SystemExit("This Blender cannot write video.")
choose(render.ffmpeg, "format", ["MPEG4"])
choose(render.ffmpeg, "codec", ["H264"])
choose(render.ffmpeg, "constant_rate_factor", ["HIGH"])
choose(render.ffmpeg, "ffmpeg_preset", ["GOOD"])
choose(render.ffmpeg, "audio_codec", ["NONE"])
render.ffmpeg.gopsize = fps
render.use_file_extension = True
render.filepath = output
bpy.ops.render.render(animation=True)

# Blender may append the frame range to the name; settle on exactly the requested file.
stem = os.path.splitext(output)[0]
made = sorted(glob.glob(stem + "*.mp4"), key=os.path.getmtime)
if not made:
    raise SystemExit("No video was written next to " + output)
if os.path.normcase(made[-1]) != os.path.normcase(output):
    if os.path.exists(output):
        os.remove(output)
    os.replace(made[-1], output)
print("ENCODED", output, len(files), "frames", os.path.getsize(output), "bytes")
