using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Terminals.Data;
using Terminals.Connections;
using Terminals.Forms;
using Terminals.Forms.DeskPilot;

namespace Terminals
{
    internal partial class MainForm
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr parameter, string text);
        private Panel chromeHost, chromeWorkspace;
        private Control chromeHeader;
        private Button customerToggle, inspectorToggle, toolbarToggle, scalingToggle, sessionDisconnect;
        private OverlayWindow customerEdge,inspectorEdge,toolbarEdge;
        private bool chromeFullscreen;
        private bool[] beforeFullscreen;
        private readonly HashSet<IConnection> scaledConnections=new HashSet<IConnection>();
        private readonly HashSet<IConnection> scalingWarnings=new HashSet<IConnection>();
        private OverlayWindow customerOverlay, inspectorOverlay, commandOverlay;
        private ChromePalette chrome;
        private Timer chromeTimer;
        private IFavorite inspectedFavorite;
        private Label inspectorTitle, inspectorState, detailValues, vpnSummary, chromeStatus;
        private TextBox notesEditor,chromeSearch;
        private TableLayoutPanel detailGrid;
        private Label detailHost,detailProtocol,detailGroups;
        private FlowLayoutPanel inspectorActions;
        private Button inspectorConnect, notesSave, vpnConnect, vpnDisconnect;
        private ComboBox vpnTunnels;
        private bool vpnChanging;
        private bool notesDirty, chromeRefreshing, vpnRefreshRunning;
        private string notesSnapshot = "";
        private string vpnReadError;
        private System.Windows.Forms.TabControl inspectorTabs;
        private readonly Dictionary<string, string> vpnAssociations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> vpnStates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly ToolTip chromeTips = new ToolTip();
        private static string ChromeSettingsPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskPilot", "IsolatedPreview", "ui-mode.txt"); } }
        private string ReadChromeMode() { try { return File.Exists(ChromeSettingsPath) ? File.ReadAllText(ChromeSettingsPath) : "dark"; } catch { return "dark"; } }
        private void SaveChromeMode(string mode)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ChromeSettingsPath)); File.WriteAllText(ChromeSettingsPath, mode);
        }
        private void InitializeDeskPilot()
        {
            if (chromeHost != null) return;
            string mode = ReadChromeMode();
            var previewMenu = new ToolStripMenuItem("DeskPilot interface next launch");
            previewMenu.Click += delegate { SaveChromeMode("dark"); MessageBox.Show(this,"The DeskPilot interface will open next launch.","DeskPilot"); };
            menuStrip.Items.Add(previewMenu);
            if (mode == "classic") return;
            chrome = new ChromePalette(mode == "light");
            SuspendLayout();
            // Reparent once before saved connections open. Visibility handlers never reparent a session.
            chromeHost = new Panel { Dock = DockStyle.Fill, BackColor = chrome.Canvas };
            commandOverlay = NewOverlay("Session controls"); commandOverlay.Embedded=true; commandOverlay.TopLevel=false; BuildSessionToolbar();
            chromeHeader = BuildChromeHeader();
            chromeWorkspace=new Panel {Dock=DockStyle.Fill,BackColor=chrome.Surface};
            chromeWorkspace.Controls.Add(chromeHost);
            Controls.Add(chromeWorkspace); Controls.Add(chromeHeader);
            chromeHost.Controls.Add(tcTerminals); tcTerminals.Dock = DockStyle.Fill;
            chromeHost.SizeChanged += delegate {PositionChrome();};
            tcTerminals.ModernTabs = true; tcTerminals.Font = new Font("Segoe UI",10F);
            customerOverlay = NewOverlay("Customers");
            customerOverlay.Embedded=true; customerOverlay.TopLevel=false; customerOverlay.Dock=DockStyle.Left; customerOverlay.Width=286;
            chromeWorkspace.Controls.Add(customerOverlay); chromeHost.BringToFront();
            customerOverlay.Controls.Add(favsList1); favsList1.Dock = DockStyle.Fill;
            var customerFooter = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, WrapContents = false };
            customerFooter.Controls.Add(ActionButton("Add customer", "folder", delegate {
                string name = NewGroupForm.AskFroGroupName(persistence);
                if(!String.IsNullOrEmpty(name)) persistence.Groups.Add(persistence.Factory.CreateGroup(name));
                favsList1.RefreshChromeCounts();
            }));
            customerFooter.Controls.Add(ActionButton("Add connection", "connect", delegate { connectionsUiFactory.CreateFavorite(); }));
            foreach(Button button in customerFooter.Controls) {button.AutoSize=false;button.MinimumSize=new Size(28,36);button.Size=new Size(134,38);}
            customerOverlay.Controls.Add(customerFooter);
            favsList1.InitializeChrome(chrome);
            favsList1.FolderPropertiesRequested=OpenFolderProperties;favsList1.EndpointInheritanceRequested=OpenEndpointInheritance;
            favsList1.PreviewSelection = delegate(IFavorite favorite) { SelectInspector(favorite); };
            favsList1.VpnIndicator = VpnDot;
            inspectorOverlay = NewOverlay("Details"); BuildInspector();
            AddOverlayDismiss(inspectorOverlay,"Inspector");
            // Retain the original menu handlers, without hosting the legacy utility bars.
            toolStripContainer.TemporaryChromeHost=true;toolStripContainer.Visible=false;
            splitContainer1.Visible=false;
            menuStrip.Items.Remove(previewMenu);previewMenu.Dispose();
            toolsToolStripMenuItem.DropDownItems.Clear();
            optionsToolStripMenuItem.Text="Options";credentialManagementToolStripMenuItem.Text="Credentials";openLogFolderToolStripMenuItem.Text="Open log folder";
            toolsToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[]{optionsToolStripMenuItem,credentialManagementToolStripMenuItem,openLogFolderToolStripMenuItem});
            foreach(var obsolete in new ToolStripItem[]{menubarToolStripMenuItem,standardToolbarToolStripMenuItem,toolStripMenuItemShowHideFavoriteToolbar,shortcutsToolStripMenuItem,lockToolbarsToolStripMenuItem}) viewToolStripMenuItem.DropDownItems.Remove(obsolete);
            captureTerminalScreenToolStripMenuItem.ShortcutKeys=Keys.None;
            chromeStatus = new Label { Dock = DockStyle.Bottom, Height = 28, Padding = new Padding(14,4,0,0), Text = "Ready · isolated DeskPilot preview" };
            Controls.Add(chromeStatus);RestoreChromeMenu();
            customerOverlay.Requested = true; inspectorOverlay.Requested = true; commandOverlay.Requested = true;
            customerEdge=BuildEdge("Show customers", "›", delegate {ToggleChrome(customerOverlay);},out customerToggle);
            inspectorEdge=BuildEdge("Show inspector", "‹", delegate {ToggleChrome(inspectorOverlay);},out inspectorToggle);
            toolbarEdge=BuildEdge("Show toolbar", "⌄ Toolbar", delegate {if(chromeFullscreen) FullScreen=false; else ToggleChrome(commandOverlay);},out toolbarToggle);
            Move += delegate { PositionChrome(); }; Resize += delegate { PositionChrome(); };
            tcTerminals.TabControlItemSelectionChanged += delegate {
                var selected = ActiveChromeFavorite();
                SelectInspector(selected); RefreshInspectorState();
            };
            chromeTimer = new Timer { Interval = 1000 };
            int ticks = 0;
            chromeTimer.Tick += delegate {
                if (IsDisposed) return;
                SyncChromeVisibility(); RecoverInspectorSelection(); RefreshInspectorState(); UpdateChromeScaling();
                if (++ticks % 5 == 0) { favsList1.RefreshChromeCounts(); RefreshVpnAsync(); }
            };
            FormClosed += delegate {
                chromeTimer.Stop(); chromeTimer.Dispose(); chromeTips.Dispose();
                foreach(var overlay in ChromeWindows()) overlay.Dispose();
            };
            ApplyChromeTheme(); ResumeLayout(true);
            Shown += delegate { RestoreChromeMenu();PositionChrome(); SyncChromeVisibility(); favsList1.RefreshChromeCounts(); RefreshVpnAsync(); chromeTimer.Start(); };
        }
        private OverlayWindow NewOverlay(string name)
        {
            return new OverlayWindow(name) { Font = new Font("Segoe UI",10F), BackColor = chrome.Surface, ForeColor = chrome.Text, Dismissed = delegate { PositionChrome(); SyncChromeVisibility(); if(CurrentTerminal != null) CurrentTerminal.Focus(); } };
        }
        private IEnumerable<OverlayWindow> ChromeOverlays() { return new[]{ customerOverlay, inspectorOverlay, commandOverlay }.Where(w => w != null); }
        private IEnumerable<OverlayWindow> ChromeWindows() {return ChromeOverlays().Concat(new[]{customerEdge,inspectorEdge,toolbarEdge}.Where(w=>w!=null));}
        private OverlayWindow BuildEdge(string name,string text,Action action,out Button button)
        {
            var window=NewOverlay(name); button=ActionButton(text,"fullscreen",action);
            button.Image.Dispose(); button.Image=null; button.AutoSize=false; button.MinimumSize=Size.Empty;
            button.Dock=DockStyle.Fill; button.Padding=Padding.Empty; button.Margin=Padding.Empty;
            button.Font=new Font("Segoe UI",text.Length==1 ? 20F : 9F); button.AccessibleName=name;
            chromeTips.SetToolTip(button,name); window.Controls.Add(button); return window;
        }
        internal void ConfigureChromeConnection(IConnection connection)
        {
            if(chromeHost==null || connection==null || scaledConnections.Contains(connection)) return;
            TryChromeFit(connection);
            RecoverInspectorSelection(); UpdateChromeScaling();
        }
        private bool TryChromeFit(IConnection connection)
        {
            var scaling=connection as IViewportScaling; if(scaling==null) return false;
            try {
                if(!scaledConnections.Contains(connection)) {scaling.FitToViewport=true;scaledConnections.Add(connection);}
                scalingWarnings.Remove(connection); return true;
            } catch(Exception error) {
                if(scalingWarnings.Add(connection)) Logging.Warn("RDP local fit is not available yet: "+error.Message);
                return false; // A presentation failure must never discard a working connection.
            }
        }
        private void UpdateChromeScaling()
        {
            if(scalingToggle==null) return;
            var tab=tcTerminals.SelectedItem as TerminalTabControlItem;
            if(sessionDisconnect!=null) sessionDisconnect.Enabled=tab!=null && tab.Connection!=null && tab.Connection.Connected;
            if(tab==null || tab.Connection==null) {scalingToggle.Enabled=false;return;}
            try {
                var scaling=tab.Connection as IViewportScaling;
                bool available=TryChromeFit(tab.Connection);
                scalingToggle.Enabled=available;
                scalingToggle.Text=available && scaling.FitToViewport ? "Fit ✓" : "100%";
                var button=scalingToggle as ChromeButton;
                if(button!=null && button.Selected!=(available && scaling.FitToViewport)) {button.Selected=available && scaling.FitToViewport;button.Invalidate();}
            } catch(Exception error) {
                scalingToggle.Enabled=false;
                if(scalingWarnings.Add(tab.Connection)) Logging.Warn("RDP local scaling is unavailable: "+error.Message);
            }
        }
        private void ToggleChromeScaling()
        {
            var tab=tcTerminals.SelectedItem as TerminalTabControlItem; var scaling=tab==null ? null : tab.Connection as IViewportScaling;
            if(scaling==null) return; scaling.FitToViewport=!scaling.FitToViewport; UpdateChromeScaling();
        }
        internal void SetChromeFullScreen(bool fullscreen)
        {
            if(chromeHost==null) return;
            if(chromeFullscreen==fullscreen) {RestoreChromeMenu();return;}
            SuspendLayout();
            if(fullscreen) {beforeFullscreen=ChromeOverlays().Select(w=>w.Requested).ToArray(); foreach(var w in ChromeOverlays()) {w.Requested=false;w.Hide();}}
            else if(beforeFullscreen!=null) {int i=0;foreach(var w in ChromeOverlays()) w.Requested=beforeFullscreen[i++];}
            chromeFullscreen=fullscreen; chromeHeader.Visible=!fullscreen; chromeStatus.Visible=!fullscreen;
            toolbarToggle.Text=fullscreen ? "Exit fullscreen" : "⌄ Toolbar";
            RestoreChromeMenu();ResumeLayout(true); PositionChrome(); SyncChromeVisibility(); UpdateChromeScaling();
        }
        private Control BuildChromeHeader()
        {
            var header=new TableLayoutPanel {Dock=DockStyle.Top,Height=76,ColumnCount=5,RowCount=1,Padding=new Padding(6,4,6,4)};
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,190));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,144));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,318));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,92));
            var row = new FlowLayoutPanel {Dock=DockStyle.Fill,WrapContents=false,Margin=Padding.Empty,Padding=Padding.Empty};
            header.Controls.Add(row,1,0);
            commandOverlay.Dock=DockStyle.Fill; commandOverlay.Margin=new Padding(0,11,0,9); header.Controls.Add(commandOverlay,3,0);
            var brand = new Panel { Dock=DockStyle.Fill,Margin=Padding.Empty };
            brand.Controls.Add(new PictureBox { Image = ChromeIcons.Create("logo"), Bounds = new Rectangle(8,12,42,42), SizeMode = PictureBoxSizeMode.Zoom });
            brand.Controls.Add(new Label { Text = "DeskPilot", Font = new Font("Segoe UI",17F,FontStyle.Bold), Bounds = new Rectangle(58,4,160,34) });
            var slogan=new Label { Text = "Remote. Manage. Support.", Font = new Font("Segoe UI",8F), Bounds = new Rectangle(60,40,158,22) }; brand.Controls.Add(slogan);
            header.Controls.Add(brand,0,0);
            AddHeaderButton(row,"Connect","connect",delegate { ShowQuickConnect(); });
            AddHeaderButton(row,"VPN","vpn",OpenChromeVpnGroups);
            AddHeaderButton(row,"Settings","settings",delegate { OptionsToolStripMenuItem_Click(this,EventArgs.Empty); });
            var search = new TextBox { Width = 180, Font = new Font("Segoe UI",11F), Margin = new Padding(6,17,4,0), AccessibleName = "Search customers, servers and IP addresses" };
            search.HandleCreated += delegate { SendMessage(search.Handle,0x1501,new IntPtr(1),"Search…"); };
            chromeTips.SetToolTip(search,"Search customers, servers and IP addresses using the existing favorites search.");
            search.TextChanged += delegate { favsList1.SearchChrome(search.Text); ShowChrome(customerOverlay); };
            chromeSearch=search;
            var searchSlot=new Panel {Dock=DockStyle.Fill,Margin=Padding.Empty};
            search.Dock=DockStyle.None;search.Anchor=AnchorStyles.Top|AnchorStyles.Left;search.Bounds=new Rectangle(8,20,150,28);
            searchSlot.Controls.Add(search);header.Controls.Add(searchSlot,2,0);
            AddHeaderButton(row,"Theme","info",delegate { chrome = new ChromePalette(!chrome.Light); SaveChromeMode(chrome.Light ? "light" : "dark"); ApplyChromeTheme(); });
            var theme=row.Controls[row.Controls.Count-1]; theme.Width=44;
            var utilities=new FlowLayoutPanel {Dock=DockStyle.Fill,WrapContents=false,Margin=Padding.Empty};
            utilities.Controls.Add(theme); header.Controls.Add(utilities,4,0);
            var layoutMenu = new ContextMenuStrip(components);
            var customerItem = new ToolStripMenuItem("Customers (fixed sidebar)", ChromeIcons.Create("folder")) {Enabled=false};
            var inspectorItem = new ToolStripMenuItem("Inspector", ChromeIcons.Create("server"));
            var toolbarItem = new ToolStripMenuItem("Session controls (top bar)", ChromeIcons.Create("settings")) {Enabled=false};
            customerItem.Click += delegate { ToggleChrome(customerOverlay); };
            inspectorItem.Click += delegate { ToggleChrome(inspectorOverlay); };
            toolbarItem.Click += delegate { ToggleChrome(commandOverlay); };
            layoutMenu.Items.AddRange(new ToolStripItem[]{customerItem,inspectorItem,toolbarItem,new ToolStripSeparator()});
            layoutMenu.Items.Add("Focus canvas", ChromeIcons.Create("fullscreen"), delegate { foreach(var overlay in ChromeOverlays().Where(w=>!w.Embedded)) { overlay.Requested=false; overlay.Hide(); } if(CurrentTerminal != null) CurrentTerminal.Focus(); });
            layoutMenu.Items.Add("Classic interface next launch", null, delegate { SaveChromeMode("classic"); MessageBox.Show(this,"The classic interface will open next launch. Current sessions stay open.","DeskPilot"); });
            layoutMenu.Opening += delegate {
                customerItem.Checked=customerOverlay.Requested; inspectorItem.Checked=inspectorOverlay.Requested; toolbarItem.Checked=commandOverlay.Requested;
                chrome.Apply(layoutMenu);
            };
            Button layoutButton = null;
            layoutButton=ActionButton("Layout", "fullscreen", delegate { layoutMenu.Show(layoutButton,new Point(0,layoutButton.Height)); });
            layoutButton.MinimumSize=new Size(68,58); layoutButton.AutoSize=false; layoutButton.Size=new Size(74,58);
            layoutButton.Padding=new Padding(3); layoutButton.TextImageRelation=TextImageRelation.ImageAboveText;
            layoutButton.ImageAlign=ContentAlignment.TopCenter; layoutButton.TextAlign=ContentAlignment.BottomCenter;
            var layoutChrome=layoutButton as ChromeButton; if(layoutChrome!=null) layoutChrome.Navigation=true;
            layoutButton.Size=new Size(44,64); layoutButton.MinimumSize=new Size(40,58); layoutButton.Margin=new Padding(1,0,1,0);
            layoutButton.Tag="Layout";chromeTips.SetToolTip(layoutButton,"Layout and inspector visibility");
            utilities.Controls.Add(layoutButton);
            Action fitHeader=delegate {
                bool compact=header.ClientSize.Width<1280;slogan.Visible=!compact;
                header.ColumnStyles[0].Width=compact ? 190 : 224;
                header.ColumnStyles[1].Width=compact ? 144 : 228;
                header.ColumnStyles[4].Width=compact ? 92 : 146;
                foreach(Control control in row.Controls.Cast<Control>().Concat(utilities.Controls.Cast<Control>())) {
                    var button=control as Button;if(button==null) continue;
                    string caption=button.Tag as string ?? button.AccessibleName;
                    button.Text=compact ? "" : caption;
                    button.TextImageRelation=compact ? TextImageRelation.ImageBeforeText : TextImageRelation.ImageAboveText;
                    button.Width=compact ? 44 : caption=="File sharing" ? 92 : 70;
                }
            };
            header.Resize+=delegate {fitHeader();};fitHeader();
            return header;
        }
        private void AddHeaderButton(FlowLayoutPanel row,string text,string icon,Action action)
        {
            var button = ActionButton(text,icon,action); ((ChromeButton)button).Navigation=true; ((ChromeButton)button).Selected=text=="Connect";
            button.Margin=new Padding(1,0,1,0); button.MinimumSize = new Size(40,58); button.AutoSize = false;
            button.Size = new Size(text == "File sharing" ? 60 : 50,64);
            button.Padding = new Padding(3); button.TextImageRelation = TextImageRelation.ImageAboveText;
            button.ImageAlign = ContentAlignment.TopCenter; button.TextAlign = ContentAlignment.BottomCenter;button.Tag=text;chromeTips.SetToolTip(button,text); row.Controls.Add(button);
        }
        private Button ActionButton(string text,string icon,Action action)
        {
            var button = new ChromeButton { Palette=chrome ?? new ChromePalette(false),Text = text, Image = ChromeIcons.Create(icon), AutoSize = true, MinimumSize = new Size(88,38), Padding = new Padding(7,3,7,3), Margin = new Padding(3), FlatStyle = FlatStyle.Flat, TextImageRelation = TextImageRelation.ImageBeforeText, Font = new Font("Segoe UI",9F), AccessibleName = text };
            button.FlatAppearance.BorderSize = 1;
            button.Click += delegate { try { action(); } catch(Exception error) { MessageBox.Show(this,error.Message,"DeskPilot",MessageBoxButtons.OK,MessageBoxIcon.Information); } };
            button.Disposed += delegate { if(button.Image != null) button.Image.Dispose(); };
            return button;
        }
        private void BuildSessionToolbar()
        {
            var row = new FlowLayoutPanel {Dock=DockStyle.Fill,AutoScroll=false,WrapContents=false,Padding=new Padding(3)};
            var actions=new List<Button>();
            sessionDisconnect=ActionButton("Disconnect","disconnect",delegate {Disconnect();});sessionDisconnect.Enabled=false;actions.Add(sessionDisconnect);
            actions.Add(ActionButton("Fullscreen","fullscreen",delegate {TsbFullScreen_Click(this,EventArgs.Empty);}));
            scalingToggle=ActionButton("Fit ✓","fullscreen",ToggleChromeScaling); actions.Add(scalingToggle);
            int[] widths={128,108,66};
            for(int i=0;i<actions.Count;i++) {
                var button=actions[i]; string caption=button.Text;
                button.AutoSize=false;button.MinimumSize=new Size(36,36);button.Size=new Size(widths[i],40);button.Margin=new Padding(1,0,1,0);button.Padding=Padding.Empty;
                ((ChromeButton)button).Navigation=true;
                if(button==scalingToggle) {button.Image.Dispose();button.Image=null;}
                chromeTips.SetToolTip(button,caption=="Disconnect" ? "Disconnect the active session. Disabled when no session is connected." : caption=="Fullscreen" ? "Use the full screen; the top edge tab exits fullscreen." : caption);
                row.Controls.Add(button);
            }
            chromeTips.SetToolTip(scalingToggle,"Fit the existing remote desktop to the window, or show 100% native pixels. No reconnect or remote resolution change.");
            commandOverlay.Controls.Add(row);
        }
        private IFavorite ActiveChromeFavorite()
        {
            var tab=tcTerminals.SelectedItem as TerminalTabControlItem;
            return tab == null || tab.Connection == null ? null : tab.Connection.OriginFavorite ?? tab.Connection.Favorite;
        }
        private void RecoverInspectorSelection()
        {
            // Tab selection fires before ConnectionsUiFactory assigns the connection object.
            // Resolve the actual tab again after that assignment, rather than leaving actions disabled.
            if(inspectedFavorite == null) {
                var favorite=ActiveChromeFavorite(); if(favorite != null) SelectInspector(favorite);
            }
        }
        private void AddOverlayDismiss(OverlayWindow window,string name)
        {
            var close=ActionButton("×","fullscreen",delegate {SetChromeVisible(window,false);});
            close.Image.Dispose();close.Image=null;close.AutoSize=false;close.MinimumSize=new Size(26,26);close.Size=new Size(28,28);
            close.Font=new Font("Segoe UI",13F);close.Padding=Padding.Empty;close.Margin=Padding.Empty;
            close.AccessibleName="Hide "+name;chromeTips.SetToolTip(close,"Hide "+name);
            window.Controls.Add(close);window.HideButton=close;
            Action position=delegate {close.Location=new Point(Math.Max(2,window.ClientSize.Width-32),3);close.BringToFront();};
            window.Resize+=delegate {position();};position();
        }
        private void HideAllChrome()
        {
            foreach(var window in ChromeOverlays().Where(w=>!w.Embedded)) {window.Requested=false;window.Hide();}
            SyncChromeVisibility(); if(CurrentTerminal != null) CurrentTerminal.Focus();
        }
        private void SetChromeVisible(OverlayWindow window,bool show)
        {
            window.Requested=show; PositionChrome(); SyncChromeVisibility();
            if(!show && CurrentTerminal != null) CurrentTerminal.Focus();
        }
        private void ShowChrome(OverlayWindow window) { if(window == null) return; window.Requested = true; PositionChrome(); SyncChromeVisibility(); }
        private void ToggleChrome(OverlayWindow window)
        {
            if(window == null || window.Embedded) return; window.Requested = !window.Requested;
            PositionChrome(); SyncChromeVisibility();
            if(!window.Requested && CurrentTerminal != null) CurrentTerminal.Focus();
        }
        private void PositionChrome()
        {
            if(chromeHost == null || !chromeHost.IsHandleCreated) return;
            Rectangle area = chromeHost.RectangleToScreen(chromeHost.ClientRectangle);
            // Fixed canvas rectangle. Only the overlay HWNDs are positioned here.
            int scale;
            using(var graphics = chromeHost.CreateGraphics()) scale = Math.Max(96,(int)graphics.DpiX);
            int inspector = Math.Min(350 * scale/96, area.Width / 2);
            int top = chromeFullscreen ? 0 : Math.Min(36, area.Height);
            inspectorOverlay.Bounds = new Rectangle(area.Right-inspector,area.Top+top,inspector,Math.Max(1,area.Height-top));
            if(customerEdge!=null) {
                int center=area.Top+top+Math.Max(0,(area.Height-top-64)/2);
                inspectorEdge.Bounds=new Rectangle(area.Right-30,center,28,64);
                toolbarEdge.Bounds=new Rectangle(area.Left+Math.Max(0,(area.Width-120)/2),area.Top+top+2,120,28);
            }
        }
        private void RestoreChromeMenu()
        {
            if(chromeHost==null) return;
            if(menuStrip.Parent!=this) Controls.Add(menuStrip);
            menuStrip.Dock=DockStyle.Top;menuStrip.AutoSize=true;menuStrip.SendToBack();
            menuStrip.Visible=!chromeFullscreen;MainMenuStrip=menuStrip;
            toolStripContainer.Visible=false;
        }
        private void SyncChromeVisibility()
        {
            if(chromeHost == null) return;
            // A modal dialog may itself be owned by an overlay. Hiding its owner here
            // can hide the dialog and strand the user. Windows keeps modal windows above
            // their disabled owners, so leave existing visibility alone until it closes.
            if(!Enabled) return;
            customerOverlay.Requested=!chromeFullscreen; commandOverlay.Requested=!chromeFullscreen;
            if(customerEdge!=null) {customerEdge.Requested=false; inspectorEdge.Requested=!inspectorOverlay.Requested; toolbarEdge.Requested=chromeFullscreen;}
            bool visible = Visible && WindowState != FormWindowState.Minimized;
            foreach(var window in ChromeWindows()) {
                if(visible && window.Requested) { if(!window.Visible) {if(window.Embedded) window.Show();else window.Show(this);} }
                else if(window.Visible) window.Hide();
            }
        }
        private void ApplyChromeTheme()
        {
            if(chrome == null) return;
            chrome.Apply(chromeHeader);chrome.Apply(menuStrip);

            if(chromeStatus != null) chrome.Apply(chromeStatus);
            chromeHost.BackColor = chrome.Canvas;
            tcTerminals.BackColor = chrome.Canvas; tcTerminals.ForeColor = chrome.Text;
            tcTerminals.ModernSelectedColor = chrome.Raised; tcTerminals.Invalidate();
            foreach(var overlay in ChromeWindows()) chrome.Apply(overlay);
            favsList1.ApplyChrome(chrome);
            if(inspectorTabs != null) inspectorTabs.Invalidate();
        }
        private void BuildInspector()
        {
            inspectorTabs = new ChromeTabs { Dock = DockStyle.Fill, Font = new Font("Segoe UI",10F), DrawMode = TabDrawMode.OwnerDrawFixed, ItemSize = new Size(76,36), SizeMode = TabSizeMode.Fixed };
            inspectorTabs.DrawItem += delegate(object sender, DrawItemEventArgs e) {
                using(var b = new SolidBrush(chrome.Surface)) e.Graphics.FillRectangle(b,e.Bounds);
                TextRenderer.DrawText(e.Graphics,inspectorTabs.TabPages[e.Index].Text,inspectorTabs.Font,e.Bounds,e.Index==inspectorTabs.SelectedIndex ? ChromePalette.Accent : chrome.Text,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
                if(e.Index==inspectorTabs.SelectedIndex) using(var pen=new Pen(ChromePalette.Accent,3)) e.Graphics.DrawLine(pen,e.Bounds.Left+8,e.Bounds.Bottom-2,e.Bounds.Right-8,e.Bounds.Bottom-2);
            };
            var details = new TabPage("Details"); var tools = new TabPage("Connection"); var notes = new TabPage("Notes"); var history = new TabPage("History");
            inspectorTabs.TabPages.AddRange(new[]{details,tools,notes,history}); inspectorOverlay.Controls.Add(inspectorTabs);
            var detailPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(12) };
            details.Controls.Add(detailPanel);
            inspectorTitle = new Label { Text = "Select a connection", AutoSize = false, Width = 305, Height = 42, Font = new Font("Segoe UI",13F,FontStyle.Bold) };
            inspectorState = new Label { AutoSize = false, Width = 305, Height = 26 };
            var hostHeading=new Panel {Width=305,Height=66};
            hostHeading.Controls.Add(new PictureBox {Image=ChromeIcons.Create("server"),Bounds=new Rectangle(0,7,42,48),SizeMode=PictureBoxSizeMode.Zoom});
            inspectorTitle.Location=new Point(54,0);inspectorTitle.Size=new Size(245,32);inspectorTitle.AutoEllipsis=true;
            inspectorState.Location=new Point(54,34);inspectorState.Size=new Size(245,26);
            hostHeading.Controls.Add(inspectorTitle);hostHeading.Controls.Add(inspectorState);detailPanel.Controls.Add(hostHeading);
            inspectorConnect = ActionButton("Connect", "connect", delegate { if(inspectedFavorite != null) connectionsUiFactory.Connect(inspectedFavorite); });
            ((ChromeButton)inspectorConnect).Primary=true; inspectorConnect.AutoSize=false;inspectorConnect.Size=new Size(300,38);
            detailPanel.Controls.Add(inspectorConnect);
            detailValues = new Label { AutoSize = false, Width = 305, Height = 64, Padding = new Padding(0,10,0,0) };
            detailPanel.Controls.Add(detailValues);
            detailGrid=new TableLayoutPanel {Width=305,Height=100,ColumnCount=2,RowCount=3,Margin=new Padding(3,10,3,8)};
            detailGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,100));detailGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            detailHost=new Label();detailProtocol=new Label();detailGroups=new Label();
            var values=new[]{detailHost,detailProtocol,detailGroups}; var captions=new[]{"Host name","Protocol","Groups"};
            for(int i=0;i<3;i++) {
                detailGrid.RowStyles.Add(new RowStyle(SizeType.Percent,33.333F));
                detailGrid.Controls.Add(new Label {Text=captions[i],Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},0,i);
                values[i].Dock=DockStyle.Fill;values[i].TextAlign=ContentAlignment.MiddleLeft;values[i].AutoEllipsis=true;detailGrid.Controls.Add(values[i],1,i);
            }
            detailPanel.Controls.Add(detailGrid);
            vpnSummary = new Label { AutoSize = false, Width = 305, Height = 58, Padding = new Padding(0,10,0,0) }; detailPanel.Controls.Add(vpnSummary);
            vpnTunnels=new ComboBox {Width=300,DropDownStyle=ComboBoxStyle.DropDownList,AccessibleName="Associated WireGuard tunnel"};
            detailPanel.Controls.Add(vpnTunnels);
            var vpnButtons=new FlowLayoutPanel {Width=305,Height=46,WrapContents=false};
            vpnConnect=ActionButton("Connect","vpn",delegate { ChangeSelectedVpn(true); });
            vpnDisconnect=ActionButton("Disconnect","disconnect",delegate { ChangeSelectedVpn(false); });
            vpnConnect.AccessibleName="Connect VPN";vpnDisconnect.AccessibleName="Disconnect VPN";
            vpnConnect.AutoSize=false;vpnDisconnect.AutoSize=false;
            vpnButtons.Controls.Add(vpnConnect); vpnButtons.Controls.Add(vpnDisconnect); detailPanel.Controls.Add(vpnButtons);
            detailPanel.Controls.Add(ActionButton("Manage VPN / groups", "vpn", OpenChromeVpnGroups));
            detailPanel.Controls.Add(new Label { Text = "Quick Actions", AutoSize = true, Margin = new Padding(3,10,3,6), Font = new Font("Segoe UI",11F,FontStyle.Bold) });
            inspectorActions = new FlowLayoutPanel { AutoSize = false, Width = 305, Height=150, WrapContents = true, Margin = Padding.Empty };
            string[] names = {"PowerShell","File Explorer","Event Viewer","Services","System Info","Copy IP / host"};
            string[] icons = {"powershell","folder","server","settings","info","copy"};
            for(int i=0;i<names.Length;i++) {
                int action = i; var button = ActionButton(names[i],icons[i],delegate { RunRemoteAction(action); });
                button.AutoSize = false; button.Size = new Size(145,44);
                chromeTips.SetToolTip(button, i == 5 ? "Copy the saved server address." : "Open a separate Windows management connection to the selected host using Windows authentication. RDP credentials are not exported. Remote services and permissions must already allow it.");
                inspectorActions.Controls.Add(button);
            }
            detailPanel.Controls.Add(inspectorActions);
            bool adjustingInspector=false;
            Action fitInspector=delegate {
                if(adjustingInspector) return;adjustingInspector=true;detailPanel.SuspendLayout();
                try {
                    int width=Math.Max(220,detailPanel.ClientSize.Width-detailPanel.Padding.Horizontal-SystemInformation.VerticalScrollBarWidth-8);
                    foreach(Control child in detailPanel.Controls) if(!child.AutoSize || child==inspectorActions) child.Width=width;
                    inspectorTitle.Width=inspectorState.Width=Math.Max(120,width-54);
                    foreach(Control action in inspectorActions.Controls) action.Width=(width-12)/2;
                    vpnConnect.Width=vpnDisconnect.Width=(width-12)/2;
                } finally {detailPanel.ResumeLayout(true);adjustingInspector=false;}
            };
            detailPanel.Resize+=delegate {fitInspector();};fitInspector();
            var toolPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(12), AutoScroll = true };
            tools.Controls.Add(toolPanel);
            toolPanel.Controls.Add(new Label { Text = "Connection tools", AutoSize = true, Font = new Font("Segoe UI",13F,FontStyle.Bold) });
            toolPanel.Controls.Add(ActionButton("Connection properties", "server",delegate { if(inspectedFavorite != null) connectionsUiFactory.EditFavorite(inspectedFavorite, result => { RefreshInspectorDetails(); }); }));
            toolPanel.Controls.Add(ActionButton("Credentials", "vpn",delegate { CredentialManagementToolStripMenuItem_Click(this,EventArgs.Empty); }));
            toolPanel.Controls.Add(ActionButton("Options", "settings",delegate { OptionsToolStripMenuItem_Click(this,EventArgs.Empty); }));
            notesEditor = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true };
            notesEditor.TextChanged += delegate { if(!chromeRefreshing) { notesDirty = true; notesSave.Enabled = inspectedFavorite != null; } };
            notesSave = ActionButton("Save notes", "copy", SaveInspectorNotes); notesSave.Dock = DockStyle.Bottom;
            notes.Controls.Add(notesEditor); notes.Controls.Add(notesSave);
            var historyTree = new Terminals.Forms.Controls.HistoryTreeView { Dock = DockStyle.Fill, Font = new Font("Segoe UI",10F), ItemHeight = 28 };
            history.Controls.Add(historyTree); historyTree.Load(persistence,favoriteIcons);
            historyTree.NodeMouseDoubleClick += delegate(object sender,TreeNodeMouseClickEventArgs e) {
                var node = e.Node as Terminals.Forms.Controls.FavoriteTreeNode;
                if(node != null) connectionsUiFactory.Connect(node.Favorite);
            };
            history.Controls.Add(new Label { Dock = DockStyle.Top, Height = 44, Text = "Connection history · all customers\nDouble-click a connection to reopen.", Padding = new Padding(8,4,0,0) });
            SelectInspector(null);
        }
        private void SelectInspector(IFavorite favorite)
        {
            if(inspectorTitle == null) return;
            favorite = favorite ?? ActiveChromeFavorite();
            if(inspectedFavorite != null && favorite != null && inspectedFavorite.StoreIdEquals(favorite)) { RefreshInspectorState(); return; }
            if(notesDirty && MessageBox.Show(this,"Discard unsaved notes before changing the inspector selection?", "DeskPilot",MessageBoxButtons.YesNo,MessageBoxIcon.Question) != DialogResult.Yes) return;
            inspectedFavorite = favorite; notesDirty = false; RefreshInspectorDetails();
        }
        private void RefreshInspectorDetails()
        {
            chromeRefreshing = true;
            try {
                var f = inspectedFavorite;
                inspectorTitle.Text = f == null ? "Select a connection" : f.Name;
                detailValues.Visible=f==null;detailGrid.Visible=f!=null;
                detailValues.Text="Choose a customer connection or an open session.";
                detailHost.Text=f==null ? "" : f.ServerName;
                detailProtocol.Text=f==null ? "" : f.Protocol+" : "+f.Port;
                detailGroups.Text=f==null ? "" : String.IsNullOrEmpty(f.GroupNames) ? "Ungrouped" : f.GroupNames;
                notesSnapshot = f == null ? "" : f.Notes ?? "";
                notesEditor.Text = notesSnapshot; notesEditor.Enabled = f != null; notesSave.Enabled = false;
                inspectorConnect.Enabled = f != null;
                foreach(Control action in inspectorActions.Controls) action.Enabled = f != null;
                RefreshInspectorState();
            } finally { chromeRefreshing = false; }
        }
        private void RefreshInspectorState()
        {
            if(inspectorState == null) return;
            bool connected = false;
            if(inspectedFavorite != null) foreach(TabControl.TabControlItem item in tcTerminals.Items) {
                var tab = item as TerminalTabControlItem;
                if(tab != null && tab.OriginFavorite != null && tab.OriginFavorite.StoreIdEquals(inspectedFavorite) && tab.Connection != null && tab.Connection.Connected) connected = true;
            }
            inspectorState.Text = inspectedFavorite == null ? "No connection selected" : connected ? "● Connected session" : "○ No connected session";
            inspectorState.ForeColor = connected ? Color.FromArgb(0,210,130) : chrome.Muted;
            string vpnDescription=vpnChanging ? "VPN operation in progress…" : DescribeVpn();
            if(vpnSummary.Text != vpnDescription) vpnSummary.Text=vpnDescription;
            UpdateVpnControls();
            if(chromeStatus != null) chromeStatus.Text = "● Ready     |     " + tcTerminals.Items.Count + " sessions     |     VPN: "+vpnStates.Values.Count(value=>value=="Running")+" running";
        }
        private void SaveInspectorNotes()
        {
            if(inspectedFavorite == null) return;
            var stored = persistence.Favorites[inspectedFavorite.Id];
            if(stored == null) { MessageBox.Show(this,"This connection no longer exists. Copy your notes before closing.","DeskPilot"); return; }
            if((stored.Notes ?? "") != notesSnapshot && MessageBox.Show(this,"Notes changed elsewhere. Replace them with these notes?", "DeskPilot",MessageBoxButtons.YesNo,MessageBoxIcon.Question) != DialogResult.Yes) return;
            string oldNotes = stored.Notes;
            try { stored.Notes = notesEditor.Text; persistence.Favorites.Update(stored); }
            catch { stored.Notes = oldNotes; throw; }
            inspectedFavorite = persistence.Favorites[stored.Id];
            notesSnapshot = notesEditor.Text; notesDirty = false; notesSave.Enabled = false;
        }
        private void RunRemoteAction(int action)
        {
            if(inspectedFavorite == null) return;
            string host = inspectedFavorite.ServerName;
            if(action == 5) { Clipboard.SetText(host); return; }
            // Hostnames only; no shell metacharacters, paths or user-provided command fragments.
            if(!Regex.IsMatch(host, @"\A[A-Za-z0-9][A-Za-z0-9_.:-]*\z")) throw new InvalidOperationException("This action requires a hostname or IP address.");
            string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
            if(action == 0) {
                string command = "Enter-PSSession -ComputerName '" + host + "'";
                Process.Start(new ProcessStartInfo(Path.Combine(system,@"WindowsPowerShell\v1.0\powershell.exe"),"-NoExit -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(command))) { UseShellExecute = false });
            } else if(action == 1) {
                Process.Start(new ProcessStartInfo("explorer.exe",@"\\" + host + @"\") { UseShellExecute = true });
            } else if(action == 2) {
                Process.Start(new ProcessStartInfo(Path.Combine(system,"mmc.exe"),"eventvwr.msc /computer:" + host) { UseShellExecute = true });
            } else if(action == 3) {
                Process.Start(new ProcessStartInfo(Path.Combine(system,"mmc.exe"),"services.msc /computer=" + host) { UseShellExecute = true });
            } else if(action == 4) {
                Process.Start(new ProcessStartInfo(Path.Combine(system,"cmd.exe"),"/k systeminfo /s " + host) { UseShellExecute = false });
            }
        }
        private string AssociationFor(IGroup group)
        {
            var visited = new List<IGroup>();
            while(group != null && !visited.Any(g => g.StoreIdEquals(group))) {
                visited.Add(group); string association;
                if(vpnAssociations.TryGetValue(FolderInheritance.GroupKey(group),out association)) return association;
                group = group.Parent;
            }
            return null;
        }
        private Color? VpnDot(IGroup group)
        {
            string tunnel = AssociationFor(group); if(String.IsNullOrEmpty(tunnel)) return null;
            string state; if(!vpnStates.TryGetValue(tunnel,out state)) return Color.SlateGray;
            return state == "Running" ? Color.FromArgb(0,210,130) : state == "Stopped" || state == "No service" ? Color.FromArgb(119,151,190) : Color.SlateGray;
        }
        private void OpenChromeVpnGroups()
        {
            IGroup group=favsList1.PreviewGroup;
            if(group==null && inspectedFavorite!=null) group=inspectedFavorite.Groups.FirstOrDefault();
            if(group!=null) {
                var visited=new List<IGroup>();
                while(group.Parent!=null && !vpnAssociations.ContainsKey(FolderInheritance.GroupKey(group)) && !visited.Any(g => g.StoreIdEquals(group))) {visited.Add(group); group=group.Parent;}
            }
            using(var dialog=new OrganizeGroupsForm(persistence,favoriteIcons)) {
                dialog.Shown += delegate {dialog.SelectChromeGroup(group);};
                dialog.ShowDialog(this);
            }
            menuLoader.LoadGroups(); RefreshVpnAsync();
        }
        private List<string> SelectionTunnels()
        {
            var tunnels=new List<string>();
            var group=favsList1.PreviewGroup;
            if(group!=null) {var name=AssociationFor(group); if(!String.IsNullOrEmpty(name)) tunnels.Add(name);}
            if(tunnels.Count==0 && inspectedFavorite!=null) foreach(var member in inspectedFavorite.Groups) {
                var name=AssociationFor(member); if(!String.IsNullOrEmpty(name) && !tunnels.Contains(name)) tunnels.Add(name);
            }
            return tunnels;
        }
        private void UpdateVpnControls()
        {
            if(vpnTunnels==null) return;
            var names=SelectionTunnels();
            if(names.Count!=vpnTunnels.Items.Count || names.Where((name,index) => index>=vpnTunnels.Items.Count || !String.Equals(name,(string)vpnTunnels.Items[index],StringComparison.OrdinalIgnoreCase)).Any()) {
                string selected=vpnTunnels.SelectedItem as string;
                vpnTunnels.Items.Clear(); vpnTunnels.Items.AddRange(names.Cast<object>().ToArray());
                if(selected!=null && vpnTunnels.Items.Contains(selected)) vpnTunnels.SelectedItem=selected;
                else if(vpnTunnels.Items.Count>0) vpnTunnels.SelectedIndex=0;
            }
            vpnTunnels.Visible=names.Count>0; vpnConnect.Parent.Visible=names.Count>0;
            vpnTunnels.Enabled=!vpnChanging && names.Count>0;
            vpnConnect.Enabled=vpnDisconnect.Enabled=!vpnChanging && vpnReadError==null && names.Count>0;
        }
        private async void ChangeSelectedVpn(bool start)
        {
            string tunnel=vpnTunnels.SelectedItem as string;
            if(vpnChanging || String.IsNullOrEmpty(tunnel)) return;
            if(MessageBox.Show(this,(start ? "Connect " : "Disconnect ")+tunnel+"?\nWindows may request administrator approval. An inactive encrypted profile is activated by official WireGuard. Its service may remain installed and start with Windows until disabled in WireGuard.","DeskPilot WireGuard",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes) return;
            vpnChanging=true; UpdateVpnControls(); vpnSummary.Text="VPN operation in progress…";
            try { await WireGuardControl.ChangeAsync(tunnel,start); }
            catch(System.ComponentModel.Win32Exception error) {
                if(IsDisposed) return;
                MessageBox.Show(this,error.NativeErrorCode==1223 ? "Administrator approval was cancelled. No VPN success is assumed." : error.Message,"DeskPilot WireGuard",MessageBoxButtons.OK,MessageBoxIcon.Information);
            } catch(Exception error) { if(IsDisposed) return; MessageBox.Show(this,error.Message,"DeskPilot WireGuard",MessageBoxButtons.OK,MessageBoxIcon.Warning); }
            finally { vpnChanging=false; if(!IsDisposed) {UpdateVpnControls(); RefreshVpnAsync();} }
        }
        private string DescribeVpn()
        {
            if(vpnReadError != null) return "VPN status unavailable: " + vpnReadError;
            var tunnels = SelectionTunnels();
            if(tunnels.Count == 0) return "VPN   No association for this selection\nManage associations in VPN / groups.";
            string context=favsList1.PreviewGroup==null ? "VPN" : "VPN · "+favsList1.PreviewGroup.Name;
            return context+"   " + String.Join("\n",tunnels.Select(t => { string state; return t + " · " + (vpnStates.TryGetValue(t,out state) ? state : "Unknown"); }).ToArray()) + "\nService state only; connectivity unverified.";
        }
        private static bool SameVpnValues(Dictionary<string,string> left,Dictionary<string,string> right)
        {
            return left.Count==right.Count && left.All(item => right.ContainsKey(item.Key) && right[item.Key]==item.Value);
        }
        private async void RefreshVpnAsync()
        {
            if(vpnRefreshRunning) return; vpnRefreshRunning = true;
            try {
                var associationSnapshot=FolderVpnBinding.Associations(persistence);
                var result = await Task.Run(() => {
                    var associations = associationSnapshot;
                    var states = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                    foreach(string tunnel in associations.Values.Distinct(StringComparer.OrdinalIgnoreCase)) {
                        try { using(var service = new ServiceController("WireGuardTunnel$" + tunnel)) states[tunnel] = service.Status.ToString(); }
                        catch(InvalidOperationException error) { var native = error.InnerException as System.ComponentModel.Win32Exception; states[tunnel] = native != null && native.NativeErrorCode == 1060 ? "No service" : "Status unavailable"; }
                        catch { states[tunnel] = "Status unavailable"; }
                    }
                    return Tuple.Create(associations,states);
                });
                if(IsDisposed) return;
                bool changed = vpnReadError != null || !SameVpnValues(vpnAssociations,result.Item1) || !SameVpnValues(vpnStates,result.Item2);
                vpnReadError = null; vpnAssociations.Clear(); vpnStates.Clear();
                foreach(var item in result.Item1) vpnAssociations[item.Key] = item.Value;
                foreach(var item in result.Item2) vpnStates[item.Key] = item.Value;
                if(changed) favsList1.InvalidateChromeTree(); RefreshInspectorState();
            } catch(Exception error) {
                if(!IsDisposed && vpnSummary != null) { vpnStates.Clear(); vpnReadError = error.GetType().Name; vpnSummary.Text = "VPN association/status unavailable: " + vpnReadError; }
            } finally { vpnRefreshRunning = false; }
        }
    }
}
