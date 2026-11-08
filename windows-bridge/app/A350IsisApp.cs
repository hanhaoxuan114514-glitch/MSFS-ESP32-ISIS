using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32.SafeHandles;

namespace A350Isis
{
    internal static class Native
    {
        [DllImport("shell32.dll", SetLastError=true)] internal static extern IntPtr CommandLineToArgvW([MarshalAs(UnmanagedType.LPWStr)] string command, out int count);
        [DllImport("kernel32.dll")] internal static extern IntPtr LocalFree(IntPtr pointer);
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] internal static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, uint count, uint flags);
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] internal static extern bool QueryFullProcessImageName(IntPtr handle, int flags, StringBuilder path, ref int count);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] internal static extern IntPtr FindWindow(string className, string title);
        [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr window, int message, IntPtr w, IntPtr l);
        internal static string CanonicalFile(string path)
        {
            try {
                using (FileStream file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) {
                    StringBuilder value = new StringBuilder(4096);
                    uint count = GetFinalPathNameByHandle(file.SafeFileHandle, value, (uint)value.Capacity, 0);
                    if (count > 0 && count < value.Capacity) return value.ToString().Replace(@"\\?\", "");
                }
            } catch { }
            return Path.GetFullPath(path);
        }
        internal static string[] Arguments(string command)
        {
            int count; IntPtr array = CommandLineToArgvW(command, out count);
            if (array == IntPtr.Zero) return new string[0];
            try {
                string[] result = new string[count];
                for (int i=0;i<count;i++) result[i] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(array, i*IntPtr.Size));
                return result;
            } finally { LocalFree(array); }
        }
    }

    internal static class ProcessIdentity
    {
        internal static bool ValidPort(string value) { return value != null && Regex.IsMatch(value, @"^COM[0-9]+$", RegexOptions.IgnoreCase); }
        private static string Normalize(string path, string root)
        {
            path = path.Replace('/', '\\');
            // 兼容本项目旧脚本使用的 W: 别名；进程位置和启动时间仍须同时匹配。
            if (path.StartsWith(@"W:\", StringComparison.OrdinalIgnoreCase)) path = Path.Combine(root, path.Substring(3));
            return Path.GetFullPath(path).TrimEnd('\\');
        }
        internal static bool IsProjectBridge(string root, DateTime recorded, DateTime actual, string image, string command)
        {
            try {
                if (recorded.ToUniversalTime().Ticks != actual.ToUniversalTime().Ticks) return false;
                string normalized = Normalize(image,root);
                if (!normalized.StartsWith(Path.Combine(root,"tools")+"\\",StringComparison.OrdinalIgnoreCase) ||
                    !String.Equals(Path.GetFileName(normalized),"python.exe",StringComparison.OrdinalIgnoreCase)) return false;
                string expected = Path.Combine(root,"windows-bridge","bridge.py");
                foreach (string arg in Native.Arguments(command))
                    if (String.Equals(Normalize(arg,root),expected,StringComparison.OrdinalIgnoreCase)) return true;
            } catch { }
            return false;
        }
    }

    internal sealed class DeckButton : Button
    {
        protected override void OnPaint(PaintEventArgs e) {
            Color fill=Enabled?BackColor:Color.FromArgb(26,39,55);
            using(SolidBrush brush=new SolidBrush(fill)) e.Graphics.FillRectangle(brush,ClientRectangle);
            TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,Enabled?ForeColor:Color.FromArgb(106,128,150),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
            if(Focused&&ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(ClientRectangle,-4,-4));
        }
    }

    internal sealed class Surface : Panel
    {
        internal Surface() {DoubleBuffered=true;BackColor=Color.FromArgb(23,34,50);}
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using(Pen border=new Pen(Color.FromArgb(44,61,80))) e.Graphics.DrawRectangle(border,0,0,Width-1,Height-1);
        }
    }

    internal sealed class MainWindow : Form
    {
        internal const string Title = "A350 ISIS 助手";
        private const int ShowMessage = 0x8001;
        private readonly string root, statePath, logPath;
        private readonly ComboBox ports = new ComboBox();
        private readonly Button start = new DeckButton(), stop = new DeckButton(), rescan = new DeckButton();
        private readonly Label overall, simulator, device, values;
        private readonly NotifyIcon tray = new NotifyIcon();
        private readonly ToolStripMenuItem trayStart, trayStop;
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private readonly Queue<string> pending = new Queue<string>();
        private readonly object logLock = new object();
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();
        private StreamWriter log;
        private Process bridge;
        private DateTime bridgeStart, lastTelemetry = DateTime.MinValue;
        private bool changing, quitting, sourceReady, usbReady, fresh, notified;
        private string selectedPort = "COM6", legacyLog;
        private long legacyPosition;
        private readonly StringBuilder legacyPending = new StringBuilder();
        private readonly Decoder legacyDecoder = Encoding.UTF8.GetDecoder();

        internal MainWindow(string projectRoot)
        {
            root=projectRoot; statePath=Path.Combine(root,"logs","bridge-process.json"); logPath=Path.Combine(root,"logs","app-bridge.log");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath));
            log=new StreamWriter(new FileStream(logPath,FileMode.Append,FileAccess.Write,FileShare.ReadWrite),new UTF8Encoding(false)); log.AutoFlush=true;
            SuspendLayout();
            AutoScaleDimensions=new SizeF(96F,96F);AutoScaleMode=AutoScaleMode.Dpi;
            Font=new Font("Microsoft YaHei UI",9F);ForeColor=Color.FromArgb(224,233,243);BackColor=Color.FromArgb(12,20,32);
            overall=new Label();simulator=new Label();device=new Label();values=new Label();
            overall.Font=new Font(Font.FontFamily,13F,FontStyle.Bold);simulator.Font=Font;device.Font=Font;values.Font=new Font(Font.FontFamily,8F);
            // 动态标签按文字测量，避免旧版 WinForms 在高 DPI 的 OnTextChanged 中重复放大请求尺寸。
            overall.AutoSize=simulator.AutoSize=device.AutoSize=values.AutoSize=true;
            overall.Name="connectionStatus";simulator.Name="simulatorStatus";device.Name="deviceStatus";values.Name="flightValues";
            Text=Title;ClientSize=new Size(560,468);
            MaximizeBox=false;FormBorderStyle=FormBorderStyle.FixedSingle;StartPosition=FormStartPosition.CenterScreen;
            Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            PictureBox logo=new PictureBox();logo.SetBounds(26,27,62,62);logo.SizeMode=PictureBoxSizeMode.Zoom;
            using(Stream art=typeof(MainWindow).Assembly.GetManifestResourceStream("A350Isis.Logo")) if(art!=null) using(Image source=Image.FromStream(art)) logo.Image=new Bitmap(source);
            Controls.Add(logo);
            AddLabel("FLIGHT DECK  /  USB LINK",104,25,410,19,8,Color.FromArgb(95,186,222));
            AddLabel("A350 ISIS",102,45,410,38,23,Color.FromArgb(242,247,253));
            AddLabel("备用仪表 · 有线连接助手",104,88,400,24,9,Color.FromArgb(143,163,185));
            Surface statusCard=new Surface();statusCard.SetBounds(24,131,512,166);Controls.Add(statusCard);
            AddLabel("连接状态",19,14,460,23,9,Color.FromArgb(139,160,182),statusCard);
            overall.SetBounds(19,42,470,30);statusCard.Controls.Add(overall);
            simulator.SetBounds(19,82,470,23);statusCard.Controls.Add(simulator);
            device.SetBounds(19,108,470,23);statusCard.Controls.Add(device);
            values.SetBounds(19,137,470,22);values.ForeColor=Color.FromArgb(139,160,182);statusCard.Controls.Add(values);
            AddLabel("设备串口",25,315,100,24,9,Color.FromArgb(153,173,194));
            ports.SetBounds(24,343,116,32);ports.DropDownStyle=ComboBoxStyle.DropDownList;ports.AccessibleName="串口";
            ports.FlatStyle=FlatStyle.Flat;ports.BackColor=Color.FromArgb(29,44,62);ports.ForeColor=ForeColor;Controls.Add(ports);
            ConfigureButton(rescan,"刷新",150,340,68,38);ConfigureButton(start,"开启桥接",246,340,139,38);ConfigureButton(stop,"停止桥接",397,340,139,38);
            start.BackColor=Color.FromArgb(41,147,221);start.ForeColor=Color.White;
            LinkLabel openLog=new LinkLabel();openLog.Font=Font;openLog.Text="打开日志";openLog.SetBounds(24,407,85,25);StyleLink(openLog);Controls.Add(openLog);
            LinkLabel hide=new LinkLabel();hide.Font=Font;hide.Text="收到托盘";hide.SetBounds(122,407,85,25);StyleLink(hide);Controls.Add(hide);
            AddLabel("关闭窗口后，连接仍在后台运行",24,440,512,20,8,Color.FromArgb(112,136,158));
            ContextMenuStrip menu=new ContextMenuStrip(); menu.Items.Add("打开控制窗口",null,delegate { ShowWindow(); }); menu.Items.Add(new ToolStripSeparator());
            trayStart=new ToolStripMenuItem("开启桥接",null,delegate { StartBridge(); }); trayStop=new ToolStripMenuItem("停止桥接",null,delegate { StopBridge(false); });
            menu.Items.Add(trayStart);menu.Items.Add(trayStop);menu.Items.Add(new ToolStripSeparator());menu.Items.Add("退出",null,delegate { ExitApp(); });
            tray.Icon=Icon; tray.Text=Title; tray.ContextMenuStrip=menu; tray.Visible=true; tray.DoubleClick+=delegate { ShowWindow(); };
            start.Click+=delegate { StartBridge(); }; stop.Click+=delegate { StopBridge(false); }; rescan.Click+=delegate { RefreshPorts(); };
            openLog.LinkClicked+=delegate { OpenLog(); }; hide.LinkClicked+=delegate { HideWindow(); };
            ports.SelectedIndexChanged+=delegate { if (ports.SelectedItem!=null) {selectedPort=ports.SelectedItem.ToString();SavePort();} };
            FormClosing+=OnClosing; Resize+=delegate { if(WindowState==FormWindowState.Minimized) HideWindow(); };
            LoadPort(); RefreshPorts(); TryAdopt(); UpdateState();
            timer.Interval=250;timer.Tick+=Poll;timer.Start();
            Shown+=delegate { Poll(null,EventArgs.Empty); };
            ResumeLayout(false);
        }
        private void AddLabel(string text,int x,int y,int width,int height,float size,Color color,Control parent=null)
        {
            Label label=new Label();label.Text=text;label.SetBounds(x,y,width,height);label.Font=new Font(Font.FontFamily,size);label.ForeColor=color;(parent??this).Controls.Add(label);
        }
        private static void StyleLink(LinkLabel link) {link.LinkColor=Color.FromArgb(106,190,234);link.ActiveLinkColor=Color.White;link.VisitedLinkColor=link.LinkColor;link.LinkBehavior=LinkBehavior.HoverUnderline;}
        private void ConfigureButton(Button button,string text,int x,int y,int w,int h)
        {
            button.Text=text;button.SetBounds(x,y,w,h);button.FlatStyle=FlatStyle.Flat;button.FlatAppearance.BorderSize=0;button.BackColor=Color.FromArgb(33,49,69);button.ForeColor=Color.FromArgb(222,234,246);button.Cursor=Cursors.Hand;button.UseVisualStyleBackColor=false;Controls.Add(button);
        }
        private void Append(string line)
        {
            if(String.IsNullOrEmpty(line)) return;
            lock(logLock) {
                try { if(log!=null) log.WriteLine(DateTime.Now.ToString("HH:mm:ss")+" "+line); } catch { }
                if(pending.Count>=500) pending.Dequeue();pending.Enqueue(line.Length>2048?line.Substring(0,2048):line);
            }
        }
        private void QueueLine(string line)
        {
            if(String.IsNullOrEmpty(line)) return;
            lock(logLock) {if(pending.Count>=500) pending.Dequeue();pending.Enqueue(line.Length>2048?line.Substring(0,2048):line);}
        }
        private bool Running { get { try { return bridge!=null && !bridge.HasExited; } catch { return false; } } }
        private void LoadPort()
        {
            foreach(string path in new[]{Path.Combine(root,"logs","app-settings.json"),Path.Combine(root,"windows-bridge","config.example.json")}) {
                try { Dictionary<string,object> settings=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(path));object port;
                    if(settings.TryGetValue("port",out port) && ProcessIdentity.ValidPort(Convert.ToString(port))) {selectedPort=Convert.ToString(port);return;} } catch { }
            }
        }
        private void SavePort() { try { File.WriteAllText(Path.Combine(root,"logs","app-settings.json"),json.Serialize(new Dictionary<string,object>{{"port",selectedPort}}),Encoding.UTF8); } catch { } }
        private void RefreshPorts()
        {
            string previous=selectedPort;List<string> found=new List<string>();
            try { found.AddRange(SerialPort.GetPortNames()); } catch(Exception e) {Append(e.Message);}
            if(!found.Contains(previous)) found.Add(previous);found.Sort(StringComparer.OrdinalIgnoreCase);
            ports.Items.Clear();foreach(string value in found) ports.Items.Add(value);ports.SelectedItem=previous;
        }
        private void TryAdopt()
        {
            Process candidate=null;
            try {
                Dictionary<string,object> state=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(statePath));
                int pid=Convert.ToInt32(state["process_id"]);candidate=Process.GetProcessById(pid);
                DateTime recorded=DateTime.Parse(Convert.ToString(state["start_time_utc"]),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind);
                StringBuilder image=new StringBuilder(4096);int length=image.Capacity;
                if(!Native.QueryFullProcessImageName(candidate.Handle,0,image,ref length)) throw new InvalidOperationException("无法核对旧桥接程序位置");
                string command;
                using(ManagementObject info=new ManagementObject("Win32_Process.Handle='"+pid.ToString(CultureInfo.InvariantCulture)+"'")) {info.Get();command=Convert.ToString(info["CommandLine"]);}
                if(!ProcessIdentity.IsProjectBridge(root,recorded,candidate.StartTime,image.ToString(),command)) throw new InvalidOperationException("旧进程身份不符，未接管");
                bridge=candidate;candidate=null;bridgeStart=bridge.StartTime;
                if(state.ContainsKey("port") && ProcessIdentity.ValidPort(Convert.ToString(state["port"]))) {selectedPort=Convert.ToString(state["port"]);RefreshPorts();}
                legacyLog=state.ContainsKey("stderr_log")?Convert.ToString(state["stderr_log"]):Path.Combine(root,"logs","bridge-live.stderr.log");
                if(legacyLog.StartsWith(@"W:\",StringComparison.OrdinalIgnoreCase)) legacyLog=Path.Combine(root,legacyLog.Substring(3));
                if(!Path.GetFullPath(legacyLog).StartsWith(Path.Combine(root,"logs")+"\\",StringComparison.OrdinalIgnoreCase)) legacyLog=null;
                if(legacyLog!=null && File.Exists(legacyLog)) legacyPosition=new FileInfo(legacyLog).Length;
                Append("已接管原有后台桥接，等待新的设备状态。");
            } catch(FileNotFoundException) { } catch(ArgumentException) { } catch(Exception e) {Append("旧桥接检测："+e.Message);}
            finally {if(candidate!=null) candidate.Dispose();}
        }
        private void StartBridge()
        {
            if(Running || changing) return;
            if(!ProcessIdentity.ValidPort(selectedPort)) {MessageBox.Show(this,"请选择有效的串口。",Title);return;}
            try {
                string python=Path.Combine(root,"tools","python311","tools","python.exe");
                string packages=Path.Combine(root,"tools","espressif","python_env","idf5.3_py3.11_env","Lib","site-packages");
                if(!File.Exists(python) || !File.Exists(Path.Combine(packages,"serial","__init__.py"))) throw new FileNotFoundException("项目内运行环境不完整，请保留 tools 文件夹。");
                ProcessStartInfo info=new ProcessStartInfo(python);
                string script=Path.Combine(root,"windows-bridge","bridge.py");
                info.Arguments="-u \""+script+"\" --config \""+Path.Combine(root,"windows-bridge","config.example.json")+"\" --port "+selectedPort;
                info.WorkingDirectory=root;info.UseShellExecute=false;info.CreateNoWindow=true;info.WindowStyle=ProcessWindowStyle.Hidden;
                info.RedirectStandardError=true;info.RedirectStandardOutput=true;
                info.EnvironmentVariables.Remove("PYTHONHOME"); info.EnvironmentVariables["PYTHONPATH"]=packages;info.EnvironmentVariables["PYTHONUTF8"]="1";
                info.EnvironmentVariables["PYTHONPYCACHEPREFIX"]=Path.Combine(root,"cache","pycache");
                string temp=Path.Combine(root,"cache","tmp");Directory.CreateDirectory(temp);info.EnvironmentVariables["TEMP"]=temp;info.EnvironmentVariables["TMP"]=temp;
                Process process=new Process();process.StartInfo=info;
                process.ErrorDataReceived+=delegate(object sender,DataReceivedEventArgs e) {Append(e.Data);};
                process.OutputDataReceived+=delegate(object sender,DataReceivedEventArgs e) {if(e.Data!=null) Append(e.Data);};
                if(!process.Start()) throw new InvalidOperationException("未能启动桥接程序。");
                bridge=process;bridgeStart=process.StartTime;legacyLog=null;lastTelemetry=DateTime.MinValue;sourceReady=usbReady=fresh=false;
                process.BeginErrorReadLine();process.BeginOutputReadLine();
                File.WriteAllText(statePath,json.Serialize(new Dictionary<string,object>{
                    {"process_id",process.Id},{"start_time_utc",bridgeStart.ToUniversalTime().ToString("o")},{"script_path",script},
                    {"port",selectedPort},{"stdout_log",logPath},{"stderr_log",logPath},{"owner","desktop-app"}}),Encoding.UTF8);
                Append("桥接已开启，正在连接模拟器和设备。");
            } catch(Exception e) {Append(e.ToString());MessageBox.Show(this,"无法开启桥接："+e.Message,Title,MessageBoxButtons.OK,MessageBoxIcon.Warning);}
            UpdateState();
        }
        private void StopBridge(bool exitAfter)
        {
            if(changing) return;
            if(!Running) {ReleaseBridge();if(exitAfter) {quitting=true;Close();}else UpdateState();return;}
            changing=true;UpdateState();Process stopping=bridge;
            ThreadPool.QueueUserWorkItem(delegate {
                Exception error=null;
                try {stopping.Kill();if(!stopping.WaitForExit(2000)) throw new TimeoutException("桥接尚未退出");} catch(InvalidOperationException) { } catch(Exception e) {error=e;}
                if(IsDisposed) return;
                try {BeginInvoke((Action)delegate {
                    changing=false;
                    if(error!=null) {Append(error.ToString());quitting=false;ShowWindow();MessageBox.Show(this,"未能停止桥接，请打开日志查看详情。",Title);UpdateState();return;}
                    ReleaseBridge();Append("桥接已停止。");UpdateState();if(exitAfter) {quitting=true;Close();}
                });} catch(InvalidOperationException) { }
            });
        }
        private void ReleaseBridge()
        {
            if(bridge!=null) {
                try {Dictionary<string,object> state=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(statePath));if(Convert.ToInt32(state["process_id"])==bridge.Id) File.Delete(statePath);} catch { }
                bridge.Dispose();bridge=null;
            }
            sourceReady=usbReady=fresh=false;legacyLog=null;lastTelemetry=DateTime.MinValue;
        }
        private void ReadLegacyLog()
        {
            if(legacyLog==null) return;
            try {using(FileStream stream=new FileStream(legacyLog,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)) {
                if(stream.Length<legacyPosition) {legacyPosition=0;legacyPending.Clear();legacyDecoder.Reset();}
                stream.Position=legacyPosition;byte[] bytes=new byte[8192];int read=stream.Read(bytes,0,bytes.Length);legacyPosition=stream.Position;
                char[] chars=new char[8192];int count=legacyDecoder.GetChars(bytes,0,read,chars,0,false);
                for(int i=0;i<count;i++) {if(chars[i]=='\n') {QueueLine(legacyPending.ToString().TrimEnd('\r'));legacyPending.Clear();}else if(legacyPending.Length<2048) legacyPending.Append(chars[i]);}
            }} catch { }
        }
        private void Poll(object sender,EventArgs args)
        {
            if(!changing && bridge!=null && !Running) {Append("桥接程序已退出，可重新开启。");ReleaseBridge();}
            ReadLegacyLog();
            for(int i=0;i<100;i++) {
                string line;lock(logLock) {if(pending.Count==0) break;line=pending.Dequeue();}
                if(line.StartsWith("SimConnect opened:")) sourceReady=true;
                if(line.StartsWith("SimConnect_Open:") || line.Contains("simulator quit") || line.Contains("no fresh simulator data") || line.Contains("SimConnect exception")) {sourceReady=false;fresh=false;}
                if(line.StartsWith("USB unavailable:") || line.StartsWith("USB disconnected")) {usbReady=false;fresh=false;}
                if(line.StartsWith("[device]")) usbReady=true;
                Match status=Regex.Match(line,@"telemetry: connected=([01])\s+seq=\d+");
                if(status.Success) {
                    lastTelemetry=DateTime.UtcNow;fresh=status.Groups[1].Value=="1";if(fresh) sourceReady=usbReady=true;
                    Match sample=Regex.Match(line,@"IAS=([\d.]+) ALT=(-?[\d.]+) HDG=([\d.]+)");
                    if(fresh && sample.Success) values.Text="空速 "+sample.Groups[1].Value+" kt    高度 "+sample.Groups[2].Value+" ft";
                }
            }
            if((DateTime.UtcNow-lastTelemetry).TotalSeconds>8) fresh=false;
            UpdateState();
        }
        private void UpdateState()
        {
            bool running=Running;
            start.Enabled=trayStart.Enabled=!running&&!changing;stop.Enabled=trayStop.Enabled=running&&!changing;ports.Enabled=rescan.Enabled=!running&&!changing;
            overall.Text=changing?"正在停止…":!running?"桥接未开启":fresh?"实时数据传输中":"桥接已开启，等待连接";
            overall.ForeColor=fresh&&running?Color.FromArgb(83,221,162):running?Color.FromArgb(255,208,111):Color.FromArgb(224,233,243);
            simulator.Text="模拟器："+(!running?"未连接":sourceReady?"已连接":"等待 MSFS 进入航班");
            device.Text="设备："+selectedPort+" · "+(!running?"未连接":usbReady?"已连接": "等待 USB 设备");
            if(!fresh || !running) values.Text=running?"连接恢复后会自动继续传输。":"选择串口后点击“开启桥接”。";
            tray.Text=Title+" · "+(!running?"已停止":fresh?"实时传输中":"等待连接");
        }
        private void OpenLog()
        {
            try {Process.Start(new ProcessStartInfo("explorer.exe","/select,\""+(legacyLog??logPath)+"\""){UseShellExecute=true});}catch(Exception e){MessageBox.Show(this,e.Message,Title);}
        }
        private void HideWindow()
        {
            Hide();ShowInTaskbar=false;
            if(!notified) {notified=true;tray.ShowBalloonTip(1800,Title,"已收到托盘。右键图标可开启、停止或退出。",ToolTipIcon.Info);}
        }
        private void ShowWindow() {ShowInTaskbar=true;Show();WindowState=FormWindowState.Normal;Activate();}
        private void ExitApp() {if(changing) return;StopBridge(true);}
        private void OnClosing(object sender,FormClosingEventArgs args)
        {
            if(args.CloseReason==CloseReason.WindowsShutDown || args.CloseReason==CloseReason.TaskManagerClosing) {
                try {if(Running) {bridge.Kill();bridge.WaitForExit(1000);}ReleaseBridge();}catch { }quitting=true;
            }
            if(!quitting) {args.Cancel=true;HideWindow();}
        }
        protected override void WndProc(ref Message message) {if(message.Msg==ShowMessage) ShowWindow();base.WndProc(ref message);}
        protected override void Dispose(bool disposing)
        {
            if(disposing) {timer.Stop();timer.Dispose();tray.Visible=false;tray.Dispose();lock(logLock){if(log!=null){log.Dispose();log=null;}}}
            base.Dispose(disposing);
        }
    }
    internal static class Program
    {
        private static string FindRoot()
        {
            DirectoryInfo folder=new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            for(int i=0;i<5 && folder!=null;i++,folder=folder.Parent) {
                string script=Path.Combine(folder.FullName,"windows-bridge","bridge.py");
                if(File.Exists(script)) return Path.GetDirectoryName(Path.GetDirectoryName(Native.CanonicalFile(script)));
            }
            throw new DirectoryNotFoundException("请把应用保留在项目文件夹内，与 windows-bridge 和 tools 文件夹一起使用。");
        }
        [STAThread] private static void Main()
        {
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            try {
                string root=FindRoot();string key;
                using(System.Security.Cryptography.SHA256 hash=System.Security.Cryptography.SHA256.Create()) key=BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(root.ToUpperInvariant()))).Replace("-","").Substring(0,16);
                bool created;using(Mutex mutex=new Mutex(true,"Local\\A350ISIS_"+key,out created)) {
                    if(!created) {IntPtr window=Native.FindWindow(null,MainWindow.Title);if(window!=IntPtr.Zero) Native.PostMessage(window,0x8001,IntPtr.Zero,IntPtr.Zero);return;}
                    try {Application.Run(new MainWindow(root));}finally {mutex.ReleaseMutex();}
                }
            }catch(Exception e){MessageBox.Show(e.Message,MainWindow.Title,MessageBoxButtons.OK,MessageBoxIcon.Error);}
        }
    }
}
