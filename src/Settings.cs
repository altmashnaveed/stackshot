// Stackshot - Settings in %LOCALAPPDATA%\Stackshot\settings.ini (plain key = value).
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.IO;
using System.Text;

namespace Stackshot
{
    public class Settings
    {
        static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        // The recording tests (tests\cine\e2e) run the real recorder against a private folder; FFmpeg stays shared.
        public static readonly string DataDir = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("STACKSHOT_TEST_DATA"))
            ? Path.Combine(LocalAppData, "Stackshot") : Environment.GetEnvironmentVariable("STACKSHOT_TEST_DATA");
        public static readonly string TempDir = Path.Combine(DataDir, "temp");
        public static readonly string FilePath = Path.Combine(DataDir, "settings.ini");
        public static readonly string LogFile = Path.Combine(DataDir, "stackshot.log");
        public static readonly string FfmpegDir = Path.Combine(LocalAppData, "Stackshot", "ffmpeg");
        public static readonly string InstallDir = Path.Combine(LocalAppData, "Programs", "Stackshot");
        public static readonly string InstalledExe = Path.Combine(InstallDir, "Stackshot.exe");

        // Where kept captures go (everything else is temporary and cleaned up).
        public string SaveFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Stackshot");
        public bool Sound = true;
        public bool CopyToClipboard = true;
        public bool FollowMouse = true;
        public bool FirstRunDone;
        // Hotkeys: one or more combos per action, comma-separated (e.g. "PrintScreen, Ctrl+Shift+Alt+4").
        public string HotRegion = "PrintScreen";
        public string HotScreen = "Ctrl+PrintScreen";
        public string HotWindow = "Alt+PrintScreen";
        public string HotScroll = "Ctrl+Alt+PrintScreen";
        public string HotVideo = "Shift+PrintScreen";
        public string HotGif = "Ctrl+Shift+PrintScreen";
        public string HotText = "Ctrl+Shift+Alt+T";
        public string HotMarkdown = "Ctrl+Shift+Alt+M";
        public string HotRepeat = "";       // capture the last area again; no shortcut unless the user gives it one
        public int VideoFps = 30;
        public int GifFps = 15;
        public int VideoQuality = 0;    // 0 standard, 1 maximum (60 fps, lossless capture, slow encode), 2 high (60 fps, crisp), 3 cinema (lossless RGB H.264 + PCM in MKV, verified)
        public bool RecordSystemAudio;  // what the computer plays
        public bool RecordMic;
        public string MicDevice = "";   // Core Audio endpoint id; empty = Windows' default microphone
        public string SystemAudioDevice = ""; // Core Audio endpoint id of the output recorded as "computer sound"; empty = Windows' default
        public int Webcam = 0;          // camera bubble while recording video: 0 off, 1 round, 2 square
        public int WebcamSize = 1;      // 0 small, 1 medium, 2 large
        public string WebcamDevice = ""; // DirectShow name; empty = first camera
        public string Ffmpeg = "";      // custom ffmpeg.exe instead of the downloaded one
        // Editor presentation backdrop. BgPadding and BgRadius range 0-100 (see Backdrop).
        public bool BgAuto = false;     // open the editor with the backdrop applied
        public int BgPreset = 0;
        public int BgPadding = 50;
        public int BgRadius = 40;
        public bool BgShadow = true;
        public string BgRatio = "auto"; // auto, 16:9, 4:3 or 1:1
        // Main window and mascot.
        public int Appearance = 0;          // 0 follow Windows, 1 light, 2 dark
        public bool AllInOne = false;       // area capture: adjust the area and choose image, video, GIF or scrolling before capturing
        public bool ShowRecent = true;      // Home shows the recent captures (off: no strip and no thumbnails are loaded)
        public int TempKeep = 0;            // how long temporary captures stay: 0 an hour, 1 until Stackshot closes, 2 a day, 3 a week, 4 a month
        public int HomeStyle = 0;           // main window home: 0 minimal, 1 widgets, 2 scene
        public bool CloseToTray = true;     // closing the window keeps Stackshot in the tray
        public int Profile = 1;          // 0 performance, 1 balanced, 2 everything (see HomeWindow.ApplyProfile)
        public bool ShowIntro = true;       // launch animation
        public bool MascotOn = true;
        public string MascotName = "Pixel";
        public int MascotColor = 0;
        public bool MascotTalks = true;     // speech bubbles with tips and greetings
        // Mascot look; indices into the MascotParts catalogs.
        public int MascotKind = 0;
        public int MascotHat = 0;
        public int MascotOutfit = 0;
        public int MascotFace = 0;
        public int MascotEyes = 0;
        public int MascotPersonality = 0;
        public bool MascotSeasonal = true;  // seasonal costume when no hat is chosen
        public bool MascotDesktop = false;  // also live on the desktop, above the taskbar
        public bool MascotClimb = true;     // the desktop mascot hops onto the foreground window
        public bool MascotAllScreens = true; // the desktop mascot roams across every monitor
        public int MascotSpot = 720;        // where the desktop mascot stands, in thousandths of its monitor width
        public int MascotLove = 0;          // friendship points (one per capture)
        public string MascotFavorites = ""; // favorite items, "slot:index" separated by commas; they come first in the grids
        public string MascotLooks = "";     // saved looks, MascotLook keys separated by |; newest first
        public int MascotAccessory = 0;     // accessory worn over the look (glasses, scarves...), index into MascotParts.AccessoryNames
        public int MascotEnergy = 1;        // how often it does things on its own: 0 calm, 1 normal, 2 lively
        public int MascotChatter = 1;       // how talkative: 0 little, 1 normal, 2 a lot
        public int MascotAlone = 15;        // what it does alone, MascotCmd.Alone* bits
        public bool MascotSleepIdle = true; // dozes off when ignored
        // Updates.
        public bool CheckUpdates = true;
        public string LastUpdateCheck = "";
        public bool AutoUpdate = false;     // download, verify and install new versions on its own
        public string UpdateNotified = "";  // release tag already announced (each version is announced once)
        // Recording package: Cine options.
        public int CineFps = 60;            // 60 or 30, for any area
        public int CinePeakMemory = 0;      // memory for motion peaks: 0 normal (min(2 GiB, free/4)), 1 high (min(8 GiB, free/2))
        public bool RecordSetup = true;     // after choosing the area: show the take's options and a Grabar button
        public int RecordCountdown = 3;     // seconds counted down before recording starts: 0, 3, 5 or 10
        // Player and export.
        public bool PlayerOpensVideos = true; // videos open in Stackshot's player (off: Windows' default app; Cine masters always in Stackshot)
        public int PlayerScaling = 0;       // picture scaling: 0 best (Lanczos), 1 fast (bilinear)
        public bool PlayerFill;             // enlarge the picture to fill the window; off: never above its real size (sharpest)
        public bool XmlWatch;               // open the XML files that appear in XmlWatchFolder (Egara's C:\egarsat\temp when empty)
        public string XmlWatchFolder = "";
        public int PlayerVolume = 100;      // 0..100
        public bool PlayerLoop = false;
        public int ExportQuality = 0;       // MP4 made by the editor: 0 maximum, 1 high, 2 light, 3 up to 16 MB, 4 up to 25 MB
        // Captures.
        public const string DefaultNameTemplate = "{tipo} {fecha} {hora}";
        public bool CaptureCursor;          // draw the mouse pointer into screenshots (where it was when the capture started)
        public string FileNameTemplate = DefaultNameTemplate; // file name of each capture ({tipo} {fecha} {hora} {app} {ventana} ...)
        public int SaveSubfolder = 0;       // kept captures go to: 0 the save folder, 1 a folder per month (2026-10), 2 per year and month (2026\10)
        public bool SensitiveAuto;          // cover the personal data (DNI, IBAN, emails...) that OCR finds in every new capture
        public bool HistorySearch = true;   // read the text of every capture, locally, so the history can be searched by it
        // Recording overlays.
        public bool RecordClicks;           // draw a ring where the mouse is clicked
        public bool RecordKeys;             // show the keys pressed (shortcuts) at the bottom of the video

