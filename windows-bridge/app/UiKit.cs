using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

// 桌面助手的界面组件：统一配色、字体、动画时钟和自绘控件。
namespace A350Isis
{
    internal static class Dwm
    {
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
        private static void Set(IntPtr window,int attribute,int value)
        {
            try {DwmSetWindowAttribute(window,attribute,ref value,4);}
            catch(DllNotFoundException) { } catch(EntryPointNotFoundException) { }
        }
        // 标题栏与内容区同色；旧版 Windows 不支持的属性会被系统忽略。
        internal static void Frame(IntPtr window)
        {
            Set(window,20,Palette.Dark?1:0);
            Set(window,35,ColorTranslator.ToWin32(Palette.Paper));
            Set(window,36,ColorTranslator.ToWin32(Palette.Muted));
            Set(window,34,ColorTranslator.ToWin32(Palette.Border));
        }
        internal static void RoundPopup(IntPtr window) {Set(window,33,3);}
    }

    internal static class Palette
    {
        internal static bool Dark;
        internal static Color Paper,Sidebar,Card,Raised,Ink,Muted,Faint,Border,Soft,Hover,Field,Live,Warn,Danger,Idle;
        internal static readonly Color MapInk=Color.FromArgb(29,29,31);
        internal static readonly Color[] Accents={
            Color.FromArgb(0,122,255),Color.FromArgb(175,82,222),Color.FromArgb(255,45,85),Color.FromArgb(255,59,48),
            Color.FromArgb(255,149,0),Color.FromArgb(40,180,99),Color.FromArgb(142,142,147)};
        internal static Color UserAccent=Accents[0];
        internal static Color Accent {get {return UserAccent;}}

        static Palette() {Apply(SystemPrefersDark());}
        internal static bool SystemPrefersDark()
        {
            try {
                using(RegistryKey key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")) {
                    object value=key==null?null:key.GetValue("AppsUseLightTheme");
                    return value is int && (int)value==0;
                }
            } catch { return false; }
        }
        private static Color C(int r,int g,int b) {return Color.FromArgb(r,g,b);}
        internal static void Apply(bool dark)
        {
            Dark=dark;
            if(dark) {
                Paper=C(29,29,31);Sidebar=C(39,39,42);Card=C(44,44,47);Raised=C(66,66,70);
                Ink=C(242,242,247);Muted=C(160,160,166);Faint=C(106,106,112);Border=C(60,60,64);
                Soft=C(56,56,60);Hover=C(62,62,66);Field=C(34,34,37);
                Live=C(48,209,88);Warn=C(255,176,32);Danger=C(255,69,58);Idle=C(108,108,113);
            } else {
                Paper=C(245,245,247);Sidebar=C(234,234,239);Card=Color.White;Raised=Color.White;
                Ink=C(29,29,31);Muted=C(108,108,114);Faint=C(164,164,170);Border=C(223,223,228);
                Soft=C(228,228,233);Hover=C(240,240,244);Field=Color.White;
                Live=C(46,190,86);Warn=C(240,146,0);Danger=C(232,58,48);Idle=C(176,176,181);
            }
        }
        internal static Color Mix(Color a,Color b,float t)
        {
            t=Math.Max(0,Math.Min(1,t));
            return Color.FromArgb((int)(a.R+(b.R-a.R)*t),(int)(a.G+(b.G-a.G)*t),(int)(a.B+(b.B-a.B)*t));
        }
        internal static Color Alpha(Color c,int alpha) {return Color.FromArgb(Math.Max(0,Math.Min(255,alpha)),c.R,c.G,c.B);}
        internal static Color OnAccent {get {return UserAccent.GetBrightness()>.62f && UserAccent.R+UserAccent.G>420?MapInk:Color.White;}}
        internal static Color Tint(float light,float dark) {return Mix(Card,UserAccent,Dark?dark:light);}

        internal static GraphicsPath Rounded(Rectangle bounds,int radius)
        {
            return Rounded(new RectangleF(bounds.X,bounds.Y,bounds.Width,bounds.Height),radius);
        }
        internal static GraphicsPath Rounded(RectangleF bounds,float radius)
        {
            float d=Math.Min(radius*2,Math.Min(bounds.Width,bounds.Height));
            GraphicsPath path=new GraphicsPath();
            if(d<2) {
                if(bounds.Width>0 && bounds.Height>0) path.AddRectangle(bounds);
                return path;
            }
            path.AddArc(bounds.Left,bounds.Top,d,d,180,90);
            path.AddArc(bounds.Right-d,bounds.Top,d,d,270,90);
            path.AddArc(bounds.Right-d,bounds.Bottom-d,d,d,0,90);
            path.AddArc(bounds.Left,bounds.Bottom-d,d,d,90,90);
            path.CloseFigure();
            return path;
        }
    }

    internal static class Ui
    {
        internal static readonly float S=1;
        private static readonly Dictionary<string,Font> fonts=new Dictionary<string,Font>();
        private static string numberFamily,iconFamily;
        internal const TextFormatFlags Left=TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|
            TextFormatFlags.NoPrefix|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPadding;
        internal const TextFormatFlags Center=TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|
            TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix|TextFormatFlags.NoPadding;

