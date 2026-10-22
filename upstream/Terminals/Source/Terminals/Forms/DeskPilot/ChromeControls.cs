using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Terminals.Forms.DeskPilot
{
    internal static class ChromeShapes
    {
        internal static GraphicsPath Rounded(Rectangle bounds,int radius)
        {
            var path=new GraphicsPath(); int diameter=Math.Min(radius*2,Math.Min(bounds.Width,bounds.Height));
            if(diameter<2) {path.AddRectangle(bounds); return path;}
            path.AddArc(bounds.Left,bounds.Top,diameter,diameter,180,90);
            path.AddArc(bounds.Right-diameter,bounds.Top,diameter,diameter,270,90);
            path.AddArc(bounds.Right-diameter,bounds.Bottom-diameter,diameter,diameter,0,90);
            path.AddArc(bounds.Left,bounds.Bottom-diameter,diameter,diameter,90,90); path.CloseFigure(); return path;
        }
    }
    internal sealed class ChromeButton : Button
    {
        internal bool Navigation,Selected,Primary;
        internal ChromePalette Palette=new ChromePalette(false);
        private bool hover;
        internal ChromeButton() {SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);}
        protected override void OnMouseEnter(EventArgs e) {hover=true;Invalidate();base.OnMouseEnter(e);}
        protected override void OnMouseLeave(EventArgs e) {hover=false;Invalidate();base.OnMouseLeave(e);}
        protected override void OnPaint(PaintEventArgs e)
        {
            var p=Palette; var g=e.Graphics; g.SmoothingMode=SmoothingMode.AntiAlias;
            g.Clear(Parent==null ? p.Surface : Parent.BackColor);
            var rect=new Rectangle(0,0,Math.Max(1,Width-1),Math.Max(1,Height-1));
            using(var shape=ChromeShapes.Rounded(rect,Navigation ? 4 : 6))
            using(var fill=new LinearGradientBrush(rect,Primary ? Color.FromArgb(0,153,248) : hover ? p.Raised : Navigation ? p.Surface : p.Raised,Primary ? Color.FromArgb(0,116,222) : p.Surface,90F)) {
                g.FillPath(fill,shape);
                if(!Navigation || hover || Focused) using(var pen=new Pen(hover ? ChromePalette.Accent : p.Border)) g.DrawPath(pen,shape);
            }
            bool vertical=TextImageRelation==TextImageRelation.ImageAboveText;
            Rectangle text=new Rectangle(2,2,Math.Max(1,Width-4),Math.Max(1,Height-4));
            if(Image!=null) {
                int size=Navigation ? 27 : Math.Min(20,Height-12);
                int x=vertical || Text.Length==0 ? (Width-size)/2 : 7, y=vertical ? 7 : (Height-size)/2;
                if(Enabled) g.DrawImage(Image,x,y,size,size); else ControlPaint.DrawImageDisabled(g,Image,x,y,p.Surface);
                if(vertical) text=new Rectangle(2,size+13,Width-4,Math.Max(1,Height-size-15));
                else text=new Rectangle(x+size+6,2,Math.Max(1,Width-x-size-13),Height-4);
            }
            TextRenderer.DrawText(g,Text,Font,text,Enabled ? Primary ? Color.White : Selected ? ChromePalette.Accent : p.Text : p.Muted,
                TextFormatFlags.VerticalCenter|TextFormatFlags.HorizontalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine);
            if(Selected) using(var pen=new Pen(ChromePalette.Accent,3)) g.DrawLine(pen,7,Height-2,Width-7,Height-2);
            if(Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g,new Rectangle(4,4,Width-8,Height-8),p.Text,p.Surface);
        }
    }
    // Paint the complete native tab surface, including the unused strip and frame.
    // Native tab selection, keyboard navigation and page ownership are retained.
    internal sealed class ChromeTabs : System.Windows.Forms.TabControl
    {
        internal ChromePalette Palette=new ChromePalette(false);
        internal ChromeTabs() {SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);}
        protected override void OnPaint(PaintEventArgs e)
        {
            var p=Palette; e.Graphics.Clear(p.Surface);
            for(int i=0;i<TabCount;i++) {
                var r=GetTabRect(i);
                TextRenderer.DrawText(e.Graphics,TabPages[i].Text,Font,r,i==SelectedIndex ? ChromePalette.Accent : p.Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);
                if(i==SelectedIndex) using(var pen=new Pen(ChromePalette.Accent,3)) e.Graphics.DrawLine(pen,r.Left+10,r.Bottom-2,r.Right-10,r.Bottom-2);
            }
            using(var pen=new Pen(p.Border)) e.Graphics.DrawLine(pen,0,DisplayRectangle.Top-2,Width,DisplayRectangle.Top-2);
        }
        protected override void OnSelectedIndexChanged(EventArgs e) {base.OnSelectedIndexChanged(e);Invalidate();}
    }
}
