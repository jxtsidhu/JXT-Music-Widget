// JXT MUSIC WIDGET v2 - classy, fluid "now playing" widget for Windows 10/11
// Publisher: JXT SIDHU
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using Windows.Media.Control;

[assembly: AssemblyTitle("JXT MUSIC WIDGET")]
[assembly: AssemblyDescription("Fluid now-playing music widget")]
[assembly: AssemblyCompany("JXT SIDHU")]
[assembly: AssemblyProduct("JXT MUSIC WIDGET")]
[assembly: AssemblyCopyright("Copyright (c) JXT SIDHU")]
[assembly: AssemblyVersion("2.0.0.0")]
[assembly: AssemblyFileVersion("2.0.0.0")]

static class Program {
    // Own awaiter for WinRT async operations (no dependency on System.Runtime.WindowsRuntime extensions)
    public static Task<T> Aw<T>(Windows.Foundation.IAsyncOperation<T> op) {
        var tcs = new TaskCompletionSource<T>();
        op.Completed = (o, st) => {
            try {
                if (st == Windows.Foundation.AsyncStatus.Completed) tcs.TrySetResult(o.GetResults());
                else if (st == Windows.Foundation.AsyncStatus.Canceled) tcs.TrySetCanceled();
                else tcs.TrySetException(o.ErrorCode ?? new Exception("WinRT async operation failed"));
            } catch (Exception ex) { tcs.TrySetException(ex); }
        };
        return tcs.Task;
    }
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint ms);
    [DllImport("winmm.dll")] static extern uint timeEndPeriod(uint ms);
    public static EventWaitHandle ShowEvt;
    public static bool Startup;
    static bool shown;

    public static void Log(Exception ex, bool box) {
        string f = Path.Combine(Cfg.Dir, "error.log");
        try { Directory.CreateDirectory(Cfg.Dir); File.AppendAllText(f, DateTime.Now + "\r\n" + ex + "\r\n\r\n"); } catch { }
        if (box && !shown) {
            shown = true;
            string st = ex.StackTrace ?? "";
            string[] ls = st.Split(new[] { '\n' }, 4);
            MessageBox.Show(Cfg.App + " hit a problem:\n\n" + ex.Message + "\n\nWhere:\n" + string.Join("\n", ls, 0, Math.Min(3, ls.Length)) +
                "\n\nDetails saved to:\n" + f, Cfg.App, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
    [STAThread] static void Main(string[] args) {
        try { SetProcessDPIAware(); } catch { }
        bool created;
        ShowEvt = new EventWaitHandle(false, EventResetMode.AutoReset, "JXT_MUSIC_WIDGET_SHOW");
        using (var m = new Mutex(true, "JXT_MUSIC_WIDGET_SINGLE", out created)) {
            if (!created) { ShowEvt.Set(); return; }   // already running: just open its settings window
            Startup = Array.IndexOf(args, "--startup") >= 0;
            if (Startup) Thread.Sleep(2000);            // let the taskbar/tray finish loading at logon
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => Log(e.Exception, true);
            P.Init();
            timeBeginPeriod(1);                         // precise timers = smoother animation
            try { Application.Run(new AppCtx()); } catch (Exception ex) { Log(ex, true); }
            timeEndPeriod(1);
        }
    }
}

static class G {
    public static GraphicsPath RR(RectangleF r, float rad) {
        var p = new GraphicsPath();
        float d = Math.Max(0.01f, Math.Min(rad * 2f, Math.Min(r.Width, r.Height)));
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure(); return p;
    }
    public static Color Mix(Color a, Color b, float t) {
        return Color.FromArgb(255, (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
    }
    public static Color A(int alpha, Color c) { return Color.FromArgb(Math.Max(0, Math.Min(255, alpha)), c); }
    public static float Sm(float cur, float target, float dt, float k) { return cur + (target - cur) * (1f - (float)Math.Exp(-k * dt)); }
    public static float Cl(float v) { return v < 0f ? 0f : (v > 1f ? 1f : v); }
    public static float Ease(float t) { t = Cl(t); return t * t * (3f - 2f * t); }
    public static Color Hsl(float h, float s, float l) {
        float c = (1f - Math.Abs(2f * l - 1f)) * s, x = c * (1f - Math.Abs((h / 60f) % 2f - 1f)), m = l - c / 2f, r = 0, g = 0, b = 0;
        if (h < 60) { r = c; g = x; } else if (h < 120) { r = x; g = c; } else if (h < 180) { g = c; b = x; }
        else if (h < 240) { g = x; b = c; } else if (h < 300) { r = x; b = c; } else { r = c; b = x; }
        return Color.FromArgb((int)((r + m) * 255), (int)((g + m) * 255), (int)((b + m) * 255));
    }
    // vivid accent colour taken from album art
    public static Color Accent(Bitmap b) {
        double r = 0, gg = 0, bb = 0, w = 0;
        using (var t = new Bitmap(20, 20)) {
            using (var g = Graphics.FromImage(t)) { g.InterpolationMode = InterpolationMode.HighQualityBilinear; g.DrawImage(b, 0, 0, 20, 20); }
            for (int y = 0; y < 20; y++)
                for (int x = 0; x < 20; x++) {
                    Color p = t.GetPixel(x, y); double s = p.GetSaturation(), l = p.GetBrightness();
                    double wt = 0.02 + s * s * Math.Max(0.05, 1 - Math.Abs(l - 0.5) * 1.6);
                    r += p.R * wt; gg += p.G * wt; bb += p.B * wt; w += wt;
                }
        }
        var avg = Color.FromArgb((int)(r / w), (int)(gg / w), (int)(bb / w));
        if (avg.GetSaturation() < 0.12f) return Color.FromArgb(190, 196, 215);
        return Hsl(avg.GetHue(), Math.Min(0.95f, Math.Max(0.55f, avg.GetSaturation() * 1.2f)), Math.Min(0.7f, Math.Max(0.58f, avg.GetBrightness())));
    }
}

// damped spring for bouncy, natural motion
class Spr {
    public float V, T, Vel, K = 170f, D = 22f;
    public void Step(float dt) {
        int n = (int)Math.Ceiling(dt / 0.006f); if (n < 1) n = 1; float h = dt / n;
        for (int i = 0; i < n; i++) { Vel += (K * (T - V) - D * Vel) * h; V += Vel * h; }
    }
    public bool Rest { get { return Math.Abs(T - V) < 0.002f && Math.Abs(Vel) < 0.02f; } }
}

// All settings. Every public instance field is saved to settings.ini automatically (bools as 0/1).
class Cfg {
    public const string App = "JXT MUSIC WIDGET", Publisher = "JXT SIDHU", Ver = "2.0";
    public static string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), App);
    public static string FilePath = Path.Combine(Dir, "settings.ini");
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    // Pos: 0 TL, 1 TC, 2 TR, 3 BL, 4 BC, 5 BR, 6 custom (dragged), 7 ML, 8 MC, 9 MR
    // Layout: 0 Classic, 1 Compact, 2 Vertical, 3 Minimal      Theme: 0 Dark, 1 Light, 2 Black, 3 Follow Windows
    // Mode: 0 always, 1 while media active, 2 peek on track change
    public int Pos = 5, X = 100, Y = 100, Scale = 100, Opacity = 100, Mode = 1, Layout = 0, Theme = 0, Accent = 0x8CAAFF,
               Radius = 22, Tint = 17, Margin = 20, Monitor = 0, Peek = 5, CloseAct = 0;   // CloseAct: 0 ask, 1 minimize to tray, 2 quit
    public bool ArtColor = true, Auto = true, ShowProgress = true, ShowTimes = true, Shadow = true, Glow = true,
                Marquee = true, OnTop = true, OpenUi = true;

    public Color Custom { get { return Color.FromArgb(255, (Accent >> 16) & 255, (Accent >> 8) & 255, Accent & 255); } }

    static FieldInfo[] Fs() { return typeof(Cfg).GetFields(BindingFlags.Public | BindingFlags.Instance); }
    static int Cl(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }
    void Fix() {
        Pos = Cl(Pos, 0, 9); Scale = Cl(Scale, 60, 200); Opacity = Cl(Opacity, 40, 100); Mode = Cl(Mode, 0, 2);
        Layout = Cl(Layout, 0, 3); Theme = Cl(Theme, 0, 3); Radius = Cl(Radius, 0, 40); Tint = Cl(Tint, 0, 40);
        Margin = Cl(Margin, 0, 100); Monitor = Cl(Monitor, 0, 8); Peek = Cl(Peek, 2, 15); CloseAct = Cl(CloseAct, 0, 2);
    }
    public void CopyFrom(Cfg o) { foreach (var f in Fs()) f.SetValue(this, f.GetValue(o)); }

    public static Cfg Load(out bool first) {
        var c = new Cfg(); first = !File.Exists(FilePath);
        if (first) return c;
        try {
            var d = new Dictionary<string, int>();
            foreach (var line in File.ReadAllLines(FilePath)) {
                int i = line.IndexOf('='), n;
                if (i > 0 && int.TryParse(line.Substring(i + 1).Trim(), out n)) d[line.Substring(0, i).Trim()] = n;
            }
            foreach (var f in Fs()) {
                int n;
                if (!d.TryGetValue(f.Name, out n)) continue;
                if (f.FieldType == typeof(bool)) f.SetValue(c, n == 1); else f.SetValue(c, n);
            }
        } catch { }
        c.Fix();
        return c;
    }
    public void Save() {
        try {
            Fix();
            Directory.CreateDirectory(Dir);
            var l = new List<string>();
            foreach (var f in Fs()) {
                object o = f.GetValue(this);
                l.Add(f.Name + "=" + (o is bool ? ((bool)o ? 1 : 0) : (int)o));
            }
            File.WriteAllLines(FilePath, l.ToArray());
        } catch { }
    }
    // Registered at EVERY launch so a moved/rebuilt exe or a disabled Startup-Apps switch heals itself.
    public static void SetAuto(bool on) {
        try {
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey)) {
                if (on) k.SetValue(App, "\"" + Application.ExecutablePath + "\" --startup"); else k.DeleteValue(App, false);
            }
            using (var k = Registry.CurrentUser.CreateSubKey(ApprovedKey)) {
                if (on) k.SetValue(App, new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary); else k.DeleteValue(App, false);
            }
        } catch { }
    }
}

