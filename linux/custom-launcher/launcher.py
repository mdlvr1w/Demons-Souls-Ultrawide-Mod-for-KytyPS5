#!/usr/bin/env python3
"""Native presentation of the original Demon's Souls launcher."""
import datetime,json,os,re,signal,subprocess,sys
from pathlib import Path
from PySide6 import QtCore as C,QtGui as G,QtWidgets as W
import backend as B

STYLE='''
QWidget {color:#F3EDE2;font-family:"Segoe UI","DejaVu Sans";font-size:14px;background:transparent;}
QPushButton {background:rgba(255,255,255,34);border:1px solid rgba(198,161,91,102);border-radius:2px;padding:8px 16px;}
QPushButton:hover {background:rgba(255,255,255,51);}
QPushButton:disabled {color:rgba(243,237,226,102);border-color:rgba(198,161,91,40);}
QPushButton[gold="true"] {background:#C6A15B;color:#1A140C;border:none;font-family:"Georgia","DejaVu Serif";font-size:22px;}
QPushButton[gold="true"]:hover {background:#D4B36A;}
QPushButton[gold="true"]:disabled {background:#5C5346;color:#aaa;}
QPushButton[tab="true"] {color:#B7AA98;background:transparent;}
QPushButton[tab="true"]:checked {color:#C6A15B;background:rgba(198,161,91,51);border-color:#C6A15B;}
QLineEdit,QComboBox {background:rgba(28,24,20,230);border:1px solid rgba(198,161,91,68);border-radius:0px;padding:5px 8px;min-height:22px;}
QComboBox QAbstractItemView {background:#1C1814;color:#F3EDE2;selection-background-color:#59492f;border:1px solid #8C6A32;}
QComboBox::drop-down {border:none;width:26px;}
QLabel[muted="true"] {color:#B7AA98;}
QLabel[hint="true"] {color:#887A6C;font-size:12px;}
QLabel#title {font-family:"Georgia","DejaVu Serif";font-size:40px;}
QLabel#heading {font-family:"Georgia","DejaVu Serif";font-size:28px;}
QScrollArea,QScrollArea>QWidget>QWidget {background:transparent;border:none;}
QScrollBar:vertical {background:transparent;width:8px;margin:0;}
QScrollBar::handle:vertical {background:rgba(198,161,91,136);border-radius:4px;min-height:24px;}
QScrollBar::add-line:vertical,QScrollBar::sub-line:vertical {height:0;}
QScrollBar::add-page:vertical,QScrollBar::sub-page:vertical {background:transparent;}
QSlider::groove:horizontal {height:4px;background:rgba(255,255,255,51);}
QSlider::sub-page:horizontal {background:#C6A15B;}
QSlider::handle:horizontal {width:12px;margin:-6px 0;background:#C6A15B;border-radius:2px;}
QProgressBar {border:none;background:rgba(255,255,255,51);height:18px;}
QProgressBar::chunk {background:#C6A15B;}
QMessageBox,QFileDialog {background:#1C1814;}
'''

def label(text='',kind=None):
 w=W.QLabel(text)
 if kind:w.setProperty(kind,True)
 w.setWordWrap(True)
 return w

def button(text,slot=None,gold=False):
 w=W.QPushButton(text);w.setCursor(C.Qt.PointingHandCursor)
 if slot:w.clicked.connect(slot)
 if gold:w.setProperty('gold',True)
 return w

