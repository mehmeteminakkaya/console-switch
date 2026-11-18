// Talks to external monitors over DDC/CI through Windows' own dxva2 API.
// VCP 0x60 = input source, 0x62 = speaker volume. Timing report = is there a picture on the active input.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace ConsoleSwitch
{
    public class MonitorInfo
    {
        public string Model;       // from the capabilities string, e.g. "VG249Q3A"; falls back to the Windows name
        public string FriendlyName;
        public string GdiDevice;   // e.g. \\.\DISPLAY2
        public int PhysicalIndex;
        public List<int> Inputs = new List<int>();
        public bool HasVolume;
        public string Key { get { return GdiDevice + "#" + PhysicalIndex; } }
        public override string ToString() { return Model; }
    }

    public static class Ddc
    {
        public const byte VCP_INPUT = 0x60;
        public const byte VCP_VOLUME = 0x62;

        // One DDC/CI conversation at a time: monitors drop or garble overlapping commands.
        static readonly object ioLock = new object();

        // Capabilities strings take ~1 s to read, so they are cached per display path until displays change.
        static readonly Dictionary<string, string> capsCache = new Dictionary<string, string>();

        public static void ClearCache() { lock (ioLock) capsCache.Clear(); }

        public static string InputName(int code)
        {
            switch (code)
            {
                case 0x01: return "VGA 1";
                case 0x02: return "VGA 2";
                case 0x03: return "DVI 1";
                case 0x04: return "DVI 2";
                case 0x0F: return "DisplayPort 1";
                case 0x10: return "DisplayPort 2";
                case 0x11: return "HDMI 1";
                case 0x12: return "HDMI 2";
                case 0x13: return "HDMI 3";
                case 0x1B: return "USB-C";
                default: return "Input 0x" + code.ToString("X2");
            }
        }

        // ---------- enumeration ----------

        delegate bool PhysicalVisitor(IntPtr handle, string gdi, int index);

        // Calls visit() for every physical monitor until it returns true. Handles are only valid inside visit().
        static void ForEachPhysical(PhysicalVisitor visit)
        {
            var hmons = new List<IntPtr>();
            Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (h, a, b, c) => { hmons.Add(h); return true; }, IntPtr.Zero);
            foreach (IntPtr hm in hmons)
            {
                var mi = new Native.MONITORINFOEX();
                mi.cbSize = Marshal.SizeOf(typeof(Native.MONITORINFOEX));
                Native.GetMonitorInfo(hm, ref mi);
                uint n;
                if (!Native.GetNumberOfPhysicalMonitorsFromHMONITOR(hm, out n) || n == 0) continue;
                var pms = new Native.PHYSICAL_MONITOR[n];
                if (!Native.GetPhysicalMonitorsFromHMONITOR(hm, n, pms)) continue;
                try
                {
                    for (int i = 0; i < pms.Length; i++)
                        if (visit(pms[i].hPhysicalMonitor, mi.szDevice, i)) return;
                }
                finally { Native.DestroyPhysicalMonitors(n, pms); }
            }
        }

        static string ReadCaps(IntPtr h, string key)
        {
            string caps;
            if (capsCache.TryGetValue(key, out caps)) return caps;
            caps = "";
            for (int attempt = 0; attempt < 3 && caps.Length == 0; attempt++)
            {
                uint len;
                if (Native.GetCapabilitiesStringLength(h, out len) && len > 0)
                {
                    var sb = new StringBuilder((int)len + 1);
                    if (Native.CapabilitiesRequestAndCapabilitiesReply(h, sb, len)) caps = sb.ToString();
                }
                if (caps.Length == 0) Thread.Sleep(200);
            }
            if (caps.Length > 0) capsCache[key] = caps;
            return caps;
        }

        public static string ParseModel(string caps)
        {
            var m = Regex.Match(caps ?? "", @"model\(([^)]*)\)", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value.Trim() : "";
        }

        // "vcp(02 04 60(0F 11 12) 62 ...)" -> [0x0F, 0x11, 0x12]
        public static List<int> ParseInputs(string caps)
        {
            var list = new List<int>();
            var vcp = Regex.Match(caps ?? "", @"vcp\((.*)\)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!vcp.Success) return list;
            var m = Regex.Match(vcp.Groups[1].Value, @"(?<![0-9A-Fa-f])60\s*\(([^)]*)\)");
            if (!m.Success) return list;
            foreach (Match v in Regex.Matches(m.Groups[1].Value, @"[0-9A-Fa-f]{1,2}"))
            {
                int code = Convert.ToInt32(v.Value, 16);
                if (code > 0 && !list.Contains(code)) list.Add(code);
            }
            return list;
        }

        public static bool CapsHasCode(string caps, string code)
        {
            var vcp = Regex.Match(caps ?? "", @"vcp\((.*)\)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!vcp.Success) return false;
            return Regex.IsMatch(vcp.Groups[1].Value, @"(?<![0-9A-Fa-f])" + code + @"(?![0-9A-Fa-f])", RegexOptions.IgnoreCase);
        }

        /// <summary>Every monitor that answers DDC/CI (laptop panels usually don't).</summary>
        public static List<MonitorInfo> Discover()
        {
            var found = new List<MonitorInfo>();
            var names = Displays.FriendlyNamesByGdi();
            lock (ioLock)
            {
                ForEachPhysical((h, gdi, idx) =>
                {
                    uint type, cur, max;
                    if (!Native.GetVCPFeatureAndVCPFeatureReply(h, VCP_INPUT, out type, out cur, out max))
                    {
                        Thread.Sleep(50);
                        if (!Native.GetVCPFeatureAndVCPFeatureReply(h, VCP_INPUT, out type, out cur, out max)) return false;
                    }
                    var info = new MonitorInfo { GdiDevice = gdi, PhysicalIndex = idx };
                    string friendly;
                    info.FriendlyName = names.TryGetValue(gdi, out friendly) ? friendly : "";
                    string caps = ReadCaps(h, info.Key);
                    info.Model = ParseModel(caps);
                    if (info.Model.Length == 0) info.Model = info.FriendlyName.Length > 0 ? info.FriendlyName : "Monitor on " + gdi.Replace(@"\\.\", "");
                    info.Inputs = ParseInputs(caps);
                    int current = (int)(cur & 0xFF);
                    if (current > 0 && !info.Inputs.Contains(current)) info.Inputs.Insert(0, current);
                    uint vt, vc, vm;
                    info.HasVolume = CapsHasCode(caps, "62") || Native.GetVCPFeatureAndVCPFeatureReply(h, VCP_VOLUME, out vt, out vc, out vm);
                    found.Add(info);
                    return false;
                });
            }
            return found;
        }

        // ---------- operations on the configured monitor ----------

        delegate T HandleFn<T>(IntPtr h);

        // Runs fn on the monitor whose model matches; if only one DDC monitor exists it is used regardless.
        static bool WithMonitor<T>(string model, HandleFn<T> fn, out T result)
        {
            T res = default(T);
            bool done = false;
            lock (ioLock)
            {
                var candidates = new List<KeyValuePair<string, IntPtr>>();
                IntPtr match = IntPtr.Zero;
                ForEachPhysical((h, gdi, idx) =>
                {
                    uint t, c, m;
                    if (!Native.GetVCPFeatureAndVCPFeatureReply(h, VCP_INPUT, out t, out c, out m)) return false;
                    string key = gdi + "#" + idx;
                    string caps;
                    bool isMatch = !string.IsNullOrEmpty(model) && capsCache.TryGetValue(key, out caps) &&
                                   string.Equals(ParseModel(caps), model, StringComparison.OrdinalIgnoreCase);
                    if (isMatch || candidates.Count == 0 && string.IsNullOrEmpty(model))
                    {
                        res = fn(h); done = true; return true;
                    }
                    candidates.Add(new KeyValuePair<string, IntPtr>(key, h));
                    return false;
                });
                if (!done)
                {
                    // Model not cached yet (or cache cleared after a display change): read caps, then retry once.
                    ForEachPhysical((h, gdi, idx) =>
                    {
                        string key = gdi + "#" + idx;
                        uint t, c, m;
                        if (!Native.GetVCPFeatureAndVCPFeatureReply(h, VCP_INPUT, out t, out c, out m)) return false;
                        string caps = ReadCaps(h, key);
                        if (string.IsNullOrEmpty(model) || string.Equals(ParseModel(caps), model, StringComparison.OrdinalIgnoreCase)
                            || ParseModel(caps).Length == 0)
                        {
                            res = fn(h); done = true; return true;
                        }
                        return false;
                    });
                }
            }
            result = res;
            return done;
        }

        /// <summary>Current value of a VCP code, or -1 if the monitor didn't answer (busy while switching).</summary>
        public static int Get(string model, byte code)
        {
            int value;
            if (!WithMonitor(model, h =>
            {
                uint t, c, m;
                return Native.GetVCPFeatureAndVCPFeatureReply(h, code, out t, out c, out m) ? (int)(c & (code == VCP_INPUT ? 0xFFu : 0xFFFFu)) : -1;
            }, out value)) return -1;
            return value;
        }

        public static bool Set(string model, byte code, int value)
        {
            bool ok;
            if (!WithMonitor(model, h => Native.SetVCPFeature(h, code, (uint)value), out ok)) return false;
            return ok;
        }

        /// <summary>1 = picture on the active input, 0 = no signal, -1 = couldn't read.</summary>
        public static int Signal(string model)
        {
            int value;
            if (!WithMonitor(model, h =>
            {
                Native.MC_TIMING_REPORT r;
                if (!Native.GetTimingReport(h, out r)) return -1;
                return (r.dwHorizontalFrequencyInHZ == 0 && r.dwVerticalFrequencyInHZ == 0) ? 0 : 1;
            }, out value)) return -1;
            return value;
        }
    }
}
