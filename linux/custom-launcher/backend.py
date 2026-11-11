"""Linux launcher backend. All writes stay inside this checkout."""
from pathlib import Path, PurePosixPath
import sys, json, os, struct, math, subprocess, zipfile, shutil, tempfile, datetime, hashlib
ROOT = Path(__file__).resolve().parents[1]
RUNTIME = ROOT / 'runtime'
SETTINGS = RUNTIME / 'custom-launcher-settings.json'
TITLE = 'PPSA01341'
LANGUAGES = ['Japanese','English (United States)','French (France)','Spanish (Spain)','German','Italian','Dutch','Portuguese (Portugal)','Russian','Korean','Chinese (Traditional)','Chinese (Simplified)','Finnish','Swedish','Danish','Norwegian','Polish','Portuguese (Brazil)','English (United Kingdom)','Turkish','Spanish (Latin America)','Arabic','French (Canada)','Czech','Hungarian','Greek','Romanian','Thai','Vietnamese','Indonesian']
DEFAULTS = dict(kyty='',game='', width=3440, height=1440, render=100, language=1, fullscreen=True, surround=False, sink='', camera=0, farther='PageUp', closer='PageDown', threads=16)
def load():
    try: return DEFAULTS | json.loads(SETTINGS.read_text())
    except (OSError, ValueError): return DEFAULTS.copy()
def save(s):
    RUNTIME.mkdir(exist_ok=True)
    tmp = SETTINGS.with_suffix('.tmp'); tmp.write_text(json.dumps(s, indent=2)+'\n'); tmp.replace(SETTINGS)
def binary_folder(s):
    path=Path(s.get('kyty',str(ROOT/'_Build/linux')))
    return path/'_Build/linux' if (path/'_Build/linux/kyty_emulator').is_file() else path

def source_folder(s):
    folder=binary_folder(s).resolve()
    for parent in [folder,*folder.parents]:
        if (parent/'tools/local/static-precompile/pass-states.json').is_file() and (parent/'CMakeLists.txt').is_file():
            return parent
    raise ValueError('Precompiling needs the Kyty source checkout used for this build. Choose its _Build/linux folder.')

def emulator_binary(s):
    return binary_folder(s)/'kyty_emulator'

def validate(s):
    binary=emulator_binary(s)
    if not binary.is_file() or not os.access(binary,os.X_OK): raise ValueError('Choose the Linux Kyty folder containing kyty_emulator.')
    with binary.open('rb') as stream:
        if stream.read(4)!=b'\x7fELF': raise ValueError('Choose a native Linux Kyty build.')
    game=Path(s['game'])
    if not (game/'eboot.bin').is_file(): raise ValueError('Choose the game folder containing eboot.bin.')
    p=json.loads((game/'sce_sys/param.json').read_text())
    if p.get('titleId') != TITLE or p.get('contentVersion') != '01.007.000':
        raise ValueError('This mod requires PPSA01341 version 01.007.000.')
    w,h=int(s['width']),int(s['height'])
    if w<1280 or h<720 or w/h < 16/9-.02 or w/h*2160>16384:
        raise ValueError('Choose a resolution of 16:9 or wider, at least 1280×720.')
    if not 50<=int(s['render'])<=100: raise ValueError('Render scale must be 50–100%.')
    if not 0<=int(s['camera'])<=4: raise ValueError('Camera step must be 0–4.')
    if not shutil.which('bwrap'): raise ValueError('Bubblewrap is required to keep the original folders read-only.')
    return p

