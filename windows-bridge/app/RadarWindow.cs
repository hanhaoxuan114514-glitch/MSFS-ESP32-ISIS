using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Drawing.Imaging;
using System.Linq;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace A350Isis
{
    internal sealed class SatelliteMap : IDisposable
    {
        private const int TileSize=512;
        private static readonly int[] Levels={1350,2700,5400,10800,21600};
        private readonly ZipArchive archive;
        private readonly Dictionary<string,Bitmap> tiles=new Dictionary<string,Bitmap>();
        private readonly Queue<string> order=new Queue<string>();

        internal SatelliteMap(string path)
        {
            var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete);
            try {archive=new ZipArchive(stream,ZipArchiveMode.Read);}
            catch {stream.Dispose();throw;}
        }

        internal bool Draw(Graphics graphics,Rectangle clip,PointF topLeft,PointF bottomRight)
        {
            double worldWidth=bottomRight.X-topLeft.X,worldHeight=bottomRight.Y-topLeft.Y;
            int width=Levels.FirstOrDefault(level=>level>=worldWidth);
            if(width==0) width=Levels[Levels.Length-1];
            int height=width/2;
            int firstX=Math.Max(0,(int)Math.Floor((clip.Left-topLeft.X)*width/worldWidth/TileSize));
            int firstY=Math.Max(0,(int)Math.Floor((clip.Top-topLeft.Y)*height/worldHeight/TileSize));
            int lastX=Math.Min((width-1)/TileSize,(int)Math.Floor((clip.Right-topLeft.X)*width/worldWidth/TileSize));
            int lastY=Math.Min((height-1)/TileSize,(int)Math.Floor((clip.Bottom-topLeft.Y)*height/worldHeight/TileSize));
            graphics.InterpolationMode=InterpolationMode.HighQualityBilinear;
            using(var edge=new ImageAttributes()) {
                edge.SetWrapMode(WrapMode.TileFlipXY);
                for(int y=firstY;y<=lastY;y++) for(int x=firstX;x<=lastX;x++) {
                    string key=width+"/"+x+"_"+y+".jpg";
                    Bitmap tile;
                    if(!tiles.TryGetValue(key,out tile)) {
                        var entry=archive.GetEntry(key);
                        if(entry==null) return false;
                        using(var stream=entry.Open())
                        using(var buffer=new MemoryStream()) {
                            stream.CopyTo(buffer);buffer.Position=0;
                            using(var image=Image.FromStream(buffer)) tile=new Bitmap(image);
                        }
                        tiles.Add(key,tile);order.Enqueue(key);
                        if(order.Count>32) {
                            string oldest=order.Dequeue();tiles[oldest].Dispose();tiles.Remove(oldest);
                        }
                    }
                    int left=(int)Math.Round(topLeft.X+x*TileSize*worldWidth/width);
                    int top=(int)Math.Round(topLeft.Y+y*TileSize*worldHeight/height);
                    int right=(int)Math.Round(topLeft.X+(x*TileSize+tile.Width)*worldWidth/width);
                    int bottom=(int)Math.Round(topLeft.Y+(y*TileSize+tile.Height)*worldHeight/height);
                    if(right>left && bottom>top)
                        graphics.DrawImage(tile,Rectangle.FromLTRB(left,top,right,bottom),0,0,tile.Width,tile.Height,GraphicsUnit.Pixel,edge);
                }
            }
            return true;
        }

        public void Dispose() {foreach(var tile in tiles.Values) tile.Dispose();archive.Dispose();}
    }

    internal sealed class RadarSite
    {
        public string name {get;set;}
        public double lat {get;set;}
        public double lon {get;set;}
        public int radius {get;set;}
        public bool enabled {get;set;}
        internal bool Valid {get {return !String.IsNullOrWhiteSpace(name) && lat>=-90 && lat<=90 && lon>=-180 && lon<=180 && radius>=5 && radius<=100;}}
        public override string ToString() {return name;}
    }

    internal sealed class RadarAircraft
    {
        public double lat {get;set;}
        public double lon {get;set;}
        public double track {get;set;}
        public string callsign {get;set;}
        public double? altitude {get;set;}
    }

    internal sealed class RadarSnapshot
    {
        public int status {get;set;}
        public double lat {get;set;}
        public double lon {get;set;}
        public int radius {get;set;}
        public List<RadarAircraft> targets {get;set;}
    }

    internal sealed class RadarMap : Control
    {
        private readonly List<PointF[]> land=new List<PointF[]>();
        private SatelliteMap satellite;
        private readonly string streetCache;
        private bool street=true;
        private readonly Dictionary<string,Bitmap> streets=new Dictionary<string,Bitmap>();
        private readonly Queue<string> streetOrder=new Queue<string>();
        private readonly HashSet<string> pendingTiles=new HashSet<string>();
        private readonly Dictionary<string,DateTime> failedTiles=new Dictionary<string,DateTime>();
        private List<RadarSite> sites=new List<RadarSite>();
        private List<RadarAircraft> tracks=new List<RadarAircraft>();
        private double latitude,longitude,pixelsPerDegree;
        private Point start,previous;
        private Point? pointer;
        private RadarAircraft hoverTarget;
        private bool dragging;
        private RadarSite active;
        private PointF? marker;
        internal event Action<double,double> PointPicked;

        internal RadarMap(string file,string cache)
        {
            streetCache=cache;
            SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|
                     ControlStyles.ResizeRedraw|ControlStyles.Selectable,true);
            TabStop=true;AccessibleName="ADS-B 地图";Cursor=Cursors.Cross;
            try {satellite=new SatelliteMap(Path.Combine(Path.GetDirectoryName(file),"blue-marble.zip"));}
            catch(IOException) { }catch(InvalidDataException) { }
            try {
                var parser=new JavaScriptSerializer();
                var json=(Dictionary<string,object>)parser.DeserializeObject(File.ReadAllText(file));
                foreach(Dictionary<string,object> feature in (IList)json["features"]) {
                    var geometry=(Dictionary<string,object>)feature["geometry"];
                    var coordinates=(IList)geometry["coordinates"];
                    if((string)geometry["type"]=="Polygon") AddPolygon(coordinates);
                    else foreach(IList polygon in coordinates) AddPolygon(polygon);
                }
            } catch(IOException) { }
        }
        private void DrawFrame(Graphics graphics)
        {
            if(Width<4 || Height<4) return;
            graphics.SmoothingMode=SmoothingMode.AntiAlias;
            var body=new RectangleF(.5f,.5f,Width-1,Height-1);
            float radius=Ui.Px(12);
            using(var outside=new GraphicsPath())
            using(var shape=Palette.Rounded(body,radius))
            using(var back=new SolidBrush(Parent!=null?Parent.BackColor:Palette.Paper))
            using(var pen=new Pen(Palette.Dark?Palette.Mix(Palette.Card,Color.White,.07f):Palette.Border)) {
                outside.FillMode=FillMode.Alternate;
                outside.AddRectangle(new RectangleF(-1,-1,Width+2,Height+2));outside.AddPath(shape,false);
                graphics.FillPath(back,outside);graphics.DrawPath(pen,shape);
            }
        }
        private void AddPolygon(IList polygon)
        {
            if(polygon.Count==0) return;
            var ring=(IList)polygon[0];
            var points=new PointF[ring.Count];
            for(int i=0;i<points.Length;i++) {
                var pair=(IList)ring[i];
                points[i]=new PointF(Convert.ToSingle(pair[0],CultureInfo.InvariantCulture),
                                     Convert.ToSingle(pair[1],CultureInfo.InvariantCulture));
            }
            if(points.Length>=3) land.Add(points);
        }
        private double WorldHeight {get {return street?360:180;}}
        private double MinimumScale {get {return Math.Max(.01,Math.Min(Width/360.0,Height/WorldHeight));}}
        private double ZoomScale {get {return Math.Max(pixelsPerDegree,MinimumScale);}}
        private double LocalCosine {get {return Math.Max(.09,Math.Cos(latitude*Math.PI/180));}}
        private double HorizontalScale {get {
            double local=Math.Max(0,Math.Min(1,(ZoomScale/MinimumScale-1)/4));
            return ZoomScale*(street?1:1+(LocalCosine-1)*local);
        }}
        private double MapY(double lat) {return street?MercatorY(lat,1)*360:90-lat;}
        private double MapLatitude(double y) {
            y=Math.Max(0,Math.Min(WorldHeight,y));
            return street?MercatorLatitude(y/360,1):90-y;
        }
        internal void World() {latitude=longitude=pixelsPerDegree=0;Invalidate();}
        internal void ToggleMap() {
            if(pixelsPerDegree>MinimumScale*5)
                pixelsPerDegree=street?Math.Min(1800,pixelsPerDegree/LocalCosine):pixelsPerDegree*LocalCosine;
            street=!street;Invalidate();
        }
        internal void SetSites(List<RadarSite> saved) {sites=saved;Invalidate();}
        internal void SetTracks(List<RadarAircraft> targets) {tracks=targets??new List<RadarAircraft>();Invalidate();}
        internal void FocusSite(RadarSite site)
        {
            active=site;marker=null;latitude=site.lat;longitude=site.lon;
            pixelsPerDegree=Math.Min(1600,Math.Min(Width,Height)*60.0/(site.radius*3.6)*
                (street?Math.Max(.09,Math.Cos(site.lat*Math.PI/180)):1));
            Invalidate();
        }
        internal void Mark(double lat,double lon) {marker=new PointF((float)lon,(float)lat);Invalidate();}
        private PointF Screen(double lat,double lon) {
            return new PointF((float)(Width/2.0+(lon-longitude)*HorizontalScale),(float)(Height/2.0+(MapY(lat)-MapY(latitude))*ZoomScale));
        }
        protected override void OnMouseDown(MouseEventArgs e) {
            base.OnMouseDown(e);if(e.Button!=MouseButtons.Left) return;
            Focus();start=previous=e.Location;dragging=false;Capture=true;
        }
        protected override void OnMouseMove(MouseEventArgs e) {
            base.OnMouseMove(e);pointer=e.Location;
            if(!Capture) {if(hoverTarget!=FindAircraft()) Invalidate();return;}
            if(Math.Abs(e.X-start.X)+Math.Abs(e.Y-start.Y)>4) dragging=true;
            if(!dragging) return;
            longitude=Math.Max(-180,Math.Min(180,longitude-(e.X-previous.X)/HorizontalScale));
            latitude=MapLatitude(MapY(latitude)-(e.Y-previous.Y)/ZoomScale);
            previous=e.Location;Invalidate();
        }
        protected override void OnMouseUp(MouseEventArgs e) {
            base.OnMouseUp(e);if(!Capture || e.Button!=MouseButtons.Left) return;
            Capture=false;Invalidate();if(dragging || !ClientRectangle.Contains(e.Location)) return;
            double lon=longitude+(e.X-Width/2.0)/HorizontalScale,y=MapY(latitude)+(e.Y-Height/2.0)/ZoomScale;
            if(y>=0 && y<=WorldHeight && lon>=-180 && lon<=180 && PointPicked!=null) PointPicked(MapLatitude(y),lon);
        }
        protected override void OnMouseLeave(EventArgs e) {pointer=null;Invalidate();base.OnMouseLeave(e);}
        protected override void OnMouseWheel(MouseEventArgs e) {
            base.OnMouseWheel(e);
            double old=ZoomScale,oldHorizontal=HorizontalScale;
            pixelsPerDegree=Math.Min(street?93000:1800,Math.Max(MinimumScale,old*Math.Pow(1.3,e.Delta/120.0)));
            longitude=Math.Max(-180,Math.Min(180,longitude+(e.X-Width/2.0)*(1/oldHorizontal-1/HorizontalScale)));
            latitude=MapLatitude(MapY(latitude)+(e.Y-Height/2.0)*(1/old-1/ZoomScale));
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var graphics=e.Graphics;
            graphics.Clear(Color.FromArgb(226,232,226));graphics.SmoothingMode=SmoothingMode.AntiAlias;
            bool photo=false;
            if(!street && satellite!=null) {
                graphics.Clear(Color.FromArgb(15,24,32));
                try {photo=satellite.Draw(graphics,e.ClipRectangle,Screen(90,-180),Screen(-90,180));}
                catch(IOException) {satellite.Dispose();satellite=null;}
                catch(ArgumentException) {satellite.Dispose();satellite=null;}
            }
            if(!photo) graphics.Clear(Color.FromArgb(226,232,226));
            if(!photo) using(var fill=new SolidBrush(Color.FromArgb(251,248,237)))
            using(var border=new Pen(Color.FromArgb(181,186,172))) {
                foreach(PointF[] ring in land) {
                    var projected=new PointF[ring.Length];
                    float minX=Width,maxX=0,minY=Height,maxY=0;
                    for(int i=0;i<ring.Length;i++) {
                        PointF p=projected[i]=Screen(ring[i].Y,ring[i].X);
                        minX=Math.Min(minX,p.X);maxX=Math.Max(maxX,p.X);
                        minY=Math.Min(minY,p.Y);maxY=Math.Max(maxY,p.Y);
                    }
                    if(maxX<=0 || minX>=Width || maxY<=0 || minY>=Height) continue;
                    graphics.FillPolygon(fill,projected);graphics.DrawPolygon(border,projected);
                }
            }
            bool streetVisible=street && DrawStreet(e.Graphics);
            photo=photo && !streetVisible;
            if(active!=null) {
                PointF center=Screen(active.lat,active.lon);
                float dy=(float)(active.radius/60.0*ZoomScale);
                float dx=(float)(active.radius/60.0*HorizontalScale/Math.Max(.09,Math.Cos(active.lat*Math.PI/180)));
                if(street) dy=dx;
                var ring=RectangleF.FromLTRB(center.X-dx,center.Y-dy,center.X+dx,center.Y+dy);
                if(ring.IntersectsWith(ClientRectangle) && !ring.Contains(ClientRectangle))
                    using(var pen=new Pen(photo?Color.FromArgb(215,240,220):Palette.Live) {DashStyle=DashStyle.Dash})
                        graphics.DrawEllipse(pen,ring);
            }
            foreach(var site in sites) DrawPin(graphics,Screen(site.lat,site.lon),photo?Color.FromArgb(255,186,113):Palette.Accent,site.name,photo);
            if(marker.HasValue) DrawPin(graphics,Screen(marker.Value.Y,marker.Value.X),photo?Color.White:Palette.MapInk,"",photo);
            foreach(var plane in tracks) {
                if(plane==null || !(plane.lat>=-90 && plane.lat<=90 && plane.lon>=-180 && plane.lon<=180)) continue;
                PointF p=Screen(plane.lat,plane.lon);
                if(p.X<0 || p.X>=Width || p.Y<0 || p.Y>=Height) continue;
                double radians=Double.IsNaN(plane.track)||Double.IsInfinity(plane.track)?0:plane.track*Math.PI/180;
                float dx=(float)Math.Sin(radians),dy=-(float)Math.Cos(radians);
                using(var pen=new Pen(photo?Color.FromArgb(158,255,191):Palette.Live,2))
                    graphics.DrawLines(pen,new[]{new PointF(p.X-dx*3-dy*4,p.Y-dy*3+dx*4),
                        new PointF(p.X+dx*6,p.Y+dy*6),new PointF(p.X-dx*3+dy*4,p.Y-dy*3-dx*4)});
            }
            hoverTarget=FindAircraft();
            if(hoverTarget!=null) DrawAircraftCard(graphics,hoverTarget,Screen(hoverTarget.lat,hoverTarget.lon));
            if(photo) DrawMapText(graphics,"NASA Earth Observatory",new Point(11,Height-Font.Height-8),Color.White,true);
            else if(streetVisible) DrawMapText(graphics,"© OpenStreetMap contributors  ·  openstreetmap.org/copyright",
                new Point(11,Height-Font.Height-8),Palette.MapInk,true);
            DrawFrame(graphics);
        }
        private RadarAircraft FindAircraft() {
            if(!pointer.HasValue || Capture) return null;
            RadarAircraft closest=null;
            float distance=144;
            foreach(var plane in tracks) {
                if(plane==null || !(plane.lat>=-90 && plane.lat<=90 && plane.lon>=-180 && plane.lon<=180)) continue;
                PointF point=Screen(plane.lat,plane.lon);
                if(!ClientRectangle.Contains(Point.Truncate(point))) continue;
                float x=point.X-pointer.Value.X,y=point.Y-pointer.Value.Y;
                if(x*x+y*y<distance) {distance=x*x+y*y;closest=plane;}
            }
            return closest;
        }
        private void DrawAircraftCard(Graphics graphics,RadarAircraft plane,PointF point) {
            string call=String.IsNullOrWhiteSpace(plane.callsign)?"未知航班":plane.callsign;
            string altitude=plane.altitude.HasValue?plane.altitude.Value.ToString("0")+" ft":"高度未知";
            string label=call+"  ·  "+altitude;
            Size text=TextRenderer.MeasureText(label,Font);
            var card=new Rectangle(Math.Max(4,Math.Min(Width-text.Width-22,(int)point.X+13)),
                Math.Max(4,Math.Min(Height-text.Height-31,(int)point.Y-text.Height-15)),text.Width+18,text.Height+10);
            using(var shadow=Palette.Rounded(new Rectangle(card.X,card.Y+2,card.Width,card.Height),9))
            using(var shade=new SolidBrush(Color.FromArgb(40,0,0,0))) graphics.FillPath(shade,shadow);
            using(var shape=Palette.Rounded(card,9))
            using(var fill=new SolidBrush(Palette.Card)) graphics.FillPath(fill,shape);
            TextRenderer.DrawText(graphics,label,Font,card,Palette.Ink,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
        }
        private static double MercatorY(double lat,int count) {
            double radians=Math.Max(-85.05112878,Math.Min(85.05112878,lat))*Math.PI/180;
            return (1-Math.Log(Math.Tan(radians)+1/Math.Cos(radians))/Math.PI)*count/2;
        }
        private static double MercatorLatitude(double row,int count) {
            return Math.Atan(Math.Sinh(Math.PI*(1-2*row/count)))*180/Math.PI;
        }
        private bool DrawStreet(Graphics graphics)
        {
            if(!IsHandleCreated || ZoomScale<=0) return false;
            int zoom=Math.Max(0,Math.Min(17,(int)Math.Ceiling(Math.Log(ZoomScale*360/256,2))));
            int count=1<<zoom;
            double west=longitude-Width/2.0/ZoomScale,east=longitude+Width/2.0/ZoomScale;
            int x1=Math.Max(0,(int)Math.Floor((west+180)*count/360)),x2=Math.Min(count-1,(int)Math.Floor((east+180)*count/360));
            double centerY=MercatorY(latitude,count), half=Height/2.0/ZoomScale*count/360;
            int y1=Math.Max(0,(int)Math.Floor(centerY-half)),y2=Math.Min(count-1,(int)Math.Floor(centerY+half));
            double side=360*ZoomScale/count;
            bool any=false;
            graphics.InterpolationMode=InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode=PixelOffsetMode.Half;
            for(int y=y1;y<=y2;y++) for(int x=x1;x<=x2;x++) {
                var tile=StreetTile(zoom,x,y,!(Capture && dragging));
                int subdivision=1;
                for(int parent=zoom-1;tile==null && parent>=0 && parent>=zoom-8;parent--) {
                    subdivision*=2;
                    tile=StreetTile(parent,x/subdivision,y/subdivision,false);
                }
                if(tile==null) continue;
                double left=Width/2.0+(x*360.0/count-180-longitude)*ZoomScale;
                double top=Height/2.0+(y-centerY)*side;
                var destination=Rectangle.FromLTRB((int)Math.Round(left),(int)Math.Round(top),
                    (int)Math.Round(left+side),(int)Math.Round(top+side));
                float source=256f/subdivision;
                graphics.DrawImage(tile,destination,(x%subdivision)*source,(y%subdivision)*source,source,source,GraphicsUnit.Pixel);
                any=true;
            }
            graphics.PixelOffsetMode=PixelOffsetMode.Default;
            return any;
        }
        private Bitmap StreetTile(int z,int x,int y,bool fetch=true)
        {
            string key=z+"_"+x+"_"+y,path=Path.Combine(streetCache,key+".png");
            Bitmap tile;
            if(streets.TryGetValue(key,out tile)) return tile;
            try {
                if(File.Exists(path)) {
                    using(var stream=new MemoryStream(File.ReadAllBytes(path)))
                    using(var image=Image.FromStream(stream)) tile=new Bitmap(image);
                    streets.Add(key,tile);streetOrder.Enqueue(key);
                    if(streetOrder.Count>80) {
                        string old=streetOrder.Dequeue();streets[old].Dispose();streets.Remove(old);
                    }
                    DateTime expires;
                    if(!DateTime.TryParse(File.Exists(path+".expiry")?File.ReadAllText(path+".expiry"):"",
                        CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out expires))
                        expires=File.GetLastWriteTimeUtc(path).AddDays(7);
                    if(expires>DateTime.UtcNow) return tile;
                }
            } catch(IOException) { }catch(ArgumentException) { }
            DateTime retry;
            if(!fetch || pendingTiles.Count>=3 || pendingTiles.Contains(key) ||
               (failedTiles.TryGetValue(key,out retry) && retry>DateTime.UtcNow)) return tile;
            pendingTiles.Add(key);
            System.Threading.ThreadPool.QueueUserWorkItem(delegate {
                bool saved=false;
                try {
                    var request=(HttpWebRequest)WebRequest.Create("https://tile.openstreetmap.org/"+z+"/"+x+"/"+y+".png");
                    request.UserAgent="MyHud/1.0 (https://github.com/hanhaoxuan114514-glitch/MSFS-ESP32-ISIS)";
                    request.Timeout=6000;request.ReadWriteTimeout=6000;
                    using(var response=(HttpWebResponse)request.GetResponse())
                    using(var input=response.GetResponseStream())
                    using(var bytes=new MemoryStream()) {
                        var buffer=new byte[8192];int count;
                        while((count=input.Read(buffer,0,buffer.Length))>0) {
                            bytes.Write(buffer,0,count);if(bytes.Length>1000000) throw new IOException("Invalid tile size");
                        }
                        byte[] image=bytes.ToArray();
                        if(image.Length<8 || image[0]!=137 || image[1]!=80) throw new IOException("Invalid tile image");
                        DateTime expiry=DateTime.UtcNow.AddDays(7);
                        var maxAge=System.Text.RegularExpressions.Regex.Match(response.Headers["Cache-Control"]??"",@"max-age=(\d+)");
                        int seconds;
                        if(maxAge.Success && Int32.TryParse(maxAge.Groups[1].Value,out seconds)) expiry=DateTime.UtcNow.AddSeconds(seconds);
                        else {
                            DateTime explicitExpiry;
                            if(DateTime.TryParse(response.Headers["Expires"],out explicitExpiry)) expiry=explicitExpiry.ToUniversalTime();
                        }
                        Directory.CreateDirectory(streetCache);
                        string temporary=path+".tmp";File.WriteAllBytes(temporary,image);
                        if(File.Exists(path)) File.Replace(temporary,path,null);else File.Move(temporary,path);
                        File.WriteAllText(path+".expiry",expiry.ToString("o",CultureInfo.InvariantCulture));saved=true;
                    }
                } catch(WebException) { }catch(IOException) { }catch(UnauthorizedAccessException) { }
                try {BeginInvoke((Action)delegate {
                    pendingTiles.Remove(key);
                    if(saved) {
                        Bitmap oldTile;
                        if(streets.TryGetValue(key,out oldTile)) oldTile.Dispose();
                        streets.Remove(key);
                        // A stale tile may still have a queued eviction entry.
                        var retained=streetOrder.Where(item=>item!=key).ToArray();streetOrder.Clear();
                        foreach(string item in retained) streetOrder.Enqueue(item);
                    } else failedTiles[key]=DateTime.UtcNow.AddSeconds(45);
                    if(Visible && street) Invalidate();
                });}catch(InvalidOperationException) { }
            });
            return tile;
        }
        private void DrawMapText(Graphics graphics,string value,Point point,Color color,bool photo)
        {
            if(photo) {
                var bounds=new Rectangle(point,TextRenderer.MeasureText(value,Font));bounds.Inflate(4,2);
                using(var shape=Palette.Rounded(bounds,6))
                using(var background=new SolidBrush(Color.FromArgb(220,23,34,47))) graphics.FillPath(background,shape);
                color=Color.White;
            }
            TextRenderer.DrawText(graphics,value,Font,point,color);
        }
        private void DrawPin(Graphics graphics,PointF p,Color color,string label,bool photo)
        {
            if(p.X<0 || p.X>=Width || p.Y<0 || p.Y>=Height) return;
            using(var fill=new SolidBrush(color)) {
                graphics.FillEllipse(fill,p.X-4,p.Y-9,8,8);
                graphics.FillPolygon(fill,new[]{new PointF(p.X-3,p.Y-5),new PointF(p.X+3,p.Y-5),p});
            }
            if(ZoomScale>5 && label.Length>0)
                DrawMapText(graphics,label,Point.Round(new PointF(p.X+7,p.Y-10)),color,photo);
        }
        protected override void Dispose(bool disposing) {
            if(disposing) {if(satellite!=null) satellite.Dispose();foreach(var tile in streets.Values) tile.Dispose();}
            base.Dispose(disposing);
        }
    }

    internal sealed class GlassField : Panel
    {
        private readonly TextBox editor;
        private bool hover;
        internal GlassField(TextBox input) {
            editor=input;DoubleBuffered=true;ResizeRedraw=true;Cursor=Cursors.IBeam;
            editor.BorderStyle=BorderStyle.None;editor.BackColor=Palette.Field;editor.ForeColor=Palette.Ink;
            editor.Font=Ui.Font(10.5f,true);BackColor=Palette.Card;
            Controls.Add(editor);
            editor.GotFocus+=delegate {Invalidate();};editor.LostFocus+=delegate {Invalidate();};
            editor.MouseEnter+=delegate {hover=true;Invalidate();};
            editor.MouseLeave+=delegate {hover=false;Invalidate();};
        }
        protected override void OnLayout(LayoutEventArgs e) {
            base.OnLayout(e);
            if(editor==null) return;
            int pad=Math.Max(Ui.Px(10),Height/4);
            editor.SetBounds(pad,Math.Max(0,(Height-editor.PreferredHeight)/2),Math.Max(0,Width-pad*2),editor.PreferredHeight);
        }
        protected override void OnClick(EventArgs e) {editor.Focus();base.OnClick(e);}
        protected override void OnPaintBackground(PaintEventArgs e) {e.Graphics.Clear(Parent!=null?Parent.BackColor:BackColor);}
        protected override void OnPaint(PaintEventArgs e) {
            if(Width<4 || Height<4) return;
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            float s=Ui.S;
            var body=new RectangleF(1.5f*s,1.5f*s,Width-3*s,Height-3*s);
            float radius=Math.Min(7*s,body.Height/2);
            if(editor.Focused)
                using(var glow=Palette.Rounded(RectangleF.Inflate(body,1.2f*s,1.2f*s),radius+1.2f*s))
                using(var pen=new Pen(Palette.Alpha(Palette.Accent,90),2.4f*s)) e.Graphics.DrawPath(pen,glow);
            using(var shape=Palette.Rounded(body,radius))
            using(var fill=new SolidBrush(Palette.Field))
            using(var border=new Pen(editor.Focused?Palette.Accent:hover?Palette.Mix(Palette.Border,Palette.Ink,.25f):
                                     Palette.Dark?Palette.Border:Color.FromArgb(206,206,212),1)) {
                e.Graphics.FillPath(fill,shape);e.Graphics.DrawPath(border,shape);
            }
        }
    }

    internal sealed class RadarWindow : Form
    {
        private readonly string root,settings,bookmarks,live;
        private readonly JavaScriptSerializer json=new JavaScriptSerializer();
        private readonly List<RadarSite> sites;
        private readonly RadarMap map;
        private readonly ListBox list=new ListBox();
        private readonly TextBox name=new TextBox(),lat=new TextBox(),lon=new TextBox();
        private readonly TextBox search=new TextBox();
        private readonly ContextMenuStrip results=new ContextMenuStrip();
        private readonly TextBox radius=new TextBox();
        private readonly GlassPanel locations=new GlassPanel();
        private readonly Label empty=new Label();
        private readonly Label status=new Label();
        private readonly Timer timer=new Timer();
        private RadarSite active;
        private DateTime feedStamp;
        private bool rebuilding;

        internal static string ConfigPath(string root) {return Path.Combine(root,"logs","radar-settings.json");}
        internal static void Disable(string root)
        {
            var codec=new JavaScriptSerializer();
            string path=ConfigPath(root);
            var site=Read<RadarSite>(codec,path);
            if(site!=null && site.enabled) {site.enabled=false;Write(codec,path,site);}
        }
        private static T Read<T>(JavaScriptSerializer codec,string path) where T:class {
            try {return codec.Deserialize<T>(File.ReadAllText(path,Encoding.UTF8));}
            catch(IOException) {return null;}catch(ArgumentException) {return null;}catch(InvalidOperationException) {return null;}
        }
        private static void Write(JavaScriptSerializer codec,string path,object value) {
            string temporary=path+".tmp";
            File.WriteAllText(temporary,codec.Serialize(value),new UTF8Encoding(false));
            if(File.Exists(path)) File.Replace(temporary,path,null);else File.Move(temporary,path);
        }

        internal RadarWindow(string projectRoot,Icon icon,bool embedded=false)
        {
            root=projectRoot;settings=ConfigPath(root);
            bookmarks=Path.Combine(root,"logs","radar-airports.json");live=Path.Combine(root,"logs","radar-live.json");
            sites=(Read<List<RadarSite>>(json,bookmarks)??new List<RadarSite>()).Where(site=>site!=null && site.Valid).ToList();
            active=Read<RadarSite>(json,settings);
            if(active==null || !active.Valid) active=new RadarSite{name="Amsterdam",lat=52.3676,lon=4.9041,radius=25};
            SuspendLayout();
            AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;
            Text="MyHud · 实时雷达";Icon=icon;ClientSize=new Size(800,532);
            FormBorderStyle=FormBorderStyle.Sizable;StartPosition=FormStartPosition.CenterScreen;
            MinimumSize=new Size(500,395);
            BackColor=Palette.Paper;ForeColor=Palette.Ink;Font=Ui.Font(10.5f,true);
            Caption("ADS-B 实时空域",22,22,460,36,18);
            Caption("搜索全球城市和机场，或在地图上拖动、缩放和选点。",23,58,590,22,9.5f);
            if(!embedded) Button("返回 HUD",668,20,108,34,delegate {Close();});
            var searchField=Input(search,24,89,237);search.AccessibleName="搜索城市或机场";
            Ui.Placeholder(search,"搜索城市、ICAO 或 IATA");
            search.KeyDown+=delegate(object sender,KeyEventArgs e) {if(e.KeyCode==Keys.Enter) {e.SuppressKeyPress=true;SearchPlaces();}};
            var find=Button("搜索地点",269,89,84,36,SearchPlaces);
            map=new RadarMap(Path.Combine(root,"windows-bridge","map","land.geojson"),Path.Combine(root,"cache","osm"));
            map.SetBounds(24,126,500,354);Controls.Add(map);
            map.PointPicked+=delegate(double latitude,double longitude) {
                lat.Text=Coordinate(latitude);lon.Text=Coordinate(longitude);
                map.Mark(latitude,longitude);
            };
            locations.SetBounds(548,88,248,411);locations.AutoScroll=true;Controls.Add(locations);
            Ui.DarkScrollbars(locations);
            Caption("常用地点",14,12,216,26,12,locations);
            list.SetBounds(13,42,216,103);list.BackColor=Palette.Card;list.ForeColor=Palette.Ink;list.Font=Ui.Font(10.5f,true);
            list.BorderStyle=BorderStyle.None;list.DrawMode=DrawMode.OwnerDrawFixed;list.IntegralHeight=false;
            list.ItemHeight=Math.Max(28,Font.Height+10);
            list.DrawItem+=DrawSite;locations.Controls.Add(list);
            empty.Text="保存图钉后会显示在这里";empty.TextAlign=ContentAlignment.MiddleCenter;
            empty.BackColor=Palette.Card;empty.ForeColor=Palette.Muted;empty.Font=Ui.Font(10f,true);empty.Bounds=list.Bounds;
            locations.Controls.Add(empty);empty.BringToFront();
            list.SelectedIndexChanged+=delegate {
                var selected=list.SelectedItem as RadarSite;
                if(!rebuilding && selected!=null) {Fill(selected);Apply(selected);}
            };
            Caption("名称或 ICAO",13,156,134,21,9,locations);
            Input(name,13,180,130,locations);Button("定位 ICAO",149,180,80,36,LookupAirport,false,locations);
            Caption("纬度",13,226,104,21,9,locations);Caption("经度",125,226,104,21,9,locations);
            Input(lat,13,249,104,locations);Input(lon,125,249,104,locations);
            Caption("范围（海里）",13,303,99,23,9,locations);
            Button("−",113,293,29,36,delegate {SetRadius(-5);},false,locations);
            Input(radius,145,293,52,locations);radius.AccessibleName="范围（海里）";
            Button("+",200,293,29,36,delegate {SetRadius(5);},false,locations);
            Button("应用位置",13,344,104,36,delegate {RadarSite site=Draft();if(site!=null) Apply(site);},true,locations);
            Button("保存图钉",125,344,104,36,SavePin,false,locations);
            Button("删除选中",13,386,104,33,DeletePin,false,locations);
            status.SetBounds(24,493,752,23);status.ForeColor=Palette.Muted;status.Font=Ui.Font(10.5f,true);status.AutoEllipsis=true;Controls.Add(status);
            Ui.StyleMenu(results);
            ReloadSites();Fill(active);
            timer.Interval=500;timer.Tick+=delegate {RefreshFeed();};
            if(!embedded) Shown+=delegate {ActivatePage();};
            FormClosed+=delegate {DeactivatePage();};
            Resize+=delegate {LayoutPage(searchField,find);};
            Shown+=delegate {LayoutPage(searchField,find);};
            ResumeLayout(false);
        }
        private void LayoutPage(Control searchField,Control find) {
            float dpi=CurrentAutoScaleDimensions.Width/96f;
            Func<int,int> px=value=>(int)Math.Round(value*dpi);
            int edge=px(22),gap=px(12),right=ClientSize.Width-edge,bottom=ClientSize.Height-px(32);
            int width=Math.Max(0,right-edge),mapTop=px(136);
            bool compact=width<px(685);
            int sideWidth=px(252),mapWidth=compact?width:Math.Max(0,width-sideWidth-gap);
            int toolWidth=Math.Max(0,mapWidth-px(104));
            searchField.SetBounds(edge,px(90),toolWidth,px(36));
            find.SetBounds(searchField.Right+px(8),px(90),px(96),px(36));
            int sideHeight=compact?Math.Min(px(192),Math.Max(px(82),(bottom-mapTop)/3)):Math.Max(0,bottom-px(89));
            int mapBottom=compact?bottom-sideHeight-gap:bottom;
            map.SetBounds(edge,mapTop,mapWidth,Math.Max(0,mapBottom-mapTop));
            locations.SetBounds(compact?edge:right-sideWidth,compact?mapBottom+gap:px(89),
                compact?width:sideWidth,sideHeight);
            status.SetBounds(edge,bottom+px(5),width,px(24));
            empty.Bounds=list.Bounds;
        }
        private void DrawSite(object sender,DrawItemEventArgs e) {
            using(var background=new SolidBrush(Palette.Card)) e.Graphics.FillRectangle(background,e.Bounds);
            if(e.Index<0) return;
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            Rectangle row=Rectangle.Inflate(e.Bounds,-2,-2);
            bool selected=(e.State&DrawItemState.Selected)!=0;
            if(selected)
                using(var shape=Palette.Rounded(row,Ui.Px(7)))
                using(var brush=new SolidBrush(Palette.Accent)) e.Graphics.FillPath(brush,shape);
            Color ink=selected?Palette.OnAccent:Palette.Ink;
            var pin=new Rectangle(row.X+Ui.Px(6),row.Y,Ui.Px(20),row.Height);
            Ui.Text(e.Graphics,"\uE707",Ui.Icons(10f),pin,selected?ink:Palette.Accent,Ui.Center);
            Ui.Text(e.Graphics,list.Items[e.Index].ToString(),list.Font,
                Rectangle.FromLTRB(pin.Right+Ui.Px(6),row.Y,row.Right-Ui.Px(6),row.Bottom),ink,Ui.Left);
        }
        private void Caption(string value,int x,int y,int width,int height,float size,Control parent=null) {
            bool heading=size>=12;
            var label=new Label{Text=value,Font=Ui.Font(heading?size:10.5f,true),ForeColor=heading?Palette.Ink:Palette.Muted,
                BackColor=Color.Transparent,AutoEllipsis=true,UseMnemonic=false};
            label.SetBounds(x,y,width,height);(parent??this).Controls.Add(label);
        }
        private GlassField Input(TextBox input,int x,int y,int width,Control parent=null) {
            var frame=new GlassField(input);frame.SetBounds(x,y,width,36);(parent??this).Controls.Add(frame);return frame;
        }
        private DeckButton Button(string label,int x,int y,int width,int height,Action action,bool primary=false,Control parent=null) {
            var button=new DeckButton{Text=label,AccentButton=primary,BackColor=parent==null?Palette.Paper:Palette.Card};
            button.SetBounds(x,y,width,height);
            button.Click+=delegate {action();};(parent??this).Controls.Add(button);return button;
        }
        internal void ActivatePage() {Apply(active);timer.Start();}
        internal void DeactivatePage() {timer.Stop();Disable(root);}

        private void SearchPlaces()
        {
            string query=search.Text.Trim();
            if(query.Length<2) {status.Text="至少输入两个字符，例如城市名、ICAO 或 IATA。";return;}
            status.Text="正在搜索离线地点索引…";
            string file=Path.Combine(root,"windows-bridge","map","places.tsv");
            System.Threading.ThreadPool.QueueUserWorkItem(delegate {
                var matches=new List<Tuple<int,string,RadarSite>>();
                try {
                    foreach(string line in File.ReadLines(file,Encoding.UTF8)) {
                        if(line.IndexOf(query,StringComparison.OrdinalIgnoreCase)<0) continue;
                        string[] fields=line.Split('\t');
                        if(fields.Length!=7) continue;
                        string[] codes=fields[6].Split(' ');
                        int rank=String.Equals(fields[1],query,StringComparison.OrdinalIgnoreCase) ||
                            (fields[0]=="A" && query.Length<=4 && codes.Any(code=>String.Equals(code,query,StringComparison.OrdinalIgnoreCase)))?0:
                            String.Equals(fields[2],query,StringComparison.OrdinalIgnoreCase)?1:
                            fields[2].StartsWith(query,StringComparison.OrdinalIgnoreCase)?2:3;
                        double latitude,longitude;
                        if(!Double.TryParse(fields[4],NumberStyles.Float,CultureInfo.InvariantCulture,out latitude) ||
                           !Double.TryParse(fields[5],NumberStyles.Float,CultureInfo.InvariantCulture,out longitude)) continue;
                        string title=(fields[0]=="A"?fields[1]+" · ":"")+fields[2]+"  ["+fields[3]+"]";
                        matches.Add(Tuple.Create(rank,title,new RadarSite{name=fields[0]=="A"?fields[1]:fields[2],lat=latitude,lon=longitude}));
                        if(matches.Count>=48) {
                            matches=matches.OrderBy(item=>item.Item1).ThenBy(item=>item.Item2.Length).Take(12).ToList();
                        }
                    }
                } catch(IOException) { }
                try {BeginInvoke((Action)delegate {
                    if(!Visible || search.Text.Trim()!=query) return;
                    results.Items.Clear();
                    foreach(var match in matches.OrderBy(item=>item.Item1).ThenBy(item=>item.Item2.Length).Take(12)) {
                        var location=match.Item3;
                        results.Items.Add(match.Item2,null,delegate {location.radius=Range;Fill(location);Apply(location);});
                    }
                    status.Text=results.Items.Count==0?"没有匹配的机场或城市。":"选择搜索结果即可切换雷达中心。";
                    if(results.Items.Count>0) results.Show(search,new Point(0,search.Height));
                });}catch(InvalidOperationException) {}
            });
        }
        private static string Coordinate(double value) {return value.ToString("0.000000",CultureInfo.InvariantCulture);}
        private int Range {
            get {int value;return Int32.TryParse(radius.Text,out value)&&value>=5&&value<=100?value:active.radius;}
        }
        private void SetRadius(int delta) {radius.Text=Math.Max(5,Math.Min(100,Range+delta)).ToString(CultureInfo.InvariantCulture);}
        private void Fill(RadarSite site) {
            name.Text=site.name;lat.Text=Coordinate(site.lat);lon.Text=Coordinate(site.lon);
            radius.Text=site.radius.ToString(CultureInfo.InvariantCulture);
        }
        private RadarSite Draft() {
            double latitude=0,longitude=0;
            bool ok=Double.TryParse(lat.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out latitude) &&
                    Double.TryParse(lon.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out longitude);
            int range;
            if(!Int32.TryParse(radius.Text,out range) || range<5 || range>100) {status.Text="范围需为 5 到 100 海里。";return null;}
            var site=new RadarSite{name=name.Text.Trim(),lat=latitude,lon=longitude,radius=range};
            if(!ok || !site.Valid) {status.Text="请输入名称、有效纬度（±90°）和经度（±180°）。";return null;}
            return site;
        }
        private void Apply(RadarSite site) {
            active=site;active.enabled=true;
            Write(json,settings,active);map.SetTracks(null);map.FocusSite(active);
            status.Text="正在获取 "+active.name+" 附近的 ADS-B 数据…";
            feedStamp=DateTime.MinValue;
        }
        private void ReloadSites() {
            rebuilding=true;list.Items.Clear();foreach(var site in sites) list.Items.Add(site);
            rebuilding=false;empty.Visible=list.Items.Count==0;list.Visible=list.Items.Count!=0;map.SetSites(sites);
        }
        private void SavePin() {
            var site=Draft();if(site==null) return;
            int index=sites.FindIndex(item=>String.Equals(item.name,site.name,StringComparison.CurrentCultureIgnoreCase));
            if(index>=0) sites[index]=site;else sites.Add(site);
            Write(json,bookmarks,sites);ReloadSites();Apply(site);
        }
        private void DeletePin() {
            var item=list.SelectedItem as RadarSite;if(item==null) {status.Text="先从常用机场选择要删除的图钉。";return;}
            sites.Remove(item);Write(json,bookmarks,sites);ReloadSites();
            status.Text="已删除 "+item.name+" 的图钉。";
        }
        private void LookupAirport() {
            string file=Path.Combine(root,"windows-bridge","map","airports.csv");
            if(!File.Exists(file)) {status.Text="本地机场索引不可用，可直接输入经纬度。";return;}
            foreach(string row in File.ReadLines(file)) {
                string[] fields=row.Split(',');
                if(fields.Length!=3 || !String.Equals(fields[0],name.Text.Trim(),StringComparison.OrdinalIgnoreCase)) continue;
                var site=new RadarSite{name=fields[0],lat=Double.Parse(fields[1],CultureInfo.InvariantCulture),
                    lon=Double.Parse(fields[2],CultureInfo.InvariantCulture),radius=Range};
                Fill(site);Apply(site);return;
            }
            status.Text="本地索引中没有找到这个 ICAO，可在地图上选点。";
        }
        private void RefreshFeed() {
            DateTime modified=File.GetLastWriteTimeUtc(live);
            if(modified==feedStamp) {
                if((DateTime.UtcNow-modified).TotalSeconds>20) {
                    map.SetTracks(null);status.Text="等待桥接或 ADS-B 数据。离线地图和图钉仍可使用。";
                }
                return;
            }
            feedStamp=modified;
            var snapshot=Read<RadarSnapshot>(json,live);
            if(snapshot==null || snapshot.status==0 || snapshot.lat!=active.lat || snapshot.lon!=active.lon || snapshot.radius!=active.radius) return;
            map.SetTracks(snapshot.status==2?snapshot.targets:null);
            status.Text=snapshot.status==2?String.Format("{0} · {1} 海里 · {2} 架真实飞机",active.name,active.radius,
                snapshot.targets==null?0:snapshot.targets.Count):
                snapshot.status==1?"正在获取 ADS-B 数据…":"ADS-B 暂时不可用，自动重试中。";
        }
        protected override void Dispose(bool disposing) {if(disposing) {timer.Dispose();results.Dispose();}base.Dispose(disposing);}
    }
}
