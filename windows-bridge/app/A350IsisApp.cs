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
            return BridgeMode(root,recorded,actual,image,command)!=null;
        }
        internal static string BridgeMode(string root, DateTime recorded, DateTime actual, string image, string command)
        {
            try {
                if (recorded.ToUniversalTime().Ticks != actual.ToUniversalTime().Ticks) return null;
                string normalized = Normalize(image,root);
                if (!normalized.StartsWith(Path.Combine(root,"tools")+"\\",StringComparison.OrdinalIgnoreCase) ||
                    !String.Equals(Path.GetFileName(normalized),"python.exe",StringComparison.OrdinalIgnoreCase)) return null;
                string folder = Path.Combine(root,"windows-bridge");
                foreach (string arg in Native.Arguments(command)) {
                    string script=Normalize(arg,root);
                    if (String.Equals(script,Path.Combine(folder,"bridge.py"),StringComparison.OrdinalIgnoreCase)) return "usb";
                    if (String.Equals(script,Path.Combine(folder,"ble_bridge.py"),StringComparison.OrdinalIgnoreCase)) return "ble";
                }
            } catch { }
            return null;
        }
    }

    internal sealed class MainWindow : Form
    {
        internal const string Title = "A350 ISIS 助手";
        private const int ShowMessage = 0x8001;
        private readonly string root, statePath, logPath;
        private readonly DeckButton ports = new DeckButton(), start = new DeckButton(), stop = new DeckButton(), rescan = new DeckButton();
        private readonly DeckButton logButton = new DeckButton(), trayButton = new DeckButton();
        private readonly ModePicker modes = new ModePicker();
        private readonly ContextMenuStrip portMenu = new ContextMenuStrip();
        private readonly ContextMenuStrip settingsMenu = new ContextMenuStrip();
        private readonly SideNav nav = new SideNav();
        private readonly PageHeader header = new PageHeader();
        private readonly StatusHero hero = new StatusHero();
        private readonly InfoTile simTile = new InfoTile(), deviceTile = new InfoTile();
        private readonly GroupCard group = new GroupCard("连接方式","设备串口");
        private readonly NotifyIcon tray = new NotifyIcon();
        private readonly ToolStripMenuItem trayStart, trayStop;
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private readonly Queue<string> pending = new Queue<string>();
        private readonly object logLock = new object();
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();
        private StreamWriter log;
        private Process bridge;
        private RadarWindow radarWindow;
        private readonly Panel workspace=new Panel(),hudCanvas=new Canvas();
        private bool bridgeSupportsRadar;
        private DateTime bridgeStart, lastTelemetry = DateTime.MinValue;
        private bool changing, quitting, sourceReady, usbReady, fresh, notified;
        private string selectedPort = "COM6", selectedMode = "usb", legacyLog;
        private string activeMode;
        private bool BluetoothMode { get { return selectedMode=="ble"; } }
        private long legacyPosition;
        private readonly StringBuilder legacyPending = new StringBuilder();
        private readonly Decoder legacyDecoder = Encoding.UTF8.GetDecoder();

        internal MainWindow(string projectRoot)
        {
            root=projectRoot; statePath=Path.Combine(root,"logs","bridge-process.json"); logPath=Path.Combine(root,"logs","app-bridge.log");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath));
            log=new StreamWriter(new FileStream(logPath,FileMode.Append,FileAccess.Write,FileShare.ReadWrite),new UTF8Encoding(false)); log.AutoFlush=true;
            SuspendLayout();
            // 所有尺寸由 Ui.Px 按系统 DPI 计算，不再叠加 WinForms 自动缩放。
            AutoScaleMode=AutoScaleMode.None;
            Font=Ui.Font(9.5F);ForeColor=Palette.Ink;BackColor=Palette.Paper;
            Text=Title;ClientSize=new Size(Ui.Px(1060),Ui.Px(700));MinimumSize=new Size(Ui.Px(820),Ui.Px(640));
            FormBorderStyle=FormBorderStyle.Sizable;StartPosition=FormStartPosition.CenterScreen;
            Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            using(Stream art=typeof(MainWindow).Assembly.GetManifestResourceStream("A350Isis.Logo")) if(art!=null) using(Image source=Image.FromStream(art)) nav.Logo=new Bitmap(source);
            BuildWorkspace();
            ContextMenuStrip menu=new ContextMenuStrip();Ui.StyleMenu(menu);Ui.StyleMenu(portMenu);
            menu.Items.Add("打开控制窗口",null,delegate { ShowWindow(); }); menu.Items.Add(new ToolStripSeparator());
            trayStart=new ToolStripMenuItem("开启桥接",null,delegate { StartBridge(); }); trayStop=new ToolStripMenuItem("停止桥接",null,delegate { StopBridge(false); });
            menu.Items.Add(trayStart);menu.Items.Add(trayStop);menu.Items.Add(new ToolStripSeparator());menu.Items.Add("退出",null,delegate { ExitApp(); });
            tray.Icon=Icon; tray.Text=Title; tray.ContextMenuStrip=menu; tray.Visible=true; tray.DoubleClick+=delegate { ShowWindow(); };
            start.Click+=delegate { StartBridge(); }; stop.Click+=delegate { StopBridge(false); }; rescan.Click+=delegate { RefreshPorts(); };
            ports.Click+=delegate {RefreshPorts();portMenu.Show(ports,new Point(0,ports.Height+Ui.Px(4)));};
            logButton.Click+=delegate { OpenLog(); }; trayButton.Click+=delegate { HideWindow(); };
            FormClosing+=OnClosing; Resize+=delegate { if(WindowState==FormWindowState.Minimized) HideWindow(); };
            LoadPort(); RefreshPorts(); TryAdopt();modes.SelectedIndex=BluetoothMode?1:0;
            try {RadarWindow.Disable(root);} catch(IOException e) {Append(e.Message);}
            modes.SelectedIndexChanged+=delegate {
                string mode=modes.SelectedIndex==1?"ble":"usb";
                if(mode==selectedMode) return;
                selectedMode=mode;SaveSettings();
                if(Running) StopBridge(false,true);else UpdateState();
            };
            UpdateState();
            timer.Interval=250;timer.Tick+=Poll;timer.Start();
            Shown+=delegate { Poll(null,EventArgs.Empty); };
            ResumeLayout(false);
        }
        protected override void OnHandleCreated(EventArgs e) {base.OnHandleCreated(e);Dwm.Frame(Handle);}
        private void BuildWorkspace()
        {
            header.Title="仪表连接";header.Subtitle="同步 MSFS 姿态、空速和高度";
            logButton.Style=trayButton.Style=DeckButton.Look.Ghost;
            logButton.Text="日志";logButton.Glyph="";logButton.AccessibleName="打开日志";
            trayButton.Text="收到托盘";trayButton.Glyph="";
            start.Style=DeckButton.Look.Primary;start.Text="开启桥接";start.Glyph="";
            stop.Text="停止";stop.Glyph="";
            rescan.Glyph="";rescan.AccessibleName="刷新串口";
            ports.Chevron=true;ports.AccessibleName="设备串口";
            simTile.Glyph="";simTile.Title="模拟器 · MSFS 2024";
            foreach(Control control in new Control[]{modes,ports,rescan}) {control.BackColor=Palette.Card;group.Controls.Add(control);}
            foreach(Control control in new Control[]{header,logButton,trayButton,hero,simTile,deviceTile,group,stop,start}) hudCanvas.Controls.Add(control);
            hudCanvas.AutoScroll=true;hudCanvas.Dock=DockStyle.Fill;Ui.DarkScrollbars(hudCanvas);
            workspace.Dock=DockStyle.Fill;workspace.BackColor=Palette.Paper;workspace.Controls.Add(hudCanvas);
            hudCanvas.Resize+=delegate {LayoutHud();};
            Shown+=delegate {LayoutHud();};
            nav.Dock=DockStyle.Left;nav.Width=Ui.Px(220);
            nav.Navigate=delegate(int page) {if(page==0) ShowHud();else OpenRadar();};
            BuildSettingsMenu();
            Controls.Add(workspace);Controls.Add(nav);
        }
        private void BuildSettingsMenu()
        {
            Ui.StyleMenu(settingsMenu);
            settingsMenu.AccessibleName="设置 · 强调色";
            settingsMenu.Items.Add(new ToolStripMenuItem("强调色"){Enabled=false});
            settingsMenu.Items.Add(new ToolStripSeparator());
            string[] names={"蓝色","紫色","粉色","红色","橙色","绿色","灰色"};
            var choices=new List<ToolStripMenuItem>();
            for(int i=0;i<Palette.Accents.Length;i++) {
                Color color=Palette.Accents[i];
                int size=Ui.Px(16);
                var swatch=new Bitmap(size,size);
                using(var graphics=Graphics.FromImage(swatch))
                using(var brush=new SolidBrush(color)) {
                    graphics.SmoothingMode=SmoothingMode.AntiAlias;
                    graphics.FillEllipse(brush,1,1,size-2,size-2);
                }
                var item=new ToolStripMenuItem(names[i],swatch,delegate {SetAccent(color);}){Tag=color};
                choices.Add(item);settingsMenu.Items.Add(item);
            }
            settingsMenu.Items.Add(new ToolStripSeparator());
            var custom=new ToolStripMenuItem("自定义颜色…",null,delegate {
                using(var chooser=new ColorDialog{Color=Palette.UserAccent,FullOpen=true})
                    if(chooser.ShowDialog(this)==DialogResult.OK) SetAccent(chooser.Color);
            });
            settingsMenu.Items.Add(custom);
            settingsMenu.Opening+=delegate {
                bool preset=false;
                for(int i=0;i<choices.Count;i++) {
                    bool selected=((Color)choices[i].Tag).ToArgb()==Palette.UserAccent.ToArgb();
                    choices[i].Text=names[i]+(selected?"  ✓":"");
                    preset|=selected;
                }
                custom.Text="自定义颜色…"+(preset?"":"  ✓");
            };
            nav.Settings.Click+=delegate {settingsMenu.Show(nav.Settings,new Point(0,0),ToolStripDropDownDirection.AboveRight);};
        }
        private void SetAccent(Color color) {
            Palette.UserAccent=Color.FromArgb(255,color);
            SaveSettings();Invalidate(true);
        }
        private bool laying;
        private void LayoutHud()
        {
            if(laying) return;
            laying=true;
            try {
                Func<float,int> px=Ui.Px;
                int total=px(30+58+16+186+16+70+16+22+36+32)+2*group.RowHeight;
                int available=hudCanvas.Width-(total>hudCanvas.Height?SystemInformation.VerticalScrollBarWidth:0);
                int width=Math.Min(px(860),Math.Max(px(440),available-px(72)));
                int left=Math.Max(px(20),(available-width)/2),y=px(30)+hudCanvas.AutoScrollPosition.Y;
                trayButton.SetBounds(left+width-px(108),y+px(4),px(108),px(32));
                logButton.SetBounds(trayButton.Left-px(80),y+px(4),px(76),px(32));
                header.SetBounds(left,y,Math.Max(0,logButton.Left-px(12)-left),px(58));
                y+=px(58)+px(16);
                hero.PlaceBody(new Rectangle(left,y,width,px(186)));y+=px(186)+px(16);
                int half=(width-px(16))/2;
                simTile.PlaceBody(new Rectangle(left,y,half,px(70)));
                deviceTile.PlaceBody(new Rectangle(left+width-half,y,half,px(70)));y+=px(70)+px(16);
                group.PlaceBody(new Rectangle(left,y,width,2*group.RowHeight));
                Rectangle first=group.Row(0),second=group.Row(1);
                int control=px(32),right=first.Right-px(16);
                group.ControlWidth=px(232);
                modes.SetBounds(right-px(224),first.Y+(first.Height-control)/2,px(224),control);
                rescan.SetBounds(right-px(36),second.Y+(second.Height-control)/2,px(36),control);
                ports.SetBounds(rescan.Left-px(8)-px(180),rescan.Top,px(180),control);
                y+=2*group.RowHeight+px(22);
                start.SetBounds(left+width-px(128),y,px(128),px(36));
                stop.SetBounds(start.Left-px(10)-px(96),y,px(96),px(36));
                y+=px(36)+px(32);
                hudCanvas.AutoScrollMinSize=new Size(0,y-hudCanvas.AutoScrollPosition.Y);
            } finally {laying=false;}
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
                try { Dictionary<string,object> settings=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(path));object port,mode,color;
                    if(settings.TryGetValue("accent",out color)) Palette.UserAccent=Color.FromArgb(Convert.ToInt32(color));
                    if(settings.TryGetValue("mode",out mode) && Convert.ToString(mode)=="ble") selectedMode="ble";
                    if(settings.TryGetValue("port",out port) && ProcessIdentity.ValidPort(Convert.ToString(port))) {selectedPort=Convert.ToString(port);return;} } catch { }
            }
        }
        private void SaveSettings() { try { File.WriteAllText(Path.Combine(root,"logs","app-settings.json"),json.Serialize(new Dictionary<string,object>{{"port",selectedPort},{"mode",selectedMode},{"accent",Palette.UserAccent.ToArgb()}}),Encoding.UTF8); } catch { } }
        private void RefreshPorts()
        {
            string previous=selectedPort;List<string> found=new List<string>();
            try { found.AddRange(SerialPort.GetPortNames()); } catch(Exception e) {Append(e.Message);}
            if(!found.Contains(previous)) found.Add(previous);found.Sort(StringComparer.OrdinalIgnoreCase);
            while(portMenu.Items.Count>0) {
                ToolStripItem old=portMenu.Items[0];portMenu.Items.RemoveAt(0);old.Dispose();
            }
            foreach(string value in found) {
                string choice=value;
                ToolStripMenuItem entry=new ToolStripMenuItem(choice,null,delegate {selectedPort=choice;SaveSettings();RefreshPorts();});
                entry.Checked=String.Equals(choice,previous,StringComparison.OrdinalIgnoreCase);portMenu.Items.Add(entry);
            }
            ports.Text=BluetoothMode?"自动搜索":previous+"  ▾";
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
                string mode=ProcessIdentity.BridgeMode(root,recorded,candidate.StartTime,image.ToString(),command);
                if(mode==null) throw new InvalidOperationException("旧进程身份不符，未接管");
                bridge=candidate;candidate=null;bridgeStart=bridge.StartTime;
                selectedMode=activeMode=mode;
                bridgeSupportsRadar=command.IndexOf("--radar-config",StringComparison.Ordinal)>=0;
                if(state.ContainsKey("port") && ProcessIdentity.ValidPort(Convert.ToString(state["port"]))) {selectedPort=Convert.ToString(state["port"]);RefreshPorts();}
                string logKey=mode=="ble"?"stdout_log":"stderr_log";
                legacyLog=state.ContainsKey(logKey)?Convert.ToString(state[logKey]):Path.Combine(root,"logs","bridge-live.stderr.log");
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
            if(!BluetoothMode && !ProcessIdentity.ValidPort(selectedPort)) {MessageBox.Show(this,"请选择有效的串口。",Title);return;}
            try {
                string python=Path.Combine(root,"tools","python311","tools","python.exe");
                string packages=Path.Combine(root,"tools","espressif","python_env","idf5.3_py3.11_env","Lib","site-packages");
                if(!File.Exists(python) || (!BluetoothMode && !File.Exists(Path.Combine(packages,"serial","__init__.py")))) throw new FileNotFoundException("项目内运行环境不完整，请保留 tools 文件夹。");
                ProcessStartInfo info=new ProcessStartInfo(python);
                string script=Path.Combine(root,"windows-bridge",BluetoothMode?"ble_bridge.py":"bridge.py");
                info.Arguments="-u \""+script+"\" --config \""+Path.Combine(root,"windows-bridge","config.example.json")+"\" "+(BluetoothMode?"--status":"--port "+selectedPort);
                info.Arguments+=" --radar-config \""+RadarWindow.ConfigPath(root)+"\"";
                info.WorkingDirectory=root;info.UseShellExecute=false;info.CreateNoWindow=true;info.WindowStyle=ProcessWindowStyle.Hidden;
                info.RedirectStandardError=true;info.RedirectStandardOutput=true;
                info.StandardOutputEncoding=Encoding.UTF8;info.StandardErrorEncoding=Encoding.UTF8;
                info.EnvironmentVariables.Remove("PYTHONHOME"); info.EnvironmentVariables["PYTHONPATH"]=packages;info.EnvironmentVariables["PYTHONUTF8"]="1";
                info.EnvironmentVariables["PYTHONPYCACHEPREFIX"]=Path.Combine(root,"cache","pycache");
                string temp=Path.Combine(root,"cache","tmp");Directory.CreateDirectory(temp);info.EnvironmentVariables["TEMP"]=temp;info.EnvironmentVariables["TMP"]=temp;
                Process process=new Process();process.StartInfo=info;
                process.ErrorDataReceived+=delegate(object sender,DataReceivedEventArgs e) {Append(e.Data);};
                process.OutputDataReceived+=delegate(object sender,DataReceivedEventArgs e) {if(e.Data!=null) Append(e.Data);};
                lock(logLock) pending.Clear();
                if(!process.Start()) throw new InvalidOperationException("未能启动桥接程序。");
                bridge=process;activeMode=selectedMode;bridgeSupportsRadar=true;bridgeStart=process.StartTime;legacyLog=null;lastTelemetry=DateTime.MinValue;sourceReady=usbReady=fresh=false;
                process.BeginErrorReadLine();process.BeginOutputReadLine();
                File.WriteAllText(statePath,json.Serialize(new Dictionary<string,object>{
                    {"process_id",process.Id},{"start_time_utc",bridgeStart.ToUniversalTime().ToString("o")},{"script_path",script},
                    {"port",selectedPort},{"mode",selectedMode},{"stdout_log",logPath},{"stderr_log",logPath},{"owner","desktop-app"}}),Encoding.UTF8);
                Append("桥接已开启，正在连接模拟器和设备。");
            } catch(Exception e) {Append(e.ToString());MessageBox.Show(this,"无法开启桥接："+e.Message,Title,MessageBoxButtons.OK,MessageBoxIcon.Warning);}
            UpdateState();
        }
        private void StopBridge(bool exitAfter,bool restart=false)
        {
            if(changing) return;
            if(!Running) {ReleaseBridge();if(exitAfter) {quitting=true;Close();}else if(restart) StartBridge();else UpdateState();return;}
            changing=true;UpdateState();Process stopping=bridge;
            ThreadPool.QueueUserWorkItem(delegate {
                Exception error=null;
                try {stopping.Kill();if(!stopping.WaitForExit(2000)) throw new TimeoutException("桥接尚未退出");stopping.WaitForExit();} catch(InvalidOperationException) { } catch(Exception e) {error=e;}
                if(IsDisposed) return;
                try {BeginInvoke((Action)delegate {
                    changing=false;
                    if(error!=null) {
                        if(restart && activeMode!=null) {selectedMode=activeMode;modes.SelectedIndex=BluetoothMode?1:0;SaveSettings();}
                        Append(error.ToString());quitting=false;ShowWindow();MessageBox.Show(this,"未能停止桥接，请打开日志查看详情。",Title);UpdateState();return;
                    }
                    ReleaseBridge();Append("桥接已停止。");UpdateState();
                    if(exitAfter) {quitting=true;Close();}else if(restart) StartBridge();
                });} catch(InvalidOperationException) { }
            });
        }
        private void ReleaseBridge()
        {
            if(bridge!=null) {
                try {Dictionary<string,object> state=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(statePath));if(Convert.ToInt32(state["process_id"])==bridge.Id) File.Delete(statePath);} catch { }
                bridge.Dispose();bridge=null;
            }
            sourceReady=usbReady=fresh=bridgeSupportsRadar=false;activeMode=null;legacyLog=null;lastTelemetry=DateTime.MinValue;
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
                if(line=="BLE link: connected") usbReady=true;
                if(line=="BLE link: disconnected") {usbReady=false;fresh=false;}
                Match status=Regex.Match(line,@"telemetry: connected=([01])\s+seq=\d+");
                if(status.Success) {
                    lastTelemetry=DateTime.UtcNow;fresh=status.Groups[1].Value=="1";if(fresh) sourceReady=usbReady=true;
                    Match sample=Regex.Match(line,@"IAS=([\d.]+) ALT=(-?[\d.]+) HDG=([\d.]+)");
                    double ias,alt,hdg;
                    if(fresh && sample.Success && Double.TryParse(sample.Groups[1].Value,NumberStyles.Float,CultureInfo.InvariantCulture,out ias) &&
                       Double.TryParse(sample.Groups[2].Value,NumberStyles.Float,CultureInfo.InvariantCulture,out alt) &&
                       Double.TryParse(sample.Groups[3].Value,NumberStyles.Float,CultureInfo.InvariantCulture,out hdg)) hero.SetMetrics(ias,alt,hdg);
                }
            }
            if((DateTime.UtcNow-lastTelemetry).TotalSeconds>8) fresh=false;
            UpdateState();
        }
        private bool previewing;
        private void UpdateState()
        {
            if(previewing) return;
            bool running=Running;
            start.Enabled=trayStart.Enabled=!running&&!changing;stop.Enabled=trayStop.Enabled=running&&!changing;
            modes.Enabled=!changing;ports.Enabled=rescan.Enabled=!BluetoothMode&&!running&&!changing;
            Present(running,changing,fresh,sourceReady,usbReady);
            tray.Text=Title+" · "+(!running?"已停止":fresh?"实时传输中":"等待连接");
        }
        private void Present(bool running,bool busy,bool live,bool simulatorReady,bool deviceReady)
        {
            live=live&&running;
            ports.Text=BluetoothMode?"自动搜索":selectedPort;ports.Chevron=!BluetoothMode;
            hero.Headline=busy?"正在更新连接":!running?"桥接未开启":live?"实时数据传输中":"正在等待连接";
            hero.Detail=busy?"正在切换连接，请稍候。":!running?"选择连接方式后点击“开启桥接”。":
                live?(BluetoothMode?"通过蓝牙发送到 MyHud 屏幕，约 20 Hz":"通过 USB "+selectedPort+" 发送到 MyHud 屏幕，约 20 Hz"):"连接恢复后会自动继续传输。";
            hero.StatusColor=live?Palette.Live:running||busy?Palette.Warn:Palette.Idle;
            hero.Pulse=running||busy;hero.Stale=!live;
            hero.Badge=BluetoothMode?"蓝牙 BLE":"USB · "+selectedPort;
            simTile.Value=!running?"未连接":simulatorReady?"已连接":"等待进入航班";
            simTile.Dot=!running?Palette.Idle:simulatorReady?Palette.Live:Palette.Warn;
            deviceTile.Glyph=BluetoothMode?"":"";
            deviceTile.Title="设备 · "+(BluetoothMode?"MyHud BLE":selectedPort);
            deviceTile.Value=!running?"未连接":deviceReady?"已连接":BluetoothMode?"正在搜索蓝牙设备":"等待 USB 设备";
            deviceTile.Dot=!running?Palette.Idle:deviceReady?Palette.Live:Palette.Warn;
            group.SetSubtitle(0,"运行中切换会自动重新连接");
            group.SetSubtitle(1,BluetoothMode?"蓝牙模式会自动搜索 MyHud BLE":"选择屏幕所在的 COM 串口");
        }
        // 仅供离屏预览使用：固定显示一种状态，不启动桥接。
        internal void PreviewScenario(bool live,bool bluetooth)
        {
            timer.Stop();previewing=true;
            selectedMode=bluetooth?"ble":"usb";modes.SelectedIndex=bluetooth?1:0;
            if(live) hero.SetMetrics(272,36000,128);
            start.Enabled=!live;stop.Enabled=live;ports.Enabled=rescan.Enabled=!live&&!bluetooth;
            Present(live,false,live,live,live);
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
        private void OpenRadar()
        {
            if(changing) return;
            if(radarWindow==null) {
                radarWindow=new RadarWindow(root,Icon,true);
                radarWindow.TopLevel=false;radarWindow.FormBorderStyle=FormBorderStyle.None;radarWindow.Dock=DockStyle.Fill;
                workspace.Controls.Add(radarWindow);
            }
            if(radarWindow.Visible) return;
            hudCanvas.Hide();radarWindow.Show();radarWindow.BringToFront();radarWindow.ActivatePage();
            nav.SelectedIndex=1;
            if(Running && !bridgeSupportsRadar) StopBridge(false,true);
            else if(!Running) StartBridge();
        }
        private void ShowHud() {
            if(radarWindow!=null && radarWindow.Visible) {radarWindow.DeactivatePage();radarWindow.Hide();}
            hudCanvas.Show();nav.SelectedIndex=0;
        }
        private void ShowWindow() {
            ShowInTaskbar=true;Show();WindowState=FormWindowState.Normal;Activate();
        }
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
            if(disposing) {
                timer.Stop();timer.Dispose();portMenu.Dispose();
                foreach(ToolStripItem item in settingsMenu.Items) if(item.Image!=null) item.Image.Dispose();
                settingsMenu.Dispose();tray.Visible=false;tray.Dispose();
                if(radarWindow!=null) {radarWindow.Dispose();try {RadarWindow.Disable(root);}catch(IOException) {}}
                lock(logLock){if(log!=null){log.Dispose();log=null;}}
            }
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
            System.Net.ServicePointManager.SecurityProtocol|=System.Net.SecurityProtocolType.Tls12;
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
