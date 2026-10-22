"""Render the existing code-native DeskPilot mark into Windows branding assets.
Requires Pillow. Run from the repository root; no reference screenshots are edited.
"""
from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
import io,re,struct,os
root=Path('upstream/Terminals/Source/Terminals');out=root/'Resources'
polygons=[('#005fbe',[(3,16),(12,4),(19,16),(12,29)]),('#00aeef',[(9,16),(20,3),(27,9),(15,23)]),('#1c83d8',[(7,18),(13,15),(20,26),(16,30)])]
def mark(size,tile=False):
 im=Image.new('RGBA',(1024,1024),(0,0,0,0));d=ImageDraw.Draw(im)
 if tile:d.rounded_rectangle((0,0,1023,1023),radius=220,fill='#101e2e')
 for color,points in polygons:
  d.polygon([(int((x*.86+2.24)*32),int((y*.86+1.5)*32)) for x,y in points],fill=color)
 return im.resize((size,size),Image.Resampling.LANCZOS)
mark(256).save(out/'DeskPilotLogo.png')
frames=[]
for size in [16,20,24,32,40,48,64,128,256]:
 buffer=io.BytesIO();mark(size,True).save(buffer,format='ICO',sizes=[(size,size)],bitmap_format='bmp' if size<128 else 'png')
 frames.append((size,buffer.getvalue()[22:]))
offset=6+16*len(frames);entries=[];payload=[]
for size,data in frames:
 entries.append(struct.pack('<BBBBHHII',size%256,size%256,0,0,1,32,len(data),offset));payload.append(data);offset+=len(data)
(root/'DeskPilot.ico').write_bytes(struct.pack('<HHH',0,1,len(frames))+b''.join(entries)+b''.join(payload))
fontroot=Path('/System/Library/Fonts/Supplemental')
# Fonts are rendering dependencies only; no font files are redistributed.
def font(size,bold=False):
 names=[fontroot/('Arial Bold.ttf' if bold else 'Arial.ttf'),Path('C:/Windows/Fonts/'+('arialbd.ttf' if bold else 'arial.ttf'))]
 return ImageFont.truetype(str(next(p for p in names if p.exists())),size)
def banner(name,size):
 w,h=size;scale=3;im=Image.new('RGB',(w*scale,h*scale),'#101e2e');logo=mark(min(h-12,int(w*.24))*scale)
 x=10*scale;y=(h*scale-logo.height)//2;im.paste(logo,(x,y),logo)
 d=ImageDraw.Draw(im);left=x+logo.width+8*scale;fs=min(int(h*.32),int((w-left/scale)/5.2))
 d.text((left,int(h*.22)*scale),'DeskPilot',font=font(fs*scale,True),fill='#f4f7fb')
 if h>=55 and w>250:d.text((left,int(h*.64)*scale),'Remote. Manage. Support.',font=font(max(10,int(fs*.36))*scale),fill='#c3d1e2')
 im.resize((w,h),Image.Resampling.LANCZOS).save(out/name)
banner('DeskPilotBanner.png',(561,166));banner('DeskPilotBannerSmall.png',(262,56));banner('DeskPilotInstallerBanner.jpg',(500,70))
for name,size in [('DeskPilotBannerLeft.png',(105,491)),('DeskPilotWizard.png',(60,280))]:
 im=Image.new('RGB',size,'#101e2e');logo=mark(size[0]-8);im.paste(logo,(4,16),logo);im.save(out/name)
# Keep resource IDs stable; all explicit dialog icons point to the shared asset.
for p in root.rglob('*.resx'):
 raw=p.read_bytes().decode('utf-8-sig')
 relative=os.path.relpath(root/'DeskPilot.ico',p.parent).replace('/',chr(92))
 pattern=r'<data name="([^"]+)"[^>]*type="System.Drawing.Icon, System.Drawing"[^>]*>.*?</data>'
 replacement=lambda m:'<data name="'+m[1]+'" type="System.Resources.ResXFileRef, System.Windows.Forms">\n    <value>'+relative+';System.Drawing.Icon, System.Drawing</value>\n  </data>'
 text=''.join(part if part.startswith('<!--') else re.sub(pattern,replacement,part,flags=re.S) for part in re.split(r'(<!--.*?-->)',raw,flags=re.S))
 if text!=raw:p.write_bytes(text.encode('utf-8'))
