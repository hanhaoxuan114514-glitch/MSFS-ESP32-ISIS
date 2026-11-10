using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

// 开发用离屏预览：不开启桥接，只渲染界面状态并检查控件是否越界或重叠。
internal static class UiPreview
{
    static int failures;
    static void Check(Control parent)
    {
        var scroll=parent as ScrollableControl;
        bool scrolls=scroll!=null && scroll.AutoScroll;
        foreach(Control c in parent.Controls) {
            if(!c.Visible) continue;
            Rectangle area=parent.ClientRectangle;
            bool inside=scrolls?c.Left>=0 && c.Right<=area.Right:area.Contains(c.Bounds);
            if(!inside && !(c is Form)) {Console.WriteLine("CLIPPED: "+Describe(c)+" in "+Describe(parent));failures++;}
            foreach(Control other in parent.Controls)
                if(other!=c && other.Visible && c.Bounds.IntersectsWith(other.Bounds) && c.GetHashCode()<other.GetHashCode()) {
                    Console.WriteLine("OVERLAP: "+Describe(c)+" / "+Describe(other));failures++;
                }
            Check(c);
        }
    }
    static string Describe(Control c) {return c.GetType().Name+(String.IsNullOrEmpty(c.Text)?"":" \""+c.Text+"\"")+" "+c.Bounds;}
    static void Settle(int milliseconds)
    {
        var watch=System.Diagnostics.Stopwatch.StartNew();
        while(watch.ElapsedMilliseconds<milliseconds) {Application.DoEvents();System.Threading.Thread.Sleep(5);}
    }
    static void Save(Form form,string path)
    {
        using(Bitmap image=new Bitmap(form.ClientSize.Width,form.ClientSize.Height)) {
            using(Bitmap whole=new Bitmap(form.Width,form.Height)) {
                form.DrawToBitmap(whole,new Rectangle(0,0,whole.Width,whole.Height));
                Point origin=form.PointToScreen(Point.Empty);
                using(Graphics g=Graphics.FromImage(image))
                    g.DrawImage(whole,new Rectangle(0,0,image.Width,image.Height),
                        new Rectangle(origin.X-form.Left,origin.Y-form.Top,image.Width,image.Height),GraphicsUnit.Pixel);
            }
            image.Save(path,ImageFormat.Png);
        }
        Console.WriteLine("saved "+path);
    }
    [STAThread] static void Main(string[] args)
    {
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        string root=args[0],output=args[1];
        bool dark=args.Length>2 && args[2]=="dark",ble=args.Length>3 && args[3]=="ble";
        A350Isis.Palette.Apply(dark);
        Directory.CreateDirectory(output);
        string suffix=(dark?"dark":"light")+(ble?"-ble":"");
        using(A350Isis.MainWindow form=new A350Isis.MainWindow(root)) {
            form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-30000,-30000);form.ShowInTaskbar=false;form.Show();
            form.PreviewScenario(true,ble);Settle(700);
            Check(form);Save(form,Path.Combine(output,"hud-live-"+suffix+".png"));
            form.PreviewScenario(false,ble);Settle(500);
            Save(form,Path.Combine(output,"hud-idle-"+suffix+".png"));
            form.Size=form.MinimumSize;Settle(300);
            Check(form);Save(form,Path.Combine(output,"hud-min-"+suffix+".png"));
        }
        using(var radar=new A350Isis.RadarWindow(root,null,true)) {
            radar.StartPosition=FormStartPosition.Manual;radar.Location=new Point(-30000,-30000);radar.ShowInTaskbar=false;
            radar.ClientSize=new Size(A350Isis.Ui.Px(840),A350Isis.Ui.Px(640));
            radar.Show();Settle(1500);
            Check(radar);Save(radar,Path.Combine(output,"radar-"+suffix+".png"));
        }
        Console.WriteLine(failures==0?"PASS: no clipped or overlapping controls.":"FAIL: layout errors="+failures);
        Environment.ExitCode=failures==0?0:1;
    }
}
