#!/usr/bin/env python3
"""Native version of the release's sharded precompile workflow."""
import collections,datetime,fcntl,hashlib,json,math,os,shutil,signal,struct,subprocess,time,traceback
from pathlib import Path
import backend as b
status=b.RUNTIME/'precompile-status.json'
started=datetime.datetime.now().isoformat();running={};gate=None
logdir=b.RUNTIME/'logs'/('precompile-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S'))
logdir.mkdir(parents=True,exist_ok=True)
def update(**extra):
 data=dict(started=started,pid=os.getpid(),log=str(logdir),**extra)
 tmp=status.with_suffix('.tmp');tmp.write_text(json.dumps(data,indent=2)+'\n');tmp.replace(status)
 if extra.get('phase')!='pipelines' or extra.get('done',0)%10==0:print(json.dumps(extra),flush=True)
def count_recipes(path):
 with path.open('rb') as f:
  f.readline();f.read(8);records=struct.unpack('<I',f.read(4))[0]
  for _ in range(records):n=struct.unpack('<I',f.read(4))[0];f.seek(n*4,1)
  return struct.unpack('<I',f.read(4))[0]
def stop(signum,frame):raise InterruptedError('Precompile stopped; completed batches can be reused.')
signal.signal(signal.SIGTERM,stop);signal.signal(signal.SIGINT,stop)
try:
 gate=(b.RUNTIME/'precompile.lock').open('a')
 try:fcntl.flock(gate,fcntl.LOCK_EX|fcntl.LOCK_NB)
 except BlockingIOError:raise SystemExit('A shader precompile is already running.')
 s=b.load();upstream=b.source_folder(s);cwd,env=b.prepare(s);exe=b.binary_folder(s)/'kyty_shader_precompile';game=s['game']
 seeds=cwd/'game.seeds';hinted=cwd/'hinted.seeds';plan=logdir/'plan.bin'
 workers=max(1,min(4,int(s['threads'])//3));threads=3
 def start(name,args,claims=False):
  cache=logdir/'driver-temp'/name;cache.mkdir(parents=True,exist_ok=True)
  childenv=env.copy();childenv['__GL_SHADER_DISK_CACHE_PATH']=str(cache)
  if claims:childenv['KYTY_PRECOMPILE_CLAIMS']=str(logdir/'claims')
  logfile=(logdir/(name+'.log')).open('w')
  command=b.readonly_command(exe,['--game',game]+list(map(str,args)))
  p=subprocess.Popen(command,cwd=cwd,env=childenv,stdin=subprocess.DEVNULL,stdout=logfile,stderr=subprocess.STDOUT)
  running[p.pid]=(p,logfile,cache,name)
  return p
 def finish(p):
  entry=running.pop(p.pid);entry[1].close();shutil.rmtree(entry[2],ignore_errors=True);return p.returncode
 def run(name,args):
  p=start(name,args);code=p.wait();finish(p)
  if code:raise RuntimeError(f'{name} exited {code}; see {logdir/name}.log')
 update(state='running',phase='preparing')
 if not seeds.exists():run('extract-seeds',['--make-seeds',seeds,'--states',upstream/'tools/local/static-precompile/pass-states.json'])
 run('apply-hints',['--seeds',seeds,'--apply-hints',upstream/'tools/local/static-precompile/hints-PPSA01341_01.007.000.hints','--out',hinted])
 run('merge-existing',['--merge'])
 for name,path in [('base',seeds),('hinted',hinted)]:
  update(state='running',phase='planning',seed=name)
  run('plan-'+name,['--seeds',path,'--dry-run','--plan',plan,'--threads',s['threads']])
 pending=collections.deque()
 for name,path,n in [('base',seeds,512),('hinted',hinted,max(1,math.ceil(count_recipes(hinted)/100)))]:
  for i in range(n):pending.append((name,path,i,n))
 jobs={};done=0;splits=0;last=0
 update(state='running',phase='pipelines',done=done,running=0,waiting=len(pending),workers=workers)
 while pending or jobs:
  while pending and len(jobs)<workers:
   name,path,i,n=pending.popleft();label=f'{name}-{i}of{n}'
   p=start(label,['--seeds',path,'--shard',f'{i}/{n}','--threads',threads,'--plan',plan],True)
   jobs[p.pid]=(p,name,path,i,n)
  time.sleep(.25)
  for pid,(p,name,path,i,n) in list(jobs.items()):
   if p.poll() is None:continue
   code=finish(p);del jobs[pid]
   if code==3:
    if n>=65536:raise RuntimeError('A shader batch could not be split further.')
    pending.extend([(name,path,i,n*2),(name,path,i+n,n*2)]);splits+=1
   elif code:raise RuntimeError(f'{name}-{i}of{n} failed ({code})')
   else:done+=1
  if time.monotonic()-last>5:
   update(state='running',phase='pipelines',done=done,running=len(jobs),waiting=len(pending),splits=splits,workers=workers);last=time.monotonic()
 update(state='running',phase='merging',done=done)
 run('merge-final',['--merge','--prune'])
 update(state='running',phase='shader-inputs',done=done)
 run('shader-inputs',['--seeds',seeds,'--no-pipelines','--static-inputs','--threads',s['threads']])
 update(state='complete',finished=datetime.datetime.now().isoformat(),done=done,splits=splits)
except BaseException as e:
 for p,log,cache,name in list(running.values()):
  if p.poll() is None:p.terminate()
 for p,log,cache,name in list(running.values()):
  try:p.wait(timeout=10)
  except subprocess.TimeoutExpired:p.kill();p.wait()
  log.close();shutil.rmtree(cache,ignore_errors=True)
 if gate is not None and not isinstance(e,SystemExit):update(state='stopped' if isinstance(e,InterruptedError) else 'failed',error=str(e),finished=datetime.datetime.now().isoformat())
 traceback.print_exc();raise