class Widget : Form {
    [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr h, IntPtr dst, ref Pt pd, ref Sz sz, IntPtr src, ref Pt ps, int key, ref Bf bf, int flags);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [StructLayout(LayoutKind.Sequential)] struct Pt { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct Sz { public int W, H; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] struct Bf { public byte Op, Flags, Alpha, Fmt; }

    // logical layout (all drawing is done in these units, then scaled by K). W/H come from the chosen layout.
    const int PAD = 28;
    const string DemoT = "Midnight City", DemoA = "M83";
    int W = 400, H = 152, FW = 456, FH = 208;

    Cfg c; float K = 1f, dpi = 1f; Bitmap buf, pvBuf;
    System.Windows.Forms.Timer tm, pt;
    Stopwatch sw = Stopwatch.StartNew(); double last; float keep, time;
    public ContextMenuStrip Ctx;
    public Action ShowRequest, CfgChanged;
    public bool PreviewOn;
    bool pinned;

    // theme
    Color tBg, tTx, tSub, tInk, tPlay, tPlayIc; bool light; int themeId;

    // media state
    GlobalSystemMediaTransportControlsSessionManager mgr; GlobalSystemMediaTransportControlsSession cur; bool polling;
    string title = "", artist = "", dT = "Nothing playing", dA = "Play something to see it here", oT = "", oA = "";
    float wT, wA, woT, woA, wDT, wDA, tx = 1f, txDir = 1f;
    bool hasSession, playing, hasTL; TimeSpan posBase, dur; DateTimeOffset posStamp = DateTimeOffset.Now;
    Bitmap artCur, artPrev; float artX = 1f, artS = 0.93f, mp; int artTries; Color acc, accT;

    // ui state
    Spr sp = new Spr(); Spr[] pr = new Spr[3]; float[] hv = new float[6]; int hot;
    bool showT, forceHidden, scrub, dragging; DateTime peekUntil = DateTime.MinValue, forceUntil = DateTime.MinValue;
    float scrubFrac, prog; Point dragOff;
    Font fTitle, fArtist, fTime; StringFormat sf, sfE; Bitmap mb; Graphics mg;

    // layout geometry (absolute logical coordinates, already offset by PAD)
    RectangleF rCard, rArt, rProg; PointF cPrev, cPlay, cNext;
    float tx0, twid, ty1, ty2, px0, pwid, py, barTh, timeY, playR = 22f, icoS = 1f, artR = 16f; bool ctr, timeOn = true;

    public Widget(Cfg cfg) {
        c = cfg; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; TopMost = c.OnTop;
        sf = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.NoWrap };
        sfE = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter };
        mb = new Bitmap(1, 1); mg = Graphics.FromImage(mb);
        acc = c.Custom; accT = acc;
        sp.K = 170f; sp.D = 22f;
        for (int i = 0; i < 3; i++) pr[i] = new Spr { K = 260f, D = 13f, V = 1f, T = 1f };
        tm = new System.Windows.Forms.Timer { Interval = 8 }; tm.Tick += (s, e) => Tick();
        pt = new System.Windows.Forms.Timer { Interval = 400 }; pt.Tick += (s, e) => OnPoll();
        ApplyTheme(); Relayout(); Init(); pt.Start();
    }

    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams {
        get { var p = base.CreateParams; p.ExStyle |= 0x80000 | 0x08000000 | 0x80; return p; }   // layered, noactivate, toolwindow
    }

    async void Init() {
        try { mgr = await Program.Aw(GlobalSystemMediaTransportControlsSessionManager.RequestAsync()); }
        catch (Exception ex) { Program.Log(ex, true); }
    }
    float TW(string s, Font f) { return mg.MeasureString(s, f, 4000, sf).Width; }

    // ---------------- theme ----------------
    static bool SysLight() {
        try {
            object v = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1);
            return v is int && (int)v == 1;
        } catch { return false; }
    }
    void ApplyTheme() {
        int t = c.Theme; if (t == 3) t = SysLight() ? 1 : 0;
        themeId = t; light = t == 1;
        if (light) {
            tBg = Color.FromArgb(244, 245, 250); tTx = Color.FromArgb(24, 26, 34); tSub = Color.FromArgb(98, 104, 122);
            tInk = Color.Black; tPlay = Color.FromArgb(28, 30, 38); tPlayIc = Color.White;
        } else {
            tBg = t == 2 ? Color.Black : Color.FromArgb(24, 25, 31); tTx = Color.FromArgb(246, 247, 251); tSub = Color.FromArgb(176, 181, 196);
            tInk = Color.White; tPlay = Color.FromArgb(250, 250, 252); tPlayIc = Color.FromArgb(22, 23, 28);
        }
    }

    // ---------------- media polling ----------------
    void OnPoll() {
        if (Program.ShowEvt.WaitOne(0)) { Reveal(); if (ShowRequest != null) ShowRequest(); }
        Poll();
    }

    async void Poll() {
        if (polling || mgr == null) return;
        polling = true;
        try {
            var s = mgr.GetCurrentSession(); cur = s; bool has = s != null;
            if (has) {
                bool pl = s.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                var tl = s.GetTimelineProperties();
                var d = tl.EndTime - tl.StartTime; var st = tl.LastUpdatedTime; var now = DateTimeOffset.Now;
                if (st > now || now - st > TimeSpan.FromHours(6)) st = now;
                if (!scrub) { posBase = tl.Position; posStamp = st; }
                dur = d; hasTL = d.TotalSeconds > 1;
                if (playing != pl) { playing = pl; Wake(); }
                var props = await Program.Aw(s.TryGetMediaPropertiesAsync());
                string t = props.Title ?? "", a = props.Artist ?? "";
                if (t != title || a != artist) { NewTrack(t, a); artTries = 0; await LoadArt(props, true); }
                else if (artCur == null && artTries < 6 && props.Thumbnail != null) { artTries++; await LoadArt(props, false); }
            } else {
                playing = false; hasTL = false;
                if (title.Length > 0 || artist.Length > 0) NewTrack("", "");
            }
            hasSession = has;
            UpdateWant();
        } catch (Exception ex) { Program.Log(ex, false); }
        finally { polling = false; }
    }

    void NewTrack(string t, string a) {
        oT = dT; oA = dA; woT = wT; woA = wA;
        title = t; artist = a;
        dT = t.Length == 0 ? "Nothing playing" : t; dA = t.Length == 0 ? "Play something to see it here" : a;
        wT = TW(dT, fTitle); wA = TW(dA, fArtist);
        tx = 0f; peekUntil = DateTime.Now.AddSeconds(c.Peek);
        if (t.Length == 0) SetArt(null);
        Wake();
    }

    async Task LoadArt(GlobalSystemMediaTransportControlsSessionMediaProperties p, bool isNew) {
        Bitmap nb = null;
        try {
            if (p.Thumbnail != null) {
                var ms = new MemoryStream();
                var ras = await Program.Aw(p.Thumbnail.OpenReadAsync());
                uint size = (uint)ras.Size;
                var rd = new Windows.Storage.Streams.DataReader(ras.GetInputStreamAt(0));
                await Program.Aw(rd.LoadAsync(size));
                byte[] buf2 = new byte[size];
                rd.ReadBytes(buf2);
                ms.Write(buf2, 0, buf2.Length);
                ms.Position = 0;
                using (var img = Image.FromStream(ms)) nb = Cover(img, 300);
                ms.Dispose();
            }
        } catch { nb = null; }
        if (nb != null || isNew) SetArt(nb);
    }
    static Bitmap Cover(Image img, int n) {
        var b = new Bitmap(n, n, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(b))
        using (var ia = new ImageAttributes()) {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            ia.SetWrapMode(WrapMode.TileFlipXY);
            float s = Math.Min(img.Width, img.Height);
            g.DrawImage(img, new Rectangle(0, 0, n, n), (img.Width - s) / 2f, (img.Height - s) / 2f, s, s, GraphicsUnit.Pixel, ia);
        }
        return b;
    }
    void SetArt(Bitmap nb) {
        if (artPrev != null) artPrev.Dispose();
        artPrev = artCur; artCur = nb; artX = 0f;
        accT = (c.ArtColor && nb != null) ? G.Accent(nb) : c.Custom;
        Wake();
    }

    void UpdateWant() {
        bool want = c.Mode == 0 || (c.Mode == 1 && hasSession) || (c.Mode == 2 && (DateTime.Now < peekUntil || hot != 0));
        if (DateTime.Now < forceUntil) want = true;
        if (forceHidden) want = false;
        if (pinned) want = true;
        if (want == showT) return;
        showT = want; sp.T = want ? 1f : 0f;
        if (want && !Visible) { Place(); Show(); }
        Wake();
    }
    public void Toggle() {
        if (showT) { forceHidden = true; forceUntil = DateTime.MinValue; }
        else { forceHidden = false; forceUntil = DateTime.Now.AddSeconds(8); }
        UpdateWant();
    }
    public void Reveal() { forceHidden = false; forceUntil = DateTime.Now.AddSeconds(8); peekUntil = DateTime.Now.AddSeconds(8); UpdateWant(); }
    // while the settings window is open the widget stays on screen so changes are visible live
    public void Pin(bool on) { pinned = on; if (on) forceHidden = false; UpdateWant(); Wake(); }
    public void ApplyCfg() {
        ApplyTheme();
        accT = (c.ArtColor && artCur != null) ? G.Accent(artCur) : c.Custom;
        TopMost = c.OnTop;
        Relayout(); UpdateWant();
    }
    public void Cleanup() {
        tm.Stop(); pt.Stop();
        if (artCur != null) artCur.Dispose(); if (artPrev != null) artPrev.Dispose(); if (buf != null) buf.Dispose(); if (pvBuf != null) pvBuf.Dispose();
    }

    // ---------------- layout / placement ----------------
    void BuildLayout() {
        float o = PAD, ax = 22, ay = 22, az = 108, ts = 17f, ta = 13.5f, tt = 10.5f;
        int L = c.Layout; ctr = false; timeOn = true;
        switch (L) {
            case 1:   // compact bar
                W = 380; H = 100; ax = 14; ay = 14; az = 72; tx0 = 98; twid = 166; ty1 = 17; ty2 = 41;
                px0 = 98; pwid = 268; py = 72; barTh = 4; timeY = 79; playR = 17; icoS = .8f; ts = 16f; ta = 12.5f; tt = 10f;
                cPrev = new PointF(o + 282, o + 34); cPlay = new PointF(o + 320, o + 34); cNext = new PointF(o + 358, o + 34);
                break;
            case 2:   // vertical album card
                W = 260; H = 398; ax = 24; ay = 24; az = 212; tx0 = 24; twid = 212; ty1 = 248; ty2 = 273; ctr = true;
                px0 = 24; pwid = 212; py = 308; barTh = 4; timeY = 317; playR = 22f; icoS = 1f; ts = 18f; ta = 14f; tt = 10.5f;
                cPrev = new PointF(o + 58, o + 354); cPlay = new PointF(o + 130, o + 354); cNext = new PointF(o + 202, o + 354);
                break;
            case 3:   // minimal pill
                W = 330; H = 68; ax = 10; ay = 10; az = 48; tx0 = 68; twid = 146; ty1 = 13; ty2 = 36; timeOn = false;
                px0 = 68; pwid = 248; py = 62; barTh = 3; timeY = 0; playR = 15f; icoS = .72f; ts = 15f; ta = 12f; tt = 10f;
                cPrev = new PointF(o + 230, o + 32); cPlay = new PointF(o + 268, o + 32); cNext = new PointF(o + 304, o + 32);
                break;
            default:  // classic
                W = 400; H = 152; ax = 22; ay = 22; az = 108; tx0 = 150; twid = 228; ty1 = 22; ty2 = 47;
                px0 = 150; pwid = 228; py = 80; barTh = 4; timeY = 90; playR = 22f; icoS = 1f; ts = 17f; ta = 13.5f; tt = 10.5f;
                cPrev = new PointF(o + 150 + 228 * 0.2f, o + 116); cPlay = new PointF(o + 150 + 228 * 0.5f, o + 116); cNext = new PointF(o + 150 + 228 * 0.8f, o + 116);
                break;
        }
        tx0 += o; ty1 += o; ty2 += o; px0 += o; py += o; timeY += o;
        FW = W + 2 * PAD; FH = H + 2 * PAD;
        rCard = new RectangleF(o, o, W, H); rArt = new RectangleF(o + ax, o + ay, az, az);
        rProg = new RectangleF(px0 - 8, py - 12, pwid + 16, 24);
        artR = Math.Min(c.Radius * 0.72f, az / 2f);
        if (fTitle != null) fTitle.Dispose(); if (fArtist != null) fArtist.Dispose(); if (fTime != null) fTime.Dispose();
        fTitle = new Font("Segoe UI Semibold", ts, FontStyle.Regular, GraphicsUnit.Pixel);
        fArtist = new Font("Segoe UI", ta, FontStyle.Regular, GraphicsUnit.Pixel);
        fTime = new Font("Segoe UI", tt, FontStyle.Regular, GraphicsUnit.Pixel);
        wT = TW(dT, fTitle); wA = TW(dA, fArtist); woT = TW(oT, fTitle); woA = TW(oA, fArtist);
        wDT = TW(DemoT, fTitle); wDA = TW(DemoA, fArtist);
    }
    void Relayout() {
        using (var g0 = Graphics.FromHwnd(IntPtr.Zero)) dpi = g0.DpiX / 96f;
        K = dpi * c.Scale / 100f;
        BuildLayout();
        int bw = (int)Math.Ceiling(FW * K), bh = (int)Math.Ceiling(FH * K);
        if (buf == null || buf.Width != bw || buf.Height != bh) { if (buf != null) buf.Dispose(); buf = new Bitmap(bw, bh, PixelFormat.Format32bppPArgb); }
        Size = new Size(bw, bh); Place(); Wake();
    }
    static readonly int[] PCol = { 0, 1, 2, 0, 1, 2, -1, 0, 1, 2 };
    static readonly int[] PRow = { 0, 0, 0, 2, 2, 2, -1, 1, 1, 1 };
    void Place() {
        var scr = Screen.PrimaryScreen; var all = Screen.AllScreens;
        if (c.Monitor > 0 && c.Monitor <= all.Length) scr = all[c.Monitor - 1];
        var wa = scr.WorkingArea; int pd = (int)(PAD * K), m = (int)(c.Margin * dpi), x, y;
        if (c.Pos == 6) {
            var vs = SystemInformation.VirtualScreen;
            x = Math.Max(vs.Left - pd, Math.Min(c.X, vs.Right - Width + pd)); y = Math.Max(vs.Top - pd, Math.Min(c.Y, vs.Bottom - Height + pd));
        } else {
            int p = Math.Max(0, Math.Min(9, c.Pos)), col = PCol[p], row = PRow[p];
            x = col == 0 ? wa.Left + m - pd : (col == 1 ? wa.Left + (wa.Width - Width) / 2 : wa.Right - Width + pd - m);
            y = row == 0 ? wa.Top + m - pd : (row == 1 ? wa.Top + (wa.Height - Height) / 2 : wa.Bottom - Height + pd - m);
        }
        Location = new Point(x, y);
    }

    // ---------------- animation loop ----------------
    public void Wake() { keep = 1.6f; if (!tm.Enabled) { last = sw.Elapsed.TotalSeconds; tm.Start(); } }
    void Tick() {
        double now = sw.Elapsed.TotalSeconds; float dt = (float)(now - last);
        if (keep <= 0f && dt < 0.030f) return;             // idle + playing: ~30 fps (saves CPU); interaction: full 60 fps
        last = now; if (dt > 0.05f) dt = 0.05f; if (dt <= 0f) return;
        if (keep > 0f) keep -= dt;
        Step(dt);
        if (Visible) Frame();
        if (!((Visible || PreviewOn) && (keep > 0f || playing || !sp.Rest))) tm.Stop();
    }
    void Step(float dt) {
        time += dt; sp.Step(dt);
        for (int i = 0; i < 3; i++) pr[i].Step(dt);
        for (int i = 0; i < 6; i++) hv[i] = G.Sm(hv[i], (hot == i || (i == 5 && hot != 0)) ? 1f : 0f, dt, 14f);
        mp = G.Sm(mp, playing ? 1f : 0f, dt, 13f);
        artS = G.Sm(artS, playing ? 1f : 0.93f, dt, 7f);
        tx = Math.Min(1f, tx + dt / 0.6f); artX = Math.Min(1f, artX + dt / 0.7f);
        if (artX >= 1f && artPrev != null) { artPrev.Dispose(); artPrev = null; }
        float ka = 1f - (float)Math.Exp(-5f * dt);
        acc = Color.FromArgb(255, (int)Math.Round(acc.R + (accT.R - acc.R) * ka), (int)Math.Round(acc.G + (accT.G - acc.G) * ka), (int)Math.Round(acc.B + (accT.B - acc.B) * ka));
        prog = scrub ? scrubFrac : G.Sm(prog, Frac(), dt, 18f);
        if (!showT && sp.V < 0.004f && Math.Abs(sp.Vel) < 0.05f && Visible) Hide();
    }
    TimeSpan CurPos() {
        var p = posBase; if (playing) p += DateTimeOffset.Now - posStamp;
        if (p < TimeSpan.Zero) p = TimeSpan.Zero; if (hasTL && p > dur) p = dur; return p;
    }
    float Frac() { return hasTL ? G.Cl((float)(CurPos().TotalSeconds / dur.TotalSeconds)) : 0f; }
    static string Fmt(TimeSpan t) { int s = (int)t.TotalSeconds; return (s / 60) + ":" + (s % 60).ToString("00"); }

    // ---------------- drawing ----------------
    void Scene(Graphics g, float k, float v, bool allowDemo) {
        g.SmoothingMode = SmoothingMode.AntiAlias; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.TextRenderingHint = TextRenderingHint.AntiAlias;
        g.ScaleTransform(k, k);
        float cx = PAD + W / 2f, cy = PAD + H / 2f;
        float sc = (0.93f + 0.07f * Math.Min(1.04f, v)) * (1f + 0.012f * hv[5]);
        g.TranslateTransform(cx, cy + (1f - Math.Min(1f, v)) * 20f); g.ScaleTransform(sc, sc); g.TranslateTransform(-cx, -cy);
        Card(g, allowDemo && !hasSession);
    }
    void Frame() {
        if (buf == null || !IsHandleCreated) return;
        using (var g = Graphics.FromImage(buf)) { g.Clear(Color.Transparent); Scene(g, K, Math.Max(0f, sp.V), false); }
        Present();
    }
    // used by the settings window: renders the widget (with sample content if nothing is playing) into a bitmap
    public Bitmap Snap(float k) {
        int bw = (int)Math.Ceiling(FW * k), bh = (int)Math.Ceiling(FH * k);
        if (pvBuf == null || pvBuf.Width != bw || pvBuf.Height != bh) { if (pvBuf != null) pvBuf.Dispose(); pvBuf = new Bitmap(bw, bh, PixelFormat.Format32bppPArgb); }
        using (var g = Graphics.FromImage(pvBuf)) { g.Clear(Color.Transparent); Scene(g, k, 1f, true); }
        return pvBuf;
    }
    public RectangleF SnapSrc(float k) { return new RectangleF((PAD - 16) * k, (PAD - 10) * k, (W + 32) * k, (H + 34) * k); }
    void Present() {
        float av = G.Ease(Math.Max(0f, Math.Min(1f, sp.V)));
        IntPtr sdc = GetDC(IntPtr.Zero), mdc = CreateCompatibleDC(sdc), hb = buf.GetHbitmap(Color.FromArgb(0)), old = SelectObject(mdc, hb);
        var sz = new Sz { W = buf.Width, H = buf.Height }; var ps = new Pt(); var pd = new Pt { X = Left, Y = Top };
        var bf = new Bf { Op = 0, Flags = 0, Alpha = (byte)(255f * av * c.Opacity / 100f), Fmt = 1 };
        UpdateLayeredWindow(Handle, sdc, ref pd, ref sz, mdc, ref ps, 0, ref bf, 2);
        SelectObject(mdc, old); DeleteObject(hb); DeleteDC(mdc); ReleaseDC(IntPtr.Zero, sdc);
    }

    void Card(Graphics g, bool dm) {
        float R = c.Radius;
        if (c.Shadow)
            for (int j = 7; j >= 1; j--)
                using (var p = G.RR(new RectangleF(rCard.X, rCard.Y + 8, rCard.Width, rCard.Height), R))
                using (var pen = new Pen(Color.FromArgb(light ? 6 : 9, 0, 0, 0), j * 3.2f) { LineJoin = LineJoin.Round }) g.DrawPath(pen, p);
        float tint = c.Tint / 100f * (themeId == 2 ? 0.6f : 1f);
        using (var path = G.RR(rCard, R)) {
            Color c0 = G.Mix(tBg, acc, tint), c1 = light ? G.Mix(tBg, acc, tint * .35f) : G.Mix(tBg, Color.Black, .3f);
            using (var br = new LinearGradientBrush(rCard, c0, c1, 50f)) g.FillPath(br, path);
            // everything below is clipped to the card, so nothing can ever bleed outside the container
            var st = g.Save(); g.SetClip(path, CombineMode.Intersect);
            if (c.Glow) {
                float ga = 0.55f + 0.45f * (dm ? 1f : mp);
                using (var gp = new GraphicsPath()) {
                    gp.AddEllipse(rArt.X - rArt.Width * .75f, rArt.Y - rArt.Height * .65f, rArt.Width * 2.5f, rArt.Height * 2.3f);
                    using (var pg = new PathGradientBrush(gp)) { pg.CenterColor = G.A((int)((light ? 55 : 80) * ga), acc); pg.SurroundColors = new Color[] { G.A(0, acc) }; g.FillPath(pg, gp); }
                }
            }
            using (var gl = new LinearGradientBrush(new RectangleF(rCard.X, rCard.Y, rCard.Width, 60), Color.FromArgb(light ? 70 : 16, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                g.FillRectangle(gl, rCard.X, rCard.Y, rCard.Width, 60);
            Body(g, dm);
            g.Restore(st);
            using (var pen = new Pen(G.A((int)((light ? 26 : 34) + (light ? 16 : 26) * hv[5]), tInk), 1.2f)) g.DrawPath(pen, path);
        }
    }

    void Body(Graphics g, bool dm) {
        // album art (breathes with play/pause, cross-fades on track change)
        float s2 = (dm ? 1f : artS) + 0.02f * hv[5];
        var ra = new RectangleF(rArt.X + rArt.Width * (1 - s2) / 2f, rArt.Y + rArt.Height * (1 - s2) / 2f, rArt.Width * s2, rArt.Height * s2);
        if (c.Shadow)
            for (int j = 5; j >= 1; j--)
                using (var p = G.RR(new RectangleF(ra.X, ra.Y + 5, ra.Width, ra.Height), artR))
                using (var pen = new Pen(Color.FromArgb(light ? 10 : 14, 0, 0, 0), j * 2.4f)) g.DrawPath(pen, p);
        using (var ap = G.RR(ra, artR)) {
            var st2 = g.Save(); g.SetClip(ap, CombineMode.Intersect);
            float ae = G.Ease(artX);
            if (artCur == null) { Placeholder(g, ra); if (artPrev != null && ae < 1f) Img(g, artPrev, ra, 1f - ae); }
            else {
                if (artPrev != null && ae < 1f) Img(g, artPrev, ra, 1f); else Placeholder(g, ra);
                Img(g, artCur, ra, ae);
            }
            g.Restore(st2);
            using (var pen = new Pen(G.A(light ? 30 : 40, tInk), 1f)) g.DrawPath(pen, ap);
        }

        // title + artist (slide/fade on change, marquee when too long)
        Color cA = G.Mix(tSub, acc, light ? .15f : .28f);
        float e = dm ? 1f : G.Ease(tx), dxo = 24f;
        if (!dm && e < 1f) {
            Txt(g, oT, woT, fTitle, tTx, ty1, 1f - e, -txDir * dxo * e, false);
            Txt(g, oA, woA, fArtist, cA, ty2, 1f - e, -txDir * dxo * e, false);
        }
        Txt(g, dm ? DemoT : dT, dm ? wDT : wT, fTitle, tTx, ty1, e, txDir * dxo * (1f - e), e >= 1f);
        Txt(g, dm ? DemoA : dA, dm ? wDA : wA, fArtist, cA, ty2, e, txDir * dxo * (1f - e), e >= 1f);

        // progress bar + times
        if (c.ShowProgress) {
            bool tl = dm || hasTL; float pg = dm ? 0.38f : prog;
            float th = barTh + 3f * hv[4], ty = py - th / 2f;
            using (var tp = G.RR(new RectangleF(px0, ty, pwid, th), th / 2f))
            using (var tb = new SolidBrush(G.A(light ? 40 : 46, tInk))) g.FillPath(tb, tp);
            if (tl) {
                float fw = Math.Max(th, pwid * pg);
                using (var fp = G.RR(new RectangleF(px0, ty, fw, th), th / 2f))
                using (var fb = new SolidBrush(light ? G.Mix(acc, Color.Black, .12f) : G.Mix(acc, Color.White, .2f))) g.FillPath(fb, fp);
                float kr = 6f * hv[4];
                if (kr > .3f) using (var kb = new SolidBrush(tTx)) g.FillEllipse(kb, px0 + pwid * pg - kr, py - kr, kr * 2, kr * 2);
                if (c.ShowTimes && timeOn) {
                    string ls = dm ? "1:24" : Fmt(scrub ? TimeSpan.FromSeconds(dur.TotalSeconds * scrubFrac) : CurPos());
                    string rs = dm ? "3:41" : Fmt(dur);
                    using (var tbr = new SolidBrush(G.A(150, tTx))) {
                        g.DrawString(ls, fTime, tbr, px0, timeY, sf);
                        float rw = g.MeasureString(rs, fTime, 200, sf).Width;
                        g.DrawString(rs, fTime, tbr, px0 + pwid - rw, timeY, sf);
                    }
                }
            }
        }

        // controls
        Ico(g, cPrev, false, .8f + .2f * hv[1], pr[0].V * (1f + .12f * hv[1]) * icoS);
        Ico(g, cNext, true, .8f + .2f * hv[3], pr[2].V * (1f + .12f * hv[3]) * icoS);
        float pscale = pr[1].V * (1f + .08f * hv[2]) * playR / 22f;
        var stp = g.Save(); g.TranslateTransform(cPlay.X, cPlay.Y); g.ScaleTransform(pscale, pscale);
        if (hv[2] > .01f) using (var hb = new SolidBrush(G.A((int)(70 * hv[2]), acc))) g.FillEllipse(hb, -27, -27, 54, 54);
        using (var wb = new SolidBrush(tPlay)) g.FillEllipse(wb, -22, -22, 44, 44);
        Morph(g, dm ? 1f : mp);
        g.Restore(stp);
    }
    void Txt(Graphics g, string s, float sw_, Font f, Color col, float y, float a, float dx, bool marq) {
        if (a <= 0.004f || s.Length == 0) return;
        bool over = sw_ > twid; float off = 0f;
        var st = g.Save(); g.SetClip(new RectangleF(tx0 - 2, y - 3, twid + 4, f.Height + 8), CombineMode.Intersect);
        using (var b = new SolidBrush(G.A((int)(255 * a), col))) {
            if (over && !c.Marquee) g.DrawString(s, f, b, new RectangleF(tx0 + dx, y, twid, f.Height + 2), sfE);
            else {
                if (over && marq) {
                    float ov = sw_ - twid, per = 2.5f + ov / 45f; float u = (float)((time % (per * 2f)) / per); float tri = u < 1f ? u : 2f - u;
                    off = -ov * G.Ease((tri - .15f) / .7f);
                } else if (!over && ctr) off = (twid - sw_) / 2f;
                g.DrawString(s, f, b, tx0 + off + dx, y, sf);
            }
        }
        g.Restore(st);
    }
    void Img(Graphics g, Bitmap b, RectangleF r, float a) {
        var pts = new PointF[] { new PointF(r.Left, r.Top), new PointF(r.Right, r.Top), new PointF(r.Left, r.Bottom) };
        using (var ia = new ImageAttributes()) {
            ia.SetWrapMode(WrapMode.TileFlipXY);
            if (a < 0.999f) ia.SetColorMatrix(new ColorMatrix { Matrix33 = a });
            g.DrawImage(b, pts, new Rectangle(0, 0, b.Width, b.Height), GraphicsUnit.Pixel, ia);
        }
    }
    void Placeholder(Graphics g, RectangleF r) {
        using (var br = new LinearGradientBrush(r, G.Mix(acc, Color.Black, .35f), G.Mix(acc, Color.Black, .7f), 45f)) g.FillRectangle(br, r);
        using (var f = new Font("Segoe UI Symbol", r.Width * 0.39f, FontStyle.Regular, GraphicsUnit.Pixel))
        using (var b = new SolidBrush(Color.FromArgb(190, 255, 255, 255)))
        using (var f2 = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center }) g.DrawString("\u266B", f, b, r, f2);
    }
    void Ico(Graphics g, PointF c0, bool next, float a, float s) {
        var st3 = g.Save(); g.TranslateTransform(c0.X, c0.Y); g.ScaleTransform(next ? s : -s, s);
        using (var b = new SolidBrush(G.A((int)(255 * G.Cl(a)), tTx))) {
            g.FillPolygon(b, new PointF[] { new PointF(-8f, -8.5f), new PointF(6f, 0f), new PointF(-8f, 8.5f) });
            g.FillRectangle(b, 6.5f, -8.5f, 3f, 17f);
        }
        g.Restore(st3);
    }
    // play <-> pause icon that morphs smoothly (m: 0 = play, 1 = pause)
    void Morph(Graphics g, float m) {
        float[][] pl = { new[] { 0f, 0f, .52f, .26f, .52f, .74f, 0f, 1f }, new[] { .48f, .26f, 1f, .5f, 1f, .5f, .48f, .74f } };
        float[][] pa = { new[] { 0f, 0f, .36f, 0f, .36f, 1f, 0f, 1f }, new[] { .64f, 0f, 1f, 0f, 1f, 1f, .64f, 1f } };
        float z = 17f, ox = -z / 2f + 1.6f * (1f - m), oy = -z / 2f;
        using (var b = new SolidBrush(tPlayIc))
            for (int q = 0; q < 2; q++) {
                var pts = new PointF[4];
                for (int i = 0; i < 4; i++)
                    pts[i] = new PointF(ox + z * (pl[q][i * 2] + (pa[q][i * 2] - pl[q][i * 2]) * m), oy + z * (pl[q][i * 2 + 1] + (pa[q][i * 2 + 1] - pl[q][i * 2 + 1]) * m));
                g.FillPolygon(b, pts);
            }
    }

    // ---------------- mouse ----------------
    PointF LP(MouseEventArgs e) { return new PointF(e.X / K, e.Y / K); }
    static float Dist(PointF a, PointF b) { float dx = a.X - b.X, dy = a.Y - b.Y; return (float)Math.Sqrt(dx * dx + dy * dy); }
    int Hit(PointF p) {
        float ih = Math.Max(14f, 20f * icoS);
        if (Dist(p, cPlay) <= playR + 4f) return 2;
        if (Dist(p, cPrev) <= ih) return 1;
        if (Dist(p, cNext) <= ih) return 3;
        if (c.ShowProgress && hasTL && rProg.Contains(p)) return 4;
        return rCard.Contains(p) ? 5 : 0;
    }
    protected override void OnMouseMove(MouseEventArgs e) {
        base.OnMouseMove(e);
        if (dragging) { Location = new Point(Left + e.X - dragOff.X, Top + e.Y - dragOff.Y); return; }
        var p = LP(e);
        if (scrub) { scrubFrac = G.Cl((p.X - px0) / pwid); Wake(); return; }
        int h = Hit(p);
        if (h != hot) { hot = h; Cursor = (h >= 1 && h <= 4) ? Cursors.Hand : Cursors.Default; UpdateWant(); Wake(); }
    }
    protected override void OnMouseDown(MouseEventArgs e) {
        base.OnMouseDown(e);
        var p = LP(e); int h = Hit(p);
        if (e.Button == MouseButtons.Right) { if (Ctx != null) { SetForegroundWindow(Handle); Ctx.Show(Cursor.Position); } return; }
        if (e.Button != MouseButtons.Left) return;
        if (h >= 1 && h <= 3) { pr[h - 1].V = 0.84f; Cmd(h); Wake(); }
        else if (h == 4) { scrub = true; scrubFrac = G.Cl((p.X - px0) / pwid); prog = scrubFrac; Wake(); }
        else if (h == 5) { dragging = true; dragOff = e.Location; }
    }
    protected override void OnMouseUp(MouseEventArgs e) {
        base.OnMouseUp(e);
        if (scrub) { scrub = false; Seek(scrubFrac); }
        if (dragging) { dragging = false; c.Pos = 6; c.X = Left; c.Y = Top; c.Save(); if (CfgChanged != null) CfgChanged(); }
    }
    protected override void OnMouseLeave(EventArgs e) {
        base.OnMouseLeave(e);
        hot = 0; if (c.Mode == 2) peekUntil = DateTime.Now.AddSeconds(1.5); Wake();
    }
    void Cmd(int k) {
        var s = cur; if (s == null) return;
        try {
            if (k == 1) { txDir = -1f; s.TrySkipPreviousAsync(); }
            else if (k == 3) { txDir = 1f; s.TrySkipNextAsync(); }
            else { s.TryTogglePlayPauseAsync(); posBase = CurPos(); posStamp = DateTimeOffset.Now; playing = !playing; }
        } catch { }
    }
    void Seek(float f) {
        var s = cur; if (s == null || !hasTL) return;
        posBase = TimeSpan.FromTicks((long)(dur.Ticks * f)); posStamp = DateTimeOffset.Now;
        try { s.TryChangePlaybackPositionAsync(posBase.Ticks); } catch { }
    }
}

// =====================================================================================
//  Settings UI - custom drawn controls (dark, DPI aware, all coordinates are logical px)
// =====================================================================================
static class P {
    public static float U = 1f;
    public static readonly Color Bg = Color.FromArgb(17, 18, 24), Side = Color.FromArgb(12, 13, 18), Card = Color.FromArgb(27, 28, 38),
        Card2 = Color.FromArgb(36, 37, 50), Line = Color.FromArgb(40, 42, 56), Tx = Color.FromArgb(238, 240, 247),
        Mu = Color.FromArgb(138, 144, 166), Ac = Color.FromArgb(122, 124, 255), Ac2 = Color.FromArgb(78, 160, 255);
    public static StringFormat SF = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.NoWrap };
    static Dictionary<string, Font> fc = new Dictionary<string, Font>();
    public static void Init() { using (var g = Graphics.FromHwnd(IntPtr.Zero)) U = g.DpiX / 96f; }
    public static int S(float v) { return (int)Math.Round(v * U); }
    public static Font F(float px, bool semi) {
        string k = px.ToString() + (semi ? "s" : "r"); Font f;
        if (!fc.TryGetValue(k, out f)) { f = new Font(semi ? "Segoe UI Semibold" : "Segoe UI", px, FontStyle.Regular, GraphicsUnit.Pixel); fc[k] = f; }
        return f;
    }
    public static void Ctr(Graphics g, string s, Font f, Color col, RectangleF r) {
        SizeF m = g.MeasureString(s, f, 4000, SF);
        using (var b = new SolidBrush(col)) g.DrawString(s, f, b, r.X + (r.Width - m.Width) / 2f, r.Y + (r.Height - f.Height) / 2f + 0.5f, SF);
    }
}

