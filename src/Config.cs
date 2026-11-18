// Settings in %APPDATA%\ConsoleSwitch\settings.ini, plus the log file.
using System;
using System.Collections.Generic;
using System.IO;

namespace ConsoleSwitch
{
    public enum LayoutMode { Auto, Laptop, DontTouch }

    public class Config
    {
        public string MonitorModel = "";
        public int PcInput;
        public int ConsoleInput;
        public string ConsoleMac = "";
        public string ConsoleName = "";
        public string ConsoleIp = "";
        public LayoutMode Layout = LayoutMode.Auto;
        public bool AutoSwitch = true;
        public int NoSignalSeconds = 2;

        public bool IsComplete
        {
            get { return MonitorModel.Length > 0 && PcInput > 0 && ConsoleInput > 0 && PcInput != ConsoleInput && ConsoleMac.Length == 12; }
        }

        public static string Dir
        {
            get
            {
                string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ConsoleSwitch");
                Directory.CreateDirectory(d);
                return d;
            }
        }

        static string FilePath { get { return Path.Combine(Dir, "settings.ini"); } }

        public static Config Load()
        {
            var c = new Config();
            try
            {
                if (!File.Exists(FilePath)) return c;
                var kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string raw in File.ReadAllLines(FilePath))
                {
                    string line = raw.Trim();
                    int eq = line.IndexOf('=');
                    if (line.StartsWith("#") || eq < 0) continue;
                    kv[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
                string v;
                if (kv.TryGetValue("MonitorModel", out v)) c.MonitorModel = v;
                if (kv.TryGetValue("PcInput", out v)) c.PcInput = ParseInt(v);
                if (kv.TryGetValue("ConsoleInput", out v)) c.ConsoleInput = ParseInt(v);
                if (kv.TryGetValue("ConsoleMac", out v)) c.ConsoleMac = Network.NormalizeMac(v);
                if (kv.TryGetValue("ConsoleName", out v)) c.ConsoleName = v;
                if (kv.TryGetValue("ConsoleIp", out v)) c.ConsoleIp = v;
                if (kv.TryGetValue("Layout", out v)) { LayoutMode m; if (Enum.TryParse(v, true, out m)) c.Layout = m; }
                if (kv.TryGetValue("AutoSwitch", out v)) c.AutoSwitch = !v.Equals("false", StringComparison.OrdinalIgnoreCase);
                if (kv.TryGetValue("NoSignalSeconds", out v)) { int s = ParseInt(v); if (s >= 1 && s <= 30) c.NoSignalSeconds = s; }
            }
            catch (Exception ex) { Log.Write("Could not read settings: " + ex.Message); }
            return c;
        }

        static int ParseInt(string v)
        {
            int n;
            if (v.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return Convert.ToInt32(v.Substring(2), 16);
            return int.TryParse(v, out n) ? n : 0;
        }

        public void Save()
        {
            try
            {
                File.WriteAllText(FilePath,
                    "# Console Switch settings. Run Settings from the tray menu to change them.\r\n" +
                    "MonitorModel=" + MonitorModel + "\r\n" +
                    "PcInput=0x" + PcInput.ToString("X2") + "\r\n" +
                    "ConsoleInput=0x" + ConsoleInput.ToString("X2") + "\r\n" +
                    "ConsoleMac=" + ConsoleMac + "\r\n" +
                    "ConsoleName=" + ConsoleName + "\r\n" +
                    "# Last known IP; updated automatically when the console moves.\r\n" +
                    "ConsoleIp=" + ConsoleIp + "\r\n" +
                    "# Auto = change the Windows layout on laptops only; Laptop = always; DontTouch = never.\r\n" +
                    "Layout=" + Layout + "\r\n" +
                    "AutoSwitch=" + (AutoSwitch ? "true" : "false") + "\r\n" +
                    "# Switch back to the PC after the console's picture has been gone this many seconds.\r\n" +
                    "NoSignalSeconds=" + NoSignalSeconds + "\r\n");
            }
            catch (Exception ex) { Log.Write("Could not save settings: " + ex.Message); }
        }
    }

    public static class Log
    {
        const long MaxBytes = 1024 * 1024;
        static readonly object gate = new object();
        public static string FilePath { get { return Path.Combine(Config.Dir, "log.txt"); } }

        public static void Write(string msg)
        {
            string line = string.Format("[{0:yyyy-MM-dd HH:mm:ss}] {1}", DateTime.Now, msg);
            lock (gate)
            {
                try
                {
                    var fi = new FileInfo(FilePath);
                    if (fi.Exists && fi.Length > MaxBytes)
                    {
                        string old = FilePath + ".old";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(FilePath, old);
                    }
                    File.AppendAllText(FilePath, line + Environment.NewLine);
                }
                catch { }
            }
        }
    }
}
