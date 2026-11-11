#!/usr/bin/env python3
"""Keep a launched game alive independently of the launcher window."""
import json,os,signal,subprocess,sys,time
from pathlib import Path
spec_path=Path(sys.argv[1]);spec=json.loads(spec_path.read_text());state_path=spec_path.with_suffix('.state.json')
def state(**values):
 tmp=state_path.with_suffix('.tmp');tmp.write_text(json.dumps(dict(supervisor=os.getpid(),kind=spec['kind'],**values)));tmp.replace(state_path)
stop_at=None;child=None
expected=spec.get('executable','')
def descendants(pid):
 try:
  ids=[int(x) for x in Path(f'/proc/{pid}/task/{pid}/children').read_text().split()]
 except OSError:return []
 return ids+[nested for p in ids for nested in descendants(p)]
def stop(sig,frame):
 global stop_at
 if child is None:return
 if sig==signal.SIGUSR2:
  try:os.killpg(child.pid,signal.SIGKILL)
  except ProcessLookupError:pass
  return
 # Signal the actual emulator, allowing SDL to turn SIGTERM into its quit event.
 for pid in [child.pid]+descendants(child.pid):
  try:
   if os.readlink(f'/proc/{pid}/exe')==expected:os.kill(pid,signal.SIGTERM)
  except (OSError,ProcessLookupError):pass
 stop_at=time.monotonic()+3
signal.signal(signal.SIGUSR1,stop);signal.signal(signal.SIGUSR2,stop)
try:
 with open(spec['log'],'a') as log:
  child=subprocess.Popen(spec['command'],cwd=spec['cwd'],env=spec['environment'],stdin=subprocess.DEVNULL,stdout=log,stderr=subprocess.STDOUT,start_new_session=True)
  state(state='running',child=child.pid)
  while child.poll() is None:
   if stop_at and time.monotonic()>stop_at:
    try:os.killpg(child.pid,signal.SIGKILL)
    except ProcessLookupError:pass
   time.sleep(.15)
  state(state='finished',exit=child.returncode)
except BaseException as error:
 state(state='failed',error=str(error));raise
