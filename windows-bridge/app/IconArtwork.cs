using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

// 原创矢量仪表图标；每个尺寸单独绘制，保留小尺寸的清晰线条。
internal static class IconArtwork
{
    static GraphicsPath Round(float x,float y,float w,float h,float r) {
        GraphicsPath p=new GraphicsPath();p.AddArc(x,y,r,r,180,90);p.AddArc(x+w-r,y,r,r,270,90);
        p.AddArc(x+w-r,y+h-r,r,r,0,90);p.AddArc(x,y+h-r,r,r,90,90);p.CloseFigure();return p;
    }
    static Bitmap Draw(int size) {
        Bitmap b=new Bitmap(size,size);using(Graphics g=Graphics.FromImage(b)) {
            g.SmoothingMode=SmoothingMode.AntiAlias;g.ScaleTransform(size/256f,size/256f);
            using(GraphicsPath p=Round(5,5,246,246,60))
            using(LinearGradientBrush bg=new LinearGradientBrush(new Point(0,0),new Point(256,256),Color.FromArgb(38,57,80),Color.FromArgb(10,19,32))) g.FillPath(bg,p);
            using(GraphicsPath face=Round(30,30,196,196,40)) {
                GraphicsState saved=g.Save();g.SetClip(face);
                using(LinearGradientBrush sky=new LinearGradientBrush(new Point(0,30),new Point(0,142),Color.FromArgb(36,169,238),Color.FromArgb(28,106,182))) g.FillRectangle(sky,28,28,200,106);
                using(LinearGradientBrush earth=new LinearGradientBrush(new Point(0,134),new Point(0,228),Color.FromArgb(184,95,62),Color.FromArgb(111,49,39))) g.FillRectangle(earth,28,134,200,96);
                using(Pen line=new Pen(Color.FromArgb(228,241,251),4)) {
                    g.DrawLine(line,29,134,227,134);
                    g.DrawLine(line,101,88,155,88);g.DrawLine(line,114,106,142,106);
                    g.DrawLine(line,114,163,142,163);g.DrawLine(line,101,181,155,181);
                }
                g.Restore(saved);
                using(Pen edge=new Pen(Color.FromArgb(85,116,148),3)) g.DrawPath(edge,face);
            }
            using(Pen white=new Pen(Color.FromArgb(232,242,250),4)) {
                for(int i=-2;i<=2;i++) {double a=(i*20-90)*Math.PI/180;float x=128+(float)Math.Cos(a)*76,y=128+(float)Math.Sin(a)*76;
                    g.DrawLine(white,x,y,128+(float)Math.Cos(a)*87,128+(float)Math.Sin(a)*87);}
            }
            using(Pen shadow=new Pen(Color.FromArgb(35,39,45),12)) {g.DrawLines(shadow,new[]{new Point(57,130),new Point(94,130),new Point(94,143)});g.DrawLines(shadow,new[]{new Point(162,143),new Point(162,130),new Point(199,130)});}
            using(Pen gold=new Pen(Color.FromArgb(255,211,90),6)) {g.DrawLines(gold,new[]{new Point(57,130),new Point(94,130),new Point(94,143)});g.DrawLines(gold,new[]{new Point(162,143),new Point(162,130),new Point(199,130)});g.DrawRectangle(gold,123,125,10,10);}
            using(SolidBrush gold=new SolidBrush(Color.FromArgb(255,211,90))) g.FillPolygon(gold,new[]{new Point(128,53),new Point(119,39),new Point(137,39)});
        }return b;
    }
    static void Main(string[] args) {
        Directory.CreateDirectory(args[0]);int[] sizes={16,20,24,32,48,64,128,256};byte[][] frames=new byte[sizes.Length][];
        for(int i=0;i<sizes.Length;i++) using(Bitmap b=Draw(sizes[i])) using(MemoryStream ms=new MemoryStream()) {b.Save(ms,ImageFormat.Png);frames[i]=ms.ToArray();if(sizes[i]==256)b.Save(Path.Combine(args[0],"app-icon.png"),ImageFormat.Png);}
        using(BinaryWriter w=new BinaryWriter(File.Create(Path.Combine(args[0],"app.ico")))) {
            w.Write((ushort)0);w.Write((ushort)1);w.Write((ushort)sizes.Length);int offset=6+16*sizes.Length;
            for(int i=0;i<sizes.Length;i++) {w.Write((byte)(sizes[i]==256?0:sizes[i]));w.Write((byte)(sizes[i]==256?0:sizes[i]));w.Write((ushort)0);w.Write((ushort)1);w.Write((ushort)32);w.Write(frames[i].Length);w.Write(offset);offset+=frames[i].Length;}
            foreach(byte[] frame in frames)w.Write(frame);
        }
    }
}
