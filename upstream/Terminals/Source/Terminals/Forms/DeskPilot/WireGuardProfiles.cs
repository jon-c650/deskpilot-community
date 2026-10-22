using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using TimeoutException = System.TimeoutException;
using Microsoft.Win32.SafeHandles;

namespace Terminals.Forms.DeskPilot
{
    internal sealed class ProfileResult { public string[] Names=new string[0];public string Imported,Error; }
    internal static class WireGuardProfiles
    {
        private const int MaximumConfiguration=1024*1024;
        [DllImport("kernel32.dll",SetLastError=true)] private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe,out uint pid);
        internal static async Task<ProfileResult> UpdateAsync(IWin32Window owner,bool import)
        {
            string source=null;
            if(import) using(var picker=new OpenFileDialog {Title="Import WireGuard configuration",Filter="WireGuard configuration (*.conf)|*.conf",CheckFileExists=true,Multiselect=false}) {
                if(picker.ShowDialog(owner)!=DialogResult.OK) return null;
                source=ValidateSourcePath(picker.FileName);
                var contents=ReadConfiguration(source);try {ValidateConfiguration(contents);} finally {Array.Clear(contents,0,contents.Length);}
            }
            string pipeName="DeskPilotWG-"+Guid.NewGuid().ToString("N");
            var security=new PipeSecurity();security.SetAccessRuleProtection(true,false);
            security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User,PipeAccessRights.FullControl,AccessControlType.Allow));
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null),PipeAccessRights.ReadWrite,AccessControlType.Allow));
            using(var pipe=new NamedPipeServerStream(pipeName,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,4096,4096,security)) {
                var connected=pipe.WaitForConnectionAsync();
                string arguments=pipeName+" "+Process.GetCurrentProcess().Id;
                string helper=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"DeskPilot.WireGuardHelper.exe");
                if(!File.Exists(helper)) throw new FileNotFoundException("The DeskPilot WireGuard helper is missing. Extract the entire Windows ZIP before using import or discovery.");
                using(var process=await Task.Run(()=>Process.Start(new ProcessStartInfo(helper,arguments) {UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden}))) {
                    if(process==null) throw new InvalidOperationException("Windows did not start the approved WireGuard operation.");
                    if(await Task.WhenAny(connected,Task.Delay(20000))!=connected) throw new TimeoutException("The WireGuard helper did not connect. No success is assumed.");
                    await connected;uint client;
                    if(!GetNamedPipeClientProcessId(pipe.SafePipeHandle,out client) || client!=(uint)process.Id) throw new InvalidOperationException("The WireGuard helper identity could not be verified.");
                    WriteRequest(pipe,import,source);
                    var pending=Task.Run(()=>ReadResult(pipe));
                    if(await Task.WhenAny(pending,Task.Delay(120000))!=pending) throw new TimeoutException("WireGuard import/discovery has not finished. Check WireGuard before retrying; the operation may still complete.");
                    var result=await pending;
                    if(!String.IsNullOrEmpty(result.Error)) throw new InvalidOperationException(result.Error);
                    result.Names=result.Names ?? new string[0];foreach(string name in result.Names) WireGuardControl.ValidateName(name);
                    if(result.Imported!=null) {WireGuardControl.ValidateName(result.Imported);result.Names=result.Names.Concat(new[]{result.Imported}).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();}
                    MergeCatalog(result.Names);return result;
                }
            }
        }
        internal static string ValidateSourcePath(string path)
        {
            if(String.IsNullOrEmpty(path) || !Path.IsPathRooted(path) || path.StartsWith(@"\\") || path.StartsWith(@"\??\")) throw new ProfileValidationException("Choose a local WireGuard .conf file.");
            string full=Path.GetFullPath(path);
            if(!String.Equals(Path.GetExtension(full),".conf",StringComparison.OrdinalIgnoreCase)) throw new ProfileValidationException("Choose a WireGuard .conf file; encrypted .conf.dpapi files are not portable imports.");
            WireGuardControl.ValidateName(Path.GetFileNameWithoutExtension(full));
            if(new DriveInfo(Path.GetPathRoot(full)).DriveType==DriveType.Network) throw new ProfileValidationException("Copy the configuration to a local drive before importing it.");
            for(string item=full;!String.IsNullOrEmpty(item);item=Path.GetDirectoryName(item)) {
                if((File.GetAttributes(item)&FileAttributes.ReparsePoint)!=0) throw new ProfileValidationException("Copy the configuration to a normal local folder before importing it; linked paths are not accepted for elevated import.");
                if(item==Path.GetPathRoot(item)) break;
            }
            return full;
        }
        private static byte[] ReadConfiguration(string path)
        {
            using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)) {
                if(file.Length<=0 || file.Length>MaximumConfiguration) throw new ProfileValidationException("The configuration is empty or exceeds the 1 MB import limit.");
                byte[] contents=new byte[(int)file.Length];int offset=0,read;
                while(offset<contents.Length && (read=file.Read(contents,offset,contents.Length-offset))>0) offset+=read;
                if(offset!=contents.Length) throw new IOException();return contents;
            }
        }
        internal static void ValidateConfiguration(byte[] contents)
        {
            if(contents.Length==0 || contents.Length>MaximumConfiguration) throw new ProfileValidationException("The configuration is empty or too large.");
            string text;
            if(contents.Length>=2 && contents[0]==255 && contents[1]==254) text=new UnicodeEncoding(false,true,true).GetString(contents,2,contents.Length-2);
            else if(contents.Length>=2 && contents[0]==254 && contents[1]==255) text=new UnicodeEncoding(true,true,true).GetString(contents,2,contents.Length-2);
            else text=new UTF8Encoding(false,true).GetString(contents).TrimStart('\uFEFF');
            int section=0;bool sawInterface=false,privateKey=false,peerKey=false;int peers=0;
            foreach(string raw in text.Split('\n')) {
                string line=raw.Split('#')[0].Trim();if(line.Length==0) continue;
                if(line.Equals("[Interface]",StringComparison.OrdinalIgnoreCase)) {
                    if(sawInterface) throw InvalidConfiguration();sawInterface=true;section=1;continue;
                }
                if(line.Equals("[Peer]",StringComparison.OrdinalIgnoreCase)) {
                    if(section==2 && !peerKey) throw InvalidConfiguration();section=2;peerKey=false;peers++;continue;
                }
                int separator=line.IndexOf('=');if(section==0 || separator<1) throw InvalidConfiguration();
                string key=line.Substring(0,separator).Trim().ToLowerInvariant(),value=line.Substring(separator+1).Trim();
                if(new[]{"preup","postup","predown","postdown"}.Contains(key)) throw new ProfileValidationException("Configuration command hooks are not accepted by DeskPilot. Remove PreUp/PostUp/PreDown/PostDown before importing.");
                if(value.Length==0) throw InvalidConfiguration();
                if(section==1) {
                    switch(key) {
                        case "privatekey":CheckKey(value);privateKey=true;break;
                        case "address":CheckAddresses(value);break;
                        case "dns":foreach(string item in List(value)) {System.Net.IPAddress ip;if(!System.Net.IPAddress.TryParse(item,out ip) && Uri.CheckHostName(item)==UriHostNameType.Unknown) throw InvalidConfiguration();}break;
                        case "listenport":CheckNumber(value,0,65535);break;
                        case "mtu":CheckNumber(value,576,65535);break;
                        case "table":uint table;if(value!="off" && value!="auto" && value!="main" && !UInt32.TryParse(value,out table)) throw InvalidConfiguration();break;
                        default:throw InvalidConfiguration();
                    }
                } else {
                    switch(key) {
                        case "publickey":CheckKey(value);peerKey=true;break;
                        case "presharedkey":CheckKey(value);break;
                        case "allowedips":CheckAddresses(value);break;
                        case "persistentkeepalive":if(value!="off") CheckNumber(value,0,65535);break;
                        case "endpoint":
                            int colon=value.LastIndexOf(':');if(colon<1) throw InvalidConfiguration();CheckNumber(value.Substring(colon+1),0,65535);
                            string host=value.Substring(0,colon);System.Net.IPAddress ip;
                            if(host.StartsWith("[") && host.EndsWith("]")) {if(!System.Net.IPAddress.TryParse(host.Substring(1,host.Length-2),out ip) || ip.AddressFamily!=System.Net.Sockets.AddressFamily.InterNetworkV6) throw InvalidConfiguration();}
                            else if(host.Contains(":") || Uri.CheckHostName(host)==UriHostNameType.Unknown) throw InvalidConfiguration();
                            break;
                        default:throw InvalidConfiguration();
                    }
                }
            }
            if(!sawInterface || !privateKey || peers==0 || section!=2 || !peerKey) throw InvalidConfiguration();
        }
        private static ProfileValidationException InvalidConfiguration() {return new ProfileValidationException("The configuration contains a missing, invalid or unsupported WireGuard setting. Check it in the official WireGuard application. Configuration contents were not logged or imported.");}
        private static string[] List(string value) {var items=value.Split(',').Select(item=>item.Trim()).ToArray();if(items.Any(item=>item.Length==0)) throw InvalidConfiguration();return items;}
        private static void CheckKey(string value) {try {if(Convert.FromBase64String(value).Length!=32) throw InvalidConfiguration();} catch(FormatException) {throw InvalidConfiguration();}}
        private static void CheckNumber(string value,int minimum,int maximum) {int number;if(!Int32.TryParse(value,out number) || number<minimum || number>maximum) throw InvalidConfiguration();}
        private static void CheckAddresses(string value)
        {
            foreach(string address in List(value)) {
                string[] parts=address.Split('/');System.Net.IPAddress ip;
                if(parts.Length>2 || !System.Net.IPAddress.TryParse(parts[0],out ip)) throw InvalidConfiguration();
                if(parts.Length==2) CheckNumber(parts[1],0,ip.AddressFamily==System.Net.Sockets.AddressFamily.InterNetworkV6 ? 128 : 32);
            }
        }
        internal static string[] DiscoverNames(string directory)
        {
            if(!Directory.Exists(directory)) throw new DirectoryNotFoundException();
            return Directory.EnumerateFiles(directory,"*.conf.dpapi",SearchOption.TopDirectoryOnly).Select(path=>Path.GetFileName(path).Substring(0,Path.GetFileName(path).Length-11)).Where(ValidName).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name=>name).Take(1000).ToArray();
        }
        private static bool ValidName(string name) {try {WireGuardControl.ValidateName(name);return true;} catch(ArgumentException) {return false;}}
        internal static void ImportIntoStore(string directory,string name,byte[] contents,int timeoutMs)
        {
            WireGuardControl.ValidateName(name);ValidateConfiguration(contents);
            if(!Directory.Exists(directory) || (File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0) throw new IOException("WireGuard store is unavailable.");
            string plain=Path.Combine(directory,name+".conf"),encrypted=plain+".dpapi";bool created=false;
            if(File.Exists(encrypted) || File.Exists(plain)) throw new IOException("A tunnel already uses this name.");
            try {
                using(var file=new FileStream(plain,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough)) {created=true;file.Write(contents,0,contents.Length);file.Flush(true);}
                var deadline=DateTime.UtcNow.AddMilliseconds(timeoutMs);
                do {
                    if(File.Exists(encrypted) && !File.Exists(plain)) return;
                    System.Threading.Thread.Sleep(100);
                } while(DateTime.UtcNow<deadline);
                throw new TimeoutException();
            } finally {
                // The manager normally consumes this file. On failure remove only the
                // unchanged staging file we created in its protected directory.
                if(created && File.Exists(plain)) {
                    try {if(File.ReadAllBytes(plain).SequenceEqual(contents)) File.Delete(plain);} catch { /* Still protected by WireGuard directory ACLs. */ }
                }
            }
        }
        internal static void MergeCatalog(IEnumerable<string> names,string directory=null)
        {
            directory=directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DeskPilot","WireGuardPreview");Directory.CreateDirectory(directory);
            string path=Path.Combine(directory,"profiles.txt");
            using(var gate=new FileStream(path+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
                var merged=names.Concat(File.Exists(path) ? File.ReadAllLines(path) : new string[0]).Select(name=>name.Trim()).Where(ValidName).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name=>name).ToArray();
                string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
                try {File.WriteAllLines(temporary,merged);if(File.Exists(path)) File.Replace(temporary,path,path+".bak");else File.Move(temporary,path);} finally {if(File.Exists(temporary)) File.Delete(temporary);}
            }
        }
        private static void WriteRequest(Stream stream,bool import,string source)
        {
            var writer=new BinaryWriter(stream,Encoding.UTF8,true);writer.Write((byte)1);writer.Write((byte)(import ? 2 : 1));
            byte[] bytes=Encoding.UTF8.GetBytes(source ?? "");if(bytes.Length>8192) throw new IOException("Profile source path is too long.");
            writer.Write(bytes.Length);writer.Write(bytes);writer.Flush();
        }
        private static string ReadText(BinaryReader reader,int maximum)
        {
            int length=reader.ReadInt32();if(length<0 || length>maximum) throw new IOException("Invalid profile response size.");
            byte[] bytes=reader.ReadBytes(length);if(bytes.Length!=length) throw new IOException("Incomplete profile response.");return new UTF8Encoding(false,true).GetString(bytes);
        }
        private static ProfileResult ReadResult(Stream stream)
        {
            var reader=new BinaryReader(stream,Encoding.UTF8,true);if(reader.ReadByte()!=1) throw new IOException("Unsupported profile response.");
            int count=reader.ReadInt32();if(count<0 || count>1000) throw new IOException("Invalid tunnel-name count.");
            var names=new string[count];for(int i=0;i<count;i++) names[i]=ReadText(reader,64);
            string imported=ReadText(reader,64),error=ReadText(reader,8192);
            return new ProfileResult {Names=names,Imported=imported.Length==0 ? null : imported,Error=error};
        }
        private sealed class ProfileValidationException : InvalidOperationException {internal ProfileValidationException(string message):base(message) {}}
    }
}