class Ctl : Control {
    protected bool hov;
    public Ctl() {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = P.Bg; Cursor = Cursors.Hand;
    }
    protected float LW { get { return Width / P.U; } }
    protected float LH { get { return Height / P.U; } }
    protected float MX(MouseEventArgs e) { return e.X / P.U; }
    protected float MY(MouseEventArgs e) { return e.Y / P.U; }
    protected Graphics Prep(PaintEventArgs e) {
        var g = e.Graphics; g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.AntiAlias; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.ScaleTransform(P.U, P.U); return g;
    }
    protected void Fire(EventHandler h) { if (h != null) h(this, EventArgs.Empty); }
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hov = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hov = false; Invalidate(); }
}

class Pg : Panel { public Pg() { DoubleBuffered = true; } }

class Lab : Ctl {
    string t; float px; bool semi; Color col;
    public Lab(string text, float px, bool semi, Color col) { t = text; this.px = px; this.semi = semi; this.col = col; Cursor = Cursors.Default; Fit(); }
    public string T { get { return t; } set { t = value; Fit(); Invalidate(); } }
    void Fit() {
        using (var g = Graphics.FromHwnd(IntPtr.Zero)) {
            var f = P.F(px, semi); SizeF s = g.MeasureString(t, f, 4000, P.SF);
            Size = new Size(P.S(s.Width + 6f), P.S(f.Height + 2f));
        }
    }
    protected override void OnPaint(PaintEventArgs e) {
        var g = Prep(e);
        using (var b = new SolidBrush(col)) g.DrawString(t, P.F(px, semi), b, 0f, 1f, P.SF);
    }
}

