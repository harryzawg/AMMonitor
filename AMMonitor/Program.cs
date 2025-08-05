// this is really janky so don't expect much
// thank you to https://github.com/PKBeam/AMWin-RP for guiding me on most stuff
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using FlaUI.Core.Capturing;
using System;
using System.Threading;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Media.Control;

namespace AMM
{
    internal class Program
    {
        static AppleMusicClientScraper scraper;
        static AppleMusicInfo? CurrentSong;

        static void Main(string[] args)
        {
            Trace.Listeners.Add(new ConsoleTraceListener());

            scraper = new AppleMusicClientScraper(3, (info) =>
            {
                CurrentSong = info;

                if (info == null)
                {
                    Trace.WriteLine("[debug - detection] nothing playing");
                }
                else
                {
                    //Trace.WriteLine($"[debug - detection] now playing: '{info.SongName}' by '{info.SongArtist}'");
                }
            });

            runHTTP();

            Trace.WriteLine("press enter to close");
            Console.ReadLine();
        }

        static async void runHTTP()
        {
            var listener = new HttpListener();
            listener.Prefixes.Add("http://localhost:22340/");
            listener.Start();
            Trace.WriteLine("[HTTP] started server on: http://localhost:22340/");

            while (true)
            {
                var context = await listener.GetContextAsync();
                var request = context.Request;
                var response = context.Response;

                try
                {
                    if (request.Url?.AbsolutePath == "/song")
                    {
                        response.ContentType = "application/json";
                        response.StatusCode = 200;

                        var albumcover = "/albumcover";

                        var res = new
                        {
                            SongName = CurrentSong?.SongName ?? "",
                            SongArtist = CurrentSong?.SongArtist ?? "",
                            SongAlbum = CurrentSong?.SongAlbum ?? "",
                            AlbumCoverUrl = CurrentSong?.AlbumCoverBase64 != null ? $"http://localhost:22340{albumcover}" : null,
                            IsPaused = CurrentSong?.IsPaused ?? true
                        };

                        var options = new JsonSerializerOptions
                        {
                            WriteIndented = true,
                            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                        };

                        var json = JsonSerializer.Serialize(res, options);
                        byte[] buffer = Encoding.UTF8.GetBytes(json);

                        response.ContentLength64 = buffer.Length;
                        await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                        response.OutputStream.Close();

                        Trace.WriteLine("[HTTP] /song 200");
                    }
                    else if (request.Url?.AbsolutePath == "/play-pause")
                    {
                        clickPP();
                        response.StatusCode = 200;
                        var restxt = Encoding.UTF8.GetBytes("Play/Pause toggled");
                        response.ContentLength64 = restxt.Length;
                        await response.OutputStream.WriteAsync(restxt, 0, restxt.Length);
                        response.OutputStream.Close();
                        Trace.WriteLine("[HTTP] /play-pause 200");
                    }
                    //else if (request.Url?.AbsolutePath == "/favouritecheck")
                    //{
                        //bool isFav = IsLoved();
                        //response.StatusCode = 200;
                        //response.ContentType = "text/plain";

                        //var restxt = isFav ? "true" : "false";
                        //byte[] buffer = Encoding.UTF8.GetBytes(restxt);

                        //response.ContentLength64 = buffer.Length;
                        //await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                        //response.OutputStream.Close();
                        //Trace.WriteLine("[HTTP] /favouritecheck 200");
                    //}
                    else if (request.Url?.AbsolutePath == "/favourite")
                    {
                        Love();
                        response.StatusCode = 200;
                        var restxt = Encoding.UTF8.GetBytes("Loved/Unloved");
                        response.ContentLength64 = restxt.Length;
                        await response.OutputStream.WriteAsync(restxt, 0, restxt.Length);
                        response.OutputStream.Close();
                        Trace.WriteLine("[HTTP] /favourite 200");
                    }
                    else if (request.Url?.AbsolutePath == "/shuffle/toggle")
                    {
                        var ns = ToggleShuffle();
                        byte[] bytes = Encoding.UTF8.GetBytes(ns);
                        response.ContentType = "text/plain";
                        response.ContentLength64 = bytes.Length;
                        await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
                        response.OutputStream.Close();
                        Trace.WriteLine($"[HTTP] /shuffle/toggle 200 -> {ns}");
                    }
                    else if (request.Url?.AbsolutePath == "/shuffle/state")
                    {
                        var state = GetShuffleState();
                        string res = (state == true).ToString().ToLower();
                        byte[] bytes = Encoding.UTF8.GetBytes(res);
                        response.ContentType = "text/plain";
                        response.ContentLength64 = bytes.Length;
                        await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
                        response.OutputStream.Close();
                        Trace.WriteLine($"[HTTP] /shuffle/state 200 -> {res}");
                    }
                    else if (request.Url?.AbsolutePath == "/repeat/toggle")
                    {
                        var res = ToggleRepeat();
                        byte[] bytes = Encoding.UTF8.GetBytes(res);
                        response.ContentType = "text/plain";
                        response.ContentLength64 = bytes.Length;
                        await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
                        response.OutputStream.Close();
                        Trace.WriteLine($"[HTTP] /repeat/toggle 200 -> {res}");
                    }

                    else if (request.Url?.AbsolutePath == "/repeat/state")
                    {
                        string state = GetRepeatState();
                        byte[] bytes = Encoding.UTF8.GetBytes(state);
                        response.ContentType = "text/plain";
                        response.ContentLength64 = bytes.Length;
                        await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
                        response.OutputStream.Close();
                        Trace.WriteLine($"[HTTP] /repeat/state 200 -> {state}");
                    }
                    else if (request.Url?.AbsolutePath == "/albumcover")
                    {   
                        if (CurrentSong?.AlbumCoverBase64 == null)
                        {
                            response.StatusCode = 404;
                            response.StatusDescription = "Album cover not found";
                            response.OutputStream.Close();
                            Trace.WriteLine("[HTTP] /albumcover 400 (no album cover found)");
                        }
                        else
                        {
                            // decode base64
                            byte[] bytes = Convert.FromBase64String(CurrentSong.AlbumCoverBase64);

                            // get image format (probably jpg or png idk what itunes serves)
                            string ct = GetMIME(bytes);

                            response.ContentType = ct;
                            response.ContentLength64 = bytes.Length;
                            await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
                            response.OutputStream.Close();

                            Trace.WriteLine("[HTTP] /albumcover 200");
                        }
                    }
                    else
                    {
                        response.StatusCode = 404;
                        response.OutputStream.Close();
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[HTTP] 500 exception: {ex}");
                    try
                    {
                        response.StatusCode = 500;
                        response.OutputStream.Close();
                    }
                    catch { }
                }
            }
        }

        // VERY janky trick but works fine sometimes
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        private const int SW_MINIMIZE = 6;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
        private static void clickPP()
        {
            try
            {
                var SessMGR = GlobalSystemMediaTransportControlsSessionManager.RequestAsync().GetAwaiter().GetResult();

                var sessions = SessMGR.GetSessions();

                var AMSession = sessions.FirstOrDefault(s =>
                    s.SourceAppUserModelId?.Contains("AppleMusic", StringComparison.OrdinalIgnoreCase) == true);

                if (AMSession != null)
                {
                    AMSession.TryTogglePlayPauseAsync().AsTask().Wait();
                    Trace.WriteLine("[debug - play/pause] sent play/pause to Apple MUsic");
                }
                else
                {
                    Trace.WriteLine("[debug - play pause] cannott find Apple Music session");
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[debug - play/pause] error in play/pause: {ex.Message}");
            }
        }
#if FAVCHECK
        private static Bitmap CaptureWindow(IntPtr hWnd)
        {
            GetWindowRect(hWnd, out RECT rc);
            int width = rc.Right - rc.Left;
            int height = rc.Bottom - rc.Top;

            Bitmap bmp = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics gfxBmp = Graphics.FromImage(bmp))
            {
                IntPtr hdcBitmap = gfxBmp.GetHdc();
                PrintWindow(hWnd, hdcBitmap, 2);
                gfxBmp.ReleaseHdc(hdcBitmap);
            }

            return bmp;
        }

        private static bool IsLoved()
        {
            var processes = Process.GetProcessesByName("AppleMusic");
            if (processes.Length == 0) return false;

            var amProcess = processes[0];
            using var automation = new UIA3Automation();
            var desktop = automation.GetDesktop();
            var windows = desktop.FindAllChildren(cf => cf.ByProcessId(amProcess.Id));
            if (windows.Length == 0) return false;

            AutomationElement? transportbar = null;
            foreach (var window in windows)
            {
                transportbar = window.FindFirstDescendant(cf => cf.ByAutomationId("TransportBar"));
                if (transportbar != null) break;
            }
            if (transportbar == null) return false;

            var favbtn = transportbar.FindFirstDescendant(cf =>
                cf.ByControlType(FlaUI.Core.Definitions.ControlType.Button)
                  .And(cf.ByName("Favorite").Or(cf.ByName("Favourite"))));
            if (favbtn == null) return false;

            var rect = favbtn.BoundingRectangle;
            if (rect.IsEmpty) return false;

            IntPtr hwnd = amProcess.MainWindowHandle;

            IntPtr cf = GetForegroundWindow();

            bool wasminimized = IsIconic(hwnd);

            ShowWindow(hwnd, SW_RESTORE);
            SetForegroundWindow(hwnd);
            Thread.Sleep(500);

            Bitmap bmp;
            if (wasminimized)
            {
                using var fullBMP = CaptureWindow(hwnd);

                int cropX = (int)(rect.Left - windows[0].BoundingRectangle.Left);
                int cropY = (int)(rect.Top - windows[0].BoundingRectangle.Top);
                bmp = fullBMP.Clone(new System.Drawing.Rectangle(cropX, cropY, (int)rect.Width, (int)rect.Height), fullBMP.PixelFormat);
            }
            else
            {
                using var capture = Capture.Rectangle(rect);
                bmp = new Bitmap(capture.Bitmap);
            }

            // if apple music was already minimized, then re minimize it (it stays appeared on other monitors)
            //if (wasminimized)
            //{
            //ShowWindow(hwnd, SW_MINIMIZE);
            //}

            //string dbg = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fav.png");
            //bmp.Save(dbg, System.Drawing.Imaging.ImageFormat.Png);
            //Trace.WriteLine($"[debug - fav] visible capture saved to {dbg}");

            double avgb = GetAverageBrightness(bmp);
            Trace.WriteLine($"[debug - fav] favourite button brightness = {avgb}");

            double[] favValues =
            {
                0.26501035196687445,
                0.3233061340478235,
                0.3290220436000505
            };

            const double TOLERANCE = 0.0001;
            return favValues.Any(val => Math.Abs(avgb - val) < TOLERANCE);
        }
        private static double GetAverageBrightness(Bitmap bmp)
        {
            double sum = 0;
            int count = 0;

            for (int y = 0; y < bmp.Height; y++)
            {
                for (int x = 0; x < bmp.Width; x++)
                {
                    var c = bmp.GetPixel(x, y);
                    if (c.R < 20 && c.G < 20 && c.B < 20) continue;
                    double brightness = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
                    sum += brightness;
                    count++;
                }
            }

            return count > 0 ? sum / count : 1.0;
        } 
#endif
        private static void Love()
        {
            // save window
            IntPtr cf = GetForegroundWindow();

            var processes = Process.GetProcessesByName("AppleMusic");
            if (processes.Length == 0)
            {
                Trace.WriteLine("[debug - favourite] Apple Music process not found");
                return;
            }

            var AMP = processes[0];
            using var automation = new UIA3Automation();
            var desktop = automation.GetDesktop();

            var windows = desktop.FindAllChildren(cf => cf.ByProcessId(AMP.Id));
            if (windows.Length == 0)
            {
                Trace.WriteLine("[debug - favourite] no Apple Music windows found");
                return;
            }

            AutomationElement? transportbar = null;

            foreach (var window in windows)
            {
                transportbar = window.FindFirstDescendant(cf => cf.ByAutomationId("TransportBar"));
                if (transportbar != null)
                {
                    Trace.WriteLine("[debug - favourite] got TransportBar");
                    break;
                }
            }

            if (transportbar == null)
            {
                Trace.WriteLine("[debug - favourite] couldn't find TransportBar");
                return;
            }

            var favbtn = transportbar.FindFirstDescendant(cf =>
                cf.ByControlType(FlaUI.Core.Definitions.ControlType.Button)
                .And(cf.ByName("Favorite").Or(cf.ByName("Favourite"))));

            if (favbtn == null)
            {
                Trace.WriteLine("[debug - favourite] favourite button not found");
                return;
            }

            try
            {
                if (favbtn.Properties.IsEnabled.ValueOrDefault)
                {
                    favbtn.AsButton().Invoke();
                    Trace.WriteLine("[debug - favourite] favourite pressed");
                }
                else
                {
                    Trace.WriteLine("[debug - favourite] favourite button not enabled, have you played a song?");
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[debug - favourite] failed to favourite: {ex.Message}");
            }

            // restore
            if (cf != IntPtr.Zero)
            {
                SetForegroundWindow(cf);
            }
        }

        private static void ShowWithoutFocus(IntPtr hWnd)
        {
            ShowWindow(hWnd, 8);
        }

        // toggle states
        private static string ToggleRepeat()
        {
            IntPtr cf = GetForegroundWindow();

            var processes = Process.GetProcessesByName("AppleMusic");
            if (processes.Length == 0)
                return "false";

            var AMP = processes[0];
            IntPtr AMPF = AMP.MainWindowHandle;
            bool wasMinimized = IsIconic(AMPF);

            using var automation = new UIA3Automation();
            var desktop = automation.GetDesktop();
            var windows = desktop.FindAllChildren(cf => cf.ByProcessId(AMP.Id));
            if (windows.Length == 0)
                return "false";

            if (wasMinimized)
                ShowWithoutFocus(AMPF);

            AutomationElement? transportbar = null;
            foreach (var window in windows)
            {
                transportbar = window.FindFirstDescendant(cf => cf.ByAutomationId("TransportBar"));
                if (transportbar != null) break;
            }
            if (transportbar == null)
                return "false";

            var repeatBtn = transportbar.FindFirstDescendant(cf =>
                cf.ByControlType(FlaUI.Core.Definitions.ControlType.Button)
                .And(cf.ByName("Repeat").Or(cf.ByName("Repeat Off"))
                .Or(cf.ByName("Repeat All")).Or(cf.ByName("Repeat One"))));

            if (repeatBtn == null)
                return "false";

            if (!repeatBtn.Patterns.Toggle.IsSupported)
            {
                Trace.WriteLine("[debug - repeat] toggle is not supported");
                return "false";
            }

            repeatBtn.Patterns.Toggle.Pattern.Toggle();

            if (cf != IntPtr.Zero)
                SetForegroundWindow(cf);

            if (wasMinimized)
                ShowWindow(AMPF, SW_MINIMIZE);

            var ns = repeatBtn.Patterns.Toggle.Pattern.ToggleState.Value;
            return ns == FlaUI.Core.Definitions.ToggleState.On
                ? "true"
                : ns == FlaUI.Core.Definitions.ToggleState.Indeterminate
                    ? "trueOne"
                    : "false";
        }
        private static string ToggleShuffle()
        {
            IntPtr cf = GetForegroundWindow();

            var processes = Process.GetProcessesByName("AppleMusic");
            if (processes.Length == 0)
                return "false";

            var AMP = processes[0];
            IntPtr AMPF = AMP.MainWindowHandle;
            bool wasMinimized = IsIconic(AMPF);

            using var automation = new UIA3Automation();
            var desktop = automation.GetDesktop();
            var windows = desktop.FindAllChildren(cf => cf.ByProcessId(AMP.Id));
            if (windows.Length == 0)
                return "false";

            if (wasMinimized)
                ShowWithoutFocus(AMPF);

            AutomationElement? transportbar = null;
            foreach (var window in windows)
            {
                transportbar = window.FindFirstDescendant(cf => cf.ByAutomationId("TransportBar"));
                if (transportbar != null) break;
            }
            if (transportbar == null)
                return "false";

            var shufflebtn = transportbar.FindFirstDescendant(cf =>
                cf.ByControlType(FlaUI.Core.Definitions.ControlType.Button)
                .And(cf.ByName("Shuffle").Or(cf.ByName("Shuffle Off")).Or(cf.ByName("Shuffle On"))));

            if (shufflebtn == null)
                return "false";

            if (!shufflebtn.Patterns.Toggle.IsSupported)
            {
                Trace.WriteLine("[debug - shuffle] shuffle is not supported");
                return "false";
            }

            shufflebtn.Patterns.Toggle.Pattern.Toggle();

            if (cf != IntPtr.Zero)
                SetForegroundWindow(cf);

            if (wasMinimized)
                ShowWindow(AMPF, SW_MINIMIZE);

            var ns = shufflebtn.Patterns.Toggle.Pattern.ToggleState.Value;
            return ns == FlaUI.Core.Definitions.ToggleState.On
                ? "true"
                : ns == FlaUI.Core.Definitions.ToggleState.Indeterminate
                    ? "trueOne"
                    : "false";
        }
        // get states
        private static bool? GetShuffleState()
        {
            var processes = Process.GetProcessesByName("AppleMusic");
            if (processes.Length == 0) return null;

            var AMP = processes[0];
            using var automation = new UIA3Automation();
            var desktop = automation.GetDesktop();

            var windows = desktop.FindAllChildren(cf => cf.ByProcessId(AMP.Id));
            if (windows.Length == 0) return null;

            AutomationElement? transportbar = null;
            foreach (var window in windows)
            {
                transportbar = window.FindFirstDescendant(cf => cf.ByAutomationId("TransportBar"));
                if (transportbar != null) break;
            }
            if (transportbar == null) return null;

            var shufflebtn = transportbar.FindFirstDescendant(cf =>
                cf.ByControlType(FlaUI.Core.Definitions.ControlType.Button)
                .And(cf.ByName("Shuffle").Or(cf.ByName("Shuffle Off")).Or(cf.ByName("Shuffle On"))));

            if (shufflebtn == null) return null;

            if (shufflebtn.Patterns.Toggle.IsSupported)
            {
                var state = shufflebtn.Patterns.Toggle.Pattern.ToggleState.Value;
                return state == FlaUI.Core.Definitions.ToggleState.On;
            }

            return null;
        }
        static string RepeatToggleStateStr(FlaUI.Core.Definitions.ToggleState? state)
        {
            if (state == FlaUI.Core.Definitions.ToggleState.On)
                return "true";
            else if (state == FlaUI.Core.Definitions.ToggleState.Indeterminate)
                return "trueOne";
            else
                return "false";
        }
        static string GetRepeatState()
        {
            var processes = Process.GetProcessesByName("AppleMusic");
            if (processes.Length == 0) return "false";

            var AMP = processes[0];
            using var automation = new UIA3Automation();
            var desktop = automation.GetDesktop();

            var windows = desktop.FindAllChildren(cf => cf.ByProcessId(AMP.Id));
            if (windows.Length == 0) return "false";

            AutomationElement? transportbar = null;
            foreach (var window in windows)
            {
                transportbar = window.FindFirstDescendant(cf => cf.ByAutomationId("TransportBar"));
                if (transportbar != null) break;
            }
            if (transportbar == null) return "false";

            var repeatbtn = transportbar.FindFirstDescendant(cf =>
                cf.ByControlType(FlaUI.Core.Definitions.ControlType.Button)
                .And(cf.ByName("Repeat").Or(cf.ByName("Repeat Off")).Or(cf.ByName("Repeat All")).Or(cf.ByName("Repeat One"))));

            if (repeatbtn == null) return "false";

            if (repeatbtn.Patterns.Toggle.IsSupported)
            {
                var toggleState = repeatbtn.Patterns.Toggle.Pattern.ToggleState.Value;
                return RepeatToggleStateStr(toggleState);
            }

            return "false";
        }

        // stupid but works
        static string GetMIME(byte[] bytes)
        {
            if (bytes.Length < 4)
                return "application/octet-stream";

            if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
                return "image/jpeg";

            if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
                return "image/png";

            if (bytes.Length >= 6 &&
                bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46 &&
                bytes[3] == 0x38 && (bytes[4] == 0x37 || bytes[4] == 0x39) && bytes[5] == 0x61)
                return "image/gif";

            // does itunes/music do any other formats? hopefully not

            return "application/octet-stream";
        }
    }

    internal class AppleMusicInfo : IEquatable<AppleMusicInfo>
    {
        public string SongName { get; set; }
        public string SongSubTitle { get; set; }
        public string SongAlbum { get; set; }
        public string SongArtist { get; set; }
        public string? AlbumCoverBase64 { get; set; } = null;
        public bool IsPaused { get; set; } = true;

        public AppleMusicInfo(string songName, string songSubTitle, string songAlbum, string songArtist)
        {
            SongName = songName;
            SongSubTitle = songSubTitle;
            SongAlbum = songAlbum;
            SongArtist = songArtist;
        }

        public override string ToString()
        {
            return $"'{SongName}' by '{SongArtist}' on '{SongAlbum}'";
        }

        public bool Equals(AppleMusicInfo? other)
        {
            if (other == null) return false;
            return SongName == other.SongName && SongArtist == other.SongArtist && SongSubTitle == other.SongSubTitle;
        }

        public override bool Equals(object? obj) => Equals(obj as AppleMusicInfo);

        public override int GetHashCode()
        {
            return SongName.GetHashCode() ^ SongArtist.GetHashCode() ^ SongSubTitle.GetHashCode();
        }
    }

    internal class AppleMusicClientScraper
    {
        private System.Timers.Timer timer;
        private AppleMusicInfo? currentSong;
        private readonly Action<AppleMusicInfo?> refresh;

        public AppleMusicClientScraper(int refreshperiod, Action<AppleMusicInfo?> refresh)
        {
            this.refresh = refresh;

            timer = new System.Timers.Timer(refreshperiod * 1000);
            timer.Elapsed += async (sender, e) => await Refresh();
            timer.AutoReset = true;
            timer.Start();

            Task.Run(Refresh);
        }

        private async Task Refresh()
        {
            try
            {
                Trace.WriteLine("[debug - refresh] refreshing Apple Music data...");
                var info = await GetAppleMusicInfo();

                if (info == null)
                {
                    Trace.WriteLine("[debug - refresh] no song data found");
                    if (currentSong != null)
                    {
                        currentSong = null;
                        refresh(null);
                    }
                }
                else
                {
                    bool songchange = !info.Equals(currentSong);
                    bool pausestate = currentSong == null || info.IsPaused != currentSong.IsPaused;

                    if (songchange)
                    {
                        Trace.WriteLine($"[debug - am scraper] got new song: {info}");
                        currentSong = info;
                        refresh(currentSong);
                    }
                    else if (pausestate)
                    {
                        Trace.WriteLine("[debug - am scraper] play/pause changed");
                        currentSong.IsPaused = info.IsPaused;
                        refresh(currentSong);
                    }
                    else
                    {
                        //Trace.WriteLine("[debug - am scraper] play/pause unchanged");
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[debug - am scraper] exception during refresh: {ex}");
            }
        }

        private async Task<AppleMusicInfo?> GetAppleMusicInfo()
        {
            await Task.Yield();

            var processes = Process.GetProcessesByName("AppleMusic");
            if (processes.Length == 0)
            {
                Trace.WriteLine("[debug - am scraper] Apple Music process not found");
                return null;
            }

            var AMP = processes[0];
            using var automation = new UIA3Automation();
            var desktop = automation.GetDesktop();

            var windows = desktop.FindAllChildren(cf => cf.ByProcessId(AMP.Id));
            if (windows.Length == 0)
            {
                Trace.WriteLine("[debug - am scraper] no Apple Music windows found/are open");
                return null;
            }

            AutomationElement? songPanel = null;
            bool isMiniPlayer = false;

            foreach (var window in windows)
            {
                if (window.Name == "Mini Player")
                {
                    isMiniPlayer = true;
                    songPanel = window.FindFirstDescendant(cf => cf.ByClassName("InputSiteWindowClass"));
                    if (songPanel != null)
                    {
                        Trace.WriteLine("[debug - am scraper] got Mini Player panel");
                        break;
                    }
                }
                else
                {
                    songPanel = window.FindFirstDescendant(cf => cf.ByAutomationId("TransportBar")) ?? songPanel;
                }
            }

            if (songPanel == null)
            {
                Trace.WriteLine("[debug - am scraper] couldn't find song panel/progress bar");
                return null;
            }

            AutomationElement? fieldspanel = isMiniPlayer ? songPanel : songPanel.FindFirstChild(cf => cf.ByAutomationId("LCD"));
            if (fieldspanel == null)
            {
                Trace.WriteLine("[debug - am scraper] could not get song fields panel.");
                return null;
            }

            var fields = fieldspanel.FindAllChildren(cf => cf.ByAutomationId("myScrollViewer"));
            if (!isMiniPlayer && fields.Length != 2)
            {
                // does this shit even happen
                Trace.WriteLine("[debug - am scraper] bad song field count for full player, returning no song??");
                return null;
            }

            var songNameBAR = fields[0];
            var songAlbumArtistBAR = fields[1];

            if (songNameBAR.BoundingRectangle.Bottom > songAlbumArtistBAR.BoundingRectangle.Bottom)
            {
                // swap if order bad
                var temp = songNameBAR;
                songNameBAR = songAlbumArtistBAR;
                songAlbumArtistBAR = temp;
            }

            var songName = songNameBAR.Name;
            var songAlbumArtist = songAlbumArtistBAR.Name;

            var (artist, album, _) = ParseSongAlbumArtist(songAlbumArtist);

            var songInfo = new AppleMusicInfo(songName, songAlbumArtist, album, artist);

            // out of all things to fuck up it's this
            var ppbutton = songPanel.FindFirstChild(cf => cf.ByAutomationId("TransportControl_PlayPauseStop"));
            if (ppbutton != null)
            {
                //Trace.WriteLine($"[Scraper] play/pause name: '{playPauseButton.Name}'");
                // if button is PLAY, the shit is paused
                songInfo.IsPaused = ppbutton.Name == "Play";
            }
            else
            {
                Trace.WriteLine("[debug - am scraper] play/pause not found somehow, so defaulting to paused");
                songInfo.IsPaused = true;
            }

            songInfo.AlbumCoverBase64 = await AlbumCoverCache.GetAlbumCoverBase64(artist, album);

            return songInfo;
        }

        // json escape characters were getting there so had to do this shit
        private static (string artist, string album, string? performer) ParseSongAlbumArtist(string songalbumartist)
        {
            var parts = songalbumartist.Split(" \u2014 ");

            if (parts.Length >= 2)
            {
                return (parts[0], parts[1], null);
            }
            else
            {
                var dashParts = songalbumartist.Split(" - ");
                if (dashParts.Length >= 2)
                    return (dashParts[0], dashParts[1], null);

                return (songalbumartist, songalbumartist, null);
            }
        }

        internal static class AlbumCoverCache
        {
            private static ConcurrentDictionary<string, string> cache = new();

            // cache cause it's useless getting the same cover over and over
            public static async Task<string?> GetAlbumCoverBase64(string artist, string album)
            {
                string key = $"{artist}|{album}".ToLowerInvariant();

                if (cache.TryGetValue(key, out var cached))
                    return cached;

                var coverb64 = await FetchAlbumB64iTunes(artist, album);
                if (coverb64 != null)
                {
                    cache[key] = coverb64;
                }
                return coverb64;
            }

            private static async Task<string?> FetchAlbumB64iTunes(string artist, string album)
            {
                try
                {
                    using var http = new HttpClient();
                    string query = WebUtility.UrlEncode($"{artist} {album}");
                    string url = $"https://itunes.apple.com/search?term={query}&media=music&entity=album&limit=1";

                    var response = await http.GetStringAsync(url);
                    using var doc = JsonDocument.Parse(response);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("results", out var results) && results.GetArrayLength() > 0)
                    {
                        var first = results[0];
                        if (first.TryGetProperty("artworkUrl100", out var artworkUrl100))
                        {
                            string arturl = artworkUrl100.GetString()!;
                            arturl = arturl.Replace("100x100bb.jpg", "600x600bb.jpg");

                            var bytes = await http.GetByteArrayAsync(arturl);
                            string base64 = Convert.ToBase64String(bytes);
                            return base64;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[debug - album] error getting album cover: {ex}");
                }
                return null;
            }
        }
    }
}
