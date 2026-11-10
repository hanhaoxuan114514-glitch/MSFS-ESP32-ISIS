using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

// 原创矢量仪表图标：圆角方形里的姿态仪（天空、地面、地平线与飞机符号）。
// 每个尺寸单独绘制，小尺寸省略细节以保持清晰。
internal static class IconArtwork
{
    static GraphicsPath Squircle(float x,float y,float w,float h,float r)
    {
        GraphicsPath p=new GraphicsPath();float d=r*2;
        p.AddArc(x,y,d,d,180,90);p.AddArc(x+w-d,y,d,d,270,90);
        p.AddArc(x+w-d,y+h-d,d,d,0,90);p.AddArc(x,y+h-d,d,d,90,90);p.CloseFigure();return p;
    }
    static Bitmap Draw(int size)
    {
        Bitmap b=new Bitmap(size,size,PixelFormat.Format32bppArgb);
        using(Graphics g=Graphics.FromImage(b)) {
            g.SmoothingMode=SmoothingMode.AntiAlias;g.PixelOffsetMode=PixelOffsetMode.HighQuality;
            g.ScaleTransform(size/256f,size/256f);
            bool tiny=size<=24,small=size<=48;
            float inset=tiny?2:8,radius=tiny?52:58;
            using(GraphicsPath tile=Squircle(inset,inset,256-2*inset,256-2*inset,radius)) {
                if(!small) using(GraphicsPath drop=Squircle(inset,inset+5,256-2*inset,256-2*inset,radius))
                    using(var shade=new SolidBrush(Color.FromArgb(46,0,0,0))) g.FillPath(shade,drop);
                using(var bezel=new LinearGradientBrush(new RectangleF(0,0,256,256),Color.FromArgb(52,58,74),Color.FromArgb(16,18,26),90f))
                    g.FillPath(bezel,tile);
                GraphicsState state=g.Save();
                g.SetClip(tile);
                // 仪表窗口
                float m=tiny?24:small?30:38;
                RectangleF dial=new RectangleF(m,m,256-2*m,256-2*m);
                using(GraphicsPath face=Squircle(dial.X,dial.Y,dial.Width,dial.Height,radius-m+18)) {
                    g.SetClip(face,CombineMode.Intersect);
                    g.TranslateTransform(128,128);g.RotateTransform(-9);g.TranslateTransform(-128,-128);
                    float horizon=136;
                    using(var sky=new LinearGradientBrush(new RectangleF(-60,-60,376,horizon+61),Color.FromArgb(28,110,232),Color.FromArgb(104,186,255),90f){WrapMode=WrapMode.TileFlipXY})
                        g.FillRectangle(sky,-60,-60,376,horizon+60);
                    using(var ground=new LinearGradientBrush(new RectangleF(-60,horizon,376,200),Color.FromArgb(176,110,58),Color.FromArgb(104,60,28),90f){WrapMode=WrapMode.TileFlipXY})
                        g.FillRectangle(ground,-60,horizon,376,200);
                    using(var line=new Pen(Color.White,tiny?10:7)) g.DrawLine(line,-60,horizon,316,horizon);
                    if(!small) using(var ladder=new Pen(Color.FromArgb(220,255,255,255),4)) {
                        ladder.StartCap=ladder.EndCap=LineCap.Round;
                        g.DrawLine(ladder,104,horizon-34,152,horizon-34);g.DrawLine(ladder,114,horizon-64,142,horizon-64);
                        g.DrawLine(ladder,104,horizon+50,152,horizon+50);
                    }
                    g.ResetTransform();g.ScaleTransform(size/256f,size/256f);
                    using(var gloss=new LinearGradientBrush(dial,Color.FromArgb(54,255,255,255),Color.FromArgb(0,255,255,255),90f))
                        g.FillRectangle(gloss,dial.X,dial.Y,dial.Width,dial.Height*.45f);
                }
                g.Restore(state);
                // 固定飞机符号
                using(var wing=new Pen(Color.FromArgb(255,206,46),tiny?18:small?15:13)) {
                    wing.StartCap=wing.EndCap=LineCap.Round;wing.LineJoin=LineJoin.Round;
                    g.DrawLines(wing,new[]{new PointF(58,134),new PointF(102,134),new PointF(128,156),new PointF(154,134),new PointF(198,134)});
                }
                if(!tiny) using(var rim=new Pen(Color.FromArgb(70,255,255,255),small?3:2)) g.DrawPath(rim,tile);
            }
        }
        return b;
    }
    static void Main(string[] args)
    {
        Directory.CreateDirectory(args[0]);int[] sizes={16,20,24,32,48,64,128,256};byte[][] frames=new byte[sizes.Length][];
        for(int i=0;i<sizes.Length;i++) using(Bitmap b=Draw(sizes[i])) using(MemoryStream ms=new MemoryStream()) {b.Save(ms,ImageFormat.Png);frames[i]=ms.ToArray();if(sizes[i]==256)b.Save(Path.Combine(args[0],"app-icon.png"),ImageFormat.Png);}
        using(BinaryWriter w=new BinaryWriter(File.Create(Path.Combine(args[0],"app.ico")))) {
            w.Write((ushort)0);w.Write((ushort)1);w.Write((ushort)sizes.Length);int offset=6+16*sizes.Length;
            for(int i=0;i<sizes.Length;i++) {w.Write((byte)(sizes[i]==256?0:sizes[i]));w.Write((byte)(sizes[i]==256?0:sizes[i]));w.Write((ushort)0);w.Write((ushort)1);w.Write((ushort)32);w.Write(frames[i].Length);w.Write(offset);offset+=frames[i].Length;}
            foreach(byte[] frame in frames)w.Write(frame);
        }
    }
}