class Tog : Ctl {
    bool on; float t; System.Windows.Forms.Timer tm; public event EventHandler Changed;
    public Tog() {
        Size = new Size(P.S(44), P.S(24));
        tm = new System.Windows.Forms.Timer { Interval = 15 };
        tm.Tick += (s, e) => { float tg = on ? 1f : 0f; t += (tg - t) * 0.38f; if (Math.Abs(tg - t) < 0.02f) { t = tg; tm.Stop(); } Invalidate(); };
    }
    public bool Value { get { return on; } set { on = value; t = value ? 1f : 0f; Invalidate(); } }
    protected override void OnClick(EventArgs e) { base.OnClick(e); on = !on; tm.Start(); Fire(Changed); }
    protected override void Dispose(bool d) { if (d && tm != null) tm.Dispose(); base.Dispose(d); }
    protected override void OnPaint(PaintEventArgs e) {
        var g = Prep(e);
        Color off = Color.FromArgb(hov ? 70 : 60, hov ? 74 : 63, hov ? 94 : 82);
        using (var p = G.RR(new RectangleF(0, 0, 44, 24), 12f)) using (var br = new SolidBrush(G.Mix(off, P.Ac, t))) g.FillPath(br, p);
        using (var b = new SolidBrush(Color.FromArgb(40, 0, 0, 0))) g.FillEllipse(b, 3f + 20f * t, 4f, 18f, 18f);
        using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, 3f + 20f * t, 3f, 18f, 18f);
    }
}