def wide(s): return abs(s['width']/s['height']-16/9)>.02
def output_width(s): return math.floor(2160*s['width']/s['height']+.5)
def u32(v): return struct.pack('<I',v).hex()
def patch(s):
    w,h=s['width'],s['height'];o=output_width(s)
    values=[('90ae7f','c74304398ee33f','c74304'+struct.pack('<f',w/h).hex()),
      ('90ac3f','48ba000f000070080000','48ba'+u32(o)+u32(2160)),
      ('90a6ab','48b9000f000070080000','48b9'+u32(o)+u32(2160)),
      ('90b217','48b9000f000070080000','48b9'+u32(o)+u32(2160)),
      ('250eb48','4006000080070000000a0000000f0000800a0000',''.join(map(u32,[1600,1920,w,3840,2688]))),
      ('237f264','000a0000a0050000000f000070080000000f000070080000',''.join(map(u32,[w,h,o,2160,o,2160]))),
      ('24f9550','000a0000a00500000000000000000000',''.join(map(u32,[w,h,0,0]))),
      ('90af74','bf000f0000','bf'+u32(o)),('90b061','bf000f0000','bf'+u32(o)),
      ('916355','8bb5ecfeffff','be'+u32(o)+'90')]
    # Scale the guest scene dimensions directly, preserving full-resolution UI/output.
    scale=s['render']/100
    rw,rh=math.floor(w*scale+.5),math.floor(h*scale+.5)
    if scale!=1:
        values[4]=(values[4][0],values[4][1],''.join(map(u32,[1600,1920,rw,3840,2688])))
        values[5]=(values[5][0],values[5][1],''.join(map(u32,[rw,rh,o,2160,o,2160])))
        values[6]=(values[6][0],values[6][1],''.join(map(u32,[rw,rh,0,0])))
    return dict(id=TITLE,version='01.007.000',process='eboot.bin',mods=[dict(name='Linux ultrawide',enabled=True,memory=[dict(offset=a,off=b,on=c) for a,b,c in values])])
def prepare(s):
    validate(s)
    # Shader identities in upstream caches predate the UI transform. Separate the
    # centered-UI profiles so a cached Windows/16:9 pipeline cannot bypass it.
    profile=RUNTIME/'profiles'/('wide-'+str(output_width(s)) if wide(s) else '16x9')
    profile.mkdir(parents=True,exist_ok=True)
    (profile/'camera-step.txt').write_text(str(s['camera'])+'\n')
    saves=RUNTIME/'_SaveData';saves.mkdir(exist_ok=True)
    link=profile/'_SaveData'
    if not link.exists(): link.symlink_to(saves, target_is_directory=True)
    env=os.environ.copy()
    for k in list(env):
        if k.startswith(('KYTY_','CODEX_UI_','CODEX_CAMERA_')) or k in ['CODEX_AUDIO_MODE','CODEX_SCENE_HEIGHT','SDL_VULKAN_LIBRARY','LD_PRELOAD']:
            del env[k]
    base=json.loads((ROOT/'performance.json').read_text())
    env.update(base['environment']);env['CODEX_CUSTOM_MODS']='1'
    # Upstream accepts this executable fingerprint for caches of modified builds.
    binary=emulator_binary(s)
    with binary.open('rb') as stream: env['KYTY_DRIVER_CACHE_KEY']=hashlib.file_digest(stream,'sha256').hexdigest()
    env['CODEX_CAMERA_STEP']=str(s['camera']);env['CODEX_CAMERA_FARTHER_KEY']=s['farther'];env['CODEX_CAMERA_CLOSER_KEY']=s['closer']
    if wide(s):
        env['CODEX_UI_OUTPUT_WIDTH']=str(output_width(s))
        env['CODEX_SCENE_HEIGHT']=str(math.floor(s['height']*s['render']/100+.5))
    else: env['KYTY_PRESENT_ASPECT']='fit'
    if not wide(s) and s['render']!=100: env['KYTY_RENDER_HEIGHT']=str(max(720,min(1440,round(1440*s['render']/100))))
    if s['surround']: env['CODEX_AUDIO_MODE']='5.1'
    if s['sink']: env['PULSE_SINK']=s['sink'];env['SDL_AUDIODRIVER']='pulseaudio'
    for key,name in [('TMPDIR','tmp'),('XDG_CONFIG_HOME','config'),('XDG_CACHE_HOME','cache')]:
        folder=RUNTIME/name;folder.mkdir(exist_ok=True);env[key]=str(folder)
    return profile,env

