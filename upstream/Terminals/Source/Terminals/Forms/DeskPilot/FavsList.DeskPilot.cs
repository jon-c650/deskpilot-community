using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Terminals.Data;
using Terminals.Forms.Controls;
using Terminals.Forms.DeskPilot;

namespace Terminals
{
    internal partial class FavsList
    {
        private ChromePalette chromePalette;
        private readonly Dictionary<string,int> chromeCounts = new Dictionary<string,int>();
        internal Action<IFavorite> PreviewSelection;
        internal Action<IGroup> FolderPropertiesRequested;
        internal Action<IFavorite> EndpointInheritanceRequested;
        internal ToolStripMenuItem FolderPropertiesMenu,EndpointInheritanceMenu;
        internal int ChromeSearchResultCount;
        private Label customerCount;
        private readonly Bitmap chromeFolderIcon=ChromeIcons.Create("folder");
        private readonly Dictionary<string,Bitmap> chromeProtocolIcons=new Dictionary<string,Bitmap> {
            {"rdp",ChromeIcons.Create("rdp")},{"ssh",ChromeIcons.Create("ssh")},
            {"web",ChromeIcons.Create("web")},{"https",ChromeIcons.Create("https")},{"server",ChromeIcons.Create("server")}
        };
        internal int ChromeTreeInvalidations;
        internal bool ChromeBufferingEnabled {get {return favsTree.ChromeBufferingEnabled;}}
        internal void InvalidateChromeTree() { ChromeTreeInvalidations++; favsTree.Invalidate(); }
        internal Func<IGroup, Color?> VpnIndicator;
        internal IFavorite PreviewFavorite { get { return GetSelectedFavorite(); } }
        internal IGroup PreviewGroup {
            get { if(tabControl1.SelectedTab!=FavoritesTabPage || searchPanel1.Visible) return null; var n = favsTree.SelectedGroupNode; return n == null ? null : n.Group; }
        }
        internal void InitializeChrome(ChromePalette palette)
        {
            FolderPropertiesMenu=new ToolStripMenuItem("Properties…",null,delegate {if(FolderPropertiesRequested!=null && PreviewGroup!=null) FolderPropertiesRequested(PreviewGroup);});
            groupsContextMenu.Items.Add(new ToolStripSeparator());groupsContextMenu.Items.Add(FolderPropertiesMenu);
            EndpointInheritanceMenu=new ToolStripMenuItem("Inherited settings…",null,delegate {if(EndpointInheritanceRequested!=null && GetSelectedFavorite()!=null) EndpointInheritanceRequested(GetSelectedFavorite());});
            favoritesContextMenu.Items.Add(EndpointInheritanceMenu);
            Font = new Font("Segoe UI", 10F);
            FavoritesTabPage.Text = "Customers"; HistoryTabPage.Text = "Recent";
            tabControl1.DrawMode = TabDrawMode.OwnerDrawFixed; tabControl1.ItemSize = new Size(120,32); tabControl1.SizeMode = TabSizeMode.Fixed;
            tabControl1.DrawItem += delegate(object sender, DrawItemEventArgs e) {
                using(var b = new SolidBrush(chromePalette.Surface)) e.Graphics.FillRectangle(b,e.Bounds);
                TextRenderer.DrawText(e.Graphics,tabControl1.TabPages[e.Index].Text,Font,e.Bounds,e.Index==tabControl1.SelectedIndex ? ChromePalette.Accent : chromePalette.Text,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
                if(e.Index==tabControl1.SelectedIndex) using(var pen=new Pen(ChromePalette.Accent,3)) e.Graphics.DrawLine(pen,e.Bounds.Left+5,e.Bounds.Bottom-2,e.Bounds.Right-5,e.Bounds.Bottom-2);
            };
            // Keep legacy tree commands available through an explicit menu instead of a second icon strip.
            favoritesTreeMenu.Visible=false;
            var commands=new ContextMenuStrip(components);
            foreach(ToolStripItem item in favoritesTreeMenu.Items) {
                var original=item; commands.Items.Add(String.IsNullOrEmpty(item.ToolTipText) ? item.Text : item.ToolTipText,item.Image,delegate {original.PerformClick();});
            }
            var more=new Button {Text="…",Dock=DockStyle.Right,Width=32,FlatStyle=FlatStyle.Flat,AccessibleName="Customer tree commands"};
            more.Click += delegate {chromePalette.Apply(commands); commands.Show(more,new Point(0,more.Height));};
            var treeHeading=new Panel {Dock=DockStyle.Top,Height=30}; treeHeading.Controls.Add(more); panel1.Controls.Add(treeHeading);
            customerCount = new Label { Dock = DockStyle.Top, Height = 30, Text = "All Customers", Padding = new Padding(8,5,0,0) };
            customerCount.Dock=DockStyle.Fill; treeHeading.Controls.Add(customerCount); more.BringToFront();
            searchTextBox.Found += delegate(object sender, FavoritesFoundEventArgs e) { ChromeSearchResultCount = e.Favorites.Count; };
            searchPanel1.ResultsSelectionChanged += delegate { if(PreviewSelection != null) PreviewSelection(searchPanel1.SelectedFavorite); };
            favsTree.ItemHeight = 32; favsTree.Indent = 19;
            favsTree.FullRowSelect = true; favsTree.HideSelection = false;
            favsTree.EnableChromeBuffering();
            Disposed += delegate { chromeFolderIcon.Dispose(); foreach(var icon in chromeProtocolIcons.Values) icon.Dispose(); };
            favsTree.ShowLines = false; favsTree.DrawMode = TreeViewDrawMode.OwnerDrawAll;
            favsTree.DrawNode += DrawChromeNode;
            favsTree.AfterSelect += delegate { if (PreviewSelection != null) PreviewSelection(GetSelectedFavorite()); };
            historyTreeView.AfterSelect += delegate(object sender, TreeViewEventArgs e) { var favorite = e.Node as FavoriteTreeNode; if (PreviewSelection != null) PreviewSelection(favorite == null ? null : favorite.Favorite); };
            ApplyChrome(palette);
        }
        internal void ApplyChrome(ChromePalette palette)
        {
            chromePalette = palette; palette.Apply(this);
            favsTree.BorderStyle = BorderStyle.None; historyTreeView.BorderStyle = BorderStyle.None;
            tabControl1.Font = new Font("Segoe UI", 10F); favsTree.Invalidate();
        }
        internal void SelectChromeFixtureGroup(IGroup group)
        {
            tabControl1.SelectedTab=FavoritesTabPage;ExpandChromeFixture();
            var node=ChromeFixtureNodes(favsTree.Nodes).OfType<GroupTreeNode>().FirstOrDefault(item=>item.Group.StoreIdEquals(group));
            if(node==null) throw new InvalidOperationException("Folder fixture was not loaded");favsTree.SelectedNode=node;
        }
        internal void SelectChromeFixtureFavorite(IFavorite favorite)
        {
            tabControl1.SelectedTab=FavoritesTabPage;ExpandChromeFixture();
            var node=ChromeFixtureNodes(favsTree.Nodes).OfType<FavoriteTreeNode>().FirstOrDefault(item=>item.Favorite.StoreIdEquals(favorite));
            if(node==null) throw new InvalidOperationException("Endpoint fixture was not loaded");favsTree.SelectedNode=node;
        }
        private static IEnumerable<TreeNode> ChromeFixtureNodes(TreeNodeCollection nodes)
        {
            foreach(TreeNode node in nodes) {yield return node;foreach(var child in ChromeFixtureNodes(node.Nodes)) yield return child;}
        }
        internal void SearchChrome(string text) { tabControl1.SelectedTab = FavoritesTabPage; ChromeSearchResultCount = -1; searchTextBox.SetChromeQuery(text); }
        internal void ShowChromeHistory() { tabControl1.SelectedTab = HistoryTabPage; }
        internal void RefreshChromeCounts()
        {
            if (persistence == null) return;
            string caption="All Customers   " + persistence.Groups.Count(g => g.Parent == null) + "   ·   " + persistence.Favorites.Count() + " connections";
            if(customerCount.Text != caption) customerCount.Text=caption;
            var updatedCounts=new Dictionary<string,int>();
            var allGroups = persistence.Groups.ToList();
            foreach (var group in allGroups) {
                var members = new HashSet<Guid>(); var visited = new List<IGroup>(); var pending = new Queue<IGroup>(); pending.Enqueue(group);
                while (pending.Count > 0) {
                    var next = pending.Dequeue(); if (visited.Any(g => g.StoreIdEquals(next))) continue;
                    visited.Add(next); foreach(var favorite in next.Favorites) members.Add(favorite.Id);
                    foreach(var child in allGroups.Where(g => g.Parent != null && g.Parent.StoreIdEquals(next))) pending.Enqueue(child);
                }
                updatedCounts[group.Name] = members.Count;
            }
            if(updatedCounts.Count!=chromeCounts.Count || updatedCounts.Any(item => !chromeCounts.ContainsKey(item.Key) || chromeCounts[item.Key]!=item.Value)) {
                chromeCounts.Clear(); foreach(var item in updatedCounts) chromeCounts[item.Key]=item.Value;
                InvalidateChromeTree();
            }
        }
        internal void CheckChromeTreeCommands()
        {
            favoritesTreeMenu.Items[5].PerformClick();
            if(favsTree.Nodes.Cast<TreeNode>().Any(node=>node.IsExpanded)) throw new InvalidOperationException("Original collapse command is inaccessible from chrome menu");
        }
        internal void ExpandChromeFixture()
        {
            foreach(TreeNode node in favsTree.Nodes) if(node.Text.StartsWith("SCVCC")) { node.Expand(); foreach(TreeNode child in node.Nodes) child.Expand(); }
        }
        private Bitmap ChromeNodeIcon(TreeNode node)
        {
            if(node is GroupTreeNode) return chromeFolderIcon;
            var favorite=node as FavoriteTreeNode;
            return chromeProtocolIcons[ChromeIcons.ProtocolKind(favorite==null ? null : favorite.Favorite.Protocol)];
        }
        internal void VerifyProtocolIconFixtures()
        {
            var signatures=new HashSet<string>();
            foreach(string protocol in new[]{"RDP","SSH","HTTP","HTTPS"}) {
                var node=ChromeFixtureNodes(favsTree.Nodes).OfType<FavoriteTreeNode>().FirstOrDefault(n=>n.Favorite.Protocol==protocol);
                if(node==null) throw new InvalidOperationException("Missing protocol tree fixture: "+protocol);
                using(var stream=new System.IO.MemoryStream()) {
                    ChromeNodeIcon(node).Save(stream,System.Drawing.Imaging.ImageFormat.Png);
                    if(!signatures.Add(Convert.ToBase64String(stream.ToArray()))) throw new InvalidOperationException("Customer tree renders identical protocol icons: "+protocol);
                }
            }
        }
        private void DrawChromeNode(object sender, DrawTreeNodeEventArgs e)
        {
            if (chromePalette == null) { e.DrawDefault = true; return; }
            var p = chromePalette;
            bool selected = e.Node == favsTree.SelectedNode;
            var row = new Rectangle(0, e.Bounds.Top, favsTree.ClientSize.Width, favsTree.ItemHeight);
            using(var brush = new SolidBrush(selected ? p.Raised : p.Surface)) e.Graphics.FillRectangle(brush,row);
            int x = e.Node.Level * favsTree.Indent + 4;
            var group = e.Node as GroupTreeNode;
            if(group != null && e.Node.Nodes.Count > 0) {
                Point[] triangle = e.Node.IsExpanded ? new[]{new Point(x, row.Top+13),new Point(x+8,row.Top+13),new Point(x+4,row.Top+19)} : new[]{new Point(x+2,row.Top+11),new Point(x+2,row.Top+21),new Point(x+7,row.Top+16)};
                using(var b = new SolidBrush(p.Muted)) e.Graphics.FillPolygon(b,triangle);
            }
            e.Graphics.DrawImage(ChromeNodeIcon(e.Node),x+14,row.Top+4,24,24);
            int count; int reserved = group == null ? 8 : 62;
            TextRenderer.DrawText(e.Graphics,e.Node.Text,Font,new Rectangle(x+44,row.Top,Math.Max(0,row.Width-x-44-reserved),row.Height),p.Text,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
            if(group != null) {
                if(chromeCounts.TryGetValue(group.Group.Name,out count)) {
                    var badge = new Rectangle(row.Right-36,row.Top+5,30,22);
                    using(var b = new SolidBrush(p.Raised)) e.Graphics.FillEllipse(b,badge);
                    TextRenderer.DrawText(e.Graphics,count.ToString(),Font,badge,p.Text,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
                }
                var dot = VpnIndicator == null ? null : VpnIndicator(group.Group);
                if(dot.HasValue) using(var b = new SolidBrush(dot.Value)) e.Graphics.FillEllipse(b,row.Right-54,row.Top+12,8,8);
            }
            if(e.Node.IsEditing) e.DrawDefault = true;
        }
    }
}

namespace Terminals.Forms.Controls
{
    internal partial class FavoritesSearchBox
    {
        internal void SetChromeQuery(string value) { searchTextBox.SetChromeQuery(value); }
    }
    internal partial class SearchTextBox
    {
        internal void SetChromeQuery(string value) { if(String.IsNullOrEmpty(value)) CancelSearch(); else valueTextBox.Text = value; }
    }
}

namespace Terminals.Forms.Controls
{
    internal partial class TreeView
    {
        [System.Runtime.InteropServices.DllImport("user32.dll",EntryPoint="SendMessageW",ExactSpelling=true)]
        private static extern IntPtr SendTreeMessage(IntPtr window,int message,IntPtr mask,IntPtr value);
        internal bool ChromeBufferingEnabled { get { return (SendTreeMessage(Handle,0x112d,IntPtr.Zero,IntPtr.Zero).ToInt64() & 4)!=0; } }
        internal void EnableChromeBuffering()
        {
            EventHandler apply=delegate { SendTreeMessage(Handle,0x112c,new IntPtr(4),new IntPtr(4)); };
            HandleCreated += apply;
            if(IsHandleCreated) apply(this,EventArgs.Empty);
        }
    }
}