class Seg : Ctl {
    string[] it; int idx, hi = -1; public event EventHandler Changed;
    public Seg(string[] items, float w) { it = items; Size = new Size(P.S(w), P.S(32)); }
    public int Value { get { return idx; } set { idx = value; Invalidate(); } }
    int At(float x) { int n = (int)(x / (LW / it.Length)); return Math.Max(0, Math.Min(it.Length - 1, n)); }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); int h = At(MX(e)); if (h != hi) { hi = h; Invalidate(); } }
    protected override void OnMouseLeave(EventArgs e) { hi = -1; base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        int n = At(MX(e)); if (n != idx) { idx = n; Invalidate(); Fire(Changed); }
    }
    protected override void OnPaint(PaintEventArgs e) {
        var g = Prep(e); float w = LW, h = LH, sw = w / it.Length;
        using (var p = G.RR(new RectangleF(0, 0, w, h), 10f)) using (var b = new SolidBrush(P.Card2)) g.FillPath(b, p);
        var f = P.F(12.5f, true);
        for (int i = 0; i < it.Length; i++) {
            var r = new RectangleF(i * sw, 0, sw, h); bool sel = i == idx;
            if (sel) {
                var pr = new RectangleF(r.X + 3, 3, r.Width - 6, h - 6);
                using (var p = G.RR(pr, 8f)) using (var b = new LinearGradientBrush(pr, P.Ac, P.Ac2, 0f)) g.FillPath(b, p);
            }
            P.Ctr(g, it[i], f, sel ? Color.White : (i == hi ? P.Tx : P.Mu), r);
        }
    }
}

class Slide : Ctl {
    int mn, mx, v; string unit; bool drag; public event EventHandler Changed;
    public Slide(int mn, int mx, string unit, float w) { this.mn = mn; this.mx = mx; this.unit = unit; v = mn; Size = new Size(P.S(w), P.S(28)); }
    public int Value { get { return v; } set { v = value; Invalidate(); } }
    float Trk { get { return LW - 64f; } }
    void SetFrom(float x) {
        float f = Math.Max(0f, Math.Min(1f, (x - 8f) / (Trk - 16f)));
        int nv = mn + (int)Math.Round(f * (mx - mn));
        if (nv != v) { v = nv; Invalidate(); Fire(Changed); }
    }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { drag = true; SetFrom(MX(e)); } }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (drag) SetFrom(MX(e)); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); drag = false; Invalidate(); }
    protected override void OnPaint(PaintEventArgs e) {
        var g = Prep(e); float f = (v - mn) / (float)(mx - mn), tw = Trk - 16f, kx = 8f + tw * f, cy = LH / 2f;
        using (var p = G.RR(new RectangleF(8f, cy - 2f, tw, 4f), 2f)) using (var b = new SolidBrush(Color.FromArgb(52, 55, 72))) g.FillPath(b, p);
        if (kx > 9f)
            using (var p = G.RR(new RectangleF(8f, cy - 2f, kx - 8f, 4f), 2f))
            using (var b = new LinearGradientBrush(new RectangleF(8f, 0f, Math.Max(1f, tw), 1f), P.Ac, P.Ac2, 0f)) g.FillPath(b, p);
        if (hov || drag) using (var b = new SolidBrush(G.A(50, P.Ac))) g.FillEllipse(b, kx - 12f, cy - 12f, 24f, 24f);
        using (var b = new SolidBrush(Color.FromArgb(50, 0, 0, 0))) g.FillEllipse(b, kx - 7.5f, cy - 6.5f, 15f, 15f);
        using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, kx - 7.5f, cy - 7.5f, 15f, 15f);
        string s = v + unit; var ft = P.F(12.5f, true); SizeF m = g.MeasureString(s, ft, 200, P.SF);
        using (var b = new SolidBrush(P.Tx)) g.DrawString(s, ft, b, LW - m.Width, cy - ft.Height / 2f + 0.5f, P.SF);
    }
}

class Swatches : Ctl {
    static readonly Color[] Pre = { Color.FromArgb(140, 170, 255), Color.FromArgb(255, 120, 150), Color.FromArgb(255, 170, 90), Color.FromArgb(255, 215, 90),
        Color.FromArgb(110, 220, 150), Color.FromArgb(90, 220, 225), Color.FromArgb(175, 130, 255), Color.FromArgb(240, 240, 245) };
    Color val = Pre[0]; public event EventHandler Changed;
    public Swatches() { Size = new Size(P.S(9 * 34), P.S(34)); }
    public Color Value { get { return val; } set { val = value; Invalidate(); } }
    static bool Same(Color a, Color b) { return a.R == b.R && a.G == b.G && a.B == b.B; }
    protected override void OnMouseDown(MouseEventArgs e) {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        int i = (int)(MX(e) / 34f);
        if (i >= 0 && i < 8) { val = Pre[i]; Invalidate(); Fire(Changed); }
        else if (i == 8) {
            using (var d = new ColorDialog { Color = val, FullOpen = true, AnyColor = true })
                if (d.ShowDialog(FindForm()) == DialogResult.OK) { val = Color.FromArgb(255, d.Color.R, d.Color.G, d.Color.B); Invalidate(); Fire(Changed); }
        }
    }
    protected override void OnPaint(PaintEventArgs e) {
        var g = Prep(e); bool isPre = false;
        for (int i = 0; i < 8; i++) {
            float x = i * 34f + 4f, y = 4f; bool sel = Same(val, Pre[i]); isPre |= sel;
            using (var b = new SolidBrush(Pre[i])) g.FillEllipse(b, x, y, 26f, 26f);
            if (sel) using (var pen = new Pen(P.Tx, 2f)) g.DrawEllipse(pen, x - 3f, y - 3f, 32f, 32f);
        }
        float cx = 8 * 34f + 4f;
        if (!isPre) {
            using (var b = new SolidBrush(val)) g.FillEllipse(b, cx, 4f, 26f, 26f);
            using (var pen = new Pen(P.Tx, 2f)) g.DrawEllipse(pen, cx - 3f, 1f, 32f, 32f);
        } else {
            using (var b = new SolidBrush(hov ? P.Card2 : P.Card)) g.FillEllipse(b, cx, 4f, 26f, 26f);
            using (var pen = new Pen(P.Mu, 1.2f) { DashStyle = DashStyle.Dot }) g.DrawEllipse(pen, cx, 4f, 26f, 26f);
            P.Ctr(g, "+", P.F(16f, true), P.Mu, new RectangleF(cx, 4f, 26f, 25f));
        }
    }
}

