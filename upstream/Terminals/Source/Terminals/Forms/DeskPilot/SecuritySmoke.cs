using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Terminals.Security;
namespace Terminals
{
    internal partial class MainForm
    {
        private void RunSecuritySmoke(string report)
        {
            const string password="CI-only-master-password-not-a-real-secret";
            // Construct the exact legacy v2 record independently of the modern writer.
            byte[] salt=Enumerable.Range(1,16).Select(i=>(byte)i).ToArray();
            byte[] validationSalt=Enumerable.Range(21,16).Select(i=>(byte)i).ToArray();
            byte[] dataKey,validation;
            using(var kdf=new Rfc2898DeriveBytes(password,salt,2121)) dataKey=kdf.GetBytes(32);
            using(var kdf=new Rfc2898DeriveBytes(password,validationSalt,2121)) validation=kdf.GetBytes(32);
            string legacy=Convert.ToBase64String(salt.Concat(validationSalt).Concat(validation).ToArray());
            string expected=Convert.ToBase64String(dataKey);
            FolderAssert(PasswordFunctions2.MasterPasswordIsValid(password,legacy),"Legacy master password cannot authenticate");
            FolderAssert(PasswordFunctions2.CalculateMasterPasswordKey(password,legacy)==expected,"Legacy data key changed");
            string oldCiphertext=PasswordFunctions2.EncryptPassword("CI-only-saved-credential",expected);
            string wrapped=MasterKeyEnvelope.Wrap(password,expected);
            string reopened=PasswordFunctions2.CalculateMasterPasswordKey(password,wrapped);
            FolderAssert(reopened==expected && PasswordFunctions2.DecryptPassword(oldCiphertext,reopened)=="CI-only-saved-credential","Master-key envelope upgrade lost an existing credential");
            FolderAssert(!PasswordFunctions2.MasterPasswordIsValid("wrong password",wrapped),"Wrong modern master password accepted");
            string[] parts=wrapped.Split('$');byte[] encrypted=Convert.FromBase64String(parts[4]);encrypted[0]^=1;parts[4]=Convert.ToBase64String(encrypted);
            FolderAssert(!PasswordFunctions2.MasterPasswordIsValid(password,String.Join("$",parts)),"Modified master-key envelope accepted");
            FolderAssert(!PasswordFunctions2.MasterPasswordIsValid(password,"DP3$600000$invalid"),"Malformed modern master-key envelope accepted");
            string directory=Path.Combine(Path.GetTempPath(),"DeskPilot-key-upgrade-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
            try {
                string path=Path.Combine(directory,"settings.config");
                var original=new System.Xml.Linq.XDocument(new System.Xml.Linq.XElement("configuration",new System.Xml.Linq.XElement("settings",new System.Xml.Linq.XAttribute("terminalsPassword",legacy),new System.Xml.Linq.XAttribute("encryptedDefaultPassword",oldCiphertext),new System.Xml.Linq.XElement("retained","unchanged"))));
                original.Save(path);byte[] before=File.ReadAllBytes(path);
                Terminals.Configuration.Settings.UpgradeMasterKeyFile(path,legacy,wrapped);
                var updated=System.Xml.Linq.XDocument.Load(path);updated.Root.Element("settings").SetAttributeValue("terminalsPassword",legacy);
                FolderAssert(System.Xml.Linq.XNode.DeepEquals(original,updated),"Key upgrade rewrote unrelated settings or ciphertext");
                string backup=Directory.GetFiles(directory,"*.dpapi").Single();
                FolderAssert(ProtectedData.Unprotect(File.ReadAllBytes(backup),null,DataProtectionScope.CurrentUser).SequenceEqual(before),"Key upgrade recovery copy is not exact or not Windows-user protected");
                byte[] good=File.ReadAllBytes(path);bool stale=false;
                try {Terminals.Configuration.Settings.UpgradeMasterKeyFile(path,legacy,wrapped);} catch(IOException) {stale=true;}
                FolderAssert(stale && File.ReadAllBytes(path).SequenceEqual(good),"Stale key upgrade overwrote newer settings");
                bool denied=false;
                using(var readLock=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)) {
                    try {Terminals.Configuration.Settings.UpgradeMasterKeyFile(path,wrapped,wrapped);} catch(IOException) {denied=true;}
                }
                FolderAssert(denied && File.ReadAllBytes(path).SequenceEqual(good),"Failed key replacement changed the original settings");
                File.AppendAllText(report,"PASS atomic master-key file upgrade changes only envelope; exact DPAPI recovery backup; stale and locked-file failures retain original settings\n");
            } finally {Directory.Delete(directory,true);}
            string fresh=PasswordFunctions2.CalculateStoredMasterPasswordKey(password);
            FolderAssert(MasterKeyEnvelope.IsModern(fresh) && PasswordFunctions2.MasterPasswordIsValid(password,fresh),"New master passwords do not use modern protection");
            string dpapi=PasswordFunctions2.EncryptPassword("CI-only-DPAPI","");
            FolderAssert(PasswordFunctions2.DecryptPassword(dpapi,"")=="CI-only-DPAPI","No-master-password Windows protection regressed");
            bool blocked=false;try {Unified.Serialize.DeSerializeBinary(new MemoryStream(new byte[]{1,2,3}));} catch(NotSupportedException) {blocked=true;}
            FolderAssert(blocked,"Unsafe binary deserialization is still enabled");
            var server=new Terminals.Network.Server(persistence,connectionManager);server.Stop();
            FolderAssert(!server.ServerOnline && !typeof(Terminals.Network.Server).GetFields(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Any(field=>field.FieldType==typeof(System.Net.Sockets.TcpListener)),"Legacy sharing listener remains available");
            FolderAssert(typeof(Newtonsoft.Json.JsonConvert).Assembly.GetName().Version.Major>=13,"Vulnerable JSON library packaged");
            FolderAssert(!Directory.GetFiles(AppDomain.CurrentDomain.BaseDirectory,"*SharpZipLib*",SearchOption.AllDirectories).Any(),"Unused vulnerable ZIP library packaged");
            int attempts=0;var sample=persistence.Factory.CreateFavorite();sample.Name="CI-only batch failure";
            var failures=Terminals.Forms.ConnectionsUiFactory.RunConnectionBatch(new[]{sample,sample},item=>{if(++attempts==1) throw new InvalidOperationException("CI-only simulated inherited settings error");});
            FolderAssert(attempts==2 && failures.Count==1,"One failed endpoint stopped the connection batch");
            File.AppendAllText(report,"PASS batch setup failure is reported while the next endpoint is attempted; no live connection used\n");
            File.AppendAllText(report,"PASS legacy master authentication, same-key envelope upgrade/old credential decryption, wrong-password/tamper/malformed rejection, modern new master, Windows DPAPI, disabled binary sharing, JSON 13 and no SharpZipLib\n");
        }
    }
}
