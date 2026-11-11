"""Run with QT_QPA_PLATFORM=offscreen python3 tests/test_launcher.py."""
import json,os,sys,tempfile
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'custom-launcher'))
from launcher import B,Launcher,W,C,STYLE
from PySide6.QtTest import QTest
app=W.QApplication([]);app.setStyleSheet(STYLE)
with tempfile.TemporaryDirectory() as temp:
 root=Path(temp);game=root/'game';(game/'sce_sys').mkdir(parents=True)
 (game/'eboot.bin').write_bytes(b'test fixture, not a game')
 (game/'sce_sys/param.json').write_text(json.dumps(dict(titleId='PPSA01341',contentVersion='01.007.000')))
 native=root/'native';native.mkdir();(native/'kyty_emulator').symlink_to(sys.executable)
 B.RUNTIME=root/'runtime';B.RUNTIME.mkdir();B.SETTINGS=B.RUNTIME/'settings.json'
 B.save(B.DEFAULTS|{'kyty':str(native),'game':str(game)})
 w=Launcher();w.game_active=lambda:False;w.show();app.processEvents()
 assert w.pages.currentIndex()==0 and w.size()==C.QSize(1280,720)
 w.open_settings();assert w.pages.currentIndex()==1 and not w.resolution.isEditable()
 assert w.audio.count()==2 and w.kyty.isReadOnly()
 w.show_tab('Camera');w.capture_key('farther');QTest.keyClick(w.farther,C.Qt.Key_F6)
 assert w.draft_keys['farther']=='F6'
 w.capture_key('closer');QTest.keyClick(w.closer,C.Qt.Key_F6);assert w.capture=='closer'
 QTest.keyClick(w.closer,C.Qt.Key_Escape);assert w.capture is None
 w.open_saves();assert w.pages.currentIndex()==2;w.back();assert w.pages.currentIndex()==1
 w.back();assert w.pages.currentIndex()==0
 w.open_settings();w.language.setCurrentIndex(4);w.back();w.open_settings();assert w.language.currentIndex()==1
 w.language.setCurrentIndex(4);w.save_settings();assert w.pages.currentIndex()==0 and B.load()['language']==4
 w.open_settings(True);assert w.save_button.text()=='Save and run' and w.manage.isHidden()
 calls=[];w.start_game=lambda:calls.append(True);w.save_settings();assert calls==[True]
 sample=json.loads((ROOT/'ultrawide-3440x1440.json').read_text())
 assert B.patch(B.DEFAULTS)['mods'][0]['memory']==sample['mods'][0]['memory']
 assert not list(B.RUNTIME.rglob('*.seeds'))
 w.close()
print('PASS: original-style navigation, modes, keys, draft/save behavior, initial setup and sample patch')