class PosGrid : Ctl {
    static readonly int[] Map = { 0, 1, 2, 7, 8, 9, 3, 4, 5 };
    int val, hi = -1; public event EventHandler Changed;
    public PosGrid() { Size = new Size(P.S(104), P.S(80)); }
    public int Value { get { return val; } set { val = value; Invalidate(); } }
    int At(MouseEventArgs e) {
        float x = MX(e) - 4f, y = MY(e) - 4f; if (x < 0 || y < 0) return -1;
        int cx = (int)(x / 34f), cy = (int)(y / 26f);
        if (cx > 2 || cy > 2 || x - cx * 34f > 28f || y - cy * 26f > 20f) return -1;
        return cy * 3 + cx;
    }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); int h = At(e); if (h != hi) { hi = h; Invalidate(); } }
    protected override void OnMouseLeave(EventArgs e) { hi = -1; base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) {
        base.OnMouseDown(e);
        int i = At(e); if (i >= 0 && e.Button == MouseButtons.Left) { val = Map[i]; Invalidate(); Fire(Changed); }
    }
    protected override void OnPaint(PaintEventArgs e) {
        var g = Prep(e);
        using (var p = G.RR(new RectangleF(0.5f, 0.5f, 103f, 79f), 9f)) using (var pen = new Pen(P.Line, 1.4f)) g.DrawPath(pen, p);
        for (int i = 0; i < 9; i++) {
            var r = new RectangleF(4f + (i % 3) * 34f, 4f + (i / 3) * 26f, 28f, 20f); bool sel = Map[i] == val;
            using (var p = G.RR(r, 5f)) {
                if (sel) using (var b = new LinearGradientBrush(r, P.Ac, P.Ac2, 45f)) g.FillPath(b, p);
                else using (var b = new SolidBrush(i == hi ? Color.FromArgb(56, 58, 76) : P.Card2)) g.FillPath(b, p);
            }
        }
    }
}

class LayoutPicker : Ctl {
    const float CW = 156f, CH = 100f, GAP = 12f;
    static readonly string[] Names = { "Classic", "Compact", "Vertical", "Minimal" };
    int idx, hi = -1; public event EventHandler Changed;
    public LayoutPicker() { Size = new Size(P.S(4 * CW + 3 * GAP), P.S(CH)); }
    public int Value { get { return idx; } set { idx = value; Invalidate(); } }
    int At(float x) { int i = (int)(x / (CW + GAP)); if (i < 0 || i > 3 || x - i * (CW + GAP) > CW) return -1; return i; }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); int h = At(MX(e)); if (h != hi) { hi = h; Invalidate(); } }
    protected override void OnMouseLeave(EventArgs e) { hi = -1; base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) {
        base.OnMouseDown(e);
        int i = At(MX(e)); if (i >= 0 && i != idx && e.Button == MouseButtons.Left) { idx = i; Invalidate(); Fire(Changed); }
    }
    protected override void OnPaint(PaintEventArgs e) {
        var g = Prep(e);
        for (int i = 0; i < 4; i++) {
            var r = new RectangleF(i * (CW + GAP) + 0.75f, 0.75f, CW - 1.5f, CH - 1.5f); bool sel = i == idx;
            using (var p = G.RR(r, 12f)) {
                using (var b = new SolidBrush(sel ? G.Mix(P.Card2, P.Ac, .14f) : (i == hi ? Color.FromArgb(42, 44, 60) : P.Card))) g.FillPath(b, p);
                using (var pen = new Pen(sel ? P.Ac : P.Line, sel ? 1.8f : 1f)) g.DrawPath(pen, p);
            }
            Color wc = sel ? P.Tx : P.Mu;
            Wire(g, i, r.X + r.Width / 2f, 38f, wc);
            P.Ctr(g, Names[i], P.F(12.5f, true), sel ? P.Tx : P.Mu, new RectangleF(r.X, CH - 28f, r.Width, 20f));
        }
    }
    static void Wire(Graphics g, int L, float cx, float cy, Color col) {
        using (var pen = new Pen(G.A(200, col), 1.4f)) using (var fill = new SolidBrush(G.A(70, col))) using (var ln = new SolidBrush(G.A(200, col))) {
            float bx, by;
            switch (L) {
                case 0:
                    bx = cx - 42f; by = cy - 19f;
                    using (var p = G.RR(new RectangleF(bx, by, 84f, 38f), 8f)) g.DrawPath(pen, p);
                    using (var p = G.RR(new RectangleF(bx + 6f, by + 6f, 26f, 26f), 5f)) g.FillPath(fill, p);
                    g.FillRectangle(ln, bx + 40f, by + 8f, 34f, 4f); g.FillRectangle(fill, bx + 40f, by + 15f, 22f, 3f); g.FillRectangle(fill, bx + 40f, by + 22f, 38f, 2f);
                    g.FillEllipse(ln, bx + 48f, by + 28f, 4f, 4f); g.FillEllipse(ln, bx + 58f, by + 28f, 4f, 4f); g.FillEllipse(ln, bx + 68f, by + 28f, 4f, 4f);
                    break;
                case 1:
                    bx = cx - 42f; by = cy - 13f;
                    using (var p = G.RR(new RectangleF(bx, by, 84f, 26f), 7f)) g.DrawPath(pen, p);
                    using (var p = G.RR(new RectangleF(bx + 5f, by + 5f, 16f, 16f), 4f)) g.FillPath(fill, p);
                    g.FillRectangle(ln, bx + 27f, by + 6f, 24f, 3.5f); g.FillRectangle(fill, bx + 27f, by + 12f, 16f, 3f); g.FillRectangle(fill, bx + 27f, by + 19f, 52f, 2f);
                    g.FillEllipse(ln, bx + 58f, by + 7f, 4f, 4f); g.FillEllipse(ln, bx + 65f, by + 7f, 4f, 4f); g.FillEllipse(ln, bx + 72f, by + 7f, 4f, 4f);
                    break;
                case 2:
                    bx = cx - 22f; by = cy - 33f;
                    using (var p = G.RR(new RectangleF(bx, by, 44f, 66f), 8f)) g.DrawPath(pen, p);
                    using (var p = G.RR(new RectangleF(bx + 5f, by + 5f, 34f, 34f), 6f)) g.FillPath(fill, p);
                    g.FillRectangle(ln, bx + 12f, by + 44f, 20f, 3f); g.FillRectangle(fill, bx + 15f, by + 50f, 14f, 2.5f); g.FillRectangle(fill, bx + 5f, by + 56f, 34f, 2f);
                    g.FillEllipse(ln, bx + 12f, by + 60f, 3.2f, 3.2f); g.FillEllipse(ln, bx + 20.4f, by + 60f, 3.2f, 3.2f); g.FillEllipse(ln, bx + 28.8f, by + 60f, 3.2f, 3.2f);
                    break;
                default:
                    bx = cx - 42f; by = cy - 11f;
                    using (var p = G.RR(new RectangleF(bx, by, 84f, 22f), 11f)) g.DrawPath(pen, p);
                    using (var p = G.RR(new RectangleF(bx + 4f, by + 4f, 14f, 14f), 7f)) g.FillPath(fill, p);
                    g.FillRectangle(ln, bx + 23f, by + 5f, 28f, 3f); g.FillRectangle(fill, bx + 23f, by + 11f, 18f, 2.5f);
                    g.FillEllipse(ln, bx + 58f, by + 9f, 4f, 4f); g.FillEllipse(ln, bx + 65f, by + 9f, 4f, 4f); g.FillEllipse(ln, bx + 72f, by + 9f, 4f, 4f);
                    break;
            }
        }
    }
}

class Btn : Ctl {
    bool prim, down;
    public Btn(string text, bool primary, float w) { Text = text; prim = primary; Size = new Size(P.S(w), P.S(34)); }
    protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnPaint(PaintEventArgs e) {
        var g = Prep(e); var r = new RectangleF(0.75f, 0.75f, LW - 1.5f, LH - 1.5f);
        using (var p = G.RR(r, 9f)) {
            if (prim) {
                using (var b = new LinearGradientBrush(r, hov ? G.Mix(P.Ac, Color.White, .12f) : P.Ac, hov ? G.Mix(P.Ac2, Color.White, .12f) : P.Ac2, 0f)) g.FillPath(b, p);
            } else {
                using (var b = new SolidBrush(down ? Color.FromArgb(30, 31, 42) : (hov ? Color.FromArgb(46, 48, 64) : P.Card2))) g.FillPath(b, p);
                using (var pen = new Pen(P.Line, 1f)) g.DrawPath(pen, p);
            }
        }
        P.Ctr(g, Text, P.F(12.5f, true), prim ? Color.White : P.Tx, new RectangleF(0, 0, LW, LH));
    }
}

class NavBtn : Ctl {
    int kind; public bool Sel;
    public NavBtn(string text, int kind) { Text = text; this.kind = kind; Size = new Size(P.S(176), P.S(42)); BackColor = P.Side; }
    protected override void OnPaint(PaintEventArgs e) {
        var g = Prep(e); float w = LW, h = LH;
        Color fg = Sel ? P.Tx : (hov ? P.Tx : P.Mu), ic = Sel ? P.Ac : fg;
        if (Sel || hov) {
            using (var p = G.RR(new RectangleF(0, 0, w, h), 10f)) using (var b = new SolidBrush(Sel ? G.Mix(P.Side, P.Ac, .16f) : Color.FromArgb(22, 24, 32))) g.FillPath(b, p);
        }
        if (Sel) using (var p = G.RR(new RectangleF(0, 10f, 3.5f, h - 20f), 2f)) using (var b = new SolidBrush(P.Ac)) g.FillPath(b, p);
        float x = 18f, y = h / 2f - 8f;
        using (var pen = new Pen(ic, 1.6f)) using (var br = new SolidBrush(ic)) {
            switch (kind) {
                case 0:
                    for (int i = 0; i < 4; i++) using (var p = G.RR(new RectangleF(x + (i % 2) * 9.6f, y + (i / 2) * 9.6f, 6.4f, 6.4f), 1.8f)) g.FillPath(br, p);
                    break;
                case 1:
                    g.DrawEllipse(pen, x + .8f, y + .8f, 14.4f, 14.4f); g.FillPie(br, x + .8f, y + .8f, 14.4f, 14.4f, 90, 180);
                    break;
                case 2:
                    using (var p = G.RR(new RectangleF(x, y + 1f, 16f, 11f), 2.5f)) g.DrawPath(pen, p);
                    g.DrawLine(pen, x + 5f, y + 15f, x + 11f, y + 15f);
                    break;
                default:
                    float[] kx = { 4f, 11f, 7f };
                    for (int i = 0; i < 3; i++) {
                        float ly = y + 2.5f + i * 5.5f; g.DrawLine(pen, x, ly, x + 16f, ly);
                        using (var b2 = new SolidBrush(Sel ? G.Mix(P.Side, P.Ac, .16f) : P.Side)) g.FillEllipse(b2, x + kx[i] - 3.2f, ly - 3.2f, 6.4f, 6.4f);
                        g.DrawEllipse(pen, x + kx[i] - 2.6f, ly - 2.6f, 5.2f, 5.2f);
                    }
                    break;
            }
        }
        using (var b = new SolidBrush(fg)) g.DrawString(Text, P.F(13.5f, Sel), b, 48f, h / 2f - P.F(13.5f, Sel).Height / 2f + 0.5f, P.SF);
    }
}

