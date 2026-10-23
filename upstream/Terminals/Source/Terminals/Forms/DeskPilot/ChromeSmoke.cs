using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.ServiceProcess;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Terminals.Configuration;
using Terminals.Connections;
using Terminals.Data;
using Terminals.Forms.DeskPilot;

namespace Terminals
{
    internal static partial class Program
    {
        // CI-only entry; refuses an existing profile. Never bypass authentication in a user session.
        internal static bool ChromeSmokeRequested { get { return Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true" && Environment.GetEnvironmentVariable("DESKPILOT_UI_SMOKE") == "1"; } }
        private static void RunChromeSmoke()
        {
            string result = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ui-smoke.txt");
            try {
                string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DeskPilot","IsolatedPreview");
                if(Directory.Exists(root)) throw new InvalidOperationException("Smoke test requires a fresh CI profile; refusing existing preview data.");
                SetApplicationProperties(); Info.SetApplicationVersion();
                var settings = Settings.Instance; settings.FileLocations.AssignCustomFileLocations(null,null,null);
                VerifyDownloadedPluginBlock();
                var manager = new ConnectionManager(new PluginsLoader(settings));
                var icons = new FavoriteIcons(manager);
                var persistence = new PersistenceFactory(settings,manager,icons).CreatePersistence();
                if(!persistence.Initialize()) throw new InvalidOperationException("Test persistence initialization failed");
                var customer = persistence.Factory.CreateGroup("SCVCC (CI sample)"); persistence.Groups.Add(customer);
                var servers = persistence.Factory.CreateGroup("Servers"); servers.Parent = customer; persistence.Groups.Add(servers);
                var favorite = persistence.Factory.CreateFavorite(); favorite.Name = "SCVCC-DC1 (CI sample)"; favorite.ServerName = "192.0.2.10"; favorite.Notes = "CI fixture — no live server or credentials.";
                persistence.Favorites.Add(favorite); persistence.Favorites.UpdateFavorite(favorite,new System.Collections.Generic.List<IGroup>{servers});
                foreach(string protocol in new[]{"SSH","HTTP","HTTPS"}) {
                    var endpoint=persistence.Factory.CreateFavorite();endpoint.Name=protocol+" (CI sample)";
                    endpoint.ServerName=protocol=="SSH" ? "192.0.2.20" : protocol=="HTTP" ? "192.0.2.30" : "192.0.2.40";
                    endpoint.ChangeProtocol(protocol,manager.UpdateProtocolPropertiesByProtocol(protocol,null));
                    endpoint.Port=protocol=="SSH" ? 22 : protocol=="HTTP" ? 80 : 443;
                    persistence.Favorites.Add(endpoint);persistence.Favorites.UpdateFavorite(endpoint,new System.Collections.Generic.List<IGroup>{servers});
                }
                foreach(string name in new[]{"Bayside","Country View HOA","Datacenter","Internal","North Ridge","Pinecrest","Riverside","Tanglewood"}) persistence.Groups.Add(persistence.Factory.CreateGroup(name+" (CI sample)"));
                // Reproduce returning users, including older previews that saved every strip hidden.
                var strips=new ToolStripSettings();
                foreach(string name in new[]{"menuStrip","toolbarStd","tsRemoteToolbar","favoriteToolBar","SpecialCommandsToolStrip"})
                    strips.Add(strips.Count,new ToolStripSetting {Name=name,Dock="Top",Row=strips.Count,Visible=false,Left=3,Top=24*strips.Count});
                strips.Save();
                MainForm.VerifyClassicLayoutRecovery(persistence,manager,icons);
                var form = new MainForm(persistence,manager,icons);
                form.Shown += delegate { form.BeginInvoke(new Action(delegate {
                    try { form.RunChromeSmokeChecks(favorite,result); }
                    catch(Exception error) { File.AppendAllText(result,"FAIL "+error+Environment.NewLine); Environment.ExitCode=1; }
                    finally { form.Dispose(); Application.ExitThread(); }
                })); };
                form.HandleCommandLineActions(new Terminals.CommandLine.CommandLineArgs());
                Application.Run(form);
            } catch(Exception error) { File.WriteAllText(result,"FAIL "+error); Environment.ExitCode=1; }
        }
    }
    internal partial class MainForm
    {
        internal static void VerifyClassicLayoutRecovery(IPersistence persistence,ConnectionManager manager,FavoriteIcons icons)
        {
            File.WriteAllText(ChromeSettingsPath,"classic");
            using(var classic=new MainForm(persistence,manager,icons)) {
                classic.Show();Application.DoEvents();
                if(classic.chromeHost!=null || !classic.menuStrip.Visible || classic.menuStrip.Parent!=classic)
                    throw new InvalidOperationException("Classic startup with hidden saved toolbars lost the recovery menu");
                var menu=classic.viewToolStripMenuItem.DropDownItems.OfType<ToolStripMenuItem>().Single(i=>i.Text=="Interface on next launch");
                var dark=menu.DropDownItems.OfType<ToolStripMenuItem>().Single(i=>(string)i.Tag=="dark");
                dark.PerformClick();
                if(classic.ReadChromeMode()!="dark" || !dark.Checked) throw new InvalidOperationException("Classic View menu failed to persist DeskPilot recovery");
                classic.Hide();
            }
        }
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")] private static extern bool IsChild(IntPtr parent,IntPtr child);
        internal void RunChromeSmokeChecks(IFavorite favorite,string report)
        {
            WindowState = FormWindowState.Maximized; Application.DoEvents();
            File.WriteAllText(report,"Integrated DeskPilot smoke; actual disconnected RDP ActiveX; no live server.\n");
            if(chromeHost==null || ReadChromeMode()!="dark") throw new InvalidOperationException("Relaunch did not restore DeskPilot after Classic View selection");
            File.AppendAllText(report,"PASS actual Classic startup with hidden saved menu, View interface selection, persisted dark mode and DeskPilot relaunch recovery\n");
            chromeTimer.Stop();
            FolderAssert(Program.DownloadedPluginBlockVerified,"Downloaded plugin protection was not exercised");
            File.AppendAllText(report,"PASS Windows downloaded-plugin block guidance: observed HRESULT 0x80131515 maps to ZIP Properties/Unblock instructions; no trust-policy change. CI marked-DLL probe: "+Program.DownloadedPluginProbeResult+"\n");
            RunSecuritySmoke(report);
            RunReleaseCompatibilitySmoke(report);
            VerifyDeskPilotBranding(report);
            System.Threading.Tasks.Task.Run(() => RunWireGuardProfilesSmoke(report)).GetAwaiter().GetResult();
            if(menuStrip.Parent!=this || !menuStrip.Visible || menuStrip.Height<20) throw new InvalidOperationException("Normal startup with saved hidden toolbar layout lost the main menu bar");
            File.AppendAllText(report,"PASS normal launch with saved all-hidden toolbar layout keeps main menu visible\n");
            // Load the same wrapper used by the RDP plugin; never call Connect.
            var wrapper = Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Plugins","Rdp","AxInterop.MSTSCLib.dll"));
            var type = wrapper.GetType("AxMSTSCLib.AxMsRdpClient6NotSafeForScripting",true);
            var ax = (Control)Activator.CreateInstance(type);
            ((ISupportInitialize)ax).BeginInit();
            var tab = new TerminalTabControlItem(favorite.Name); tab.Controls.Add(ax); ax.Dock = DockStyle.Fill;
            terminalsControler.AddAndSelect(tab); ((ISupportInitialize)ax).EndInit();
            // Match production ordering: tab selected first, connection assigned later.
            tab.Connection=new SmokeConnection(favorite);
            RecoverInspectorSelection();
            if(inspectedFavorite==null || !inspectedFavorite.StoreIdEquals(favorite)) throw new InvalidOperationException("Late connection assignment left inspector empty");
            foreach(Control action in inspectorActions.Controls) if(!action.Enabled) throw new InvalidOperationException("Active host action is disabled");
            SelectInspector(null); // A group/header selection must not blank an active session's actions.
            if(inspectedFavorite==null || !inspectorConnect.Enabled) throw new InvalidOperationException("Group selection blanked active host inspector");
            File.AppendAllText(report,"PASS late-bound active tab enables inspector actions\n");
            favsList1.RefreshChromeCounts(); favsList1.ExpandChromeFixture();
            favsList1.VerifyProtocolIconFixtures();
            File.AppendAllText(report,"PASS customer tree renders distinct RDP monitor, SSH terminal, HTTP globe and HTTPS lock icons from saved protocol; no endpoint data rewritten\n");
            favsList1.CheckChromeTreeCommands();favsList1.ExpandChromeFixture();
            File.AppendAllText(report,"PASS original tree command remains callable from compact menu\n");
            int invalidations=favsList1.ChromeTreeInvalidations;
            for(int poll=0;poll<4;poll++) favsList1.RefreshChromeCounts();
            if(favsList1.ChromeTreeInvalidations!=invalidations) throw new InvalidOperationException("Unchanged counts repeatedly repaint customer tree");
            if(!favsList1.ChromeBufferingEnabled) throw new InvalidOperationException("Native tree double buffering is disabled");
            File.AppendAllText(report,"PASS native tree buffering and no unchanged-count repaint\n");
            // Exercise the compiled RDP plugin's scaling facade on the actual ActiveX, without connecting.
            var pluginAssembly=Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Plugins","Rdp","Terminals.Plugins.Rdp.dll"));
            var pluginType=pluginAssembly.GetType("Terminals.Connections.RDPConnection",true);
            var plugin=(Connection)Activator.CreateInstance(pluginType,true);
            plugin.Favorite=favorite.Copy(); plugin.OriginFavorite=favorite;
            pluginType.GetField("client",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(plugin,ax);
            tab.Connection=plugin; ConfigureChromeConnection(plugin);
            var scaling=(IViewportScaling)plugin;
            if(!scaling.FitToViewport) throw new InvalidOperationException("DeskPilot did not enable real RDP SmartSizing");
            Application.DoEvents();
            type.GetProperty("DesktopWidth").SetValue(ax,ax.Width,null); type.GetProperty("DesktopHeight").SetValue(ax,ax.Height,null);
            Rectangle bounds = ax.RectangleToScreen(ax.ClientRectangle); int sizeEvents=0; ax.SizeChanged += delegate {sizeEvents++;};
            int width=(int)type.GetProperty("DesktopWidth").GetValue(ax,null), height=(int)type.GetProperty("DesktopHeight").GetValue(ax,null);
            scalingToggle.PerformClick(); if(scaling.FitToViewport) throw new InvalidOperationException("100% control failed");
            scalingToggle.PerformClick(); if(!scaling.FitToViewport) throw new InvalidOperationException("Fit control failed");
            if((int)type.GetProperty("DesktopWidth").GetValue(ax,null)!=width || (int)type.GetProperty("DesktopHeight").GetValue(ax,null)!=height) throw new InvalidOperationException("Scaling renegotiated desktop dimensions");
            File.AppendAllText(report,"PASS real RDP SmartSizing and Fit/100% controls preserve remote dimensions\n");
            var sessionButtons=commandOverlay.Controls.OfType<FlowLayoutPanel>().Single().Controls.OfType<Button>().ToArray();
            if(!sessionButtons.Select(button=>button.AccessibleName).SequenceEqual(new[]{"Disconnect","Fullscreen","Fit ✓"})) throw new InvalidOperationException("Session header contains misleading or duplicate actions");
            UpdateChromeScaling();
            if(sessionDisconnect.Enabled) throw new InvalidOperationException("Disconnect is enabled without a connected session");
            File.AppendAllText(report,"PASS header exposes only labeled session actions and disables disconnected-session control\n");
            if(customerOverlay.TopLevel || customerOverlay.Parent!=chromeWorkspace || commandOverlay.TopLevel || commandOverlay.Parent!=chromeHeader) throw new InvalidOperationException("Sidebar/session controls are not embedded in the application");
            if(customerOverlay.Right!=chromeHost.Left || customerOverlay.Width!=286) throw new InvalidOperationException("RDP host does not start at the stationary sidebar edge: sidebar="+customerOverlay.Bounds+" host="+chromeHost.Bounds);
            if(!chromeSearch.Visible || chromeSearch.Width<100 || chromeSearch.Height<18 || chromeSearch.Top<0) throw new InvalidOperationException("Compact header search is not visible");
            File.AppendAllText(report,"PASS stationary customer sidebar reserves space at the RDP edge; session controls belong to main header\n");
            var windows = new[]{inspectorOverlay};
            for(int mask=0;mask<2;mask++) {
                for(int i=0;i<windows.Length;i++) windows[i].Requested=(mask&(1<<i))!=0;
                PositionChrome(); SyncChromeVisibility(); Application.DoEvents();
                if(ax.RectangleToScreen(ax.ClientRectangle)!=bounds || sizeEvents!=0 || width!=(int)type.GetProperty("DesktopWidth").GetValue(ax,null) || height!=(int)type.GetProperty("DesktopHeight").GetValue(ax,null)) throw new InvalidOperationException("Canvas changed at overlay mask "+mask);
                // Commands can overlap other overlays; check each window alone for native hit testing.
                File.AppendAllText(report,"PASS geometry mask="+mask+" resizeEvents="+sizeEvents+Environment.NewLine);
            }
            if(menuStrip.Parent!=this || !menuStrip.Visible || toolStripContainer.Visible) throw new InvalidOperationException("Main menu bar is missing or legacy utility bars are exposed");
            if(!toolsToolStripMenuItem.DropDownItems.Cast<ToolStripItem>().SequenceEqual(new ToolStripItem[]{optionsToolStripMenuItem,credentialManagementToolStripMenuItem,openLogFolderToolStripMenuItem})) throw new InvalidOperationException("Tools menu contains unexpected utility actions");
            if(captureTerminalScreenToolStripMenuItem.ShortcutKeys!=Keys.None) throw new InvalidOperationException("Legacy capture shortcut is still exposed");
            string stripPath=FileLocations.ToolStripsFullFileName;
            string savedStrips=File.Exists(stripPath) ? File.ReadAllText(stripPath) : null;
            toolStripContainer.SaveLayout();
            if(savedStrips!=(File.Exists(stripPath) ? File.ReadAllText(stripPath) : null)) throw new InvalidOperationException("Preview changed classic toolbar layout");
            // Exercise the original File/New Favorite command through its real editor.
            bool editorOpened=false;
            using(var closeEditor=new Timer {Interval=100}) {
                closeEditor.Tick+=delegate {
                    foreach(Form dialog in Application.OpenForms.Cast<Form>().ToArray()) if(!(dialog is MainForm) && !(dialog is OverlayWindow) && dialog.Visible) {editorOpened=true;dialog.DialogResult=DialogResult.Cancel;dialog.Close();}
                };
                closeEditor.Start();newTerminalToolStripMenuItem.PerformClick();closeEditor.Stop();
            }
            if(!editorOpened) throw new InvalidOperationException("Main File menu did not open the existing favorite editor");
            foreach(var retained in new[]{optionsToolStripMenuItem,credentialManagementToolStripMenuItem}) {
                bool opened=false;
                using(var closeDialog=new Timer {Interval=100}) {
                    closeDialog.Tick+=delegate {
                        foreach(Form dialog in Application.OpenForms.Cast<Form>().ToArray()) if(!(dialog is MainForm) && !(dialog is OverlayWindow) && dialog.Visible) {opened=true;dialog.DialogResult=DialogResult.Cancel;dialog.Close();}
                    };
                    closeDialog.Start();retained.PerformClick();closeDialog.Stop();
                }
                if(!opened) throw new InvalidOperationException("Retained Tools command did not open: "+retained.Text);
            }
            File.AppendAllText(report,"PASS retained Options and Credentials open their original dialogs and cancel cleanly\n");
            toolsToolStripMenuItem.ShowDropDown();Application.DoEvents();SaveChromeScreenshot("ui-menu.png");toolsToolStripMenuItem.HideDropDown();
            bool aboutOpened=false;
            using(var closeAbout=new Timer {Interval=100}) {
                closeAbout.Tick+=delegate {
                    foreach(Form dialog in Application.OpenForms.Cast<Form>().ToArray()) if(dialog is AboutForm) {aboutOpened=dialog.Visible && dialog.Text=="About DeskPilot";SaveChromeScreenshot("ui-about.png");dialog.Close();}
                };
                closeAbout.Start();aboutToolStripMenuItem.PerformClick();closeAbout.Stop();
            }
            if(!aboutOpened) throw new InvalidOperationException("Tools menu did not invoke the original About command");
            File.AppendAllText(report,"PASS main menu File editor and About commands; Tools has only Options, Credentials and Open log folder; classic layout preserved\n");
            foreach(var window in windows.Where(w=>!w.Embedded)) {
                foreach(var other in windows.Where(w=>!w.Embedded)) {other.Requested=false;other.Hide();}
                ShowChrome(window); Application.DoEvents();
                var point = new Point(window.Right-8,window.Top+8); var hit=WindowFromPoint(point);
                if(hit!=window.Handle && !IsChild(window.Handle,hit)) throw new InvalidOperationException("Native overlay hit-test failed: "+window.Text);
                File.AppendAllText(report,"PASS native hit-test "+window.Text+Environment.NewLine);
            }
            foreach(var window in windows.Where(w=>!w.Embedded)) {
                ShowChrome(window); Application.DoEvents();
                if(window.HideButton==null || !window.HideButton.Visible) throw new InvalidOperationException("No visible hide button: "+window.Text);
                window.HideButton.PerformClick(); SyncChromeVisibility(); Application.DoEvents();
                if(window.Requested || window.Visible) throw new InvalidOperationException("Hide button failed or polling reopened overlay: "+window.Text);
            }
            inspectorToggle.PerformClick(); Application.DoEvents();
            if(!customerOverlay.Visible || !inspectorOverlay.Visible || !commandOverlay.Visible) throw new InvalidOperationException("Fixed sidebar, integrated controls or inspector edge missing");
            HideAllChrome(); SyncChromeVisibility();
            foreach(var window in windows.Where(w=>!w.Embedded)) if(window.Visible || window.Requested) throw new InvalidOperationException("Hide all failed");
            if(ax.RectangleToScreen(ax.ClientRectangle)!=bounds || sizeEvents!=0) throw new InvalidOperationException("User hide/show controls resized viewport");
            File.AppendAllText(report,"PASS inspector hide/reopen and focus canvas preserve viewport and fixed sidebar\n");
            if(WireGuardControl.ChooseRequest(null,true)!=TunnelRequest.Install || WireGuardControl.ChooseRequest(null,false)!=TunnelRequest.None ||
               WireGuardControl.ChooseRequest(ServiceControllerStatus.Stopped,true)!=TunnelRequest.Start || WireGuardControl.ChooseRequest(ServiceControllerStatus.Running,false)!=TunnelRequest.Stop)
                throw new InvalidOperationException("VPN missing-service/installed-service plan is wrong");
            foreach(string bad in new[]{"../other","a\\b","a\"b","a;whoami","",".."}) {
                bool rejected=false; try {WireGuardControl.ValidateName(bad);} catch(ArgumentException) {rejected=true;}
                if(!rejected) throw new InvalidOperationException("Unsafe tunnel name accepted");
            }
            string install=WireGuardControl.InstallArguments(@"C:\Program Files\WireGuard","CI-Test");
            if(!install.StartsWith("/installtunnelservice \"") || !install.EndsWith("CI-Test.conf.dpapi\"")) throw new InvalidOperationException("Encrypted-profile activation arguments invalid");
            File.AppendAllText(report,"PASS VPN activation/start/stop planning and tunnel-name validation; UAC/live activation NOT exercised\n");
            customerOverlay.Requested=true; inspectorOverlay.Requested=true; commandOverlay.Requested=true;
            PositionChrome(); SyncChromeVisibility(); Application.DoEvents(); SaveChromeScreenshot("ui-dark.png");
            chrome = new ChromePalette(true); ApplyChromeTheme(); Application.DoEvents();
            if(ax.RectangleToScreen(ax.ClientRectangle)!=bounds || sizeEvents!=0) throw new InvalidOperationException("Theme changed viewport");
            SaveChromeScreenshot("ui-light.png");
            File.AppendAllText(report,"PASS dark/light theme preserves viewport\nConnected RDP regression remains manual.\n");
            favsList1.SearchChrome("192.0.2.10");
            var deadline=DateTime.UtcNow.AddSeconds(10);
            while(favsList1.ChromeSearchResultCount < 0 && DateTime.UtcNow < deadline) { Application.DoEvents(); System.Threading.Thread.Sleep(20); }
            if(favsList1.ChromeSearchResultCount != 1) throw new InvalidOperationException("Address search did not return the fixture");
            favsList1.SearchChrome(""); Application.DoEvents();
            File.AppendAllText(report,"PASS existing search pipeline finds saved address and clears\n");
            bool modalChecked=false;
            using(var dialog = new Form { Text="CI modal lifecycle",Size=new Size(200,100),StartPosition=FormStartPosition.CenterParent }) {
                dialog.Shown += delegate { dialog.BeginInvoke(new Action(delegate {
                    bool wasVisible=customerOverlay.Visible; SyncChromeVisibility();
                    modalChecked=dialog.Visible && customerOverlay.Visible==wasVisible;
                    dialog.Close();
                })); };
                dialog.ShowDialog(customerOverlay);
            }
            if(!modalChecked) throw new InvalidOperationException("Modal lifecycle changed overlay ownership/visibility");
            File.AppendAllText(report,"PASS modal dialog remains visible over its overlay owner\n");
            // Merely showing or searching cannot write notes; explicit Save must preserve identity and memberships.
            var stored = persistence.Favorites[favorite.Id]; var identity = stored.Id; var credential = stored.Security.Credential; var groups = stored.GroupNames;
            string originalNotes = stored.Notes;
            notesEditor.Text = "Updated CI test note";
            if(stored.Notes != originalNotes) throw new InvalidOperationException("Editor changed notes before Save");
            SaveInspectorNotes();
            stored = persistence.Favorites[identity];
            if(stored == null || stored.Notes != "Updated CI test note" || stored.Security.Credential != credential || stored.GroupNames != groups) throw new InvalidOperationException("Notes save lost identity, credentials or group membership");
            File.AppendAllText(report,"PASS explicit notes save preserves favorite identity, credential association and groups\n");
            RunFolderInheritanceSmoke(favorite,report);
            var originalDisplay=favorite.Display.DesktopSize;
            WindowState=FormWindowState.Normal;
            Bounds=new Rectangle(Screen.PrimaryScreen.WorkingArea.Left+60,Screen.PrimaryScreen.WorkingArea.Top+60,Math.Max(700,Screen.PrimaryScreen.WorkingArea.Width-120),Math.Max(500,Screen.PrimaryScreen.WorkingArea.Height-120));
            Application.DoEvents();
            if(ax.RectangleToScreen(ax.ClientRectangle)==bounds || ax.Size!=tab.DisplayRectangle.Size || !scaling.FitToViewport) throw new InvalidOperationException("Real host resize did not fill and scale the RDP viewport");
            WindowState=FormWindowState.Maximized; Application.DoEvents();
            sessionButtons.Single(button=>button.AccessibleName=="Fullscreen").PerformClick(); Application.DoEvents();
            if(!FullScreen) throw new InvalidOperationException("Labeled fullscreen action failed");
            if(menuStrip.Visible || chromeHeader.Visible || chromeStatus.Visible || chromeHost.ClientSize!=ClientSize || !toolbarEdge.Visible || !scaling.FitToViewport) throw new InvalidOperationException("Fullscreen did not expose the complete scalable RDP viewport and exit handle");
            toolbarToggle.PerformClick(); Application.DoEvents();
            if(FullScreen || menuStrip.Parent!=this || !menuStrip.Visible || !chromeHeader.Visible || !chromeStatus.Visible) throw new InvalidOperationException("Fullscreen exit edge did not restore the interface");
            if((int)type.GetProperty("DesktopWidth").GetValue(ax,null)!=width || (int)type.GetProperty("DesktopHeight").GetValue(ax,null)!=height || favorite.Display.DesktopSize!=originalDisplay) throw new InvalidOperationException("Resize/fullscreen modified remote or saved resolution");
            File.AppendAllText(report,"PASS host resize, maximize, fullscreen and exit edge preserve SmartSizing and remote/saved dimensions\nLive connected scaling remains a manual gate.\n");
            plugin.Dispose();
            RunSessionProtocolsSmoke(report);
        }
        private sealed class SmokeConnection : IConnection
        {
            internal SmokeConnection(IFavorite original) { OriginFavorite=original; Favorite=original.Copy(); }
            public IFavorite Favorite {get;private set;}
            public IFavorite OriginFavorite {get;set;}
            public bool Connected {get {return false;}}
            public bool Connect() {throw new InvalidOperationException("CI fixture cannot connect");}
            public void ChangeDesktopSize(DesktopSize size) {throw new InvalidOperationException("CI fixture cannot resize a session");}
            public void Dispose() {}
        }
        private void VerifyDeskPilotBranding(string report)
        {
            var version=System.Diagnostics.FileVersionInfo.GetVersionInfo(Application.ExecutablePath);
            if(version.ProductName!="DeskPilot" || !version.FileDescription.StartsWith("DeskPilot")) throw new InvalidOperationException("Executable metadata is not DeskPilot branded");
            using(Icon expected=new Icon(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"DeskPilot.ico")),extracted=Icon.ExtractAssociatedIcon(Application.ExecutablePath)) {
                foreach(var actual in new[]{extracted,Icon,MainWindowNotifyIcon.Icon}) {
                    if(actual==null) throw new InvalidOperationException("Application, window or tray icon missing");
                    using(var a=new Bitmap(32,32)) using(var b=new Bitmap(32,32)) {
                        using(var g=Graphics.FromImage(a)) {g.Clear(Color.White);g.DrawIcon(expected,new Rectangle(0,0,32,32));}
                        using(var g=Graphics.FromImage(b)) {g.Clear(Color.White);g.DrawIcon(actual,new Rectangle(0,0,32,32));}
                        long difference=0;for(int x=0;x<32;x++) for(int y=0;y<32;y++) {var left=a.GetPixel(x,y);var right=b.GetPixel(x,y);difference+=Math.Abs(left.R-right.R)+Math.Abs(left.G-right.G)+Math.Abs(left.B-right.B);}
                        if(difference>32*32*10) throw new InvalidOperationException("Executable/window/tray icon differs from DeskPilot branding");
                    }
                }
            }
            if(File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"terminalsicon.ico"))) throw new InvalidOperationException("Old Terminals application icon was packaged");
            if(Terminals.Localization.LocalizedValues.Terminals!="DeskPilot") throw new InvalidOperationException("Localized product name is still Terminals");
            File.AppendAllText(report,"PASS DeskPilot executable metadata, extracted Windows icon, main window/tray icon and localized product name; old application icon absent\n");
        }
        private void SaveChromeScreenshot(string name)
        {
            using(var bitmap=new Bitmap(Width,Height)) {
                using(var graphics=Graphics.FromImage(bitmap)) graphics.CopyFromScreen(Location,Point.Empty,Size);
                bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,name));
            }
        }
    }
}
