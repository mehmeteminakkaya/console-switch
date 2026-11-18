// Windows display layout (extend / clone / laptop only) and display names.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace ConsoleSwitch
{
    public static class Displays
    {
        static string SourceName(Native.LUID adapter, uint id)
        {
            var req = new Native.DISPLAYCONFIG_SOURCE_DEVICE_NAME();
            req.header.type = 1;
            req.header.size = (uint)Marshal.SizeOf(req);
            req.header.adapterId = adapter;
            req.header.id = id;
            return Native.DisplayConfigGetDeviceInfo(ref req) == 0 ? req.viewGdiDeviceName : "";
        }

        static Native.DISPLAYCONFIG_TARGET_DEVICE_NAME TargetInfo(Native.LUID adapter, uint id)
        {
            var req = new Native.DISPLAYCONFIG_TARGET_DEVICE_NAME();
            req.header.type = 2;
            req.header.size = (uint)Marshal.SizeOf(req);
            req.header.adapterId = adapter;
            req.header.id = id;
            Native.DisplayConfigGetDeviceInfo(ref req);
            return req;
        }

        static bool Query(uint flags, out Native.DISPLAYCONFIG_PATH_INFO[] paths, out Native.DISPLAYCONFIG_MODE_INFO[] modes)
        {
            paths = null; modes = null;
            uint np, nm;
            if (Native.GetDisplayConfigBufferSizes(flags, out np, out nm) != 0) return false;
            paths = new Native.DISPLAYCONFIG_PATH_INFO[np];
            modes = new Native.DISPLAYCONFIG_MODE_INFO[nm];
            if (Native.QueryDisplayConfig(flags, ref np, paths, ref nm, modes, IntPtr.Zero) != 0) return false;
            Array.Resize(ref paths, (int)np);
            Array.Resize(ref modes, (int)nm);
            return true;
        }

        /// <summary>GDI device (\\.\DISPLAY2) -> monitor name, for active displays.</summary>
        public static Dictionary<string, string> FriendlyNamesByGdi()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Native.DISPLAYCONFIG_PATH_INFO[] paths; Native.DISPLAYCONFIG_MODE_INFO[] modes;
            if (!Query(Native.QDC_ONLY_ACTIVE_PATHS, out paths, out modes)) return map;
            foreach (var p in paths)
            {
                string gdi = SourceName(p.sourceInfo.adapterId, p.sourceInfo.id);
                string name = TargetInfo(p.targetInfo.adapterId, p.targetInfo.id).monitorFriendlyDeviceName ?? "";
                if (gdi.Length > 0 && !map.ContainsKey(gdi)) map[gdi] = name;
            }
            return map;
        }

        /// <summary>True when this PC has a built-in screen (a laptop). Layout changes only make sense then.</summary>
        public static bool HasInternalDisplay()
        {
            Native.DISPLAYCONFIG_PATH_INFO[] paths; Native.DISPLAYCONFIG_MODE_INFO[] modes;
            if (!Query(Native.QDC_ALL_PATHS, out paths, out modes)) return false;
            foreach (var p in paths)
            {
                uint tech = p.targetInfo.outputTechnology;
                // INTERNAL, LVDS(6), DISPLAYPORT_EMBEDDED(11), UDI_EMBEDDED(13)
                if (p.targetInfo.targetAvailable != 0 && (tech == Native.OUTPUT_TECH_INTERNAL || tech == 6 || tech == 11 || tech == 13)) return true;
            }
            return false;
        }

        /// <summary>True when two or more displays are active with separate desktops (extend), i.e. not mirrored.</summary>
        public static bool IsExtended()
        {
            Native.DISPLAYCONFIG_PATH_INFO[] paths; Native.DISPLAYCONFIG_MODE_INFO[] modes;
            if (!Query(Native.QDC_ONLY_ACTIVE_PATHS, out paths, out modes) || paths.Length < 2) return false;
            var sources = new HashSet<string>();
            foreach (var p in paths)
                sources.Add(p.sourceInfo.adapterId.LowPart + ":" + p.sourceInfo.adapterId.HighPart + ":" + p.sourceInfo.id);
            return sources.Count >= 2;
        }

        static int Apply(uint topology)
        {
            return Native.SetDisplayConfigTopology(0, IntPtr.Zero, 0, IntPtr.Zero, Native.SDC_APPLY | topology);
        }

        /// <summary>Console mode on a laptop: mirror, so the external monitor stays attached and DDC (volume) keeps working,
        /// while windows and the mouse stay on the laptop screen.</summary>
        public static int Clone() { return Apply(Native.SDC_TOPOLOGY_CLONE); }

        public static int LaptopOnly() { return Apply(Native.SDC_TOPOLOGY_INTERNAL); }

        /// <summary>PC mode on a laptop: extend and make the external monitor the main display.</summary>
        public static void ExtendWithExternalPrimary()
        {
            Apply(Native.SDC_TOPOLOGY_EXTEND);
            Thread.Sleep(500);
            MakeExternalPrimary();
        }

        // Shifts every source so the first external display sits at (0,0), which makes it primary.
        static void MakeExternalPrimary()
        {
            Native.DISPLAYCONFIG_PATH_INFO[] paths; Native.DISPLAYCONFIG_MODE_INFO[] modes;
            if (!Query(Native.QDC_ONLY_ACTIVE_PATHS, out paths, out modes)) return;
            int idx = -1;
            foreach (var p in paths)
            {
                uint tech = p.targetInfo.outputTechnology;
                bool internalPanel = tech == Native.OUTPUT_TECH_INTERNAL || tech == 6 || tech == 11 || tech == 13;
                if (!internalPanel && p.sourceInfo.modeInfoIdx < modes.Length) { idx = (int)p.sourceInfo.modeInfoIdx; break; }
            }
            if (idx < 0) return;
            int dx = modes[idx].sourceMode.position.x, dy = modes[idx].sourceMode.position.y;
            if (dx == 0 && dy == 0) return;
            for (int i = 0; i < modes.Length; i++)
            {
                if (modes[i].infoType != 1) continue; // source modes only
                modes[i].sourceMode.position.x -= dx;
                modes[i].sourceMode.position.y -= dy;
            }
            uint flags = Native.SDC_APPLY | Native.SDC_USE_SUPPLIED_DISPLAY_CONFIG | Native.SDC_ALLOW_CHANGES | Native.SDC_SAVE_TO_DATABASE;
            if (Native.SetDisplayConfig((uint)paths.Length, paths, (uint)modes.Length, modes, flags) != 0)
                Native.SetDisplayConfig((uint)paths.Length, paths, (uint)modes.Length, modes, flags & ~Native.SDC_SAVE_TO_DATABASE);
        }
    }
}