class Logo : Ctl {
    public Logo() { Size = new Size(P.S(172), P.S(48)); BackColor = P.Side; Cursor = Cursors.Default; }
    protected override void OnPaint(PaintEventArgs e) {
        var g = Prep(e); var r = new RectangleF(0, 4, 40, 40);
        using (var p = G.RR(r, 11f)) using (var b = new LinearGradientBrush(r, Color.FromArgb(150, 110, 255), Color.FromArgb(70, 150, 255), 45f)) g.FillPath(b, p);
        using (var f = new Font("Segoe UI Symbol", 24f, FontStyle.Regular, GraphicsUnit.Pixel)) P.Ctr(g, "\u266B", f, Color.White, new RectangleF(0, 4, 40, 40));
        using (var b = new SolidBrush(P.Tx)) g.DrawString("JXT MUSIC", P.F(14f, true), b, 50f, 7f, P.SF);
        using (var b = new SolidBrush(P.Tx)) g.DrawString("WIDGET", P.F(14f, true), b, 50f, 23f, P.SF);
    }
}

// live preview of the real widget renderer on a soft "wallpaper"
class PreviewBox : Ctl {
    Widget w;
    public PreviewBox(Widget wd) { w = wd; Cursor = Cursors.Default; }
    protected override void OnPaint(PaintEventArgs e) {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        var rc = ClientRectangle; if (rc.Width < 4 || rc.Height < 4) return;
        using (var br = new LinearGradientBrush(rc, Color.FromArgb(46, 34, 96), Color.FromArgb(14, 44, 102), 30f)) g.FillRectangle(br, rc);
        DrawBlob(g, rc.Width * 0.18f, rc.Height * 0.25f, rc.Width * 0.45f, Color.FromArgb(120, 90, 255));
        DrawBlob(g, rc.Width * 0.85f, rc.Height * 0.8f, rc.Width * 0.4f, Color.FromArgb(40, 170, 255));
        Bitmap bmp = w.Snap(P.U); RectangleF src = w.SnapSrc(P.U);
        float availW = rc.Width - P.S(60), availH = rc.Height - P.S(64);
        float s = Math.Min(Math.Min(availW / src.Width, availH / src.Height), 1.25f);
        float dw = src.Width * s, dh = src.Height * s;
        var dst = new RectangleF((rc.Width - dw) / 2f, (rc.Height - dh) / 2f + P.S(10), dw, dh);
        g.DrawImage(bmp, dst, src, GraphicsUnit.Pixel);
        g.ScaleTransform(P.U, P.U); g.TextRenderingHint = TextRenderingHint.AntiAlias;
        using (var b = new SolidBrush(Color.FromArgb(150, 255, 255, 255))) g.DrawString("LIVE PREVIEW", P.F(11f, true), b, 20f, 16f, P.SF);
        using (var b = new SolidBrush(Color.FromArgb(200, 120, 255, 170))) g.FillEllipse(b, 108f, 19f, 6f, 6f);
    }
    static void DrawBlob(Graphics g, float cx, float cy, float r, Color c) {
        using (var gp = new GraphicsPath()) {
            gp.AddEllipse(cx - r, cy - r, r * 2, r * 2);
            using (var pg = new PathGradientBrush(gp)) { pg.CenterColor = Color.FromArgb(90, c); pg.SurroundColors = new Color[] { Color.FromArgb(0, c) }; g.FillPath(pg, gp); }
        }
    }
}

class SettingsForm : Form {
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);

    const int RW = 680;               // logical width of a settings page
    Cfg c; Widget w; Action quit; PreviewBox pv; System.Windows.Forms.Timer tm;
    List<Action> syncs = new List<Action>(); Pg[] pages = new Pg[4]; NavBtn[] nav = new NavBtn[4];

    public SettingsForm(Cfg cfg, Widget wd, Icon ico, Action quitAct) {
        c = cfg; w = wd; quit = quitAct;
        Text = Cfg.App + " - Settings"; Icon = ico; BackColor = P.Bg; DoubleBuffered = true;
        FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false; StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(P.S(940), P.S(660));
        BuildSide(); BuildPreview(); BuildPages(); Go(0);
        tm = new System.Windows.Forms.Timer { Interval = 33 };
        tm.Tick += (s, e) => { w.Wake(); pv.Invalidate(); };
    }

    protected override void OnHandleCreated(EventArgs e) {
        base.OnHandleCreated(e);
        try {                                            // dark title bar (Windows 10 2004+ / 11)
            int on = 1, col = 17 | (18 << 8) | (24 << 16);
            if (DwmSetWindowAttribute(Handle, 20, ref on, 4) != 0) DwmSetWindowAttribute(Handle, 19, ref on, 4);
            DwmSetWindowAttribute(Handle, 35, ref col, 4);
        } catch { }
    }
    protected override void OnVisibleChanged(EventArgs e) {
        base.OnVisibleChanged(e);
        if (tm == null || w == null) return;
        if (Visible) { w.PreviewOn = true; w.Pin(true); tm.Start(); }
        else { tm.Stop(); w.PreviewOn = false; w.Pin(false); }
    }
    protected override void OnFormClosing(FormClosingEventArgs e) {
        if (e.CloseReason == CloseReason.UserClosing) {
            e.Cancel = true;
            int act = c.CloseAct;
            if (act == 0) {
                using (var d = new CloseDlg()) {
                    d.ShowDialog(this);
                    act = d.Result;
                    if (act != 0 && d.Remember) { c.CloseAct = act; c.Save(); Sync(); }
                }
            }
            if (act == 1) Hide();
            else if (act == 2 && quit != null) BeginInvoke(quit);
            return;
        }
        base.OnFormClosing(e);
    }

    public void Open() {
        Sync();
        if (!Visible) Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate(); BringToFront();
    }
    public void Sync() { foreach (var a in syncs) a(); if (pv != null) pv.Invalidate(); }
    void Apply() { c.Save(); w.ApplyCfg(); }

    // ---------------- construction helpers ----------------
    void BuildSide() {
        var side = new Pg { BackColor = P.Side, Bounds = new Rectangle(0, 0, P.S(200), P.S(660)) };
        Controls.Add(side);
        side.Controls.Add(new Logo { Location = new Point(P.S(14), P.S(20)) });
        string[] nm = { "Layout", "Style", "Display", "General" };
        for (int i = 0; i < 4; i++) {
            int k = i;
            var b = new NavBtn(nm[i], i) { Location = new Point(P.S(12), P.S(96 + i * 46)) };
            b.Click += (s, e) => Go(k);
            nav[i] = b; side.Controls.Add(b);
        }
        side.Controls.Add(new Lab("v" + Cfg.Ver + "   by " + Cfg.Publisher, 11f, false, P.Mu) { BackColor = P.Side, Location = new Point(P.S(16), P.S(660 - 88)) });
        var q = new Btn("Quit widget", false, 176) { BackColor = P.Side, Location = new Point(P.S(12), P.S(660 - 58)) };
        q.Click += (s, e) => { if (quit != null) quit(); };
        side.Controls.Add(q);
        Controls.Add(new Panel { BackColor = P.Line, Bounds = new Rectangle(P.S(200), 0, 1, P.S(660)) });
    }
    void BuildPreview() {
        pv = new PreviewBox(w) { Bounds = new Rectangle(P.S(201), 0, P.S(739), P.S(220)) };
        Controls.Add(pv);
    }
    Pg NewPage(int i) {
        var p = new Pg { BackColor = P.Bg, Bounds = new Rectangle(P.S(230), P.S(234), P.S(RW), P.S(410)), Visible = false };
        pages[i] = p; Controls.Add(p); return p;
    }
    void Go(int i) {
        for (int k = 0; k < 4; k++) { pages[k].Visible = k == i; nav[k].Sel = k == i; nav[k].Invalidate(); }
    }
    // one settings row: title + description on the left, control on the right
    void Row(Pg pg, int y, string title, string desc, Control ctl, int h) {
        pg.Controls.Add(new Lab(title, 13.5f, true, P.Tx) { Location = new Point(0, P.S(y + 7)) });
        if (desc != null) pg.Controls.Add(new Lab(desc, 12f, false, P.Mu) { Location = new Point(0, P.S(y + 26)) });
        ctl.Location = new Point(P.S(RW) - ctl.Width, P.S(y) + (P.S(h) - ctl.Height) / 2);
        pg.Controls.Add(ctl);
        pg.Controls.Add(new Panel { BackColor = P.Line, Bounds = new Rectangle(0, P.S(y + h) - 1, P.S(RW), 1) });
    }
    Tog MkTog(Func<bool> get, Action<bool> set) {
        var t = new Tog(); t.Changed += (s, e) => { set(t.Value); Apply(); };
        syncs.Add(() => t.Value = get()); return t;
    }
    Slide MkSlide(int mn, int mx, string unit, Func<int> get, Action<int> set) {
        var t = new Slide(mn, mx, unit, 300f); t.Changed += (s, e) => { set(t.Value); Apply(); };
        syncs.Add(() => t.Value = get()); return t;
    }
    Seg MkSeg(string[] items, float wd, Func<int> get, Action<int> set) {
        var t = new Seg(items, wd); t.Changed += (s, e) => { set(t.Value); Apply(); };
        syncs.Add(() => t.Value = get()); return t;
    }

    void BuildPages() {
        // ---------- LAYOUT ----------
        var p0 = NewPage(0);
        p0.Controls.Add(new Lab("Widget layout", 13.5f, true, P.Tx) { Location = new Point(0, P.S(6)) });
        p0.Controls.Add(new Lab("Choose how the now-playing card is arranged", 12f, false, P.Mu) { Location = new Point(0, P.S(25)) });
        var lp = new LayoutPicker { Location = new Point(0, P.S(50)) };
        lp.Changed += (s, e) => { c.Layout = lp.Value; Apply(); };
        syncs.Add(() => lp.Value = c.Layout);
        p0.Controls.Add(lp);
        var pg = new PosGrid();
        pg.Changed += (s, e) => { c.Pos = pg.Value; Apply(); };
        syncs.Add(() => pg.Value = c.Pos);
        Row(p0, 160, "Position", "Or drag the widget anywhere on screen", pg, 92);
        Row(p0, 252, "Size", "Scale the whole widget up or down", MkSlide(60, 200, "%", () => c.Scale, v => c.Scale = v), 48);
        Row(p0, 300, "Edge margin", "Distance from the screen edge", MkSlide(0, 100, " px", () => c.Margin, v => c.Margin = v), 48);
        int sc = Math.Min(4, Math.Max(1, Screen.AllScreens.Length));
        if (sc > 1) {
            string[] mn = new string[sc]; mn[0] = "Main";
            for (int i = 1; i < sc; i++) mn[i] = "Screen " + (i + 1);
            Row(p0, 348, "Display", "Which monitor the widget lives on", MkSeg(mn, 84f * sc, () => c.Monitor, v => c.Monitor = v), 48);
        }

        // ---------- STYLE ----------
        var p1 = NewPage(1);
        Row(p1, 0, "Theme", "Card colour scheme", MkSeg(new[] { "Dark", "Light", "Black", "Auto" }, 336f, () => c.Theme, v => c.Theme = v), 48);
        var sw = new Swatches();
        sw.Changed += (s, e) => { var k = sw.Value; c.Accent = (k.R << 16) | (k.G << 8) | k.B; Apply(); };
        syncs.Add(() => sw.Value = c.Custom);
        Row(p1, 48, "Accent colour", "Used when album-art colours are off", sw, 48);
        Row(p1, 96, "Album-art colours", "Tint the card using the current cover", MkTog(() => c.ArtColor, v => c.ArtColor = v), 48);
        Row(p1, 144, "Corner radius", "From sharp edges to pill-shaped", MkSlide(0, 40, "", () => c.Radius, v => c.Radius = v), 48);
        Row(p1, 192, "Accent tint", "How strongly the accent colours the card", MkSlide(0, 40, "%", () => c.Tint, v => c.Tint = v), 48);
        Row(p1, 240, "Opacity", "Overall widget transparency", MkSlide(40, 100, "%", () => c.Opacity, v => c.Opacity = v), 48);
        Row(p1, 288, "Drop shadow", "Soft shadow behind the card and cover", MkTog(() => c.Shadow, v => c.Shadow = v), 48);
        Row(p1, 336, "Cover glow", "Soft light behind the album art", MkTog(() => c.Glow, v => c.Glow = v), 48);

        // ---------- DISPLAY ----------
        var p2 = NewPage(2);
        Row(p2, 0, "Visibility", "When the widget appears", MkSeg(new[] { "Always", "While playing", "On new track" }, 348f, () => c.Mode, v => c.Mode = v), 48);
        Row(p2, 48, "Peek duration", "How long it shows when a track changes", MkSlide(2, 15, " s", () => c.Peek, v => c.Peek = v), 48);
        Row(p2, 96, "Progress bar", "Show the seekable progress bar", MkTog(() => c.ShowProgress, v => c.ShowProgress = v), 48);
        Row(p2, 144, "Time labels", "Elapsed and total time under the bar", MkTog(() => c.ShowTimes, v => c.ShowTimes = v), 48);
        Row(p2, 192, "Scroll long titles", "Marquee instead of trimming with an ellipsis", MkTog(() => c.Marquee, v => c.Marquee = v), 48);
        Row(p2, 240, "Keep on top", "Stay above other windows", MkTog(() => c.OnTop, v => c.OnTop = v), 48);

        // ---------- GENERAL ----------
        var p3 = NewPage(3);
        Row(p3, 0, "Start with Windows", "Launch quietly in the system tray when you sign in", MkTog(() => c.Auto, v => { c.Auto = v; Cfg.SetAuto(v); }), 48);
        Row(p3, 48, "Open this window at launch", "Only when you start the app yourself", MkTog(() => c.OpenUi, v => c.OpenUi = v), 48);
        Row(p3, 96, "When closing this window", "Choose what the X button does", MkSeg(new[] { "Ask me", "Minimize to tray", "Quit" }, 380f, () => c.CloseAct, v => c.CloseAct = v), 48);
        var reset = new Btn("Reset to defaults", false, 150f);
        reset.Click += (s, e) => {
            if (MessageBox.Show(this, "Reset all settings to their defaults?", Cfg.App, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            var d = new Cfg(); d.X = c.X; d.Y = c.Y; c.CopyFrom(d); Cfg.SetAuto(c.Auto); Apply(); Sync();
        };
        reset.Location = new Point(0, P.S(166)); p3.Controls.Add(reset);
        var folder = new Btn("Open settings folder", false, 170f);
        folder.Click += (s, e) => { try { Directory.CreateDirectory(Cfg.Dir); Process.Start("explorer.exe", "\"" + Cfg.Dir + "\""); } catch { } };
        folder.Location = new Point(P.S(162), P.S(166)); p3.Controls.Add(folder);
        p3.Controls.Add(new Lab(Cfg.App + "  v" + Cfg.Ver, 13.5f, true, P.Tx) { Location = new Point(0, P.S(224)) });
        p3.Controls.Add(new Lab("Made by " + Cfg.Publisher + ". Works with Spotify, browsers, media players - anything Windows lists as playing.", 12f, false, P.Mu) { Location = new Point(0, P.S(246)) });
        p3.Controls.Add(new Lab("Closing this window keeps the widget running in the system tray. Right-click the tray icon or the widget for quick options.", 12f, false, P.Mu) { Location = new Point(0, P.S(266)) });
    }
}

// "Close or minimize to tray?" prompt shown when the settings window's X is pressed
class CloseDlg : Form {
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
    public int Result;          // 0 cancel, 1 minimize to tray, 2 quit
    public bool Remember;
    Tog tg;

    public CloseDlg() {
        Text = Cfg.App; BackColor = P.Bg; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        ShowInTaskbar = false; StartPosition = FormStartPosition.CenterParent; KeyPreview = true; DoubleBuffered = true;
        ClientSize = new Size(P.S(420), P.S(196));
        Controls.Add(new Lab("Close JXT MUSIC WIDGET?", 16f, true, P.Tx) { Location = new Point(P.S(24), P.S(20)) });
        Controls.Add(new Lab("Minimize to tray keeps the widget running in the background,", 12f, false, P.Mu) { Location = new Point(P.S(24), P.S(52)) });
        Controls.Add(new Lab("or quit to stop it completely.", 12f, false, P.Mu) { Location = new Point(P.S(24), P.S(70)) });
        tg = new Tog { Location = new Point(P.S(24), P.S(102)) };
        Controls.Add(tg);
        Controls.Add(new Lab("Don't ask me again", 12.5f, false, P.Tx) { Location = new Point(P.S(80), P.S(104)) });
        var b1 = new Btn("Minimize to tray", true, 160f) { Location = new Point(P.S(24), P.S(146)) };
        b1.Click += (s, e) => Done(1);
        var b2 = new Btn("Quit", false, 100f) { Location = new Point(P.S(196), P.S(146)) };
        b2.Click += (s, e) => Done(2);
        var b3 = new Btn("Cancel", false, 100f) { Location = new Point(P.S(308), P.S(146)) };
        b3.Click += (s, e) => Done(0);
        Controls.Add(b1); Controls.Add(b2); Controls.Add(b3);
    }
    void Done(int r) { Result = r; Remember = tg.Value; Close(); }
    protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (e.KeyCode == Keys.Escape) Done(0); }
    protected override void OnHandleCreated(EventArgs e) {
        base.OnHandleCreated(e);
        try {
            int on = 1, col = 17 | (18 << 8) | (24 << 16);
            if (DwmSetWindowAttribute(Handle, 20, ref on, 4) != 0) DwmSetWindowAttribute(Handle, 19, ref on, 4);
            DwmSetWindowAttribute(Handle, 35, ref col, 4);
        } catch { }
    }
}

