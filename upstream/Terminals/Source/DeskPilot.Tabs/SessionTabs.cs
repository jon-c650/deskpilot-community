// Copyright (c) 2026 DeskPilot contributors. Licensed under the project's MS-CL.
// Independently implemented session container. Compatibility names describe the
// application's existing API contract; no TabStrips implementation is compiled.
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace TabControl
{
    public enum TabControlItemChangeTypes { Added, Removed, Changed, SelectionChanged }
    public class TabControlItemChangedEventArgs : EventArgs
    {
        public TabControlItem Item { get; private set; }
        public TabControlItemChangeTypes ChangeType { get; private set; }
        public TabControlItemChangedEventArgs(TabControlItem item, TabControlItemChangeTypes change) { Item=item; ChangeType=change; }
    }
    public class TabControlItemClosingEventArgs : CancelEventArgs
    {
        public TabControlItem Item { get; private set; }
        public TabControlItemClosingEventArgs(TabControlItem item) { Item=item; }
    }
    public class TabControlItemClosedEventArgs : EventArgs { public TabControlItem Item { get; set; } }
    public class TabControlMouseOnTitleEventArgs : EventArgs
    {
        public TabControlItem Item { get; private set; }
        public Point Location { get; private set; }
        public TabControlMouseOnTitleEventArgs(TabControlItem item, Point location) { Item=item; Location=location; }
    }
    public delegate void TabControlItemChangedHandler(TabControlItemChangedEventArgs e);
    public delegate void TabControlItemClosingHandler(TabControlItemClosingEventArgs e);
    public delegate void TabControlItemClosedHandler(object sender, TabControlItemClosedEventArgs e);
    public delegate void TabControlMouseOnTitleHandler(TabControlMouseOnTitleEventArgs e);
    public delegate void TabControlMouseLeftTitleHandler(TabControlMouseOnTitleEventArgs e);

    public class TabControlItem : Panel
    {
        private string title="";
        public event EventHandler Changed;
        public string Title { get { return title; } set { title=value??""; if(Changed!=null) Changed(this,EventArgs.Empty); } }
        public string Caption { get { return Title; } set { Title=value; } }
        public string ToolTipText { get; set; }
        public bool CanClose { get; set; } = true;
        public bool Selected { get; set; }
        public bool IsDrawn { get; set; }
        public RectangleF StripRect { get; internal set; }
        public TabControlItem() : this("",null) { }
        public TabControlItem(Control content) : this("",content) { }
        public TabControlItem(string caption, Control content) { Title=caption; if(content!=null) {Controls.Add(content);content.Dock=DockStyle.Fill;} }
        public override string ToString() { return Title; }
    }
    public sealed class TabControlItemCollection : IEnumerable<TabControlItem>
    {
        private readonly List<TabControlItem> items=new List<TabControlItem>();
        private readonly TabControl owner;
        internal TabControlItemCollection(TabControl control) {owner=control;}
        public int Count { get {return items.Count;} }
        public TabControlItem this[int index] { get { return items[index]; } }
        public bool Contains(TabControlItem item) { return items.Contains(item); }
        public int IndexOf(TabControlItem item) {return items.IndexOf(item);}
        public int Add(TabControlItem item) {Insert(Count,item);return IndexOf(item);}
        public void AddRange(TabControlItem[] values) { foreach(var item in values) Add(item); }
        public void Insert(int index,TabControlItem item) {if(item==null) throw new ArgumentNullException("item");if(Contains(item)) return;items.Insert(index,item);owner.Added(item);}
        public void Remove(TabControlItem item) {if(items.Remove(item)) owner.Removed(item);}
        public void Clear() {foreach(var item in items.ToArray()) Remove(item);}
        public TabControlItem MoveTo(int index,TabControlItem item) {if(!items.Remove(item))return item;items.Insert(Math.Max(0,Math.Min(index,items.Count)),item);owner.InvalidateStrip();return item;}
        // Structural/layout updates always apply during detach; application-level
        // event suppression must never leave a native child under the wrong parent.
        public void SuspendEvents() {owner.SuppressEvents++;}
        public void ResumeEvents() {if(owner.SuppressEvents>0)owner.SuppressEvents--;}
        public IEnumerator<TabControlItem> GetEnumerator() {return items.GetEnumerator();}
        IEnumerator IEnumerable.GetEnumerator() {return GetEnumerator();}
    }
    public class TabControl : Panel, ISupportInitialize
    {
        private sealed class Strip : Panel {public Strip() {DoubleBuffered=true;ResizeRedraw=true;} }
        private readonly Strip strip=new Strip();
        private readonly Panel canvas=new Panel();
        private TabControlItem selected,hover,drag;
        private Point dragStart;
        private bool showTabs=true,modernTabs;
        private Color selectedColor=Color.FromArgb(28,43,60);
        internal int SuppressEvents;
        public event TabControlItemClosingHandler TabControlItemClosing;
        public event TabControlItemClosedHandler TabControlItemClosed;
        public event TabControlItemChangedHandler TabControlItemSelectionChanged;
        public event TabControlItemChangedHandler TabControlItemDetach;
        public event TabControlMouseOnTitleHandler TabControlMouseOnTitle;
        public event TabControlMouseLeftTitleHandler TabControlMouseLeftTitle;
        public event EventHandler MenuItemsLoaded;
        public TabControlItemCollection Items {get;private set;}
        public ContextMenuStrip Menu {get;private set;}
        public bool MenuOpen {get {return Menu.Visible;}}
        public bool AlwaysShowClose {get;set;}
        public bool AlwaysShowMenuGlyph {get;set;} = true;
        public bool ShowBorder {get;set;}
        public bool ShowToolTipOnTitle {get;set;}
        public bool ModernTabs {get {return modernTabs;} set {modernTabs=value;LayoutCanvas();}}
        public bool ShowTabs {get {return showTabs;}set {showTabs=value;LayoutCanvas();}}
        public Color ModernSelectedColor {get {return selectedColor;}set {selectedColor=value;InvalidateStrip();}}
        public TabControlItem SelectedItem
        {
            get {return selected;}
            set {
                if(value!=null && !Items.Contains(value)) return;
                if(selected==value) return;
                var before=selected;selected=value;
                // Select without removing/re-adding controls or destroying handles.
                if(value!=null) {value.Selected=true;value.Bounds=canvas.ClientRectangle;value.Show();value.BringToFront();}
                if(before!=null) {before.Selected=false;before.Hide();}
                InvalidateStrip();
                if(SuppressEvents==0 && TabControlItemSelectionChanged!=null) TabControlItemSelectionChanged(new TabControlItemChangedEventArgs(value,TabControlItemChangeTypes.SelectionChanged));
            }
        }
        public TabControl()
        {
            Items=new TabControlItemCollection(this);Menu=new ContextMenuStrip();
            BackColor=Color.FromArgb(10,22,36);ForeColor=Color.FromArgb(225,235,247);
            canvas.Dock=DockStyle.Fill;strip.Dock=DockStyle.Top;Controls.Add(canvas);Controls.Add(strip);
            canvas.Resize+=delegate {foreach(var item in Items) item.Bounds=canvas.ClientRectangle;};
            strip.Paint+=PaintStrip;strip.MouseDown+=Press;strip.MouseUp+=delegate {drag=null;};strip.MouseMove+=MovePointer;
            strip.MouseLeave+=delegate {SetHover(null,Point.Empty);};
            strip.DoubleClick+=delegate {OnDoubleClick(EventArgs.Empty);};
            LayoutCanvas();
        }
        protected override void OnBackColorChanged(EventArgs e) {base.OnBackColorChanged(e);if(canvas!=null)canvas.BackColor=BackColor;if(strip!=null)strip.BackColor=BackColor;InvalidateStrip();}
        internal void InvalidateStrip() {if(strip!=null)strip.Invalidate();}
        private void LayoutCanvas() {if(strip==null)return;SuspendLayout();strip.Height=modernTabs?35:25;strip.Visible=showTabs;ResumeLayout(true);InvalidateStrip();}
        internal void Added(TabControlItem item) {item.Hide();item.Dock=DockStyle.Fill;canvas.Controls.Add(item);item.Changed+=ItemChanged;if(selected==null)SelectedItem=item;InvalidateStrip();}
        internal void Removed(TabControlItem item) {item.Changed-=ItemChanged;if(canvas.Controls.Contains(item))canvas.Controls.Remove(item);if(selected==item)SelectedItem=Items.LastOrDefault();InvalidateStrip();}
        private void ItemChanged(object sender,EventArgs e) {InvalidateStrip();}
        public void AddTab(TabControlItem item) {Items.Add(item);SelectedItem=item;}
        public void RemoveTab(TabControlItem item) {Items.Remove(item);}
        public void CloseTab(TabControlItem item) {Close(item,false);}
        public void ForceCloseTab(TabControlItem item) {Close(item,true);}
        private void Close(TabControlItem item,bool force)
        {
            if(item==null || !Items.Contains(item) || (!force && !item.CanClose))return;
            var args=new TabControlItemClosingEventArgs(item);if(!force && TabControlItemClosing!=null)TabControlItemClosing(args);if(args.Cancel)return;
            Items.Remove(item);try {if(TabControlItemClosed!=null)TabControlItemClosed(this,new TabControlItemClosedEventArgs {Item=item});}finally {item.Dispose();}
        }
        private Rectangle CloseRect(TabControlItem item) {var r=Rectangle.Round(item.StripRect);return new Rectangle(r.Right-25,r.Top+6,19,Math.Max(16,r.Height-12));}
        private void ComputeHeaders()
        {
            int x=4,limit=Math.Max(0,strip.Width-34),start=0;
            var all=Items.ToArray();
            var widths=all.Select(item=>Math.Min(230,Math.Max(126,TextRenderer.MeasureText(item.Title,Font).Width+48))).ToArray();
            int active=Array.IndexOf(all,selected),used=0;
            for(int i=0;i<=active;i++)used+=widths[i]+3;
            while(start<active && used>limit-4) {used-=widths[start]+3;start++;}
            for(int i=0;i<all.Length;i++) {
                var item=all[i];int width=widths[i];
                item.IsDrawn=i>=start && x+width<=limit;
                item.StripRect=item.IsDrawn?new Rectangle(x,3,width,strip.Height-3):Rectangle.Empty;
                if(item.IsDrawn)x+=width+3;
            }
        }

        public TabControlItem GetTabItemByPoint(Point point) {ComputeHeaders();return Items.FirstOrDefault(item=>item.IsDrawn && item.StripRect.Contains(point));}
        private void PaintStrip(object sender,PaintEventArgs e)
        {
            ComputeHeaders();
            foreach(var item in Items.Where(i=>i.IsDrawn)) {
                var r=Rectangle.Round(item.StripRect);bool active=item==selected;
                using(var brush=new SolidBrush(active?selectedColor:BackColor))e.Graphics.FillRectangle(brush,r);
                using(var pen=new Pen(active?Color.FromArgb(0,173,239):Color.FromArgb(49,66,84)))e.Graphics.DrawRectangle(pen,r.X,r.Y,r.Width-1,r.Height-1);
                if(active)using(var pen=new Pen(Color.FromArgb(0,173,239),2))e.Graphics.DrawLine(pen,r.Left+1,r.Top+1,r.Right-2,r.Top+1);
                TextRenderer.DrawText(e.Graphics,item.Title,Font,new Rectangle(r.X+12,r.Y,r.Width-43,r.Height),ForeColor,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine);
                if(item.CanClose)TextRenderer.DrawText(e.Graphics,"×",Font,CloseRect(item),ForeColor,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
            }
            TextRenderer.DrawText(e.Graphics,"▾",Font,new Rectangle(strip.Width-30,0,28,strip.Height),ForeColor,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
        }
        private void Press(object sender,MouseEventArgs e)
        {
            var item=GetTabItemByPoint(e.Location);
            if(item==null || e.Button==MouseButtons.Right) {OpenMenu(e.Location,item);return;}
            if(e.Button==MouseButtons.Middle || CloseRect(item).Contains(e.Location)) {CloseTab(item);return;}
            if(e.Button==MouseButtons.Left) {SelectedItem=item;drag=item;dragStart=e.Location;}
        }
        private void MovePointer(object sender,MouseEventArgs e)
        {
            var item=GetTabItemByPoint(e.Location);SetHover(item,e.Location);
            if(drag!=null && e.Button==MouseButtons.Left) {
                if(Math.Abs(e.Y-dragStart.Y)>48) {var detached=drag;drag=null;if(TabControlItemDetach!=null)TabControlItemDetach(new TabControlItemChangedEventArgs(detached,TabControlItemChangeTypes.Changed));}
                else if(item!=null && item!=drag && Math.Abs(e.X-dragStart.X)>20) {Items.MoveTo(Items.IndexOf(item),drag);dragStart=e.Location;}
            }
        }
        private void SetHover(TabControlItem item,Point location)
        {
            if(hover!=item) {if(hover!=null && TabControlMouseLeftTitle!=null)TabControlMouseLeftTitle(new TabControlMouseOnTitleEventArgs(hover,location));hover=item;}
            if(item!=null && TabControlMouseOnTitle!=null)TabControlMouseOnTitle(new TabControlMouseOnTitleEventArgs(item,location));
        }
        private void OpenMenu(Point location,TabControlItem target)
        {
            Menu.Items.Clear();
            foreach(var item in Items) {var tab=item;var entry=new ToolStripMenuItem(item.Title,null,delegate {SelectedItem=tab;});entry.Checked=item==selected;Menu.Items.Add(entry);}
            if(target!=null) {Menu.Items.Add(new ToolStripSeparator());Menu.Items.Add("Close",null,delegate {CloseTab(target);});Menu.Items.Add("Detach",null,delegate {if(TabControlItemDetach!=null)TabControlItemDetach(new TabControlItemChangedEventArgs(target,TabControlItemChangeTypes.Changed));});}
            if(MenuItemsLoaded!=null)MenuItemsLoaded(this,EventArgs.Empty);Menu.Show(strip,location);
        }
        public void BeginInit() {SuspendLayout();}
        public void EndInit() {ResumeLayout(true);}
        protected override void Dispose(bool disposing) {if(disposing && Menu!=null)Menu.Dispose();base.Dispose(disposing);}
    }
}
