#!/usr/bin/env python3
"""Install a menu entry for this unpacked mod; no game artwork is copied."""
import os,subprocess,sys
from pathlib import Path
root=Path(__file__).resolve().parent
script=str(root/'run-custom-launcher.sh')
if any(c in script for c in '\n\r'):raise SystemExit('The installation path must not contain line breaks.')
quoted='"'+script.replace('\\','\\\\').replace('"','\\"').replace('`','\\`').replace('$','\\$').replace('%','%%')+'"'
apps=Path(os.environ.get('XDG_DATA_HOME',str(Path.home()/'.local/share')))/'applications'
apps.mkdir(parents=True,exist_ok=True)
desktop=apps/'demons-souls-ultrawide-linux.desktop'
python_arg='"PYTHON='+sys.executable.replace('\\','\\\\').replace('"','\\"').replace('$','\\$').replace('`','\\`').replace('%','%%')+'"'
desktop.write_text('[Desktop Entry]\nType=Application\nName=Demon\'s Souls Linux\nComment=Ultrawide mod launcher for native KytyPS5\nExec=env '+python_arg+' '+quoted+'\nIcon=applications-games\nTerminal=false\nCategories=Game;Emulator;\nKeywords=PS5;Kyty;Demons;Souls;Ultrawide;\nStartupNotify=true\n')
try:subprocess.run(['update-desktop-database',str(apps)],check=False,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
except FileNotFoundError:pass
print('Created:',desktop)