class AppCtx : ApplicationContext {
    Cfg c; Widget w; NotifyIcon ni; SettingsForm ui; Icon ico; List<Action> refresh = new List<Action>();

    public AppCtx() {
        bool first; c = Cfg.Load(out first);
        if (first) c.Save();
        Cfg.SetAuto(c.Auto);                       // re-register startup on every launch (self-heal)
        ico = MakeIcon();
        w = new Widget(c);
        ui = new SettingsForm(c, w, ico, Quit);
        w.ShowRequest = () => ui.Open(); w.CfgChanged = () => ui.Sync();
        w.Ctx = BuildMenu();
        ni = new NotifyIcon { Visible = true, Text = Cfg.App, Icon = ico, ContextMenuStrip = w.Ctx };
        ni.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ui.Open(); };
        if (!Program.Startup) { if (first || c.OpenUi) ui.Open(); else w.Reveal(); }
    }

    ToolStripMenuItem Sub(string title, string[] names, int[] vals, Func<int> get, Action<int> set) {
        var top = new ToolStripMenuItem(title); var items = new ToolStripMenuItem[names.Length];
        for (int i = 0; i < names.Length; i++) {
            int v = vals[i]; items[i] = new ToolStripMenuItem(names[i]);
            items[i].Click += (s, e) => { set(v); c.Save(); w.ApplyCfg(); ui.Sync(); };
            top.DropDownItems.Add(items[i]);
        }
        refresh.Add(() => { for (int k = 0; k < items.Length; k++) items[k].Checked = get() == vals[k]; });
        return top;
    }
    ContextMenuStrip BuildMenu() {
        var m = new ContextMenuStrip();
        m.Items.Add(new ToolStripMenuItem(Cfg.App + "  -  by " + Cfg.Publisher) { Enabled = false });
        m.Items.Add(new ToolStripSeparator());
        var open = new ToolStripMenuItem("Open settings...") { Font = new Font(SystemFonts.MenuFont, FontStyle.Bold) };
        open.Click += (s, e) => ui.Open(); m.Items.Add(open);
        m.Items.Add("Show / hide widget", null, (s, e) => w.Toggle());
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add(Sub("Layout", new[] { "Classic", "Compact", "Vertical", "Minimal" }, new[] { 0, 1, 2, 3 }, () => c.Layout, v => c.Layout = v));
        m.Items.Add(Sub("Theme", new[] { "Dark", "Light", "Black", "Follow Windows" }, new[] { 0, 1, 2, 3 }, () => c.Theme, v => c.Theme = v));
        m.Items.Add(Sub("Position", new[] { "Top left", "Top center", "Top right", "Middle left", "Center", "Middle right", "Bottom left", "Bottom center", "Bottom right", "Custom (drag the widget)" },
            new[] { 0, 1, 2, 7, 8, 9, 3, 4, 5, 6 }, () => c.Pos, v => c.Pos = v));
        m.Items.Add(Sub("Size", new[] { "80%", "100%", "125%", "150%", "175%", "200%" }, new[] { 80, 100, 125, 150, 175, 200 }, () => c.Scale, v => c.Scale = v));
        m.Items.Add(Sub("Visibility", new[] { "Always show", "Show while media is active", "Peek on track change" }, new[] { 0, 1, 2 }, () => c.Mode, v => c.Mode = v));
        var auto = new ToolStripMenuItem("Start with Windows");
        auto.Click += (s, e) => { c.Auto = !c.Auto; Cfg.SetAuto(c.Auto); c.Save(); ui.Sync(); };
        refresh.Add(() => auto.Checked = c.Auto); m.Items.Add(auto);
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add("Exit", null, (s, e) => Quit());
        m.Opening += (s, e) => { foreach (var r in refresh) r(); };
        return m;
    }
    Icon MakeIcon() {
        using (var b = new Bitmap(32, 32))
        using (var g = Graphics.FromImage(b)) {
            g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.AntiAlias;
            using (var p = G.RR(new RectangleF(1, 1, 30, 30), 8))
            using (var br = new LinearGradientBrush(new Rectangle(0, 0, 32, 32), Color.FromArgb(150, 110, 255), Color.FromArgb(70, 150, 255), 45f)) g.FillPath(br, p);
            using (var f = new Font("Segoe UI Symbol", 19f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var sf2 = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString("\u266B", f, Brushes.White, new RectangleF(0, 1, 32, 32), sf2);
            return Icon.FromHandle(b.GetHicon());
        }
    }
    void Quit() { ni.Visible = false; ni.Dispose(); ui.Dispose(); w.Cleanup(); w.Close(); Application.Exit(); }
}
