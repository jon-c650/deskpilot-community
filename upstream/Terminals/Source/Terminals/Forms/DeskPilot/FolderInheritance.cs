using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using Terminals.Configuration;
using Terminals.Connections;
using Terminals.Data;

namespace Terminals.Forms.DeskPilot
{
    [Serializable] public sealed class FolderSettingValue
    {
        public string Path;
        public string Value;
        public bool IsNull;
        public FolderSettingValue Copy() {return new FolderSettingValue {Path=Path,Value=Value,IsNull=IsNull};}
    }
    [Serializable] public sealed class FolderSettingsRecord
    {
        public string Key;
        public string Protocol;
        public string SourceFolder;
        public List<FolderSettingValue> Values=new List<FolderSettingValue>();
        public List<string> LocalOverrides=new List<string>();
    }
    [Serializable] public sealed class FolderSettingsFile
    {
        public int Version=1;
        public List<FolderSettingsRecord> Folders=new List<FolderSettingsRecord>();
        public List<FolderSettingsRecord> Endpoints=new List<FolderSettingsRecord>();
    }
    // Sparse defaults are resolved on connection copies. Saved favorites and live sessions are never rewritten.
    // Credential references only: encrypted credential contents stay in the original credential store.
    internal sealed class FolderInheritance
    {
        private readonly IPersistence persistence;
        private readonly ConnectionManager manager;
        private readonly string path;
        private FolderSettingsFile data;
        private byte[] loadedBytes;
        private static readonly XmlSerializer Serializer=new XmlSerializer(typeof(FolderSettingsFile));
        private static readonly byte[] Entropy=Encoding.UTF8.GetBytes("DeskPilot.folder-defaults.v1");
        internal string FilePath {get {return path;}}
        internal FolderInheritance(IPersistence persistence,ConnectionManager manager,string overridePath=null)
        {
            this.persistence=persistence;this.manager=manager;
            string scope=persistence.TypeId+"|"+Settings.Instance.FileLocations.Favorites+"|"+(persistence.TypeId==0 ? "" : Settings.Instance.ConnectionString);
            string suffix;
            using(var sha=SHA256.Create()) suffix=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(scope))).Replace("-","");
            path=overridePath ?? Path.Combine(Path.GetDirectoryName(Settings.Instance.FileLocations.Favorites),"FolderDefaults-"+suffix+".dpapi");
            loadedBytes=File.Exists(path) ? File.ReadAllBytes(path) : null;
            try {
                data=loadedBytes==null ? new FolderSettingsFile() : Deserialize(ProtectedData.Unprotect(loadedBytes,Entropy,DataProtectionScope.CurrentUser));
                if(data.Version!=1) throw new InvalidDataException("Unsupported folder defaults version.");
                Validate(data);
            } catch(Exception error) {throw new InvalidOperationException("Folder defaults could not be read. Keep the file and its .bak backup; do not reset your connections. "+error.Message,error);}
        }
        internal void ReloadIfChanged()
        {
            var current=File.Exists(path) ? File.ReadAllBytes(path) : null;
            if((current==null)==(loadedBytes==null) && (current==null || current.SequenceEqual(loadedBytes))) return;
            var fresh=new FolderInheritance(persistence,manager,path);data=fresh.data;loadedBytes=fresh.loadedBytes;
        }
        internal void RestoreFolderRecord(IGroup group,FolderSettingsRecord original)
        {
            Mutate(copy=>{copy.Folders.RemoveAll(item=>item.Key==GroupKey(group));if(original.Values.Count>0) copy.Folders.Add(CopyRecord(original));});
        }
        internal static string GroupKey(IGroup group)
        {
            if(group==null) return null;
            var type=group is Group ? typeof(Group) : group is Terminals.Data.DB.DbGroup ? typeof(Terminals.Data.DB.DbGroup) : group.GetType();
            var id=type.GetProperty("Id").GetValue(group,null);
            return type.FullName+":"+Convert.ToString(id,CultureInfo.InvariantCulture);
        }
        internal static string GroupPath(IGroup group)
        {
            return String.Join(" / ",Chain(group).Select(item=>item.Name).ToArray());
        }
        private static List<IGroup> Chain(IGroup group)
        {
            var result=new List<IGroup>();var seen=new HashSet<string>();
            while(group!=null) {
                if(!seen.Add(GroupKey(group))) throw new InvalidOperationException("Folder hierarchy contains a cycle.");
                result.Insert(0,group);group=group.Parent;
            }
            return result;
        }
        internal FolderSettingsRecord FolderRecord(IGroup group) {return CopyRecord(data.Folders.FirstOrDefault(item=>item.Key==GroupKey(group)));}
        internal FolderSettingsRecord EndpointRecord(IFavorite favorite) {return CopyRecord(data.Endpoints.FirstOrDefault(item=>item.Key==favorite.Id.ToString()));}
        private static FolderSettingsRecord CopyRecord(FolderSettingsRecord record)
        {
            return record==null ? new FolderSettingsRecord() : new FolderSettingsRecord {Key=record.Key,Protocol=record.Protocol,SourceFolder=record.SourceFolder,Values=record.Values.Select(value=>value.Copy()).ToList(),LocalOverrides=record.LocalOverrides.ToList()};
        }
        internal Dictionary<string,string> FolderSources(IGroup group,bool includeSelf=true)
        {
            var result=new Dictionary<string,string>();string protocol="RDP";
            foreach(var ancestor in Chain(group).Where(item=>includeSelf || !item.StoreIdEquals(group))) {
                var record=data.Folders.FirstOrDefault(item=>item.Key==GroupKey(ancestor));if(record==null) continue;
                var selected=record.Values.FirstOrDefault(item=>item.Path=="Protocol");
                if(selected!=null && selected.Value!=protocol) {protocol=selected.Value;foreach(var key in result.Keys.Where(item=>item.StartsWith("ProtocolProperties.")).ToArray()) result.Remove(key);}
                foreach(var value in record.Values) if(!value.Path.StartsWith("ProtocolProperties.") || record.Protocol==protocol) result[value.Path]=GroupPath(ancestor);
            }
            return result;
        }
        internal IFavorite FolderModel(IGroup group,bool includeSelf=true)
        {
            var model=persistence.Factory.CreateFavorite();manager.ChangeProtocol(model,"RDP");
            foreach(var ancestor in Chain(group).Where(item=>includeSelf || !item.StoreIdEquals(group))) Apply(model,data.Folders.FirstOrDefault(record=>record.Key==GroupKey(ancestor)),null,false);
            return model;
        }
        internal IGroup Source(IFavorite favorite,string choice=null)
        {
            var groups=favorite.Groups;
            string requested=choice ?? EndpointRecord(favorite).SourceFolder;
            if(requested=="none") return null;
            if(!String.IsNullOrEmpty(requested)) {
                var selected=groups.FirstOrDefault(group=>GroupKey(group)==requested);
                if(selected==null) throw new InvalidOperationException("The inherited folder is no longer assigned to "+favorite.Name+". Choose a folder in Inherited settings.");
                return selected;
            }
            var relevant=groups.Where(group=>Chain(group).Any(parent=>data.Folders.Any(record=>record.Key==GroupKey(parent) && record.Values.Count>0))).ToList();
            if(relevant.Count==0) return null;
            var deepest=relevant.OrderByDescending(group=>Chain(group).Count).First();
            if(relevant.Any(group=>!Chain(deepest).Any(parent=>parent.StoreIdEquals(group))))
                throw new InvalidOperationException(favorite.Name+" belongs to multiple folders with defaults. Choose its source folder in Inherited settings; no arbitrary folder was used.");
            return deepest;
        }
        internal IFavorite Resolve(IFavorite favorite,string choice=null,bool endpointOverrides=true)
        {
            var result=favorite.Copy();
            var source=Source(favorite,choice);
            var excluded=new HashSet<string>(endpointOverrides ? EndpointRecord(favorite).LocalOverrides : new List<string>());
            foreach(var ancestor in Chain(source)) Apply(result,data.Folders.FirstOrDefault(record=>record.Key==GroupKey(ancestor)),excluded);
            return result;
        }
        internal bool HasDefaults(IFavorite favorite)
        {
            return EndpointRecord(favorite).LocalOverrides.Count>0 || favorite.Groups.Any(group=>Chain(group).Any(parent=>data.Folders.Any(record=>record.Key==GroupKey(parent) && record.Values.Count>0)));
        }
        internal void SaveFolder(IGroup group,IFavorite model,IEnumerable<string> defined)
        {
            var values=Capture(model);var chosen=new HashSet<string>(defined);
            EnsureCredentialReferences(model,chosen);
            foreach(var value in values.Values.Where(item=>chosen.Contains(item.Path) && item.Path.EndsWith(".Credential") && !item.IsNull && !String.IsNullOrEmpty(item.Value))) {
                var id=new Guid(value.Value);if(id!=Guid.Empty && persistence.Credentials[id]==null) throw new InvalidOperationException("The selected saved credential no longer exists. Choose another saved credential before saving.");
            }
            var record=new FolderSettingsRecord {Key=GroupKey(group),Protocol=model.Protocol,Values=values.Values.Where(value=>chosen.Contains(value.Path)).Select(value=>value.Copy()).ToList()};
            Mutate(copy=>{copy.Folders.RemoveAll(item=>item.Key==record.Key);if(record.Values.Count>0) copy.Folders.Add(record);});
        }
        internal void SaveFolderWithAssociation(IGroup group,IFavorite model,IEnumerable<string> defined,Action saveAssociation)
        {
            var previous=FolderRecord(group);SaveFolder(group,model,defined);
            try {saveAssociation();}
            catch(Exception failure) {
                try {RestoreFolderRecord(group,previous);}
                catch(Exception rollback) {throw new AggregateException("The VPN association failed and folder settings could not be restored. Reopen Properties to review them.",failure,rollback);}
                throw new InvalidOperationException("Nothing was saved. The VPN association failed and the folder settings were restored. "+failure.Message,failure);
            }
        }
        internal void SaveEndpoint(IFavorite favorite,IEnumerable<string> defined,string source)
        {
            var record=new FolderSettingsRecord {Key=favorite.Id.ToString(),SourceFolder=source,LocalOverrides=defined.Distinct().ToList()};
            Mutate(copy=>{copy.Endpoints.RemoveAll(item=>item.Key==record.Key);if(record.LocalOverrides.Count>0 || !String.IsNullOrEmpty(source)) copy.Endpoints.Add(record);});
        }
        // Edits in the original endpoint editor become explicit per-setting overrides.
        internal void RecordEndpointEdits(IFavorite favorite,IFavorite before)
        {
            if(!HasDefaults(favorite)) return;
            var old=Capture(before);var after=Capture(favorite);
            var changed=after.Values.Where(value=>!old.ContainsKey(value.Path) || !Equal(value,old[value.Path])).Select(value=>value.Path).ToList();
            if(!CredentialsEqual(favorite.Security,before.Security)) changed.Add("Security.Credential");
            var currentGateway=favorite.ProtocolProperties as IContainsCredentials;var oldGateway=before.ProtocolProperties as IContainsCredentials;
            if(currentGateway!=null && oldGateway!=null && !CredentialsEqual(currentGateway.GetSecurity(),oldGateway.GetSecurity()))
                changed.AddRange(after.Keys.Where(key=>key.StartsWith("ProtocolProperties.") && key.EndsWith(".Credential")));
            if(changed.Count==0) return;
            var record=EndpointRecord(favorite);
            SaveEndpoint(favorite,record.LocalOverrides.Concat(changed),record.SourceFolder);
        }
        private bool CredentialsEqual(ICredentialBase a,ICredentialBase b)
        {
            var factory=new Terminals.Data.Credentials.GuardedCredentialFactory(persistence);var left=factory.CreateCredential(a);var right=factory.CreateCredential(b);
            return left.Domain==right.Domain && left.UserName==right.UserName && left.Password==right.Password;
        }
        internal void Apply(IFavorite target,FolderSettingsRecord record,HashSet<string> excluded=null,bool checkCredentials=true)
        {
            if(record==null || record.Values.Count==0) return;
            var protocol=record.Values.FirstOrDefault(value=>value.Path=="Protocol");
            if(protocol!=null && (excluded==null || !excluded.Contains("Protocol"))) {
                if(!manager.IsKnownProtocol(protocol.Value)) throw new InvalidOperationException("Folder defaults require an unavailable protocol: "+protocol.Value);
                manager.ChangeProtocol(target,protocol.Value);
            }
            foreach(var value in record.Values.Where(item=>item.Path!="Protocol" && (excluded==null || !excluded.Contains(item.Path)))) {
                if(value.Path.StartsWith("ProtocolProperties.") && target.Protocol!=record.Protocol) continue;
                if(checkCredentials && value.Path.EndsWith(".Credential") && !value.IsNull && !String.IsNullOrEmpty(value.Value)) {
                    var credential=new Guid(value.Value);
                    if(credential!=Guid.Empty && persistence.Credentials[credential]==null) throw new InvalidOperationException("An inherited saved credential no longer exists. Choose a credential in folder Properties.");
                }
                Set(target,value);
                if(value.Path.EndsWith(".Credential") && !value.IsNull && !String.IsNullOrEmpty(value.Value)) {
                    var id=new Guid(value.Value);var saved=id==Guid.Empty ? null : persistence.Credentials[id];
                    var gateway=target.ProtocolProperties as IContainsCredentials;
                    ISecurityOptions secured=value.Path=="Security.Credential" ? target.Security : gateway==null ? null : gateway.GetSecurity();
                    // Some existing transport paths read the encrypted fields directly rather
                    // than resolving the credential ID. Hydrate only this temporary copy.
                    if(saved!=null && secured!=null && secured.Credential==id) {
                        secured.EncryptedDomain=saved.EncryptedDomain;secured.EncryptedUserName=saved.EncryptedUserName;secured.EncryptedPassword=saved.EncryptedPassword;
                    }
                }
            }
        }
        internal static bool Equal(FolderSettingValue left,FolderSettingValue right) {return (left.IsNull || String.IsNullOrEmpty(left.Value)) && (right.IsNull || String.IsNullOrEmpty(right.Value)) || left.IsNull==right.IsNull && left.Value==right.Value;}
        internal static Dictionary<string,FolderSettingValue> Capture(IFavorite favorite)
        {
            var result=new Dictionary<string,FolderSettingValue>();
            Put(result,"Protocol",favorite.Protocol);Put(result,"Port",favorite.Port);Put(result,"NewWindow",favorite.NewWindow);Put(result,"DesktopShare",favorite.DesktopShare);
            CaptureObject(result,"Display",favorite.Display,typeof(IDisplayOptions));
            Put(result,"Security.Credential",favorite.Security.Credential);
            CaptureObject(result,"ExecuteBeforeConnect",favorite.ExecuteBeforeConnect,typeof(IBeforeConnectExecuteOptions));
            CaptureObject(result,"ProtocolProperties",favorite.ProtocolProperties,favorite.ProtocolProperties.GetType());
            return result;
        }
        private static void Put(Dictionary<string,FolderSettingValue> values,string path,object value)
        {
            values[path]=new FolderSettingValue {Path=path,IsNull=value==null,Value=value==null ? null : Convert.ToString(value,CultureInfo.InvariantCulture)};
        }
        private static void CaptureObject(Dictionary<string,FolderSettingValue> result,string path,object value,Type contract)
        {
            if(value==null) return;
            if(path.Split('.').Length>10) throw new InvalidOperationException("Unsupported recursive protocol settings.");
            foreach(var property in contract.GetProperties(BindingFlags.Instance|BindingFlags.Public).Where(item=>item.CanRead && item.GetIndexParameters().Length==0)) {
                if(property.Name.StartsWith("Encrypted",StringComparison.Ordinal)) continue;
                var item=property.GetValue(value,null);var type=property.PropertyType;
                if(type.IsPrimitive || type.IsEnum || type==typeof(string) || type==typeof(Guid) || type==typeof(decimal)) {
                    if(property.CanWrite) Put(result,path+"."+property.Name,item);
                } else if(item!=null && !typeof(System.Collections.IEnumerable).IsAssignableFrom(type)) CaptureObject(result,path+"."+property.Name,item,type);
            }
        }
        private static bool Allowed(string path)
        {
            return path=="Protocol" || path=="Port" || path=="NewWindow" || path=="DesktopShare" || path=="Security.Credential" || path.StartsWith("Display.") || path.StartsWith("ExecuteBeforeConnect.") || path.StartsWith("ProtocolProperties.");
        }
        private static void Set(IFavorite favorite,FolderSettingValue value)
        {
            if(!Allowed(value.Path) || value.Path.Split('.').Any(part=>part.StartsWith("Encrypted",StringComparison.Ordinal))) throw new InvalidDataException("Unsupported inherited setting.");
            object target=favorite;Type contract=typeof(IFavorite);var parts=value.Path.Split('.');
            for(int i=0;i<parts.Length-1;i++) {
                var nested=contract.GetProperty(parts[i]);
                if(nested==null || (target=nested.GetValue(target,null))==null) throw new InvalidDataException("Inherited setting is not supported: "+value.Path);
                contract=nested.PropertyType==typeof(ProtocolOptions) ? target.GetType() : nested.PropertyType;
            }
            var property=contract.GetProperty(parts[parts.Length-1]);
            if(property==null || !property.CanWrite) throw new InvalidDataException("Inherited setting is not writable: "+value.Path);
            var type=property.PropertyType;
            object converted=value.IsNull ? null : type==typeof(string) ? (object)value.Value : type.IsEnum ? Enum.Parse(type,value.Value) : type==typeof(Guid) ? new Guid(value.Value) : Convert.ChangeType(value.Value,type,CultureInfo.InvariantCulture);
            if(property.Name=="Credential") {
                var secret=target as ICredentialBase;
                if(secret!=null) {secret.EncryptedUserName=null;secret.EncryptedDomain=null;secret.EncryptedPassword=null;}
            }
            property.SetValue(target,converted,null);
        }
        private static void EnsureCredentialReferences(IFavorite model,HashSet<string> chosen)
        {
            if(chosen.Contains("Security.Credential")) CheckCredentials(model.Security);
            var nested=model.ProtocolProperties as IContainsCredentials;
            if(nested!=null && chosen.Any(key=>key.StartsWith("ProtocolProperties.") && key.EndsWith(".Credential"))) CheckCredentials(nested.GetSecurity());
        }
        private static void CheckCredentials(ICredentialBase credentials)
        {
            if(credentials==null) return;
            var options=credentials as ISecurityOptions;
            if(options!=null && options.Credential!=Guid.Empty) return;
            if(!String.IsNullOrEmpty(credentials.EncryptedPassword) || !String.IsNullOrEmpty(credentials.EncryptedUserName) || !String.IsNullOrEmpty(credentials.EncryptedDomain))
                throw new InvalidOperationException("Use a saved credential for inherited authentication (Tools → Credentials). Inline credential contents are not copied into folder defaults.");
        }
        private static void Validate(FolderSettingsFile config)
        {
            if(config.Folders==null || config.Endpoints==null) throw new InvalidDataException("Invalid folder defaults file.");
            foreach(var record in config.Folders.Concat(config.Endpoints)) {
                if(String.IsNullOrEmpty(record.Key) || record.Values==null || record.LocalOverrides==null || record.Values.GroupBy(value=>value.Path).Any(group=>group.Count()>1)) throw new InvalidDataException("Invalid or duplicate inherited setting.");
                foreach(var key in record.LocalOverrides) if(String.IsNullOrEmpty(key) || !Allowed(key) || key.Split('.').Any(part=>part.StartsWith("Encrypted",StringComparison.Ordinal))) throw new InvalidDataException("Invalid endpoint override.");
                foreach(var value in record.Values) if(value==null || !Allowed(value.Path) || value.Path.Split('.').Any(part=>part.StartsWith("Encrypted",StringComparison.Ordinal))) throw new InvalidDataException("Invalid inherited setting path.");
            }
            if(config.Folders.GroupBy(record=>record.Key).Any(group=>group.Count()>1) || config.Endpoints.GroupBy(record=>record.Key).Any(group=>group.Count()>1)) throw new InvalidDataException("Duplicate folder defaults entry.");
        }
        private static byte[] Serialize(FolderSettingsFile config) {using(var stream=new MemoryStream()) {Serializer.Serialize(stream,config);return stream.ToArray();}}
        private static FolderSettingsFile Deserialize(byte[] bytes)
        {
            using(var stream=new MemoryStream(bytes)) using(var reader=XmlReader.Create(stream,new XmlReaderSettings {DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null})) return (FolderSettingsFile)Serializer.Deserialize(reader);
        }
        private void Mutate(Action<FolderSettingsFile> action)
        {
            var copy=Deserialize(Serialize(data));action(copy);Validate(copy);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using(var editLock=new FileStream(path+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
            var current=File.Exists(path) ? File.ReadAllBytes(path) : null;
            if((current==null)!=(loadedBytes==null) || current!=null && !current.SequenceEqual(loadedBytes)) throw new IOException("Folder defaults changed in another window. Reopen Properties before saving.");
            byte[] protectedBytes=ProtectedData.Protect(Serialize(copy),Entropy,DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(path));string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try {
                using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {file.Write(protectedBytes,0,protectedBytes.Length);file.Flush(true);}
                if(File.Exists(path)) File.Replace(temporary,path,path+".bak");else File.Move(temporary,path);
                loadedBytes=protectedBytes;data=copy;
            } finally {if(File.Exists(temporary)) File.Delete(temporary);}
            }
        }
    }
}