        // Every save keeps the file it replaces as settings.ini.bak, used when the current one can't be read or came out
        // empty or zeroed (a power cut right after a save), so settings are never silently reset to the defaults.
        public static Settings Load()
        {
            Settings s = Load(FilePath);
            Policy.Apply(s); // what IT forces; the file keeps the user's own choices
            return s;
        }

        // Reads a settings file; a missing, unreadable or meaningless one falls back to its backup, then to the defaults.
        public static Settings Load(string path)
        {
            Settings s = new Settings();
            string[] lines = Read(path);
            if (lines != null && s.Parse(lines) > 0) return s;
            string bak = path + ".bak";
            string[] old = Read(bak);
            if (old != null)
            {
                Settings b = new Settings();
                if (b.Parse(old) > 0)
                {
                    if (lines != null || File.Exists(path)) ShotStack.Log("Ajustes: " + Path.GetFileName(path) + " no se pudo leer; se usa la copia anterior");
                    lock (saveLock) badFile = path; // its next save must not turn the broken file into the backup
                    return b;
                }
            }
            return s;
        }

        // The file's lines, or null if it doesn't exist or stays unreadable (another process briefly holding it).
        static string[] Read(string path)
        {
            for (int attempt = 0; ; attempt++)
            {
                try { return File.Exists(path) ? Lines(File.ReadAllBytes(path)) : null; }
                catch (Exception ex)
                {
                    if (attempt >= 4 || !(ex is IOException || ex is UnauthorizedAccessException))
                    {
                        ShotStack.Log("Ajustes: " + ex.Message);
                        return null;
                    }
                    System.Threading.Thread.Sleep(40);
                }
            }
        }

