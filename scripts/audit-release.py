"""Check the reviewed compiled graph, artwork and redistributable payload."""
from pathlib import Path
import xml.etree.ElementTree as E,json,hashlib,sys
repo=Path(__file__).resolve().parent.parent;ns={'m':'http://schemas.microsoft.com/developer/msbuild/2003'}
projects=set()
def inspect(p):
 p=p.resolve()
 if p in projects:return
 projects.add(p);root=E.parse(p)
 for ref in root.findall('.//m:ProjectReference',ns):
  q=(p.parent/ref.get('Include').replace('\\','/')).resolve()
  if q.parent.name in ['TabControl','Metro','SharpPcap','HexEditor']:raise RuntimeError('Uncleared project: '+str(q))
  inspect(q)
 for ref in root.findall('.//m:Reference',ns):
  name=ref.get('Include').split(',')[0]
  if name in ['AWSSDK','FlickrNet','TransPort2006','TransPort2006.Design','ZedGraph','SharpPcap','Metro','TabControl','HexEditor']:raise RuntimeError('Uncleared runtime reference: '+name)
 for r in root.findall('.//m:EmbeddedResource',ns):
  f=p.parent/r.get('Include').replace('\\','/')
  if f.suffix!='.resx':continue
  for d in E.parse(f).getroot().findall('data'):
   if d.get('mimetype') and ('System.Drawing' in d.get('type','') or d.get('name','').endswith('.ImageStream')):raise RuntimeError('Inherited embedded image: '+str(f))
   if 'ResXFileRef' not in d.get('type',''):continue
   value=d.findtext('value','');parts=value.split(';')
   if len(parts)>1 and 'System.Drawing' in parts[1]:
    q=(f.parent/parts[0].replace('\\','/')).resolve()
    original=(repo/'branding/generated').resolve()
    if not q.is_relative_to(original) and not (q.name.startswith('DeskPilot') and q.is_relative_to((repo/'upstream/Terminals/Source/Terminals').resolve())):raise RuntimeError('Unknown artwork origin: '+str(q))
inspect(repo/'upstream/Terminals/Source/Terminals/Terminals.csproj')
for n in ['Rdp','Putty','Web']:inspect(repo/f'upstream/Terminals/Source/Terminals.Plugins.{n}/Terminals.Plugins.{n}.csproj')
external=E.parse(repo/'upstream/Terminals/Source/Terminals.External/Terminals.External.csproj')
allowed={'CommandLineArguments.cs','Properties\\AssemblyInfo.cs','..\\Terminals\\Properties\\Common.AssemblyInfo.cs','DeskPilotCompatibility.cs'}
if {c.get('Include') for c in external.findall('.//m:Compile',ns)}!=allowed:raise RuntimeError('External source graph changed; review required')
for a in json.loads((repo/'branding/RESOURCE-PROVENANCE.json').read_text()):
 if a['origin']=='DeskPilotArtwork.cs':continue
 q=repo/a['origin']
 if hashlib.sha256(q.read_bytes()).hexdigest()!=a['sha256']:raise RuntimeError('Artwork provenance hash mismatch: '+a['origin'])
if len(sys.argv)>1:
 payload=Path(sys.argv[1]);rules=json.loads((repo/'packaging/redistributables.json').read_text());provenance=json.loads((repo/'upstream/Terminals/Resources/Libraries/mstsc/GENERATION.json').read_text())
 wrappers={x['name']:x['sha256'].lower() for x in provenance['wrappers']}
 known=rules['builtFromReviewedSource']+list(rules['vendorHashes'])+['MSTSCLib.dll','AxInterop.MSTSCLib.dll']
 for f in payload.rglob('*'):
  if f.suffix.lower() not in ['.dll','.exe']:continue
  if f.name not in known:raise RuntimeError('Unreviewed binary: '+str(f.relative_to(payload)))
  if f.name in wrappers and hashlib.sha256(f.read_bytes()).hexdigest()!=wrappers[f.name]:raise RuntimeError('RDP wrapper differs from generated provenance: '+str(f))
  if f.name in rules['vendorHashes']:
   actual=hashlib.sha256(f.read_bytes()).hexdigest()
   if actual not in rules['vendorHashes'][f.name]:raise RuntimeError('Vendor binary differs from reviewed version: '+str(f.relative_to(payload)))
 for required in ['DeskPilot.Preview.exe','DeskPilot.Tabs.dll','EntityFramework.dll','EntityFramework.SqlServer.dll','log4net.dll']:
  if not (payload/required).exists():raise RuntimeError('Missing reviewed runtime '+required)
print('PASS release provenance: compiled project/source graph, independent artwork'+(', generated RDP bindings and exact vendor binary hashes' if len(sys.argv)>1 else ''))