        static Ui()
        {
            try {using(Graphics g=Graphics.FromHwnd(IntPtr.Zero)) S=Math.Max(1f,g.DpiX/96f);} catch { }
        }
        internal static int Px(float value) {return (int)Math.Round(value*S);}
        private static string Family(params string[] names)
        {
            foreach(string name in names) {
                try {using(FontFamily family=new FontFamily(name)) return family.Name;} catch(ArgumentException) { }
            }
            return "Segoe UI";
        }
        private static Font Get(string family,float size,FontStyle style)
        {
            string key=family+"|"+size+"|"+(int)style;
            Font font;
            if(!fonts.TryGetValue(key,out font)) {font=new Font(family,size,style);fonts.Add(key,font);}
            return font;
        }
        internal static Font Font(float size,bool bold=false) {return Get("Microsoft YaHei UI",size,bold?FontStyle.Bold:FontStyle.Regular);}
        internal static Font Number(float size)
        {
            if(numberFamily==null) numberFamily=Family("Segoe UI Variable Display Semib","Segoe UI Semibold","Segoe UI");
            return Get(numberFamily,size,FontStyle.Regular);
        }
        internal static Font Icons(float size)
        {
            if(iconFamily==null) iconFamily=Family("Segoe Fluent Icons","Segoe MDL2 Assets");
            return Get(iconFamily,size,FontStyle.Regular);
        }
        internal static void Text(Graphics g,string text,Font font,Rectangle bounds,Color color,TextFormatFlags flags)
        {
            if(bounds.Width>0 && bounds.Height>0 && !String.IsNullOrEmpty(text)) TextRenderer.DrawText(g,text,font,bounds,color,flags);
        }
        private static Bitmap probeImage;
        private static Graphics probe;
        // 需要设备上下文，否则高 DPI 下 NoPadding 不生效。
        internal static Size Measure(string text,Font font)
        {
            if(String.IsNullOrEmpty(text)) return Size.Empty;
            if(probe==null) {probeImage=new Bitmap(1,1);probe=Graphics.FromImage(probeImage);}
            return TextRenderer.MeasureText(probe,text,font,Size.Empty,TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine);
        }
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr window,int message,IntPtr w,string l);
        internal static void Placeholder(TextBox box,string text)
        {
            EventHandler apply=delegate {SendMessage(box.Handle,0x1501,(IntPtr)1,text);};
            if(box.IsHandleCreated) apply(box,EventArgs.Empty);else box.HandleCreated+=apply;
        }
        [DllImport("uxtheme.dll",CharSet=CharSet.Unicode)] private static extern int SetWindowTheme(IntPtr window,string app,string list);
        // 深色模式下让原生滚动条也变暗。
        internal static void DarkScrollbars(Control control)
        {
            if(!Palette.Dark) return;
            EventHandler apply=delegate {try {SetWindowTheme(control.Handle,"DarkMode_Explorer",null);} catch(DllNotFoundException) { } catch(EntryPointNotFoundException) { }};
            if(control.IsHandleCreated) apply(control,EventArgs.Empty);else control.HandleCreated+=apply;
        }
        internal static void StyleMenu(ToolStripDropDown menu)
        {
            menu.Renderer=new SoftMenuRenderer();menu.Font=Font(10.5f,true);menu.ForeColor=Palette.Ink;menu.BackColor=Palette.Card;
            menu.Padding=new Padding(0,Px(5),0,Px(5));
            var context=menu as ContextMenuStrip;
            if(context!=null) {context.ShowImageMargin=true;context.ShowCheckMargin=false;}
            menu.ItemAdded+=delegate(object sender,ToolStripItemEventArgs e) {
                if(!(e.Item is ToolStripSeparator)) e.Item.Padding=new Padding(Px(2),Px(5),Px(10),Px(5));
            };
            menu.HandleCreated+=delegate {Dwm.RoundPopup(menu.Handle);};
        }
        internal static void Lucide(Graphics g,string glyph,Color color,float x,float y,float size)
        {
            using(var pen=new Pen(color,1.8f)) {
                pen.StartCap=pen.EndCap=LineCap.Round;pen.LineJoin=LineJoin.Round;
                GraphicsState state=g.Save();
                g.TranslateTransform(x,y);g.ScaleTransform(size/24f,size/24f);
                if(glyph=="radar") { // Lucide Radar, ISC
                    g.DrawEllipse(pen,2,2,20,20);g.DrawEllipse(pen,7,7,10,10);g.DrawLine(pen,12,12,20,4);
                } else { // Lucide Gauge, ISC
                    g.DrawArc(pen,3,4,18,18,145,250);g.DrawLine(pen,12,13,16,8.5f);
                    using(var dot=new SolidBrush(color)) g.FillEllipse(dot,10.5f,11.5f,3,3);
                }
                g.Restore(state);
            }
        }
    }

    // 单一界面时钟：所有动画共用一个 60 Hz 计时器，空闲时自动停止。
    internal static class Clock
    {
        private static readonly Timer timer=new Timer();
        private static readonly List<Func<bool>> jobs=new List<Func<bool>>();
        internal static bool Animate=SystemInformation.UIEffectsEnabled;
        static Clock()
        {
            timer.Interval=15;
            timer.Tick+=delegate {
                foreach(Func<bool> job in jobs.ToArray()) {
                    bool keep;
                    try {keep=job();} catch(ObjectDisposedException) {keep=false;}
                    if(!keep) jobs.Remove(job);
                }
                if(jobs.Count==0) timer.Stop();
            };
        }
        internal static void Run(Func<bool> job)
        {
            if(!jobs.Contains(job)) jobs.Add(job);
            if(!timer.Enabled) timer.Start();
        }
    }

    internal sealed class Tween
    {
        private readonly Stopwatch watch=new Stopwatch();
        private float from,to,duration=1;
        internal float Value;
        internal float Target {get {return to;}}
        internal bool Running {get {return watch.IsRunning;}}
        internal void Jump(float value) {from=to=Value=value;watch.Reset();}
        internal void To(float target,float milliseconds)
        {
            if(!Clock.Animate || milliseconds<=0) {Jump(target);return;}
            if(target==to && watch.IsRunning) return;
            from=Value;to=target;duration=milliseconds;watch.Restart();
        }
        internal bool Step()
        {
            if(!watch.IsRunning) return false;
            float t=Math.Min(1f,watch.ElapsedMilliseconds/duration);
            float eased=1-(1-t)*(1-t)*(1-t);
            Value=from+(to-from)*eased;
            if(t>=1) {watch.Stop();Value=to;return false;}
            return true;
        }
    }

    internal static class Surfaces
    {
        internal static Bitmap Card(Size size,Rectangle card,int radius,Color back)
        {
            var image=new Bitmap(Math.Max(1,size.Width),Math.Max(1,size.Height),PixelFormat.Format32bppPArgb);
            using(Graphics g=Graphics.FromImage(image)) {
                g.Clear(back);g.SmoothingMode=SmoothingMode.AntiAlias;
                int depth=Ui.Px(7),alpha=Palette.Dark?9:3;
                using(var shade=new SolidBrush(Color.FromArgb(alpha,0,0,0)))
                    for(int i=depth;i>=1;i--) {
                        RectangleF layer=new RectangleF(card.X-i+1,card.Y-i+1+Ui.S*2,card.Width+2*i-2,card.Height+2*i-2);
                        using(GraphicsPath path=Palette.Rounded(layer,radius+i)) g.FillPath(shade,path);
                    }
                using(GraphicsPath body=Palette.Rounded(new RectangleF(card.X,card.Y,card.Width,card.Height),radius))
                using(var fill=new SolidBrush(Palette.Card)) g.FillPath(fill,body);
                using(GraphicsPath edge=Palette.Rounded(new RectangleF(card.X+.5f,card.Y+.5f,card.Width-1,card.Height-1),radius))
                using(var pen=new Pen(Palette.Dark?Palette.Mix(Palette.Card,Color.White,.07f):Palette.Mix(Palette.Border,Palette.Card,.25f))) g.DrawPath(pen,edge);
            }
            return image;
        }
    }

    // 带柔和阴影的卡片底板。控件边界比卡片本体四周多出 Gutter 留给阴影。
    internal class CardControl : Control
    {
        private Bitmap surface;
        internal CardControl()
        {
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|
                     ControlStyles.ResizeRedraw,true);
            BackColor=Palette.Paper;ForeColor=Palette.Ink;
        }
        internal static int Gutter {get {return Ui.Px(8);}}
        protected int Radius {get {return Ui.Px(12);}}
        protected Rectangle Body {get {int m=Gutter;return new Rectangle(m,m,Math.Max(0,Width-2*m),Math.Max(0,Height-2*m));}}
        internal void PlaceBody(Rectangle body) {Bounds=Rectangle.Inflate(body,Gutter,Gutter);}
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if(surface==null || surface.Size!=Size) {Drop();surface=Surfaces.Card(Size,Body,Radius,BackColor);}
            e.Graphics.DrawImageUnscaled(surface,0,0);
        }
        protected override void OnBackColorChanged(EventArgs e) {Drop();base.OnBackColorChanged(e);}
        private void Drop() {if(surface!=null) {surface.Dispose();surface=null;}}
        protected override void Dispose(bool disposing) {if(disposing) Drop();base.Dispose(disposing);}
    }

    internal sealed class DeckButton : Button
    {
        internal enum Look {Secondary,Primary,Ghost}
        private Look look;
        private readonly Tween hover=new Tween(),press=new Tween();
        private readonly Func<bool> animate;
        private string glyph;
        private bool chevron;
        internal DeckButton()
        {
            SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|
                     ControlStyles.SupportsTransparentBackColor|ControlStyles.ResizeRedraw,true);
            FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;UseVisualStyleBackColor=false;Cursor=Cursors.Hand;
            Font=Ui.Font(10.5f,true);ForeColor=Palette.Ink;BackColor=Palette.Paper;
            animate=delegate {
                bool busy=hover.Step()|press.Step();
                if(IsDisposed) return false;
                Invalidate();return busy;
            };
        }
        internal Look Style {get {return look;} set {look=value;Invalidate();}}
        internal bool AccentButton {get {return look==Look.Primary;} set {Style=value?Look.Primary:Look.Secondary;}}
        internal string Glyph {get {return glyph;} set {if(glyph!=value) {glyph=value;Invalidate();}}}
        internal bool Chevron {get {return chevron;} set {if(chevron!=value) {chevron=value;Invalidate();}}}
        private void Animate(Tween tween,float target,float milliseconds)
        {
            tween.To(target,milliseconds);
            if(tween.Running) Clock.Run(animate);else Invalidate();
        }
        protected override void OnMouseEnter(EventArgs e) {if(Enabled) Animate(hover,1,120);base.OnMouseEnter(e);}
        protected override void OnMouseLeave(EventArgs e) {Animate(hover,0,240);Animate(press,0,180);base.OnMouseLeave(e);}
        protected override void OnMouseDown(MouseEventArgs e) {if(e.Button==MouseButtons.Left && Enabled) Animate(press,1,70);base.OnMouseDown(e);}
        protected override void OnMouseUp(MouseEventArgs e) {Animate(press,0,220);base.OnMouseUp(e);}
        protected override void OnEnabledChanged(EventArgs e) {if(!Enabled) {hover.Jump(0);press.Jump(0);}Invalidate();base.OnEnabledChanged(e);}
        protected override void OnGotFocus(EventArgs e) {Invalidate();base.OnGotFocus(e);}
        protected override void OnLostFocus(EventArgs e) {Invalidate();base.OnLostFocus(e);}
        protected override void OnTextChanged(EventArgs e) {Invalidate();base.OnTextChanged(e);}
        protected override void OnPaint(PaintEventArgs e)
        {
            if(Width<4 || Height<4) return;
            Graphics g=e.Graphics;
            if(BackColor.A<255) base.OnPaintBackground(e);else g.Clear(BackColor);
            Color surface=BackColor.A<255?Palette.Paper:BackColor;
            g.SmoothingMode=SmoothingMode.AntiAlias;
            float s=Ui.S,h=hover.Value,p=press.Value;
            RectangleF body=new RectangleF(1.5f*s,1.5f*s,Width-3f*s,Height-3f*s);
            float radius=Math.Min(7*s,body.Height/2);
            Color fill,ink=Enabled?Palette.Ink:Palette.Faint,rim=Color.Empty;
            if(look==Look.Primary) {
                fill=Palette.Mix(Palette.Mix(Palette.Accent,Color.White,.10f*h),Color.Black,.16f*p);
                if(!Enabled) fill=Palette.Mix(Palette.Accent,surface,.6f);
                ink=Enabled?Palette.OnAccent:Palette.Mix(Palette.OnAccent,fill,.35f);
            } else if(look==Look.Secondary) {
                Color baseFill=Palette.Dark?Palette.Raised:Color.White;
                fill=Palette.Mix(Palette.Mix(baseFill,Palette.Ink,.035f*h),Palette.Ink,.08f*p);
                rim=Palette.Dark?Palette.Mix(baseFill,Color.White,.08f):Color.FromArgb(206,206,212);
                if(!Enabled) {fill=Palette.Mix(baseFill,surface,.55f);rim=Palette.Mix(rim,surface,.55f);}
            } else fill=Palette.Mix(surface,Palette.Ink,.06f*h+.05f*p);
            if(look!=Look.Ghost && Enabled && !Palette.Dark)
                using(GraphicsPath drop=Palette.Rounded(new RectangleF(body.X,body.Y+s,body.Width,body.Height),radius))
                using(var shade=new SolidBrush(Color.FromArgb(look==Look.Primary?34:16,0,0,0))) g.FillPath(shade,drop);
            using(GraphicsPath shape=Palette.Rounded(body,radius)) {
                if(look!=Look.Ghost || h+p>0) using(var brush=new SolidBrush(fill)) g.FillPath(brush,shape);
                if(look==Look.Primary && Enabled)
                    using(var gloss=new LinearGradientBrush(new RectangleF(body.X,body.Y-1,body.Width,body.Height/2+2),
                        Color.FromArgb(34,255,255,255),Color.FromArgb(0,255,255,255),90f)) g.FillPath(gloss,shape);
                if(rim!=Color.Empty) using(var pen=new Pen(rim)) g.DrawPath(pen,shape);
            }
            if(Focused && ShowFocusCues)
                using(GraphicsPath ring=Palette.Rounded(RectangleF.Inflate(body,.5f*s,.5f*s),radius+s))
                using(var pen=new Pen(Palette.Alpha(Palette.Accent,150),2*s)) g.DrawPath(pen,ring);
            Font iconFont=Ui.Icons(10f);
            Size textSize=Ui.Measure(Text,Font);
            int iconWidth=String.IsNullOrEmpty(glyph)?0:Ui.Measure(glyph,iconFont).Width;
            int gap=iconWidth>0 && textSize.Width>0?Ui.Px(7):0;
            if(chevron) {
                int pad=Ui.Px(12);
                Ui.Text(g,Text,Font,new Rectangle(pad,0,Width-pad*2-Ui.Px(14),Height),ink,Ui.Left);
                Ui.Text(g,"",Ui.Icons(7.5f),new Rectangle(Width-pad-Ui.Px(12),0,Ui.Px(12),Height),
                        Enabled?Palette.Muted:Palette.Faint,Ui.Center);
            } else {
                int total=iconWidth+gap+textSize.Width,x=(Width-total)/2;
                if(iconWidth>0) Ui.Text(g,glyph,iconFont,new Rectangle(x,0,iconWidth,Height+1),ink,Ui.Center);
                Ui.Text(g,Text,Font,new Rectangle(x+iconWidth+gap,0,Math.Min(textSize.Width+2,Width-x-iconWidth-gap),Height),ink,Ui.Left);
            }
        }
    }

    internal sealed class SoftMenuRenderer : ToolStripProfessionalRenderer
    {
        private sealed class Colors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground {get {return Palette.Card;}}
            public override Color ImageMarginGradientBegin {get {return Palette.Card;}}
            public override Color ImageMarginGradientMiddle {get {return Palette.Card;}}
            public override Color ImageMarginGradientEnd {get {return Palette.Card;}}
            public override Color MenuBorder {get {return Palette.Border;}}
            public override Color MenuItemBorder {get {return Color.Transparent;}}
            public override Color MenuItemSelected {get {return Palette.Accent;}}
            public override Color SeparatorDark {get {return Palette.Border;}}
            public override Color SeparatorLight {get {return Palette.Card;}}
        }
        internal SoftMenuRenderer() : base(new Colors()) {RoundedEdges=false;}
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using(var fill=new SolidBrush(Palette.Card)) e.Graphics.FillRectangle(fill,e.AffectedBounds);
        }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using(var pen=new Pen(Palette.Border)) e.Graphics.DrawRectangle(pen,0,0,e.ToolStrip.Width-1,e.ToolStrip.Height-1);
        }
        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if(!e.Item.Selected || !e.Item.Enabled) return;
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            var row=new RectangleF(Ui.Px(5),.5f,e.Item.Width-Ui.Px(10),e.Item.Height-1);
            using(GraphicsPath path=Palette.Rounded(row,Ui.Px(5)))
            using(var fill=new SolidBrush(Palette.Accent)) e.Graphics.FillPath(fill,path);
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor=!e.Item.Enabled?Palette.Faint:e.Item.Selected?Palette.OnAccent:Palette.Ink;
            base.OnRenderItemText(e);
        }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Color ink=e.Item.Selected?Palette.OnAccent:Palette.Accent;
            Ui.Text(e.Graphics,"",Ui.Icons(9f),e.ImageRectangle,ink,Ui.Center);
        }
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y=e.Item.Height/2;
            using(var pen=new Pen(Palette.Border)) e.Graphics.DrawLine(pen,Ui.Px(12),y,e.Item.Width-Ui.Px(12),y);
        }
    }

    internal sealed class ModePicker : Control
    {
        private readonly Tween slide=new Tween();
        private readonly Func<bool> animate;
        private string[] labels={"USB 有线","蓝牙 BLE"};
        private int selected;
        internal event EventHandler SelectedIndexChanged;

        internal ModePicker()
        {
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|
                     ControlStyles.ResizeRedraw|ControlStyles.Selectable,true);
            BackColor=Palette.Card;Font=Ui.Font(10.5f,true);
            TabStop=true;Cursor=Cursors.Hand;AccessibleName="连接方式";AccessibleRole=AccessibleRole.PageTabList;
            AccessibleDescription="已选择 "+labels[0]+"，使用左右方向键切换";
            animate=delegate {bool busy=slide.Step();if(IsDisposed) return false;Invalidate();return busy;};
        }
        internal string[] Labels {get {return labels;} set {labels=value;selected=Math.Min(selected,labels.Length-1);slide.Jump(selected);Invalidate();}}
        internal int SelectedIndex {
            get {return selected;}
            set {
                if(value<0 || value>=labels.Length || value==selected) return;
                selected=value;
                if(Visible) {slide.To(value,260);Clock.Run(animate);} else slide.Jump(value);
                AccessibleDescription="已选择 "+labels[selected];
                Invalidate();
                if(SelectedIndexChanged!=null) SelectedIndexChanged(this,EventArgs.Empty);
            }
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if(e.Button==MouseButtons.Left) {Focus();SelectedIndex=Math.Max(0,Math.Min(labels.Length-1,e.X*labels.Length/Math.Max(1,Width)));}
        }
        protected override bool IsInputKey(Keys key) {return key==Keys.Left || key==Keys.Right || base.IsInputKey(key);}
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if(e.KeyCode==Keys.Left) {SelectedIndex=selected-1;e.Handled=true;}
            else if(e.KeyCode==Keys.Right) {SelectedIndex=selected+1;e.Handled=true;}
            else if(e.KeyCode==Keys.Space) {SelectedIndex=(selected+1)%labels.Length;e.Handled=true;}
            base.OnKeyDown(e);
        }
        protected override void OnGotFocus(EventArgs e) {base.OnGotFocus(e);Invalidate();}
        protected override void OnLostFocus(EventArgs e) {base.OnLostFocus(e);Invalidate();}
        protected override void OnEnabledChanged(EventArgs e) {base.OnEnabledChanged(e);Invalidate();}
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g=e.Graphics;g.Clear(BackColor);
            if(Width<18 || Height<8) return;
            g.SmoothingMode=SmoothingMode.AntiAlias;
            float s=Ui.S,pad=2*s;
            int count=labels.Length;
            RectangleF track=new RectangleF(.5f,.5f,Width-1,Height-1);
            float segment=(track.Width-2*pad)/count;
            using(GraphicsPath path=Palette.Rounded(track,Math.Min(8*s,track.Height/2)))
            using(var fill=new SolidBrush(Palette.Soft)) g.FillPath(fill,path);
            RectangleF thumb=new RectangleF(track.X+pad+slide.Value*segment,track.Y+pad,segment,track.Height-2*pad);
            float radius=Math.Min(6.5f*s,thumb.Height/2);
            if(!Palette.Dark)
                using(GraphicsPath drop=Palette.Rounded(new RectangleF(thumb.X,thumb.Y+s,thumb.Width,thumb.Height),radius))
                using(var shade=new SolidBrush(Color.FromArgb(22,0,0,0))) g.FillPath(shade,drop);
            using(GraphicsPath path=Palette.Rounded(thumb,radius))
            using(var fill=new SolidBrush(Enabled?(Palette.Dark?Palette.Raised:Color.White):Palette.Hover)) g.FillPath(fill,path);
            if(Focused && ShowFocusCues)
                using(GraphicsPath ring=Palette.Rounded(thumb,radius))
                using(var pen=new Pen(Palette.Alpha(Palette.Accent,160),1.6f*s)) g.DrawPath(pen,ring);
            for(int i=0;i<count;i++) {
                var cell=new Rectangle((int)(track.X+pad+i*segment),0,(int)segment,Height);
                float near=1-Math.Min(1,Math.Abs(slide.Value-i));
                Color ink=!Enabled?Palette.Faint:Palette.Mix(Palette.Muted,Palette.Ink,near);
                Ui.Text(g,labels[i],Font,cell,ink,Ui.Center);
            }
        }
    }

    internal sealed class SideNav : Control
    {
        private readonly string[] items={"HUD 仪表","ADS-B 雷达"},glyphs={"gauge","radar"};
        private readonly Tween slide=new Tween();
        private readonly Func<bool> animate;
        private int selected,hoverItem=-1;
        internal Image Logo;
        internal Action<int> Navigate;
        internal readonly DeckButton Settings=new DeckButton();

        internal SideNav()
        {
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|
                     ControlStyles.ResizeRedraw|ControlStyles.Selectable,true);
            TabStop=true;AccessibleName="工作区";AccessibleRole=AccessibleRole.PageTabList;
            Settings.Glyph="\uE713";Settings.AccessibleName="设置";Settings.Style=DeckButton.Look.Ghost;
            Settings.BackColor=Palette.Sidebar;Controls.Add(Settings);
            animate=delegate {bool busy=slide.Step();if(IsDisposed) return false;Invalidate();return busy;};
        }
        internal int SelectedIndex {
            get {return selected;}
            set {
                if(value==selected || value<0 || value>=items.Length) return;
                selected=value;slide.To(value,280);Clock.Run(animate);
                AccessibleDescription="当前："+items[value];Invalidate();
            }
        }
        private int Step {get {return Ui.Px(40);}}
        private Rectangle Item(int i) {return new Rectangle(Ui.Px(10),Ui.Px(112)+i*Step,Width-Ui.Px(20),Ui.Px(36));}
        protected override void OnResize(EventArgs e) {
            base.OnResize(e);
            if(Settings!=null) Settings.SetBounds(Ui.Px(12),Height-Ui.Px(78),Ui.Px(40),Ui.Px(36));
        }
        private void Track(Point point)
        {
            int item=-1;
            for(int i=0;i<items.Length;i++) if(Item(i).Contains(point)) item=i;
            if(item!=hoverItem) {hoverItem=item;Invalidate();}
            Cursor=item>=0?Cursors.Hand:Cursors.Default;
        }
        protected override void OnMouseMove(MouseEventArgs e) {base.OnMouseMove(e);Track(e.Location);}
        protected override void OnMouseLeave(EventArgs e) {base.OnMouseLeave(e);hoverItem=-1;Invalidate();}
        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if(e.Button!=MouseButtons.Left) return;
            Track(e.Location);
            if(hoverItem>=0 && Navigate!=null) Navigate(hoverItem);
        }
        protected override bool IsInputKey(Keys key) {return key==Keys.Up || key==Keys.Down || base.IsInputKey(key);}
        protected override void OnKeyDown(KeyEventArgs e)
        {
            int next=e.KeyCode==Keys.Up?selected-1:e.KeyCode==Keys.Down?selected+1:-1;
            if(next>=0 && next<items.Length && Navigate!=null) {Navigate(next);e.Handled=true;}
            base.OnKeyDown(e);
        }
        protected override void OnGotFocus(EventArgs e) {base.OnGotFocus(e);Invalidate();}
        protected override void OnLostFocus(EventArgs e) {base.OnLostFocus(e);Invalidate();}
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g=e.Graphics;g.Clear(Palette.Sidebar);
            g.SmoothingMode=SmoothingMode.AntiAlias;g.InterpolationMode=InterpolationMode.HighQualityBicubic;
            using(var line=new Pen(Palette.Dark?Color.FromArgb(22,22,24):Palette.Mix(Palette.Border,Palette.Sidebar,.2f)))
                g.DrawLine(line,Width-1,0,Width-1,Height);
            if(Logo!=null) g.DrawImage(Logo,new Rectangle(Ui.Px(18),Ui.Px(24),Ui.Px(38),Ui.Px(38)));
            Ui.Text(g,"MyHud",Ui.Number(15f),new Rectangle(Ui.Px(66),Ui.Px(22),Width-Ui.Px(76),Ui.Px(24)),Palette.Ink,Ui.Left);
            Ui.Text(g,"A350 ISIS 助手",Ui.Font(10f,true),new Rectangle(Ui.Px(66),Ui.Px(46),Width-Ui.Px(76),Ui.Px(18)),Palette.Muted,Ui.Left);
            Ui.Text(g,"工作区",Ui.Font(10f,true),new Rectangle(Ui.Px(20),Ui.Px(86),Width-Ui.Px(40),Ui.Px(18)),Palette.Muted,Ui.Left);
            for(int i=0;i<items.Length;i++)
                if(i==hoverItem && i!=selected) {
                    using(GraphicsPath path=Palette.Rounded(Item(i),Ui.Px(8)))
                    using(var fill=new SolidBrush(Palette.Mix(Palette.Sidebar,Palette.Ink,.055f))) g.FillPath(fill,path);
                }
            Rectangle first=Item(0);
            var highlight=new RectangleF(first.X,first.Y+slide.Value*Step,first.Width,first.Height);
            using(GraphicsPath path=Palette.Rounded(highlight,Ui.Px(8)))
            using(var fill=new SolidBrush(Palette.Mix(Palette.Sidebar,Palette.Accent,Palette.Dark?.32f:.17f))) g.FillPath(fill,path);
            if(Focused && ShowFocusCues)
                using(GraphicsPath path=Palette.Rounded(highlight,Ui.Px(8)))
                using(var pen=new Pen(Palette.Alpha(Palette.Accent,150),1.5f*Ui.S)) g.DrawPath(pen,path);
            for(int i=0;i<items.Length;i++) {
                Rectangle row=Item(i);
                float near=1-Math.Min(1,Math.Abs(slide.Value-i));
                Color icon=Palette.Mix(Palette.Muted,Palette.Dark?Palette.Mix(Palette.Accent,Color.White,.25f):Palette.Accent,near);
                float size=18*Ui.S;
                Ui.Lucide(g,glyphs[i],icon,row.X+Ui.Px(12),row.Y+(row.Height-size)/2,size);
                Ui.Text(g,items[i],Ui.Font(11f,true),new Rectangle(row.X+Ui.Px(42),row.Y,row.Width-Ui.Px(48),row.Height),
                        Palette.Ink,Ui.Left);
            }
            Ui.Text(g,"V2.1  ·  MSFS 2024",Ui.Font(9.5f,true),new Rectangle(Ui.Px(20),Height-Ui.Px(32),
                Width-Ui.Px(40),Ui.Px(20)),Palette.Muted,Ui.Left);
        }
    }
    internal sealed class PageHeader : Control
    {
        private string title="",subtitle="";
        internal PageHeader()
        {
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
            BackColor=Palette.Paper;
        }
        internal string Title {get {return title;} set {title=value;Invalidate();}}
        internal string Subtitle {get {return subtitle;} set {subtitle=value;Invalidate();}}
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            Ui.Text(e.Graphics,title,Ui.Font(18f,true),new Rectangle(0,0,Width,Ui.Px(34)),Palette.Ink,Ui.Left);
            Ui.Text(e.Graphics,subtitle,Ui.Font(10.5f,true),new Rectangle(Ui.Px(1),Ui.Px(36),Width,Ui.Px(20)),Palette.Muted,Ui.Left);
        }
    }

    internal sealed class StatusHero : CardControl
    {
        private string headline="",detail="",badge="";
        private readonly Tween shade=new Tween(),ias=new Tween(),alt=new Tween(),hdg=new Tween();
        private readonly Stopwatch pulseClock=Stopwatch.StartNew();
        private readonly Func<bool> animate;
        private Color fromColor=Palette.Idle,toColor=Palette.Idle;
        private bool pulse,hasData,stale=true;

        internal StatusHero()
        {
            animate=delegate {
                if(IsDisposed) return false;
                bool busy=shade.Step()|ias.Step()|alt.Step()|hdg.Step();
                bool beating=pulse && Visible;
                if(busy) Invalidate();else if(beating) Invalidate(OrbArea);
                return busy || beating;
            };
        }
        private int Pad {get {return Ui.Px(22);}}
        private Rectangle OrbArea {get {Rectangle b=Body;return new Rectangle(b.X+Pad-Ui.Px(14),b.Y+Pad-Ui.Px(8),Ui.Px(40),Ui.Px(40));}}
        private void Kick() {Clock.Run(animate);}
        internal string Headline {set {if(headline!=value) {headline=value;Invalidate();}}}
        internal string Detail {set {if(detail!=value) {detail=value;Invalidate();}}}
        internal string Badge {set {if(badge!=value) {badge=value;Invalidate();}}}
        internal bool Pulse {set {if(pulse!=value) {pulse=value;if(value) Kick();else Invalidate(OrbArea);}}}
        internal bool Stale {set {if(stale!=value) {stale=value;Invalidate();}}}
        internal Color StatusColor {
            set {
                if(value.ToArgb()==toColor.ToArgb()) return;
                fromColor=Current;toColor=value;shade.Jump(0);shade.To(1,360);Kick();Invalidate();
            }
        }
        private Color Current {get {return Palette.Mix(fromColor,toColor,shade.Running||shade.Value>0?shade.Value:1);}}
        internal void SetMetrics(double speed,double altitude,double heading)
        {
            if(!hasData) {ias.Jump((float)speed);alt.Jump((float)altitude);hdg.Jump((float)heading);hasData=true;Invalidate();return;}
            float current=hdg.Value,delta=(float)((heading-current)%360+540)%360-180;
            ias.To((float)speed,480);alt.To((float)altitude,480);hdg.To(current+delta,480);Kick();
        }
        protected override void OnVisibleChanged(EventArgs e) {base.OnVisibleChanged(e);if(Visible && pulse) Kick();}
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g=e.Graphics;Rectangle b=Body;int pad=Pad;
            if(b.Width<Ui.Px(60) || b.Height<Ui.Px(60)) return;
            g.SmoothingMode=SmoothingMode.AntiAlias;
            Color status=Current;
            Rectangle orb=OrbArea;
            if(e.ClipRectangle.IntersectsWith(orb)) {
                float cx=b.X+pad+Ui.Px(6),cy=b.Y+pad+Ui.Px(12),r=5.5f*Ui.S;
                if(pulse && Clock.Animate) {
                    for(int k=0;k<2;k++) {
                        float phase=(float)((pulseClock.ElapsedMilliseconds/1800.0+k*.5)%1.0);
                        float grow=r+phase*11*Ui.S;
                        using(var halo=new SolidBrush(Palette.Alpha(status,(int)(90*(1-phase)*(1-phase)))))
                            g.FillEllipse(halo,cx-grow,cy-grow,grow*2,grow*2);
                    }
                } else using(var halo=new SolidBrush(Palette.Alpha(status,40))) g.FillEllipse(halo,cx-r-3*Ui.S,cy-r-3*Ui.S,2*r+6*Ui.S,2*r+6*Ui.S);
                using(var dot=new SolidBrush(status)) g.FillEllipse(dot,cx-r,cy-r,r*2,r*2);
            }
            int textLeft=b.X+pad+Ui.Px(26);
            Font badgeFont=Ui.Font(10f,true);
            Size badgeText=Ui.Measure(badge,badgeFont);
            var pill=new Rectangle(b.Right-pad-badgeText.Width-Ui.Px(20),b.Y+pad+Ui.Px(1),badgeText.Width+Ui.Px(20),Ui.Px(24));
            if(badge.Length>0) {
                using(GraphicsPath path=Palette.Rounded(pill,pill.Height/2))
                using(var fill=new SolidBrush(Palette.Soft)) g.FillPath(fill,path);
                Ui.Text(g,badge,badgeFont,pill,Palette.Muted,Ui.Center);
            }
            int textRight=(badge.Length>0?pill.Left:b.Right-pad)-Ui.Px(12);
            Ui.Text(g,headline,Ui.Font(15f,true),new Rectangle(textLeft,b.Y+pad-Ui.Px(2),textRight-textLeft,Ui.Px(28)),Palette.Ink,Ui.Left);
            Ui.Text(g,detail,Ui.Font(10.5f,true),new Rectangle(textLeft,b.Y+pad+Ui.Px(29),b.Right-pad-textLeft,Ui.Px(20)),Palette.Muted,Ui.Left);
            int divider=b.Y+pad+Ui.Px(64);
            using(var line=new Pen(Palette.Border)) {
                g.SmoothingMode=SmoothingMode.None;
                g.DrawLine(line,b.X+pad,divider,b.Right-pad,divider);
                int width=(b.Width-2*pad)/3;
                for(int i=1;i<3;i++) {int x=b.X+pad+i*width;g.DrawLine(line,x,divider+Ui.Px(16),x,b.Bottom-pad+Ui.Px(2));}
                g.SmoothingMode=SmoothingMode.AntiAlias;
            }
            string[] names={"空速","高度","航向"},units={"kt","ft","°"},codes={"IAS","ALT","HDG"};
            float heading=((hdg.Value%360)+360)%360;
            string[] values=hasData?new[]{Math.Round(ias.Value).ToString("0"),Math.Round(alt.Value).ToString("#,##0"),
                                         (Math.Round(heading)%360).ToString("000")}:new[]{"—","—","—"};
            int column=(b.Width-2*pad)/3,top=divider+Ui.Px(16);
            Font label=Ui.Font(10f,true),code=Ui.Font(9f,true),number=Ui.Number(22f),unit=Ui.Font(10.5f,true);
            Color valueInk=!hasData||stale?Palette.Faint:Palette.Ink;
            for(int i=0;i<3;i++) {
                int x=b.X+pad+i*column+(i==0?0:Ui.Px(20));
                Size nameSize=Ui.Measure(names[i],label);
                Ui.Text(g,names[i],label,new Rectangle(x,top,nameSize.Width+2,Ui.Px(18)),Palette.Muted,Ui.Left);
                Ui.Text(g,codes[i],code,new Rectangle(x+nameSize.Width+Ui.Px(6),top,Ui.Px(40),Ui.Px(18)),Palette.Muted,Ui.Left);
                Size valueSize=Ui.Measure(values[i],number);
                var valueBox=new Rectangle(x-Ui.Px(1),top+Ui.Px(20),Math.Min(valueSize.Width+2,column-Ui.Px(24)),Ui.Px(40));
                Ui.Text(g,values[i],number,valueBox,valueInk,Ui.Left);
                if(hasData) Ui.Text(g,units[i],unit,new Rectangle(valueBox.Right+Ui.Px(4),valueBox.Y+Ui.Px(9),Ui.Px(28),valueBox.Height-Ui.Px(9)),
                                    Palette.Muted,Ui.Left);
            }
        }
    }

    internal sealed class InfoTile : CardControl
    {
        private string glyph="",title="",value="";
        private Color dot=Palette.Idle;
        internal string Glyph {set {if(glyph!=value) {glyph=value;Invalidate();}}}
        internal string Title {set {if(title!=value) {title=value;Invalidate();}}}
        internal string Value {set {if(this.value!=value) {this.value=value;Invalidate();}}}
        internal Color Dot {set {if(dot.ToArgb()!=value.ToArgb()) {dot=value;Invalidate();}}}
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g=e.Graphics;Rectangle b=Body;
            if(b.Width<Ui.Px(80) || b.Height<Ui.Px(30)) return;
            g.SmoothingMode=SmoothingMode.AntiAlias;
            int size=Ui.Px(36),mid=b.Y+b.Height/2;
            var icon=new Rectangle(b.X+Ui.Px(16),mid-size/2,size,size);
            using(GraphicsPath path=Palette.Rounded(icon,Ui.Px(9)))
            using(var fill=new SolidBrush(Palette.Tint(.12f,.26f))) g.FillPath(fill,path);
            Color glyphInk=Palette.Dark?Palette.Mix(Palette.Accent,Color.White,.3f):Palette.Accent;
            Ui.Text(g,glyph,Ui.Icons(12.5f),new Rectangle(icon.X,icon.Y+Ui.Px(1),icon.Width,icon.Height),glyphInk,Ui.Center);
            int x=icon.Right+Ui.Px(13),right=b.Right-Ui.Px(40);
            Ui.Text(g,title,Ui.Font(10f,true),new Rectangle(x,mid-Ui.Px(19),right-x,Ui.Px(18)),Palette.Muted,Ui.Left);
            Ui.Text(g,value,Ui.Font(10.5f,true),new Rectangle(x,mid-Ui.Px(1),right-x,Ui.Px(22)),Palette.Ink,Ui.Left);
            float r=4*Ui.S,cx=b.Right-Ui.Px(22),cy=mid;
            using(var halo=new SolidBrush(Palette.Alpha(dot,50))) g.FillEllipse(halo,cx-r*2,cy-r*2,r*4,r*4);
            using(var fill=new SolidBrush(dot)) g.FillEllipse(fill,cx-r,cy-r,r*2,r*2);
        }
    }

    internal sealed class GroupCard : CardControl
    {
        private readonly string[] titles,subtitles;
        internal int ControlWidth;
        internal GroupCard(params string[] rows) {titles=rows;subtitles=new string[rows.Length];}
        internal int RowHeight {get {return Ui.Px(64);}}
        internal Rectangle Row(int i) {Rectangle b=Body;return new Rectangle(b.X,b.Y+i*RowHeight,b.Width,RowHeight);}
        internal void SetSubtitle(int i,string text) {if(subtitles[i]!=text) {subtitles[i]=text;Invalidate(Row(i));}}
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g=e.Graphics;
            for(int i=0;i<titles.Length;i++) {
                Rectangle row=Row(i);
                if(!e.ClipRectangle.IntersectsWith(row)) continue;
                int x=row.X+Ui.Px(18),width=Math.Max(0,row.Width-Ui.Px(36)-ControlWidth);
                Ui.Text(g,titles[i],Ui.Font(11f,true),new Rectangle(x,row.Y+Ui.Px(12),width,Ui.Px(22)),Palette.Ink,Ui.Left);
                Ui.Text(g,subtitles[i],Ui.Font(10f,true),new Rectangle(x,row.Y+Ui.Px(35),width,Ui.Px(18)),Palette.Muted,Ui.Left);
                if(i>0) using(var line=new Pen(Palette.Border)) g.DrawLine(line,x,row.Y,row.Right-Ui.Px(1),row.Y);
            }
        }
    }

    internal sealed class Canvas : Panel
    {
        internal Canvas()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.UserPaint,true);
            BackColor=Palette.Paper;
        }
    }

    // 带圆角的浅色卡片容器（雷达页侧栏）。
    internal sealed class GlassPanel : Panel
    {
        internal GlassPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.UserPaint|ControlStyles.ResizeRedraw,true);
            BackColor=Palette.Card;
        }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Graphics g=e.Graphics;
            g.Clear(Parent!=null?Parent.BackColor:Palette.Paper);
            if(Width<4 || Height<4) return;
            g.SmoothingMode=SmoothingMode.AntiAlias;
            var body=new RectangleF(.5f,.5f,Width-1,Height-1);
            using(GraphicsPath path=Palette.Rounded(body,Ui.Px(12)))
            using(var fill=new SolidBrush(Palette.Card))
            using(var pen=new Pen(Palette.Dark?Palette.Mix(Palette.Card,Color.White,.07f):Palette.Border)) {
                g.FillPath(fill,path);g.DrawPath(pen,path);
            }
        }
    }
}
