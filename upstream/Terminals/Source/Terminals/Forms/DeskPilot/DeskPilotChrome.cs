using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Terminals.Forms.DeskPilot
{
    // Opaque, owned native windows follow the verified ActiveX proof. Never TopMost.
    internal sealed class OverlayWindow : Form
    {
        internal bool Requested;
        internal bool Embedded;
        internal Button HideButton;
        internal Action Dismissed;
        internal OverlayWindow(string title)
        {
            Icon = ChromeIcons.ApplicationIcon;
            Text = title; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual; Padding = Padding.Empty;
            SetStyle(ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
            KeyPreview = true;
            KeyDown += delegate(object sender, KeyEventArgs e) {
                if (!Embedded && e.KeyCode == Keys.Escape) { Requested = false; Hide(); if(Dismissed != null) Dismissed(); e.Handled = true; }
            };
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Requested = false; Hide(); }
            base.OnFormClosing(e);
        }
    }
    internal sealed class ChromePalette
    {
        internal readonly bool Light;
        internal readonly Color Canvas, Surface, Raised, Text, Muted, Border;
        internal static readonly Color Accent = Color.FromArgb(0, 174, 239);
        internal ChromePalette(bool light)
        {
            Light = light;
            Canvas = light ? Color.FromArgb(233, 239, 246) : Color.FromArgb(7, 20, 35);
            Surface = light ? Color.FromArgb(248, 250, 253) : Color.FromArgb(18, 29, 41);
            Raised = light ? Color.FromArgb(221, 231, 243) : Color.FromArgb(28, 43, 60);
            Text = light ? Color.FromArgb(22, 38, 57) : Color.FromArgb(235, 242, 250);
            Muted = light ? Color.FromArgb(72, 92, 115) : Color.FromArgb(157, 179, 204);
            Border = light ? Color.FromArgb(166, 185, 207) : Color.FromArgb(52, 75, 98);
        }
        internal void Apply(Control root)
        {
            if (root is AxHost || root is TabControl.TabControl) return; // Never theme a remote desktop.
            root.BackColor = root is TextBoxBase ? Raised : Surface; root.ForeColor = Text;
            var rounded=root as ChromeButton; if(rounded!=null) rounded.Palette=this;
            var tabs=root as ChromeTabs; if(tabs!=null) tabs.Palette=this;
            var button = root as Button;
            if (button != null) { button.UseVisualStyleBackColor = false; button.BackColor = Raised; button.FlatAppearance.BorderColor = Border; }
            var strip = root as ToolStrip;
            if (strip != null) {
                strip.Renderer = new ToolStripProfessionalRenderer(new ChromeColorTable(this));
                foreach(ToolStripItem item in strip.Items) ApplyItem(item);
            }
            foreach (Control child in root.Controls) Apply(child);
            root.Invalidate();
        }
        private void ApplyItem(ToolStripItem item)
        {
            item.ForeColor = Text; item.BackColor = Surface;
            var dropdown = item as ToolStripDropDownItem;
            if(dropdown != null) {
                dropdown.DropDown.BackColor = Surface; dropdown.DropDown.ForeColor = Text;
                foreach(ToolStripItem child in dropdown.DropDownItems) ApplyItem(child);
            }
        }
    }
    internal sealed class ChromeColorTable : ProfessionalColorTable
    {
        private readonly ChromePalette p;
        internal ChromeColorTable(ChromePalette palette) { p = palette; UseSystemColors = false; }
        public override Color ToolStripGradientBegin { get { return p.Surface; } }
        public override Color ToolStripGradientMiddle { get { return p.Surface; } }
        public override Color ToolStripGradientEnd { get { return p.Surface; } }
        public override Color MenuStripGradientBegin { get { return p.Surface; } }
        public override Color MenuStripGradientEnd { get { return p.Surface; } }
        public override Color ToolStripDropDownBackground { get { return p.Surface; } }
        public override Color ImageMarginGradientBegin { get { return p.Surface; } }
        public override Color ImageMarginGradientMiddle { get { return p.Surface; } }
        public override Color ImageMarginGradientEnd { get { return p.Surface; } }
        public override Color MenuItemSelected { get { return p.Raised; } }
        public override Color MenuItemSelectedGradientBegin { get { return p.Raised; } }
        public override Color MenuItemSelectedGradientEnd { get { return p.Raised; } }
        public override Color MenuItemPressedGradientBegin { get { return p.Raised; } }
        public override Color MenuItemPressedGradientMiddle { get { return p.Raised; } }
        public override Color MenuItemPressedGradientEnd { get { return p.Raised; } }
        public override Color MenuItemBorder { get { return p.Border; } }
        public override Color MenuBorder { get { return p.Border; } }
        public override Color ButtonSelectedGradientBegin { get { return p.Raised; } }
        public override Color ButtonSelectedGradientEnd { get { return p.Raised; } }
    }
    internal static class ChromeIcons
    {
        // Shared process-lifetime icon for dialogs created without a designer.
        internal static readonly Icon ApplicationIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        // Code-native icons retain the reference's blue/cyan/yellow action palette.
        internal static string ProtocolKind(string protocol)
        {
            switch ((protocol ?? "").Trim().ToUpperInvariant()) {
                case "RDP": return "rdp";
                case "SSH": return "ssh";
                case "HTTP": return "web";
                case "HTTPS": return "https";
                default: return "server";
            }
        }
        internal static Bitmap Create(string kind)
        {
            if(kind=="logo") return new Bitmap(Terminals.Properties.Resources.terminalsicon);
            var bitmap = new Bitmap(28, 28);
            using (var g = Graphics.FromImage(bitmap))
            using (var blue = new Pen(Color.FromArgb(113, 164, 231), 2))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                if (kind == "folder") {
                    using (var b = new SolidBrush(Color.FromArgb(255, 195, 40))) {
                        g.FillRectangle(b, 2, 8, 24, 17); g.FillRectangle(b, 3, 4, 10, 8);
                    }
                    g.DrawLine(Pens.Khaki, 3, 10, 25, 10);
                } else if (kind == "connect") {
                    using(var cyan = new Pen(ChromePalette.Accent, 2)) g.DrawRectangle(cyan, 4, 3, 20, 16); g.DrawLine(blue, 1, 23, 27, 23);
                } else if (kind == "rdp") {
                    using(var fill=new SolidBrush(Color.FromArgb(0,137,239))) g.FillRectangle(fill,2,3,24,17);
                    g.FillRectangle(Brushes.MidnightBlue,5,6,18,11);
                    using(var cyan=new Pen(Color.FromArgb(65,205,255),2)) {
                        g.DrawRectangle(cyan,2,3,24,17);g.DrawLine(cyan,14,21,14,25);g.DrawLine(cyan,7,25,21,25);
                    }
                } else if (kind == "ssh") {
                    using(var fill=new SolidBrush(Color.FromArgb(22,113,68))) g.FillRectangle(fill,2,3,24,22);
                    using(var green=new Pen(Color.FromArgb(58,215,130),2)) g.DrawRectangle(green,2,3,24,22);
                    using(var white=new Pen(Color.White,2)) {
                        g.DrawLines(white,new[]{new Point(6,9),new Point(11,14),new Point(6,19)});g.DrawLine(white,14,19,21,19);
                    }
                } else if (kind == "web" || kind == "https") {
                    using(var fill=new SolidBrush(Color.FromArgb(119,74,202))) g.FillEllipse(fill,2,2,24,24);
                    using(var white=new Pen(Color.White,1.5F)) {
                        g.DrawEllipse(white,3,3,22,22);g.DrawEllipse(white,9,3,10,22);
                        g.DrawLine(white,3,14,25,14);g.DrawArc(white,3,7,22,13,0,180);
                    }
                    if(kind=="https") {
                        g.FillRectangle(Brushes.MidnightBlue,16,16,12,12);
                        using(var gold=new Pen(Color.Gold,2)) g.DrawArc(gold,19,15,6,9,180,180);
                        g.FillRectangle(Brushes.Gold,17,20,11,8);
                    }
                } else if (kind == "tools") {
                    using(var thick = new Pen(Color.FromArgb(113,164,231),6)) g.DrawLine(thick,5,24,19,9);
                    g.DrawArc(blue,13,1,13,13,30,280); g.DrawEllipse(blue,3,21,4,4);
                } else if (kind == "vpn") {
                    g.DrawPolygon(blue, new[] {new Point(14,2),new Point(24,6),new Point(22,18),new Point(14,26),new Point(6,18),new Point(4,6)});
                    g.DrawLine(blue, 14, 8, 14, 19); g.DrawLine(blue, 9, 13, 19, 13);
                } else if (kind == "powershell") {
                    g.Clear(Color.FromArgb(0, 137, 239)); g.DrawLines(Pens.White, new[]{new Point(7,7),new Point(14,13),new Point(6,21)}); g.DrawLine(Pens.White, 15, 21, 22, 21);
                } else if (kind == "disconnect") {
                    using (var pen = new Pen(Color.FromArgb(245,80,80), 4)) g.DrawEllipse(pen, 4, 4, 20, 20);
                } else if (kind == "copy") {
                    g.FillRectangle(Brushes.LightSteelBlue, 7, 6, 16, 20); g.FillRectangle(Brushes.SlateGray, 11, 3, 8, 6);
                    g.DrawLine(blue, 10, 14, 20, 14); g.DrawLine(blue, 10, 19, 20, 19);
                } else if (kind == "server") {
                    g.FillRectangle(Brushes.SteelBlue, 6, 2, 16, 24);
                    for(int y=5;y<24;y+=8) {g.FillRectangle(Brushes.MidnightBlue, 8,y,12,5);g.FillEllipse(Brushes.LightSkyBlue,9,y+1,2,2);}
                } else if (kind == "info") {
                    g.FillEllipse(Brushes.DodgerBlue, 1, 1, 26, 26);
                    using(var font = new Font("Segoe UI",18,FontStyle.Bold)) g.DrawString("i",font,Brushes.White,8,-3);
                } else if (kind == "fullscreen") {
                    g.DrawLines(blue,new[]{new Point(3,11),new Point(3,3),new Point(11,3)});
                    g.DrawLines(blue,new[]{new Point(17,3),new Point(25,3),new Point(25,11)});
                    g.DrawLines(blue,new[]{new Point(25,17),new Point(25,25),new Point(17,25)});
                    g.DrawLines(blue,new[]{new Point(11,25),new Point(3,25),new Point(3,17)});
                } else {
                    g.DrawEllipse(blue, 7,7,14,14);
                    for(int i=0;i<8;i++) {double a=i*Math.PI/4; g.DrawLine(blue,14+(float)Math.Cos(a)*9,14+(float)Math.Sin(a)*9,14+(float)Math.Cos(a)*13,14+(float)Math.Sin(a)*13);}
                }
            }
            return bitmap;
        }
    }
}
