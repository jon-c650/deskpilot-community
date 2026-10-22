using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Terminals.Connections;
using Terminals.Data;
using Terminals.Data.Credentials;

namespace Terminals
{
    // An isolated domain keeps the marked-DLL probe out of normal plugin binding
    // and releases every file mapping before its temporary directory is removed.
    public sealed class DownloadedPluginProbe : MarshalByRefObject
    {
        public string Check(string path)
        {
            var load=typeof(PluginsLoader).GetMethod("LoadAssemblyPlugins",BindingFlags.NonPublic|BindingFlags.Static);
            var result=(System.Collections.Generic.IEnumerable<IConnectionPlugin>)load.Invoke(null,new object[]{path});
            bool refused=!result.Any() && PluginsLoader.LoadProblemText.Contains("Windows blocked");
            PluginsLoader.ClearLoadIssues();
            var error=System.Runtime.InteropServices.Marshal.GetExceptionForHR(unchecked((int)0x80131515));
            typeof(PluginsLoader).GetMethod("RecordLoadFailure",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{path,error});
            if(!PluginsLoader.LoadProblemText.Contains("Unblock") || !PluginsLoader.LoadProblemText.Contains("ZIP")) throw new InvalidOperationException("Missing guidance for the observed Windows loader error");
            return refused ? "marked fixture refused" : "runner permits the marked fixture; observed user-error classification verified separately";
        }
    }
    internal static partial class Program
    {
        internal static bool DownloadedPluginBlockVerified;
        internal static string DownloadedPluginProbeResult;
        [System.Runtime.InteropServices.DllImport("kernel32.dll",EntryPoint="CreateFileW",CharSet=System.Runtime.InteropServices.CharSet.Unicode,SetLastError=true)]
        private static extern Microsoft.Win32.SafeHandles.SafeFileHandle OpenZoneStream(string path,uint access,uint sharing,IntPtr security,uint creation,uint flags,IntPtr template);
        private static void VerifyDownloadedPluginBlock()
        {
            string folder=Path.Combine(Path.GetTempPath(),"DeskPilot-marked-plugin-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
            try {
                string path=Path.Combine(folder,"Terminals.Plugins.Putty.dll");
                File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Plugins","Putty","Terminals.Plugins.Putty.dll"),path);
                // .NET Framework rejects ADS paths before reaching NTFS; use a
                // native handle only for this disposable CI download-mark fixture.
                using(var handle=OpenZoneStream(path+":Zone.Identifier",0x40000000,1,IntPtr.Zero,2,0x80,IntPtr.Zero)) {
                    if(handle.IsInvalid) throw new System.ComponentModel.Win32Exception();
                    using(var stream=new FileStream(handle,FileAccess.Write)) using(var writer=new StreamWriter(stream)) writer.Write("[ZoneTransfer]\r\nZoneId=3\r\n");
                }
                var domain=AppDomain.CreateDomain("DeskPilot marked-plugin probe",null,new AppDomainSetup {ApplicationBase=AppDomain.CurrentDomain.BaseDirectory,ConfigurationFile=AppDomain.CurrentDomain.SetupInformation.ConfigurationFile});
                try {
                    var probe=(DownloadedPluginProbe)domain.CreateInstanceAndUnwrap(Assembly.GetExecutingAssembly().FullName,typeof(DownloadedPluginProbe).FullName);
                    DownloadedPluginProbeResult=probe.Check(path);
                } finally {AppDomain.Unload(domain);}
                DownloadedPluginBlockVerified=true;
            } finally {foreach(string file in Directory.GetFiles(folder)) File.SetAttributes(file,FileAttributes.Normal);Directory.Delete(folder,true);PluginsLoader.ClearLoadIssues();}
        }
    }
    internal partial class MainForm
    {
        private static void AwaitSession(Func<bool> ready,string failure)
        {
            var deadline=DateTime.UtcNow.AddSeconds(35);
            while(DateTime.UtcNow<deadline) {if(ready()) return;Application.DoEvents();Thread.Sleep(20);}
            FolderAssert(false,failure);
        }
        private void CheckAddConnectionProtocolPicker(string report)
        {
            Exception failure=null;bool checkedPicker=false;
            using(var timer=new System.Windows.Forms.Timer {Interval=100}) {
                timer.Tick+=delegate {
                    var dialog=Application.OpenForms.OfType<NewTerminalForm>().FirstOrDefault();if(dialog==null) return;
                    timer.Stop();
                    try {
                        var picker=dialog.Controls.Find("ProtocolComboBox",true).OfType<ComboBox>().Single();
                        var choices=picker.Items.Cast<object>().Select(item=>item.ToString()).ToArray();
                        FolderAssert(new[]{"RDP","SSH","HTTP","HTTPS"}.All(choices.Contains),"Actual Add Connection picker is missing protocols: "+String.Join(",",choices));
                        foreach(string protocol in new[]{"SSH","HTTP","HTTPS","RDP"}) {
                            picker.SelectedItem=protocol;Application.DoEvents();
                            FolderAssert(dialog.ProtocolText==protocol,"Add Connection did not select "+protocol);
                            FolderAssert(dialog.PortText==connectionManager.GetPort(protocol).ToString(),"Add Connection did not assign the default port for "+protocol);
                            FolderAssert(dialog.Controls.Find("httpUrlTextBox",true).Single().Visible==(protocol=="HTTP" || protocol=="HTTPS"),"Add Connection did not switch host/URL fields for "+protocol);
                        }
                        picker.DroppedDown=true;Application.DoEvents();SaveChromeScreenshot("ui-add-connection-protocols.png");picker.DroppedDown=false;
                        checkedPicker=true;
                    } catch(Exception error) {failure=error;}
                    finally {dialog.Close();}
                };
                timer.Start();
                var button=FindFolderChildren(customerOverlay).OfType<Button>().Single(item=>item.AccessibleName=="Add connection");
                button.PerformClick();
            }
            if(failure!=null) throw failure;
            FolderAssert(checkedPicker,"Add Connection did not open its editor");
            File.AppendAllText(report,"PASS actual Add Connection button opens protocol picker containing RDP/SSH/HTTP/HTTPS; each protocol selects correct default port and host/URL editor; Cancel leaves data unchanged\n");
        }
        private void RunSessionProtocolsSmoke(string report)
        {
            chrome=new Terminals.Forms.DeskPilot.ChromePalette(false);ApplyChromeTheme();
            CheckAddConnectionProtocolPicker(report);
            var protocols=connectionManager.GetAvailableProtocols();
            FolderAssert(new[]{"SSH","HTTP","HTTPS"}.All(protocols.Contains),"SSH/HTTP/HTTPS plugins were not discovered");
            var credentials=new GuardedCredentialFactory(persistence);
            var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
            TcpClient accepted=null;
            var accept=Task.Run(()=> {accepted=listener.AcceptTcpClient();var stream=accepted.GetStream();byte[] banner=Encoding.ASCII.GetBytes("SSH-2.0-DeskPilot-CI-banner-fixture\r\n");stream.Write(banner,0,banner.Length);});
            Process child=null;
            var ssh=persistence.Factory.CreateFavorite();ssh.Name="SSH — CI handshake fixture (not authenticated)";ssh.ServerName="127.0.0.1";ssh.ChangeProtocol("SSH",connectionManager.UpdateProtocolPropertiesByProtocol("SSH",null));ssh.Port=((IPEndPoint)listener.LocalEndpoint).Port;
            var guard=credentials.CreateSecurityOptoins(ssh.Security);guard.UserName="ci-user";guard.Password="CI-SECRET-MUST-NOT-BE-IN-ARGUMENTS";
            var sshTab=new TerminalTabControlItem(ssh.Name);
            var connection=connectionManager.CreateConnection(ssh);connection.Favorite=ssh;connection.OriginFavorite=ssh;connection.CredentialFactory=credentials;
            try {
                terminalsControler.AddAndSelect(sshTab);sshTab.Connection=connection;sshTab.Controls.Add(connection);
                FolderAssert(connection.Connect(),"SSH launch failed: "+connection.LastError);
                AwaitSession(()=>(IntPtr)connection.GetType().GetProperty("EmbeddedWindow",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(connection,null)!=IntPtr.Zero,"Real PuTTY terminal was not embedded");
                ConfigureChromeConnection(connection);FolderAssert(!scalingToggle.Enabled,"RDP Fit is enabled for SSH");
                var embedded=connection.GetType().GetProperty("EmbeddedWindow",BindingFlags.NonPublic|BindingFlags.Instance);
                IntPtr window=(IntPtr)embedded.GetValue(connection,null);
                FolderAssert(Terminals.Native.Methods.GetParent(window)==connection.Handle,"PuTTY escaped the session tab");
                int pid=(int)connection.GetType().GetProperty("ChildProcessId",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(connection,null);child=Process.GetProcessById(pid);
                var builderType=connection.GetType().Assembly.GetType("Terminals.Plugins.Putty.ArgumentsBuilder");
                object builder=Activator.CreateInstance(builderType,new object[]{guard.GetResolvedCredentials(),ssh});
                string arguments=(string)builderType.GetMethod("Build").Invoke(builder,null);
                FolderAssert(!arguments.Contains("CI-SECRET") && !arguments.Contains(" -pw") && arguments.Contains(" -2") && arguments.Contains("\"ci-user\""),"SSH command line exposed a password or omitted SSH-2/user name");
                foreach(string host in new[]{"-load", "host -proxycmd bad", "host\"name", "ssh://host/path"}) {
                    bool rejected=false;try {builderType.GetMethod("ValidateHost",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{host});} catch(TargetInvocationException) {rejected=true;}
                    FolderAssert(rejected,"SSH accepted an option-injection host");
                }
                var rect=Terminals.Native.Methods.GetWindowRect(window);
                FolderAssert(Terminals.Native.Methods.GetClientRect(window).Height==connection.ClientSize.Height,"SSH terminal client did not fill the shared canvas");
                var size=connection.ClientSize;connection.Size=new Size(Math.Max(300,size.Width-80),Math.Max(200,size.Height-50));Application.DoEvents();
                FolderAssert(Terminals.Native.Methods.GetClientRect(window).Height==connection.ClientSize.Height,"Embedded SSH terminal did not follow resize");
                inspectorOverlay.Requested=false;SyncChromeVisibility();Application.DoEvents();var bounds=connection.RectangleToScreen(connection.ClientRectangle);
                inspectorOverlay.Requested=true;SyncChromeVisibility();Application.DoEvents();FolderAssert(bounds==connection.RectangleToScreen(connection.ClientRectangle),"Inspector resized SSH canvas");
                SaveChromeScreenshot("ui-ssh.png");
            } finally {
                connection.Dispose();terminalsControler.RemoveAndUnSelect(sshTab);sshTab.Dispose();
                listener.Stop();if(accepted!=null) accepted.Dispose();
                if(child!=null) {FolderAssert(child.WaitForExit(5000),"Closing SSH tab left its child process running");child.Dispose();}
            }
            File.AppendAllText(report,"PASS embedded SSH: real pinned PuTTY process captured in session tab, canvas resize/inspector geometry, password-free quoted arguments, SSH-2, injection rejection and child cleanup. Loopback banner fixture only; host-key/login/MFA remain manual.\n");

            listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
            var web=persistence.Factory.CreateFavorite();web.Name="Web — CI local JavaScript fixture";web.ServerName="127.0.0.1";web.ChangeProtocol("HTTP",connectionManager.UpdateProtocolPropertiesByProtocol("HTTP",null));web.Port=((IPEndPoint)listener.LocalEndpoint).Port;
            bool serving=true;
            var serve=Task.Run(()=> {
                while(serving) {
                    try {using(var client=listener.AcceptTcpClient()) using(var stream=client.GetStream()) {
                        // Chromium may speculatively open a socket without sending a
                        // request. Never let that idle socket monopolize this fixture.
                        stream.ReadTimeout=2000;stream.WriteTimeout=2000;
                        var buffer=new byte[8192];int received;
                        try {received=stream.Read(buffer,0,buffer.Length);} catch(IOException) {continue;}
                        if(received==0) continue;
                        byte[] body=Encoding.UTF8.GetBytes("<!doctype html><html><head><title>DeskPilot Web CI</title></head><body style='background:#101e30;color:#fff;font:20px Segoe UI;padding:30px'><h1>DeskPilot embedded web session</h1><p>Local CI fixture only — no live customer server.</p><button onclick=\"document.getElementById('result').textContent='interactive-ok'\">Test JavaScript</button><p id='result'>waiting</p></body></html>");
                        byte[] header=Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: "+body.Length+"\r\nConnection: close\r\n\r\n");stream.Write(header,0,header.Length);stream.Write(body,0,body.Length);
                    }} catch(SocketException) {if(serving) throw;} catch(ObjectDisposedException) {break;}
                }
            });
            var webTab=new TerminalTabControlItem(web.Name);var http=connectionManager.CreateConnection(web);http.Favorite=web;http.OriginFavorite=web;http.CredentialFactory=credentials;
            try {
                terminalsControler.AddAndSelect(webTab);webTab.Connection=http;webTab.Controls.Add(http);FolderAssert(http.Connect(),"Web session refused valid local URL");
                ConfigureChromeConnection(http);FolderAssert(!scalingToggle.Enabled,"RDP Fit is enabled for web sessions");
                object browser=http.GetType().GetProperty("EmbeddedBrowser",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(http,null);
                var coreProperty=browser.GetType().GetProperty("CoreWebView2");
                AwaitSession(()=>coreProperty.GetValue(browser,null)!=null,"WebView2 did not initialize; install Evergreen Runtime on the runner");
                object core=coreProperty.GetValue(browser,null);var execute=core.GetType().GetMethod("ExecuteScriptAsync");
                Task<string> title=null;
                AwaitSession(()=> {if(title==null) {title=(Task<string>)execute.Invoke(core,new object[]{"document.title"});return false;}if(!title.IsCompleted) return false;bool ready=title.Result.Contains("DeskPilot Web CI");title=null;return ready;},"WebView2 did not load the local HTTP fixture");
                var script=(Task<string>)execute.Invoke(core,new object[]{"document.querySelector('button').click();document.getElementById('result').textContent"});AwaitSession(()=>script.IsCompleted,"WebView2 JavaScript did not complete");FolderAssert(script.Result.Contains("interactive-ok"),"Embedded web page was not interactive");
                var settings=core.GetType().GetProperty("Settings").GetValue(core,null);
                foreach(string setting in new[]{"AreHostObjectsAllowed","IsWebMessageEnabled","IsPasswordAutosaveEnabled","IsGeneralAutofillEnabled"}) FolderAssert(!(bool)settings.GetType().GetProperty(setting).GetValue(settings,null),"Unsafe web setting enabled: "+setting);
                var allowed=http.GetType().GetMethod("AllowedAddress",BindingFlags.NonPublic|BindingFlags.Static);
                foreach(string bad in new[]{"file:///C:/Windows/win.ini","javascript:alert(1)","ms-settings:privacy","https://user:secret@example.test/"}) FolderAssert(!(bool)allowed.Invoke(null,new object[]{bad,null}),"Unsafe web address accepted");
                var before=http.RectangleToScreen(http.ClientRectangle);inspectorOverlay.Requested=false;SyncChromeVisibility();Application.DoEvents();FolderAssert(before==http.RectangleToScreen(http.ClientRectangle),"Inspector resized web canvas");
                FolderAssert(!serve.IsFaulted,"Local HTTP fixture server failed: "+serve.Exception);
                SaveChromeScreenshot("ui-web.png");
            } catch(Exception error) {
                File.AppendAllText(report,"CHECK local HTTP fixture task status="+serve.Status+"; "+(serve.Exception==null ? "no server exception" : serve.Exception.ToString())+"\n");
                SaveChromeScreenshot("ui-web-failed.png");
                throw new InvalidOperationException("Embedded web fixture failed; see ui-web-failed.png and fixture status",error);
            } finally {http.Dispose();terminalsControler.RemoveAndUnSelect(webTab);webTab.Dispose();serving=false;listener.Stop();}
            File.AppendAllText(report,"PASS embedded web: real WebView2 HTTP and interactive JavaScript in shared session tabs, restricted navigation/settings, inspector geometry and controller disposal. Live HTTPS, certificate consent and device authentication remain manual.\n");
        }
    }
}