def readonly_command(exe,args):
    return ['bwrap','--die-with-parent','--ro-bind','/','/','--bind',str(ROOT),str(ROOT),'--dev-bind','/dev','/dev','--proc','/proc','--',str(exe)]+list(map(str,args))

def game_command(s):
    profile,env=prepare(s)
    args=['--game',s['game'],'--screen-width',str(s['width']),'--screen-height',str(s['height']),'--console-language',str(s['language']),'--shader-validation','false','--shader-log-direction','Silent','--printf-direction','Silent','--vblank-frequency','60','--present-mode','Immediate']
    if s['fullscreen']: args+=['--fullscreen']
    if wide(s):
        p=profile/'ultrawide.json';p.write_text(json.dumps(patch(s),indent=2)+'\n');args+=['--game-patch',str(p)]
    return readonly_command(emulator_binary(s),args),profile,env

def precompile_commands(s):
    profile,env=prepare(s)
    return [[sys.executable,str(ROOT/'custom-launcher/precompile-job.py')]],profile,env

def sinks():
    try:
        default=subprocess.check_output(['pactl','get-default-sink'],text=True,timeout=3).strip()
        entries=json.loads(subprocess.check_output(['pactl','--format=json','list','sinks'],text=True,timeout=3))
        return default,[(e['name'],e.get('description',e['name']),e.get('channel_map','')) for e in entries]
    except (OSError,ValueError,subprocess.SubprocessError): return '',[]

def export_save(destination):
    root=RUNTIME/'_SaveData'
    if not (root/TITLE).is_dir(): raise ValueError('No Linux save exists yet.')
    with zipfile.ZipFile(destination,'w',zipfile.ZIP_DEFLATED) as archive:
        for rel in [Path(TITLE),Path('.kyty-capacity')/TITLE]:
            for f in (root/rel).rglob('*'):
                if f.is_file() and not f.is_symlink(): archive.write(f,f.relative_to(root))

def import_save(source):
    root=RUNTIME/'_SaveData';root.mkdir(exist_ok=True)
    with zipfile.ZipFile(source) as z:
        entries=[]
        for info in z.infolist():
            name=PurePosixPath(info.filename.replace('\\','/'))
            allowed=name.parts[:1]==(TITLE,) or name.parts[:2]==('.kyty-capacity',TITLE)
            if name.is_absolute() or '..' in name.parts or not allowed or (info.external_attr>>16)&0o170000==0o120000:
                raise ValueError('The archive contains an unexpected save path.')
            if not info.is_dir(): entries.append((info,name))
        if not any('USR-DATA' in str(n).upper() for _,n in entries): raise ValueError('No Demon’s Souls USR-DATA save was found.')
        if sum(i.file_size for i,_ in entries)>2*1024**3: raise ValueError('Save archive is too large.')
        with tempfile.TemporaryDirectory(dir=RUNTIME) as temp:
            stage=Path(temp)
            for info,name in entries:
                dest=stage/str(name);dest.parent.mkdir(parents=True,exist_ok=True)
                with z.open(info) as inp,dest.open('wb') as out: shutil.copyfileobj(inp,out)
            backup=root/'_backups'/datetime.datetime.now().strftime('%Y%m%d-%H%M%S-%f')
            for rel in [Path(TITLE),Path('.kyty-capacity')/TITLE]:
                dest=root/rel
                if dest.exists():
                    b=backup/rel;b.parent.mkdir(parents=True,exist_ok=True);shutil.copytree(dest,b)
                if (stage/rel).exists():
                    if dest.exists(): shutil.rmtree(dest)
                    dest.parent.mkdir(parents=True,exist_ok=True);shutil.move(stage/rel,dest)
    return backup
