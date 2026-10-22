"""Independently generate shipped UI artwork; never reuse inherited image pixels.
Run after build_assets.py. Outputs and a source-to-resource manifest are tracked.
"""
from pathlib import Path
import xml.etree.ElementTree as ET,re,hashlib,json,io,base64,os
from PIL import Image,ImageDraw
base=Path('upstream/Terminals/Source');ns={'m':'http://schemas.microsoft.com/developer/msbuild/2003'}
projects=['Terminals','Terminals.Common','Terminals.Plugins.Rdp','Terminals.Plugins.Putty','Terminals.Plugins.Web']
out=Path('branding/generated');out.mkdir(exist_ok=True)
manifest=json.loads(Path('branding/RESOURCE-PROVENANCE.json').read_text()) if Path('branding/RESOURCE-PROVENANCE.json').exists() else []
def icon(name):
 im=Image.new('RGBA',(128,128));d=ImageDraw.Draw(im);key=name.lower()
 if any(w in key for w in ['folder','group','open','explorer']):
  d.rounded_rectangle((10,24,65,53),8,fill='#ffcf45');d.rounded_rectangle((10,40,119,108),9,fill='#ffbf24');d.line((15,43,112,43),fill='#ffe481',width=5)
 elif any(w in key for w in ['server','computer','rdp','terminal','desktop','screen','connect']):
  d.rounded_rectangle((13,18,115,90),8,fill='#6facf2');d.rounded_rectangle((20,26,108,79),3,fill='#102c49');d.rectangle((56,91,71,105),fill='#6facf2');d.rounded_rectangle((35,105,93,112),3,fill='#6facf2')
 elif any(w in key for w in ['delete','close','cancel','stop','disconnect']):
  d.ellipse((18,18,110,110),fill='#ee5353');d.line((44,44,84,84),fill='white',width=9);d.line((44,84,84,44),fill='white',width=9)
 elif any(w in key for w in ['search','find','zoom']):
  d.ellipse((16,13,88,85),outline='#9ebfe8',width=10);d.line((80,79,115,114),fill='#9ebfe8',width=12)
 elif any(w in key for w in ['add','new','plus','insert']):
  d.rounded_rectangle((15,15,113,113),15,fill='#078bea');d.rectangle((57,31,72,97),fill='white');d.rectangle((31,57,97,72),fill='white')
 elif any(w in key for w in ['vpn','secure','shield','lock','key','credential']):
  d.polygon([(64,10),(111,29),(102,85),(64,118),(26,85),(17,29)],fill='#66a0ed');d.polygon([(64,24),(96,38),(89,78),(64,102),(39,78),(32,38)],fill='#102e4d');d.line([(44,63),(59,78),(85,47)],fill='#1be391',width=8)
 else:
  colors=['#38a4ef','#64b5f6','#19c6bd','#b296f2'];c=colors[int(hashlib.sha256(key.encode()).hexdigest()[:2],16)%len(colors)]
  d.rounded_rectangle((21,12,106,117),8,fill=c);d.polygon([(79,12),(106,39),(79,39)],fill='#daedff')
  for y in [57,76,95]:d.rounded_rectangle((36,y,91,y+6),2,fill='#eff7ff')
 return im.resize((32,32),Image.Resampling.LANCZOS)
for project in projects:
 cp=base/project/(project+'.csproj');tree=ET.parse(cp)
 for resource in tree.findall('.//m:EmbeddedResource',ns):
  rel=resource.get('Include');
  if not rel.endswith('.resx'):continue
  p=cp.parent/rel.replace('\\','/');raw=p.read_bytes();text=raw.decode('utf-8-sig');root=ET.fromstring(text);updated_text=text
  designers=[p.with_suffix('.Designer.cs'),p.with_suffix('.designer.cs')];designer=next((x for x in designers if x.exists()),None)
  for data in root.findall('data'):
   name=data.get('name');kind=data.get('type','');mime=data.get('mimetype','');v=data.findtext('value','')
   data_pattern=r'<data\b(?=[^>]*\bname="'+re.escape(name)+r'")[^>]*>.*?</data>'
   if name.endswith('.ImageStream') and mime:
    if designer is None:raise RuntimeError(str(p)+' missing designer')
    component=name[:-12];src_raw=designer.read_bytes();src=src_raw.decode('utf-8-sig')
    indices=[int(x) for x in re.findall(r'this\.'+re.escape(component)+r'\.Images\.SetKeyName\((\d+),',src)]
    if not indices:raise RuntimeError(str(p)+' no image keys for '+component)
    count=max(indices)+1
    pattern=r'this\.'+re.escape(component)+r'\.ImageStream = \(\(System.Windows.Forms.ImageListStreamer\)\(resources.GetObject\("'+re.escape(name)+r'"\)\)\);'
    updated,n=re.subn(pattern,'global::Terminals.Common.DeskPilotArtwork.Populate(this.'+component+', '+str(count)+');',src)
    if n!=1:raise RuntimeError(str(designer)+' unexpected ImageStream assignment '+name)
    designer.write_bytes((b'\xef\xbb\xbf' if src_raw.startswith(b'\xef\xbb\xbf') else b'')+updated.encode('utf-8'));updated_text=re.sub(data_pattern,'',updated_text,flags=re.S);manifest.append({'resource':str(p),'key':name,'origin':'DeskPilotArtwork.cs','count':count});continue
   is_image='System.Drawing.' in kind and any(x in kind for x in ['Bitmap','Image','Icon'])
   if 'ResXFileRef' in kind:
    parts=v.split(';');is_image=len(parts)>1 and any(x in parts[1] for x in ['System.Drawing.Bitmap','System.Drawing.Image','System.Drawing.Icon'])
    if is_image and Path(parts[0].replace('\\','/')).name.startswith('DeskPilot'):continue
   if not is_image:continue
   key=str(p)+':'+name;filename=hashlib.sha256(key.encode()).hexdigest()[:20]+'.png';dest=out/filename;icon(name).save(dest)
   data.attrib.clear();data.set('name',name);data.set('type','System.Resources.ResXFileRef, System.Windows.Forms')
   for child in list(data):data.remove(child)
   value=ET.SubElement(data,'value');value.text=os.path.relpath(dest,p.parent).replace('/',chr(92))+';System.Drawing.Bitmap, System.Drawing'
   if 'Icon' in kind or ('ResXFileRef' in kind and 'System.Drawing.Icon' in v):
    value.text=os.path.relpath(base/'Terminals/DeskPilot.ico',p.parent).replace('/',chr(92))+';System.Drawing.Icon, System.Drawing'
   data.tail=None
   updated_text=re.sub(data_pattern,lambda m:ET.tostring(data,encoding='unicode'),updated_text,flags=re.S)
   manifest.append({'resource':str(p),'key':name,'origin':str(dest),'sha256':hashlib.sha256(dest.read_bytes()).hexdigest()})
  # Preserve all non-artwork XML values (including control state and strings).
  if updated_text!=text:p.write_bytes((b'\xef\xbb\xbf' if raw.startswith(b'\xef\xbb\xbf') else b'')+updated_text.encode('utf-8'))
Path('branding/RESOURCE-PROVENANCE.json').write_text(json.dumps(list({(x['resource'],x['key']):x for x in manifest}.values()),indent=2)+'\n')
print('Generated independent artwork entries:',len(manifest))
