using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

// 开发用离屏预览：不触发开启/停止桥接按钮，也不进入消息循环。
internal static class UiPreview
{
    static int failures;
    static void Check(Control parent) {
        foreach(Control c in parent.Controls) {
            if(c is Label || c is Button || c is ComboBox) {
                if(!parent.ClientRectangle.Contains(c.Bounds)) {Console.WriteLine("CLIPPED: "+c.Text);failures++;}
                foreach(Control other in parent.Controls) if(other!=c && other.Visible && c.Bounds.IntersectsWith(other.Bounds)) {Console.WriteLine("OVERLAP: "+c.Text+" / "+other.Text);failures++;}
            }
            Check(c);
        }
    }
    [STAThread] static void Main(string[] args) {
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        using(A350Isis.MainWindow form=new A350Isis.MainWindow(args[0])) {
            form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-30000,-30000);form.ShowInTaskbar=false;form.Show();
            if(args.Length>2) form.Scale(new SizeF(float.Parse(args[2],System.Globalization.CultureInfo.InvariantCulture),float.Parse(args[2],System.Globalization.CultureInfo.InvariantCulture)));
            Check(form);
            // 重复更新实际控件文本，覆盖触发重复缩放的 OnTextChanged 路径。
            for(int i=0;i<5;i++) foreach(string status in new[]{"桥接已开启，等待连接","实时数据传输中","桥接未开启"}) {
                form.Controls.Find("connectionStatus",true)[0].Text=status;
                form.Controls.Find("simulatorStatus",true)[0].Text="模拟器：等待 MSFS 进入航班";
                form.Controls.Find("deviceStatus",true)[0].Text="设备：COM6 · 等待 USB 设备";
                form.Controls.Find("flightValues",true)[0].Text="空速 270.0 kt    高度 38000.0 ft";
                Check(form);
            }
            form.Controls.Find("connectionStatus",true)[0].Text="桥接已开启，等待连接";
            using(Bitmap image=new Bitmap(form.Width,form.Height)) {
                form.DrawToBitmap(image,new Rectangle(0,0,image.Width,image.Height));image.Save(args[1],ImageFormat.Png);
            }
        }
        Console.WriteLine(failures==0?"PASS: no clipped or overlapping controls after repeated status updates.":"FAIL: layout errors="+failures);
        Environment.ExitCode=failures==0?0:1;
    }
}