class Backdrop(W.QWidget):
 def __init__(self):super().__init__();self.image=G.QPixmap()
 def paintEvent(self,event):
  p=G.QPainter(self);p.fillRect(self.rect(),G.QColor('#0B0B0C'))
  if not self.image.isNull():
   image=self.image.scaled(self.size(),C.Qt.KeepAspectRatioByExpanding,C.Qt.SmoothTransformation)
   p.drawPixmap((self.width()-image.width())//2,(self.height()-image.height())//2,image)
  g=G.QLinearGradient(0,0,self.width(),self.height())
  for stop,alpha in [(0,230),(.45,153),(1,204)]:g.setColorAt(stop,G.QColor(11,11,12,alpha))
  p.fillRect(self.rect(),g)
 def mousePressEvent(self,event):
  if event.button()==C.Qt.LeftButton and not self.window().isMaximized() and self.window().windowHandle():
   self.window().windowHandle().startSystemMove()

class Launcher(W.QMainWindow):
 def __init__(self):
  super().__init__();self.s=B.load();self.capture=None;self.prompt_run=False;self.page_name='home';self.session=None;self.profile=None;self.quitting=False;self.last_shader_state=None
  self.setWindowFlags(C.Qt.Window|C.Qt.FramelessWindowHint);self.setFixedSize(1280,720)
  self.body=Backdrop();self.setCentralWidget(self.body)
  outer=W.QVBoxLayout(self.body);outer.setContentsMargins(48,28,36,32);outer.setSpacing(0)
  header=W.QHBoxLayout();header.setSpacing(22)
  self.tile=W.QLabel();self.tile.setFixedSize(92,92);self.tile.setStyleSheet('border:1px solid rgba(198,161,91,102);background:rgba(0,0,0,34);');header.addWidget(self.tile)
  names=W.QVBoxLayout();names.setSpacing(6);names.setAlignment(C.Qt.AlignVCenter);self.title=label("Demon’s Souls");self.title.setObjectName('title');self.detail=label('', 'muted');names.addWidget(self.title);names.addWidget(self.detail);header.addLayout(names,1)
  controls=W.QHBoxLayout();controls.setSpacing(8)
  for text,slot in [('—',self.showMinimized),('□',self.toggle_maximize),('×',self.close)]:
   b=button(text,slot);b.setFixedSize(36,28);b.setStyleSheet('padding:0;');controls.addWidget(b)
   if text=='□':self.maximize_button=b
  header.addLayout(controls);header.setAlignment(controls,C.Qt.AlignTop);outer.addLayout(header)
  self.pages=W.QStackedWidget();outer.addWidget(self.pages,1)
  self.make_home();self.make_settings();self.make_saves();self.refresh_art();self.load_session();self.to_home()
  self.timer=C.QTimer(self);self.timer.timeout.connect(self.poll);self.timer.start(1000);self.poll()
  W.QApplication.instance().installEventFilter(self)
  self.escape=G.QShortcut(G.QKeySequence('Escape'),self);self.escape.activated.connect(self.back)
  screen=W.QApplication.primaryScreen()
  if screen:self.move(screen.availableGeometry().center()-self.rect().center())
 def make_home(self):
  page=W.QWidget();v=W.QVBoxLayout(page);v.setContentsMargins(0,0,0,0);v.setSpacing(0);v.addStretch()
  self.play=button('Play',self.play_clicked,True);self.play.setFixedSize(220,58);v.addWidget(self.play,0,C.Qt.AlignLeft);v.addSpacing(16)
  v.addWidget(button('Settings',self.open_settings),0,C.Qt.AlignLeft);v.addSpacing(14)
  self.status=label('', 'muted');v.addWidget(self.status);self.pages.addWidget(page)
 def make_settings(self):
  page=W.QWidget();v=W.QVBoxLayout(page);v.setContentsMargins(0,28,0,0);v.setSpacing(0)
  heading=label('Settings');heading.setObjectName('heading');v.addWidget(heading);v.addSpacing(18)
  tabs=W.QHBoxLayout();tabs.setSpacing(8);self.tab_buttons={}
  for name in ['General','Picture','Audio','Camera','Shaders']:
   b=button(name,lambda checked=False,n=name:self.show_tab(n));b.setProperty('tab',True);b.setCheckable(True);tabs.addWidget(b);self.tab_buttons[name]=b
  tabs.addStretch();v.addLayout(tabs);v.addSpacing(16)
  scroll=W.QScrollArea();scroll.setWidgetResizable(True);scroll.setHorizontalScrollBarPolicy(C.Qt.ScrollBarAlwaysOff)
  self.tab_stack=W.QStackedWidget();scroll.setWidget(self.tab_stack);v.addWidget(scroll,1)
  self.forms={}
  for name in self.tab_buttons:
   panel=W.QWidget();form=W.QVBoxLayout(panel);form.setContentsMargins(0,0,0,0);form.setSpacing(8);self.forms[name]=form;self.tab_stack.addWidget(panel)
  general=self.forms['General'];self.kyty=W.QLineEdit();self.kyty.setReadOnly(True);self.game=W.QLineEdit();self.game.setReadOnly(True)
  general.addWidget(self.field('Kyty',self.kyty,button('Browse',lambda:self.choose_folder('kyty'))))
  general.addWidget(self.field('Game',self.game,button('Browse',lambda:self.choose_folder('game'))))
  self.language=W.QComboBox();self.language.addItems(B.LANGUAGES);general.addWidget(self.field('Language',self.language));general.addStretch()
  picture=self.forms['Picture'];self.resolution=W.QComboBox();self.resolution.setEditable(False);self.resolution.currentIndexChanged.connect(self.picture_label);picture.addWidget(self.field('Resolution',self.resolution))
  picture.addWidget(label('16:9 fills the window. A wider screen keeps the interface at 16:9 in the center.','hint'))
  self.render=W.QSlider(C.Qt.Horizontal);self.render.setRange(50,100);self.render.valueChanged.connect(self.picture_label);self.render_label=label();self.render_label.setFixedWidth(220);self.render_label.setAlignment(C.Qt.AlignRight|C.Qt.AlignVCenter)
  self.render_row=self.field('Render',self.render,self.render_label);picture.addWidget(self.render_row);self.render_hint=label('The 3D picture only. The window and the HUD stay full size.','hint');picture.addWidget(self.render_hint);picture.addStretch()
  audio=self.forms['Audio'];self.audio=W.QComboBox();self.audio.addItems(['Stereo','5.1']);audio.addWidget(self.field('Output',self.audio));audio.addWidget(label("5.1 sends the game's directional sound to six speakers. Stereo is the emulator's normal mix. Start the game again after changing it.",'hint'));audio.addStretch()
  camera=self.forms['Camera'];camera.addWidget(label('The original distance is as close as it gets. Four steps is as far as it goes.','hint'));self.farther=button('',lambda:self.capture_key('farther'));self.closer=button('',lambda:self.capture_key('closer'))
  camera.addWidget(self.field('Farther',self.farther));camera.addWidget(self.field('Closer',self.closer));camera.addStretch()
  shaders=self.forms['Shaders'];self.compile=button('Precompile shaders',self.precompile);shaders.addWidget(self.compile,0,C.Qt.AlignLeft);shaders.addSpacing(6)
  row=W.QHBoxLayout();row.setSpacing(12);self.shader_bar=W.QProgressBar();self.shader_bar.setTextVisible(False);self.shader_bar.setFixedHeight(18);row.addWidget(self.shader_bar,1);self.shader_label=label('');self.shader_label.setFixedWidth(140);self.shader_label.setAlignment(C.Qt.AlignRight|C.Qt.AlignVCenter);row.addWidget(self.shader_label);shaders.addLayout(row);shaders.addStretch()
  v.addSpacing(22);footer=W.QHBoxLayout();footer.setSpacing(10);self.save_button=button('Save',self.save_settings,True);self.save_button.setFixedSize(220,48);self.save_button.setStyleSheet('font-size:18px;');footer.addWidget(self.save_button)
  self.manage=button('Manage savegames',self.open_saves);footer.addWidget(self.manage);footer.addWidget(button('Back',self.back));footer.addStretch();v.addLayout(footer);v.addSpacing(14);self.settings_status=label('', 'muted');v.addWidget(self.settings_status);self.pages.addWidget(page)
 def make_saves(self):
  page=W.QWidget();v=W.QVBoxLayout(page);v.setContentsMargins(0,28,0,0);v.setSpacing(0);heading=label('Manage savegames');heading.setObjectName('heading');v.addWidget(heading);v.addSpacing(22)
  row=W.QHBoxLayout();row.setSpacing(10);self.export_button=button('Export save',self.export);self.import_button=button('Import save',self.restore);row.addWidget(self.export_button);row.addWidget(self.import_button);row.addWidget(button('Back',self.back));row.addStretch();v.addLayout(row);v.addSpacing(14);self.saves_status=label('', 'muted');v.addWidget(self.saves_status);v.addStretch();self.pages.addWidget(page)
 def field(self,name,control,extra=None):
  widget=W.QWidget();row=W.QHBoxLayout(widget);row.setContentsMargins(0,0,0,0);row.setSpacing(10);caption=label(name,'muted');caption.setFixedWidth(80);row.addWidget(caption);row.addWidget(control,1)
  if extra:row.addWidget(extra)
  if isinstance(control,W.QPushButton):
   control.setMinimumWidth(160);control.setMaximumWidth(180);row.setStretch(1,0);row.addStretch(1)
  return widget
 def refresh_art(self):
  game=Path(self.s['game']);title="Demon's Souls";detail=''
  try:
   p=json.loads((game/'sce_sys/param.json').read_text());names=p.get('localizedParameters',{});title=names.get(names.get('defaultLanguage','en-GB'),{}).get('titleName',title);detail='   ·   '.join(filter(None,[p.get('titleId'),p.get('contentVersion')]))
  except (OSError,ValueError):pass
  self.title.setText(title);self.detail.setText(detail);self.setWindowTitle(title);self.body.image=G.QPixmap(str(game/'sce_sys/pic0.png'));self.body.update()
  icon=G.QPixmap(str(game/'sce_sys/icon0.png'));self.setWindowIcon(G.QIcon(icon));ratio=self.devicePixelRatioF();scaled=icon.scaled(round(90*ratio),round(90*ratio),C.Qt.KeepAspectRatioByExpanding,C.Qt.SmoothTransformation);scaled.setDevicePixelRatio(ratio);self.tile.setPixmap(scaled)
 def toggle_maximize(self):
  if self.isMaximized():self.showNormal();self.setFixedSize(1280,720);self.maximize_button.setText('□')
  else:self.setMaximumSize(16777215,16777215);self.showMaximized();self.maximize_button.setText('❐')
 def to_home(self):self.prompt_run=False;self.capture=None;self.page_name='home';self.pages.setCurrentIndex(0)
 def open_settings(self,prompt=False):
  self.prompt_run=bool(prompt);self.kyty.setText(self.s.get('kyty',str(B.ROOT/'_Build/linux')));self.game.setText(self.s['game']);self.language.setCurrentIndex(self.s['language']);self.audio.setCurrentIndex(int(self.s['surround']));self.render.setValue(self.s['render']);self.draft_keys={k:self.s[k] for k in ['farther','closer']};self.capture=None;self.show_keys();self.fill_resolutions();self.settings_status.clear();self.save_button.setText('Save and run' if prompt else 'Save')
  for w in [self.render_row,self.render_hint,self.manage,self.tab_buttons['Camera'],self.tab_buttons['Shaders']]:w.setVisible(not prompt)
  selected=getattr(self,'selected_tab','General')
  if prompt and selected in ['Camera','Shaders']:selected='General'
  self.show_tab(selected);self.page_name='settings';self.pages.setCurrentIndex(1)
 def open_saves(self):self.page_name='saves';self.saves_status.clear();self.pages.setCurrentIndex(2);self.poll()
 def back(self):
  if self.capture:self.capture=None;self.show_keys();self.settings_status.clear();return
  if self.page_name=='saves':self.page_name='settings';self.pages.setCurrentIndex(1)
  else:self.to_home()
 def show_tab(self,name):
  self.selected_tab=name;self.tab_stack.setCurrentIndex(list(self.tab_buttons).index(name))
  for n,b in self.tab_buttons.items():b.setChecked(n==name)
 def choose_folder(self,which):
  control=self.kyty if which=='kyty' else self.game
  value=W.QFileDialog.getExistingDirectory(self,('Kyty folder' if which=='kyty' else 'Game folder'),control.text())
  if value:control.setText(value)
 def fill_resolutions(self):
  modes={(1920,1080),(3440,1440),(self.s['width'],self.s['height'])}
  try:
   data=json.loads(subprocess.check_output(['kscreen-doctor','-j'],text=True,timeout=3))
   outputs=[o for o in data['outputs'] if o.get('connected') and o.get('enabled')];outputs.sort(key=lambda o:o.get('priority',999))
   if outputs:
    for mode in outputs[0].get('modes',[]):modes.add((mode['size']['width'],mode['size']['height']))
  except (OSError,ValueError,KeyError,subprocess.SubprocessError):
   screen=W.QApplication.primaryScreen()
   if screen:modes.add((screen.size().width(),screen.size().height()))
  self.resolution.blockSignals(True);self.resolution.clear()
  for w,h in sorted(modes):
   if w>=1280 and h>=720 and w/h>=16/9-.02:self.resolution.addItem(f'{w}×{h}',(w,h))
  index=self.resolution.findData((self.s['width'],self.s['height']));self.resolution.setCurrentIndex(max(0,index));self.resolution.blockSignals(False);self.picture_label()
 def picture_label(self):
  size=self.resolution.currentData()
  if size:
   w,h=size;p=self.render.value();self.render_label.setText(f'{p}%   ·   {round(w*p/100)}×{round(h*p/100)}')
 def show_keys(self):
  for key,control in [('farther',self.farther),('closer',self.closer)]:
   name=self.draft_keys.get(key,self.s[key]);control.setText('Press a key' if self.capture==key else {'PageUp':'Page Up','PageDown':'Page Down'}.get(name,name))
 def capture_key(self,which):self.capture=which;self.settings_status.setText('Press a key. Escape cancels.');self.show_keys()
 def eventFilter(self,watched,event):
  if self.capture and event.type()==C.QEvent.KeyPress:
   self.keyPressEvent(event);return True
  return super().eventFilter(watched,event)
 def keyPressEvent(self,event):
  if self.capture:
   key=event.key()
   if key==C.Qt.Key_Escape:self.back();return
   if key in [C.Qt.Key_Shift,C.Qt.Key_Control,C.Qt.Key_Alt,C.Qt.Key_Meta]:return
   mapping={C.Qt.Key_PageUp:'PageUp',C.Qt.Key_PageDown:'PageDown',C.Qt.Key_Space:'Space',C.Qt.Key_Return:'Return'};name=mapping.get(key,G.QKeySequence(key).toString())
   if name==self.draft_keys['closer' if self.capture=='farther' else 'farther']:self.settings_status.setText('That key is already used.');return
   self.draft_keys[self.capture]=name;self.capture=None;self.settings_status.clear();self.show_keys();return
  super().keyPressEvent(event)
 def collect(self):
  size=self.resolution.currentData()
  if not size:raise ValueError('Choose a resolution of 16:9 or wider.')
  s=self.s|dict(kyty=self.kyty.text(),game=self.game.text(),width=size[0],height=size[1],render=self.render.value(),language=self.language.currentIndex(),surround=self.audio.currentIndex()==1,farther=self.draft_keys['farther'],closer=self.draft_keys['closer'],fullscreen=True)
  B.validate(s);return s
 def commit(self,s):self.s=s;B.save(s);self.refresh_art()
 def save_settings(self):
  try:
   s=self.collect();run=self.prompt_run;self.commit(s);self.to_home()
   if run:self.start_game()
  except (OSError,ValueError,KeyError) as e:self.settings_status.setText(str(e))
 def game_active(self):
  try:
   expected=str(B.emulator_binary(self.s).resolve())
   return any(self.read_exe(p)==expected for p in Path('/proc').iterdir() if p.name.isdigit())
  except OSError:return False
 @staticmethod
 def read_exe(p):
  try:return os.readlink(p/'exe')
  except OSError:return ''
 def load_session(self):
  self.session=None
  for spec in sorted((B.RUNTIME/'sessions').glob('*.json'),reverse=True):
   if spec.name.endswith('.state.json'):continue
   try:
    state=json.loads(spec.with_suffix('.state.json').read_text());pid=state['supervisor'];command=Path(f'/proc/{pid}/cmdline').read_bytes()
    if state.get('state')=='running' and str(B.ROOT/'custom-launcher/session-runner.py').encode() in command:self.session=spec;self.profile=Path(json.loads(spec.read_text())['cwd']);return
   except (OSError,ValueError,KeyError):pass
 def start_session(self,command,cwd,env):
  directory=B.RUNTIME/'sessions';directory.mkdir(exist_ok=True);logs=B.RUNTIME/'logs';logs.mkdir(exist_ok=True);stamp=datetime.datetime.now().strftime('%Y%m%d-%H%M%S-%f');spec=directory/(stamp+'.json')
  data=dict(kind='game',command=command,cwd=str(cwd),environment=env,executable=str(B.emulator_binary(self.s).resolve()),log=str(logs/('game-'+stamp+'.log')));spec.write_text(json.dumps(data));spec.chmod(0o600)
  with open(data['log'],'a') as log:subprocess.Popen([sys.executable,str(B.ROOT/'custom-launcher/session-runner.py'),str(spec)],stdin=subprocess.DEVNULL,stdout=log,stderr=log,start_new_session=True)
  self.session=spec;self.profile=cwd;self.quitting=False
 def play_clicked(self):
  if self.game_active() or self.session_running():self.quit_game();return
  try:B.validate(self.s)
  except (OSError,ValueError):self.open_settings(True);return
  self.start_game()
 def start_game(self):
  try:
   if self.shader_running():self.status.setText('Wait for shader preparation to finish before playing.');return
   command,cwd,env=B.game_command(self.s);self.start_session(command,cwd,env);self.status.setText(f"{self.s['width']}×{self.s['height']} started.");self.play.setText('Quit game')
  except (OSError,ValueError,KeyError) as e:self.status.setText(str(e))
 def session_running(self):
  if not self.session:return False
  try:
   state=json.loads(self.session.with_suffix('.state.json').read_text())
   return state.get('state')=='running' and Path(f"/proc/{state['supervisor']}").exists()
  except (OSError,ValueError,KeyError):return (datetime.datetime.now().timestamp()-self.session.stat().st_mtime)<5
 def quit_game(self):
  try:
   state=json.loads(self.session.with_suffix('.state.json').read_text());pid=state['supervisor']
   if str(B.ROOT/'custom-launcher/session-runner.py').encode() in Path(f'/proc/{pid}/cmdline').read_bytes():os.kill(pid,signal.SIGUSR2 if self.quitting else signal.SIGUSR1)
  except (OSError,ValueError,KeyError,AttributeError):
   for p in Path('/proc').iterdir():
    if p.name.isdigit() and self.read_exe(p)==str(B.emulator_binary(self.s).resolve()):
     try:os.kill(int(p.name),signal.SIGKILL if self.quitting else signal.SIGTERM)
     except ProcessLookupError:pass
  self.quitting=True;self.status.setText('The game is closing.')
 def shader_state(self):
  try:return json.loads((B.RUNTIME/'precompile-status.json').read_text())
  except (OSError,ValueError):return {}
 def shader_running(self):
  state=self.shader_state()
  try:return state.get('state')=='running' and str(B.ROOT/'custom-launcher/precompile-job.py').encode() in Path(f"/proc/{state['pid']}/cmdline").read_bytes()
  except (OSError,KeyError):return False
 def precompile(self):
  if self.game_active():self.settings_status.setText('Close the game before precompiling shaders.');return
  if self.shader_running():return
  try:
   s=self.collect();self.commit(s);logs=B.RUNTIME/'logs';logs.mkdir(exist_ok=True)
   with (logs/'precompile-job.log').open('a') as log:subprocess.Popen([sys.executable,str(B.ROOT/'custom-launcher/precompile-job.py')],stdin=subprocess.DEVNULL,stdout=log,stderr=log,start_new_session=True)
   self.shader_bar.setRange(0,0);self.shader_label.setText('Preparing');self.settings_status.setText('Preparing shaders.');self.compile.setEnabled(False)
  except (OSError,ValueError,KeyError) as e:self.settings_status.setText(str(e))
 def poll(self):
  active=self.game_active() or self.session_running();self.play.setText('Quit game' if active else 'Play')
  if not active:self.quitting=False
  self.export_button.setEnabled(not active);self.import_button.setEnabled(not active)
  if active and self.page_name=='saves':self.saves_status.setText('Close the game before copying a save.')
  state=self.shader_state();running=self.shader_running();self.compile.setEnabled(not running and not active)
  if running:
   total=state.get('done',0)+state.get('waiting',0)+state.get('running',0)
   if state.get('phase')=='pipelines' and total:self.shader_bar.setRange(0,100);percent=min(99,state.get('done',0)*100//total);self.shader_bar.setValue(percent);self.shader_label.setText(f'{percent}%')
   else:self.shader_bar.setRange(0,0);self.shader_label.setText('Preparing')
  elif state.get('state')=='complete':self.shader_bar.setRange(0,100);self.shader_bar.setValue(100);self.shader_label.setText('Complete')
  else:self.shader_bar.setRange(0,100);self.shader_bar.setValue(0);self.shader_label.setText({'stopped':'Stopped','failed':'Failed'}.get(state.get('state'),''))
  if self.profile:
   try:
    step=int((self.profile/'camera-step.txt').read_text())
    if 0<=step<=4 and step!=self.s['camera']:self.s['camera']=step;B.save(self.s)
   except (OSError,ValueError):pass
 def export(self):
  if self.game_active():self.saves_status.setText('Close the game before copying a save.');return
  folder=W.QFileDialog.getExistingDirectory(self,'Folder for the save copy',str(B.RUNTIME))
  if folder:
   try:B.export_save(Path(folder)/('DemonsSouls-Save-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S')+'.zip'));self.saves_status.setText('Save exported.')
   except (OSError,ValueError) as e:self.saves_status.setText(str(e))
 def restore(self):
  if self.game_active():self.saves_status.setText('Close the game before copying a save.');return
  path,_=W.QFileDialog.getOpenFileName(self,'Import save','','Save copy (*.zip)')
  if not path:return
  answer=W.QMessageBox.question(self,"Demon's Souls",'Replace the current save with this copy? The current save is kept in _SaveData/_backups.',W.QMessageBox.Yes|W.QMessageBox.No,W.QMessageBox.No)
  if answer!=W.QMessageBox.Yes:return
  try:B.import_save(path);self.saves_status.setText('Save imported. The previous one is in _SaveData/_backups.')
  except (OSError,ValueError) as e:self.saves_status.setText(str(e))

def main():
 import fcntl
 B.RUNTIME.mkdir(exist_ok=True);gate=(B.RUNTIME/'custom-launcher.lock').open('a')
 try:fcntl.flock(gate,fcntl.LOCK_EX|fcntl.LOCK_NB)
 except BlockingIOError:return 0
 app=W.QApplication(sys.argv);app.setStyle('Fusion');app.setStyleSheet(STYLE+'QComboBox::down-arrow {image:url('+str(B.ROOT/'custom-launcher/chevron.svg')+');width:10px;height:6px;}');w=Launcher()
 if '--screenshot-page' in sys.argv:
  page=sys.argv[sys.argv.index('--screenshot-page')+1]
  if page=='saves':w.open_settings();w.open_saves()
  elif page!='home':w.open_settings();w.show_tab(page)
 w.show()
 if '--screenshot' in sys.argv:
  output=B.ROOT/'verification'/('launcher-restored-'+(sys.argv[sys.argv.index('--screenshot-page')+1] if '--screenshot-page' in sys.argv else 'home')+'.png')
  C.QTimer.singleShot(350,lambda:(w.grab().save(str(output)),app.quit()))
 return app.exec()
if __name__=='__main__':sys.exit(main())