        // UTF-8 (with or without BOM) or UTF-16 with BOM; a file that is not valid UTF-8 is taken as Windows-1252 (ANSI).
        static string[] Lines(byte[] b)
        {
            string text;
            if (b.Length >= 2 && ((b[0] == 0xFF && b[1] == 0xFE) || (b[0] == 0xFE && b[1] == 0xFF)))
                using (StreamReader r = new StreamReader(new MemoryStream(b), Encoding.UTF8, true)) text = r.ReadToEnd();
            else
            {
                try { text = new UTF8Encoding(false, true).GetString(b); }
                catch (DecoderFallbackException) { text = Encoding.GetEncoding(1252).GetString(b); }
            }
            if (text.Length > 0 && text[0] == (char)0xFEFF) text = text.Substring(1);
            return text.Split(new string[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
        }

        // Applies key = value lines; returns how many known keys were found.
        int Parse(string[] lines)
        {
            int known = 0;
            Settings s = this;
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string k = line.Substring(0, eq).Trim().ToLowerInvariant(), v = line.Substring(eq + 1).Trim();
                if (k.IndexOf('\0') >= 0 || v.IndexOf('\0') >= 0) continue;
                known++;
                try
                {
                    switch (k)
                    {
                        case "savefolder": v = Unquote(v); if (v.Length > 0) s.SaveFolder = Folder(v, s.SaveFolder); break;
                        case "sound": s.Sound = Bool(v, s.Sound); break;
                        case "copytoclipboard": s.CopyToClipboard = Bool(v, s.CopyToClipboard); break;
                        case "followmouse": s.FollowMouse = Bool(v, s.FollowMouse); break;
                        case "firstrundone": s.FirstRunDone = Bool(v, s.FirstRunDone); break;
                        case "region": s.HotRegion = v; break;
                        case "screen": s.HotScreen = v; break;
                        case "window": s.HotWindow = v; break;
                        case "scroll": s.HotScroll = v; break;
                        case "appearance": s.Appearance = Int(v, s.Appearance, 0, 2); break;
                        case "allinone": s.AllInOne = Bool(v, s.AllInOne); break;
                        case "showrecent": s.ShowRecent = Bool(v, s.ShowRecent); break;
                        case "tempkeep": s.TempKeep = Int(v, s.TempKeep, 0, 4); break;
                        case "homestyle": s.HomeStyle = Int(v, s.HomeStyle, 0, 2); break;
                        case "closetotray": s.CloseToTray = Bool(v, s.CloseToTray); break;
                        case "profile": s.Profile = Int(v, s.Profile, 0, 2); break;
                        case "showintro": s.ShowIntro = Bool(v, s.ShowIntro); break;
                        case "mascot": s.MascotOn = Bool(v, s.MascotOn); break;
                        case "mascotname": if (v.Length > 0) s.MascotName = Cut(v, 16); break;
                        case "mascotcolor": s.MascotColor = Int(v, s.MascotColor, 0, 99); break;
                        case "mascottalks": s.MascotTalks = Bool(v, s.MascotTalks); break;
                        case "mascotkind": s.MascotKind = Int(v, s.MascotKind, 0, 99); break;
                        case "mascothat": s.MascotHat = Int(v, s.MascotHat, 0, 99); break;
                        case "mascotoutfit": s.MascotOutfit = Int(v, s.MascotOutfit, 0, 99); break;
                        case "mascotface": s.MascotFace = Int(v, s.MascotFace, 0, 99); break;
                        case "mascoteyes": s.MascotEyes = Int(v, s.MascotEyes, 0, MascotParts.EyeNames.Length - 1); break;
                        case "mascotpersonality": s.MascotPersonality = Int(v, s.MascotPersonality, 0, MascotParts.Personalities.Length - 1); break;
                        case "mascotseasonal": s.MascotSeasonal = Bool(v, s.MascotSeasonal); break;
                        case "mascotdesktop": s.MascotDesktop = Bool(v, s.MascotDesktop); break;
                        case "mascotclimb": s.MascotClimb = Bool(v, s.MascotClimb); break;
                        case "mascotallscreens": s.MascotAllScreens = Bool(v, s.MascotAllScreens); break;
                        case "mascotspot": s.MascotSpot = Int(v, s.MascotSpot, 0, 1000); break;
                        case "mascotlove": s.MascotLove = Int(v, s.MascotLove, 0, 1000000); break;
                        case "mascotfavorites": s.MascotFavorites = v; break;
                        case "mascotlooks": s.MascotLooks = v; break;
                        case "mascotaccessory": s.MascotAccessory = Int(v, s.MascotAccessory, 0, 99); break;
                        case "mascotenergy": s.MascotEnergy = Int(v, s.MascotEnergy, 0, 2); break;
                        case "mascotchatter": s.MascotChatter = Int(v, s.MascotChatter, 0, 2); break;
                        case "mascotalone": s.MascotAlone = Int(v, s.MascotAlone, 0, 15); break;
                        case "mascotsleepidle": s.MascotSleepIdle = Bool(v, s.MascotSleepIdle); break;
                        case "checkupdates": s.CheckUpdates = Bool(v, s.CheckUpdates); break;
                        case "lastupdatecheck": s.LastUpdateCheck = v; break;
                        case "autoupdate": s.AutoUpdate = Bool(v, s.AutoUpdate); break;
                        case "updatenotified": s.UpdateNotified = v; break;
                        case "video": s.HotVideo = v; break;
                        case "gif": s.HotGif = v; break;
                        case "markdown": s.HotMarkdown = v; break;
                        case "text": s.HotText = v; break;
                        case "repeat": s.HotRepeat = v; break;
                        case "videofps": s.VideoFps = Int(v, s.VideoFps, 5, 60); break;
                        case "giffps": s.GifFps = Int(v, s.GifFps, 5, 30); break;
                        case "videoquality": s.VideoQuality = Int(v, s.VideoQuality, 0, 3); break;
                        case "systemaudio": s.RecordSystemAudio = Bool(v, s.RecordSystemAudio); break;
                        case "microphone": s.RecordMic = Bool(v, s.RecordMic); break;
                        case "micdevice": s.MicDevice = v; break;
                        case "systemaudiodevice": s.SystemAudioDevice = v; break;
                        case "webcam": s.Webcam = Int(v, s.Webcam, 0, 2); break;
                        case "webcamsize": s.WebcamSize = Int(v, s.WebcamSize, 0, 2); break;
                        case "webcamdevice": s.WebcamDevice = v; break;
                        case "ffmpeg": s.Ffmpeg = Unquote(v); break;
                        case "bgauto": s.BgAuto = Bool(v, s.BgAuto); break;
                        case "bgpreset": s.BgPreset = Int(v, s.BgPreset, 0, 99); break;
                        case "bgpadding": s.BgPadding = Int(v, s.BgPadding, 0, 100); break;
                        case "bgradius": s.BgRadius = Int(v, s.BgRadius, 0, 100); break;
                        case "bgshadow": s.BgShadow = Bool(v, s.BgShadow); break;
                        case "bgratio": if (v == "auto" || v == "16:9" || v == "4:3" || v == "1:1") s.BgRatio = v; break;
                        // Recording package.
                        case "cinefps": s.CineFps = Int(v, s.CineFps, 1, 1000) >= 45 ? 60 : 30; break;
                        case "cinepeakmemory": s.CinePeakMemory = Int(v, s.CinePeakMemory, 0, 1); break;
                        case "recordsetup": s.RecordSetup = Bool(v, s.RecordSetup); break;
                        case "recordcountdown": s.RecordCountdown = Countdown(Int(v, s.RecordCountdown, 0, 60)); break;
                        case "playeropensvideos": s.PlayerOpensVideos = Bool(v, s.PlayerOpensVideos); break;
                        case "playerscaling": s.PlayerScaling = Int(v, s.PlayerScaling, 0, 1); break;
                        case "playerfill": s.PlayerFill = Bool(v, s.PlayerFill); break;
                        case "xmlwatch": s.XmlWatch = Bool(v, s.XmlWatch); break;
                        case "xmlwatchfolder": v = Unquote(v); s.XmlWatchFolder = v.Length > 0 ? Folder(v, "") : ""; break;
                        case "capturecursor": s.CaptureCursor = Bool(v, s.CaptureCursor); break;
                        case "filenametemplate": v = Unquote(v).Trim(); if (v.Length > 0) s.FileNameTemplate = v; break;
                        case "savesubfolder": s.SaveSubfolder = Int(v, s.SaveSubfolder, 0, 2); break;
                        case "sensitiveauto": s.SensitiveAuto = Bool(v, s.SensitiveAuto); break;
                        case "historysearch": s.HistorySearch = Bool(v, s.HistorySearch); break;
                        case "recordclicks": s.RecordClicks = Bool(v, s.RecordClicks); break;
                        case "recordkeys": s.RecordKeys = Bool(v, s.RecordKeys); break;
                        case "playervolume": s.PlayerVolume = Int(v, s.PlayerVolume, 0, 100); break;
                        case "playerloop": s.PlayerLoop = Bool(v, s.PlayerLoop); break;
                        case "exportquality": s.ExportQuality = Int(v, s.ExportQuality, 0, 4); break;
                        default: known--; break;
                    }
                }
                catch (Exception ex) { ShotStack.Log("Ajustes (" + k + "): " + ex.Message); }
            }
            return known;
        }

        // At most n UTF-16 units, never ending in half of a surrogate pair.
        static string Cut(string v, int n)
        {
            if (v.Length <= n) return v;
            if (char.IsHighSurrogate(v[n - 1])) n--;
            return v.Substring(0, n);
        }

        // A path pasted with Explorer's "Copy as path" comes in quotes.
        static string Unquote(string v)
        {
            return v.Length >= 2 && v[0] == '"' && v[v.Length - 1] == '"' ? v.Substring(1, v.Length - 2).Trim() : v;
        }

        // A save folder must be a full path that Windows accepts; anything else keeps the default.
        internal static string Folder(string v, string fallback)
        {
            try
            {
                string p = Environment.ExpandEnvironmentVariables(v);
                bool drive = p.Length >= 3 && p[1] == ':' && (p[2] == '\\' || p[2] == '/'), unc = p.StartsWith("\\\\", StringComparison.Ordinal);
                if (!drive && !unc) return fallback;
                return Path.GetFullPath(p);
            }
            catch { return fallback; }
        }

        // With --test (and in tooling) settings are read but never written.
        public static bool ReadOnly;

        static readonly object saveLock = new object();
        static string badFile;  // a settings file that was unreadable when loaded (its backup was used instead)
        internal Policy.Kept PolicyKept; // the user's own values of what IT policies force (see Policy)

        public void Save()
        {
            if (ReadOnly) return;
            try
            {
                lock (saveLock) Policy.ForFile(this).Write(FilePath);
            }
            catch (Exception ex) { ShotStack.Log("Guardar ajustes: " + ex.Message); }
        }

        void Write(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            StringBuilder b = new StringBuilder();
            b.AppendLine("# Ajustes de Stackshot. Se pueden cambiar desde el icono de la bandeja > Ajustes.");
            b.AppendLine("SaveFolder = " + OneLine(SaveFolder));
            b.AppendLine("Sound = " + (Sound ? "1" : "0"));
            b.AppendLine("CopyToClipboard = " + (CopyToClipboard ? "1" : "0"));
            b.AppendLine("FollowMouse = " + (FollowMouse ? "1" : "0"));
            b.AppendLine("FirstRunDone = " + (FirstRunDone ? "1" : "0"));
            b.AppendLine("# Atajos (varios separados por comas). Ejemplos: PrintScreen, Ctrl+Shift+S, Alt+F1");
            b.AppendLine("Region = " + OneLine(HotRegion));
            b.AppendLine("Screen = " + OneLine(HotScreen));
            b.AppendLine("Window = " + OneLine(HotWindow));
            b.AppendLine("Scroll = " + OneLine(HotScroll));
            b.AppendLine("Video = " + OneLine(HotVideo));
            b.AppendLine("Gif = " + OneLine(HotGif));
            b.AppendLine("Text = " + OneLine(HotText));
            b.AppendLine("Markdown = " + OneLine(HotMarkdown));
            b.AppendLine("Repeat = " + OneLine(HotRepeat));
            b.AppendLine("VideoFps = " + VideoFps);
            b.AppendLine("GifFps = " + GifFps);
            b.AppendLine("Profile = " + Profile);
            b.AppendLine("VideoQuality = " + VideoQuality);
            b.AppendLine("SystemAudio = " + (RecordSystemAudio ? "1" : "0"));
            b.AppendLine("Microphone = " + (RecordMic ? "1" : "0"));
            b.AppendLine("MicDevice = " + OneLine(MicDevice));
            b.AppendLine("SystemAudioDevice = " + OneLine(SystemAudioDevice));
            b.AppendLine("Webcam = " + Webcam);
            b.AppendLine("WebcamSize = " + WebcamSize);
            b.AppendLine("WebcamDevice = " + OneLine(WebcamDevice));
            b.AppendLine("# Ruta a ffmpeg.exe (vac\u00EDo = el que descarga Stackshot la primera vez que grabas)");
            b.AppendLine("Ffmpeg = " + OneLine(Ffmpeg));
            b.AppendLine("# Fondo de presentaci\u00F3n del editor (margen y esquinas de 0 a 100; proporci\u00F3n: auto, 16:9, 4:3 o 1:1)");
            b.AppendLine("BgAuto = " + (BgAuto ? "1" : "0"));
            b.AppendLine("BgPreset = " + BgPreset);
            b.AppendLine("BgPadding = " + BgPadding);
            b.AppendLine("BgRadius = " + BgRadius);
            b.AppendLine("BgShadow = " + (BgShadow ? "1" : "0"));
            b.AppendLine("BgRatio = " + OneLine(BgRatio));
            b.AppendLine("# Tema: 0 como Windows, 1 claro, 2 oscuro");
            b.AppendLine("Appearance = " + Appearance);
            b.AppendLine("AllInOne = " + (AllInOne ? "1" : "0"));
            b.AppendLine("ShowRecent = " + (ShowRecent ? "1" : "0"));
            b.AppendLine("# Capturas temporales: 0 una hora, 1 hasta cerrar, 2 un d\u00EDa, 3 una semana, 4 un mes");
            b.AppendLine("TempKeep = " + TempKeep);
            b.AppendLine("HomeStyle = " + HomeStyle);
            b.AppendLine("CloseToTray = " + (CloseToTray ? "1" : "0"));
            b.AppendLine("ShowIntro = " + (ShowIntro ? "1" : "0"));
            b.AppendLine("Mascot = " + (MascotOn ? "1" : "0"));
            b.AppendLine("MascotName = " + OneLine(MascotName));
            b.AppendLine("MascotColor = " + MascotColor);
            b.AppendLine("MascotTalks = " + (MascotTalks ? "1" : "0"));
            b.AppendLine("MascotKind = " + MascotKind);
            b.AppendLine("MascotHat = " + MascotHat);
            b.AppendLine("MascotOutfit = " + MascotOutfit);
            b.AppendLine("MascotFace = " + MascotFace);
            b.AppendLine("MascotEyes = " + MascotEyes);
            b.AppendLine("MascotPersonality = " + MascotPersonality);
            b.AppendLine("MascotSeasonal = " + (MascotSeasonal ? "1" : "0"));
            b.AppendLine("MascotDesktop = " + (MascotDesktop ? "1" : "0"));
            b.AppendLine("MascotClimb = " + (MascotClimb ? "1" : "0"));
            b.AppendLine("MascotAllScreens = " + (MascotAllScreens ? "1" : "0"));
            b.AppendLine("MascotSpot = " + MascotSpot);
            b.AppendLine("MascotLove = " + MascotLove);
            b.AppendLine("MascotFavorites = " + OneLine(MascotFavorites));
            b.AppendLine("MascotLooks = " + OneLine(MascotLooks));
            b.AppendLine("MascotAccessory = " + MascotAccessory);
            b.AppendLine("MascotEnergy = " + MascotEnergy);
            b.AppendLine("MascotChatter = " + MascotChatter);
            b.AppendLine("MascotAlone = " + MascotAlone);
            b.AppendLine("MascotSleepIdle = " + (MascotSleepIdle ? "1" : "0"));
            b.AppendLine("CheckUpdates = " + (CheckUpdates ? "1" : "0"));
            b.AppendLine("LastUpdateCheck = " + OneLine(LastUpdateCheck));
            b.AppendLine("AutoUpdate = " + (AutoUpdate ? "1" : "0"));
            b.AppendLine("UpdateNotified = " + OneLine(UpdateNotified));
            b.AppendLine("# Cine: fotogramas por segundo (60 o 30) y memoria para picos de movimiento (0 normal, 1 alta)");
            b.AppendLine("CineFps = " + (CineFps == 30 ? 30 : 60));
            b.AppendLine("CinePeakMemory = " + CinePeakMemory);
            b.AppendLine("# Antes de grabar: opciones de la toma (1/0) y cuenta atr\u00E1s en segundos (0, 3, 5 o 10)");
            b.AppendLine("RecordSetup = " + (RecordSetup ? "1" : "0"));
            b.AppendLine("RecordCountdown = " + Countdown(RecordCountdown));
            b.AppendLine("# Reproductor: abrir los v\u00EDdeos en Stackshot (1/0), escalado (0 mejor, 1 r\u00E1pido), ampliar para llenar la ventana (1/0; 0 = tama\u00F1o real), volumen 0-100, repetir (1/0)");
            b.AppendLine("PlayerOpensVideos = " + (PlayerOpensVideos ? "1" : "0"));
            b.AppendLine("PlayerScaling = " + PlayerScaling);
            b.AppendLine("PlayerFill = " + (PlayerFill ? "1" : "0"));
            b.AppendLine("PlayerVolume = " + PlayerVolume);
            b.AppendLine("PlayerLoop = " + (PlayerLoop ? "1" : "0"));
            b.AppendLine("# MP4 del editor: 0 m\u00E1xima, 1 alta, 2 ligera, 3 hasta 16 MB (WhatsApp), 4 hasta 25 MB (correo)");
            b.AppendLine("ExportQuality = " + ExportQuality);
            b.AppendLine("# Visor XML: abrir solos los XML nuevos de una carpeta (1/0) y esa carpeta (vac\u00EDa = la de Egara, C:\\egarsat\\temp)");
            b.AppendLine("XmlWatch = " + (XmlWatch ? "1" : "0"));
            b.AppendLine("XmlWatchFolder = " + OneLine(XmlWatchFolder));
            b.AppendLine("# Capturas: puntero (1/0), nombre ({tipo} {fecha} {hora} {app} {ventana}), subcarpetas al guardar (0 no, 1 por mes, 2 por a\u00F1o y mes), tapar datos sensibles (1/0), buscar por el texto (1/0)");
            b.AppendLine("CaptureCursor = " + (CaptureCursor ? "1" : "0"));
            b.AppendLine("FileNameTemplate = " + OneLine(FileNameTemplate));
            b.AppendLine("SaveSubfolder = " + SaveSubfolder);
            b.AppendLine("SensitiveAuto = " + (SensitiveAuto ? "1" : "0"));
            b.AppendLine("HistorySearch = " + (HistorySearch ? "1" : "0"));
            b.AppendLine("# Grabaci\u00F3n: mostrar los clics (1/0) y las teclas pulsadas (1/0)");
            b.AppendLine("RecordClicks = " + (RecordClicks ? "1" : "0"));
            b.AppendLine("RecordKeys = " + (RecordKeys ? "1" : "0"));
            // Write to a temp file and swap it in, so a crash mid-write never leaves an empty file; the file it
            // replaces becomes the backup.
            string tmp = path + ".tmp", bak = path + ".bak";
            File.WriteAllText(tmp, b.ToString(), new UTF8Encoding(false));
            // A file known to be broken is replaced without becoming the backup, so the good backup stays.
            bool broken = string.Equals(path, badFile, StringComparison.OrdinalIgnoreCase);
            if (!File.Exists(path)) File.Move(tmp, path);
            else
            {
                for (int attempt = 0; ; attempt++)
                {
                    try { File.Replace(tmp, path, broken ? null : bak, true); break; }
                    catch (IOException)
                    {
                        if (attempt < 2) { System.Threading.Thread.Sleep(30); continue; }
                        // Some volumes and filters refuse the swap: overwrite in place instead.
                        File.Copy(tmp, path, true);
                        File.Delete(tmp);
                        break;
                    }
                }
            }
            if (broken) badFile = null;
        }

        // A line break in a value would inject extra keys into the file (e.g. a different Ffmpeg path).
        static string OneLine(string v)
        {
            return (v ?? "").Replace("\r", "").Replace("\n", "");
        }

        // Hotkey actions, in menu order.
        public static readonly string[] Actions = { "region", "repeat", "screen", "window", "scroll", "text", "video", "gif", "markdown" };

        public string HotkeysFor(string action)
        {
            switch (action)
            {
                case "region": return HotRegion;
                case "repeat": return HotRepeat;
                case "screen": return HotScreen;
                case "window": return HotWindow;
                case "scroll": return HotScroll;
                case "text": return HotText;
                case "markdown": return HotMarkdown;
                case "video": return HotVideo;
                default: return HotGif;
            }
        }

        public void SetHotkeys(string action, string value)
        {
            switch (action)
            {
                case "region": HotRegion = value; break;
                case "repeat": HotRepeat = value; break;
                case "screen": HotScreen = value; break;
                case "window": HotWindow = value; break;
                case "scroll": HotScroll = value; break;
                case "text": HotText = value; break;
                case "markdown": HotMarkdown = value; break;
                case "video": HotVideo = value; break;
                default: HotGif = value; break;
            }
        }

        public Settings Clone()
        {
            return (Settings)MemberwiseClone();
        }

        static bool Bool(string v, bool def)
        {
            v = v.ToLowerInvariant();
            if (v == "1" || v == "true" || v == "si" || v == "yes") return true;
            if (v == "0" || v == "false" || v == "no") return false;
            return def;
        }

        static int Int(string v, int def, int min, int max)
        {
            long n;
            if (long.TryParse(v, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out n))
                return (int)Math.Max(min, Math.Min(max, n));
            // A run of digits too long for a long still means "far past the limit".
            int digits = v.Length > 0 && (v[0] == '-' || v[0] == '+') ? 1 : 0;
            if (v.Length > digits)
            {
                for (int i = digits; i < v.Length; i++) if (v[i] < '0' || v[i] > '9') return def;
                return v[0] == '-' ? min : max;
            }
            return def;
        }

        // The nearest of the offered countdowns: 0, 3, 5 or 10 seconds.
        internal static int Countdown(int s)
        {
            return s <= 1 ? 0 : s <= 4 ? 3 : s <= 7 ? 5 : 10;
        }
    }
}
