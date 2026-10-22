// Copyright (c) 2026 DeskPilot contributors. Project MS-CL. Original vector artwork.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
namespace Terminals.Common
{
    public static class DeskPilotArtwork
    {
        public static void Populate(ImageList list,int count)
        {
            if(list==null)throw new ArgumentNullException("list");
            if(count<1 || count>512)throw new ArgumentOutOfRangeException("count");
            list.Images.Clear();list.ColorDepth=ColorDepth.Depth32Bit;
            var owned=new List<Bitmap>();
            // Keep the originals alive while WinForms may recreate its native
            // image list after a DPI/image-size change; release on owner disposal.
            list.Disposed+=delegate {foreach(var image in owned)image.Dispose();};
            for(int i=0;i<count;i++) {
                var image=new Bitmap(24,24);owned.Add(image);
                using(var g=Graphics.FromImage(image))using(var fill=new SolidBrush(i%3==0?Color.FromArgb(255,195,43):Color.FromArgb(75,158,238))) {
                    g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    if(i%3==0) {g.FillRectangle(fill,2,5,10,5);g.FillRectangle(fill,2,8,20,13);}
                    else {g.FillRectangle(fill,4,3,16,18);g.FillRectangle(Brushes.White,7,7,10,2);g.FillRectangle(Brushes.White,7,12,10,2);}
                }
                list.Images.Add(image);
            }
        }
    }
}
