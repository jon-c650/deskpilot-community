using System;
using System.IO;
using System.IO.Pipes;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Terminals.Forms.DeskPilot;
namespace Terminals
{
    internal partial class MainForm
    {
        [DllImport("kernel32.dll",EntryPoint="GetNamedPipeClientProcessId",SetLastError=true)]
        private static extern bool ProfileTestClientPid(SafePipeHandle pipe,out uint pid);
        private void RunWireGuardProfilesSmoke(string report)
        {
            string directory=Path.Combine(Path.GetTempPath(),"DeskPilot-profile-smoke-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
            // Synthetic test keys only. No service, protected WireGuard directory,
            // elevation, network interface or live tunnel is used by these tests.
            string text="[Interface]\nPrivateKey = "+Convert.ToBase64String(Enumerable.Repeat((byte)7,32).ToArray())+"\nAddress = 192.0.2.2/32\n[Peer]\nPublicKey = "+Convert.ToBase64String(Enumerable.Repeat((byte)9,32).ToArray())+"\nAllowedIPs = 192.0.2.0/24\nEndpoint = 192.0.2.1:51820\n";
            byte[] contents=Encoding.UTF8.GetBytes(text);
            try {
                File.AppendAllText(report,"CHECK WireGuard validation and disposable store tests\n");
                string source=Path.Combine(directory,"Fixture.conf");File.WriteAllBytes(source,contents);
                FolderAssert(WireGuardProfiles.ValidateSourcePath(source)==source,"Local configuration path rejected");
                WireGuardProfiles.ValidateConfiguration(contents);
                WireGuardProfiles.ValidateConfiguration(Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(text)).ToArray());
                foreach(string hook in new[]{"PreUp","PostUp","PreDown","PostDown"}) {
                    bool blocked=false;try {WireGuardProfiles.ValidateConfiguration(Encoding.UTF8.GetBytes(text+"\n "+hook+" = command-not-executed\n"));} catch(InvalidOperationException) {blocked=true;}
                    FolderAssert(blocked,"Configuration command hook accepted");
                }
                foreach(byte[] bad in new[]{new byte[0],new byte[1024*1024+1],Encoding.UTF8.GetBytes("[Interface]\nPrivateKey = invalid\n[Peer]\n")}) {
                    bool rejected=false;try {WireGuardProfiles.ValidateConfiguration(bad);} catch(InvalidOperationException) {rejected=true;}
                    FolderAssert(rejected,"Invalid or oversized configuration accepted");
                }
                string store=Path.Combine(directory,"test-store");Directory.CreateDirectory(store);
                bool timeout=false;try {WireGuardProfiles.ImportIntoStore(store,"TimeoutFixture",contents,10);} catch(TimeoutException) {timeout=true;}
                FolderAssert(timeout && !File.Exists(Path.Combine(store,"TimeoutFixture.conf")),"Failed import left an unchanged staging file");
                string protectedPath=Path.Combine(store,"Existing.conf.dpapi");byte[] original=new byte[]{1,2,3};File.WriteAllBytes(protectedPath,original);
                bool duplicate=false;try {WireGuardProfiles.ImportIntoStore(store,"Existing",contents,10);} catch(IOException) {duplicate=true;}
                FolderAssert(duplicate && File.ReadAllBytes(protectedPath).SequenceEqual(original),"Import overwrote an existing profile");
                // Simulate only the manager's protection acknowledgement, using a
                // disposable test directory. Actual official-manager import is manual.
                var acknowledgement=Task.Run(()=> {
                    string staging=Path.Combine(store,"ImportedFixture.conf");var deadline=DateTime.UtcNow.AddSeconds(5);
                    while(DateTime.UtcNow<deadline) {
                        try {
                            if(File.Exists(staging)) {
                                byte[] bytes=File.ReadAllBytes(staging);if(bytes.Length!=contents.Length) continue;
                                File.WriteAllBytes(staging+".dpapi",ProtectedData.Protect(bytes,null,DataProtectionScope.CurrentUser));File.Delete(staging);return;
                            }
                        } catch(IOException) {}
                        System.Threading.Thread.Sleep(10);
                    }
                    throw new TimeoutException("Simulated manager did not receive the staging file");
                });
                WireGuardProfiles.ImportIntoStore(store,"ImportedFixture",contents,5000);acknowledgement.GetAwaiter().GetResult();
                FolderAssert(WireGuardProfiles.DiscoverNames(store).Contains("ImportedFixture") && File.ReadAllBytes(source).SequenceEqual(contents),"Profile discovery missed imported name or import changed the source");
                string catalog=Path.Combine(directory,"catalog");Directory.CreateDirectory(catalog);File.WriteAllLines(Path.Combine(catalog,"profiles.txt"),new[]{"ManualTunnel"});
                WireGuardProfiles.MergeCatalog(new[]{"ImportedFixture","ImportedFixture"},catalog);
                var names=File.ReadAllLines(Path.Combine(catalog,"profiles.txt"));
                FolderAssert(names.Length==2 && names.Contains("ManualTunnel") && names.Contains("ImportedFixture"),"Native catalog update lost manual entries or duplicated tunnel names");
                string helper=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"DeskPilot.WireGuardHelper.exe");
                FolderAssert(File.Exists(helper) && FileVersionInfo.GetVersionInfo(helper).ProductName=="DeskPilot","Branded native profile helper missing from ZIP");
                bool native=false;try {AssemblyName.GetAssemblyName(helper);} catch(BadImageFormatException) {native=true;}
                FolderAssert(native,"Elevated helper loads the managed app/runtime");
                string testHelper=Path.Combine(Path.GetTempPath(),"DeskPilot.WireGuardPipeTests.exe");
                FolderAssert(File.Exists(testHelper),"Native IPC test helper was not built");
                File.AppendAllText(report,"CHECK WireGuard native pipe tests\n");
                foreach(bool wrongParent in new[]{false,true}) {
                    File.AppendAllText(report,"CHECK native pipe wrongParent="+wrongParent+"\n");
                    string name="DeskPilotWG-"+Guid.NewGuid().ToString("N");
                    using(var pipe=new NamedPipeServerStream(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous)) {
                        File.AppendAllText(report,"CHECK pipe created\n");
                        var connected=pipe.WaitForConnectionAsync();
                        File.AppendAllText(report,"CHECK pipe listening\n");
                        using(var process=Process.Start(new ProcessStartInfo(testHelper,name+" "+(wrongParent ? 1 : Process.GetCurrentProcess().Id)) {UseShellExecute=false,CreateNoWindow=true})) {
                            File.AppendAllText(report,"CHECK native child started PID="+process.Id+"\n");
                            FolderAssert(connected.Wait(10000),"Native helper did not connect to the test pipe");uint client;
                            File.AppendAllText(report,"CHECK native child connected\n");
                            if(!wrongParent) FolderAssert(ProfileTestClientPid(pipe.SafePipeHandle,out client) && client==(uint)process.Id,"Native helper process identity mismatch");
                            if(!wrongParent) {
                                File.AppendAllText(report,"CHECK native identity verified\n");
                                var writer=new BinaryWriter(pipe,Encoding.UTF8,true);writer.Write((byte)1);writer.Write((byte)3);writer.Write(0);writer.Flush(); // deliberately unsupported operation; no protected paths touched
                                File.AppendAllText(report,"CHECK invalid request sent\n");
                                var read=Task.Run(()=> (ProfileResult)typeof(WireGuardProfiles).GetMethod("ReadResult",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{pipe}));
                                FolderAssert(read.Wait(10000) && !String.IsNullOrEmpty(read.Result.Error) && read.Result.Names.Length==0,"Native helper did not safely reject the unsupported operation");
                            }
                            FolderAssert(process.WaitForExit(10000) && process.ExitCode!=0,"Native helper accepted an invalid request or wrong parent process");
                        }
                    }
                }
                File.AppendAllText(report,"PASS standalone native helper packaging/no CLR, real local-pipe framing and process identity, invalid request and wrong-parent rejection; no UAC/service/profile changes\n");
                File.AppendAllText(report,"PASS native WireGuard import safeguards: local .conf validation, command-hook/invalid/oversize rejection, duplicate protection, source preservation, staging timeout cleanup, protection acknowledgement and names-only catalog merge; UAC/official manager remain manual\n");
            } finally {Directory.Delete(directory,true);}
        }
    }
}
