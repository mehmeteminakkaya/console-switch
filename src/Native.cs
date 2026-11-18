// Win32 declarations used across the app. C# 5 (built with the .NET Framework csc that ships with Windows).
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace ConsoleSwitch
{
    static class Native
    {
        // ---------- monitors / DDC/CI (dxva2) ----------
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct PHYSICAL_MONITOR
        {
            public IntPtr hPhysicalMonitor;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szPhysicalMonitorDescription;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct MC_TIMING_REPORT
        {
            public uint dwHorizontalFrequencyInHZ;
            public uint dwVerticalFrequencyInHZ;
            public byte bTimingStatusByte;
        }

        public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, IntPtr rect, IntPtr data);

        [DllImport("user32.dll")]
        public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc cb, IntPtr data);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);

        [DllImport("dxva2.dll", SetLastError = true)]
        public static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint count);
        [DllImport("dxva2.dll", SetLastError = true)]
        public static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint count, [Out] PHYSICAL_MONITOR[] monitors);
        [DllImport("dxva2.dll", SetLastError = true)]
        public static extern bool DestroyPhysicalMonitors(uint count, PHYSICAL_MONITOR[] monitors);
        [DllImport("dxva2.dll", SetLastError = true)]
        public static extern bool GetTimingReport(IntPtr hPhysicalMonitor, out MC_TIMING_REPORT report);
        [DllImport("dxva2.dll", SetLastError = true)]
        public static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr hPhysicalMonitor, byte code, out uint type, out uint current, out uint max);
        [DllImport("dxva2.dll", SetLastError = true)]
        public static extern bool SetVCPFeature(IntPtr hPhysicalMonitor, byte code, uint value);
        [DllImport("dxva2.dll", SetLastError = true)]
        public static extern bool GetCapabilitiesStringLength(IntPtr hPhysicalMonitor, out uint length);
        [DllImport("dxva2.dll", SetLastError = true, CharSet = CharSet.Ansi)]
        public static extern bool CapabilitiesRequestAndCapabilitiesReply(IntPtr hPhysicalMonitor, StringBuilder caps, uint length);

        // ---------- display topology ----------
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int left, top, right, bottom; }
        [StructLayout(LayoutKind.Sequential)]
        public struct LUID { public uint LowPart; public int HighPart; }
        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_RATIONAL { public uint Numerator; public uint Denominator; }
        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_2DREGION { public uint cx; public uint cy; }
        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_VIDEO_SIGNAL_INFO
        {
            public ulong pixelRate;
            public DISPLAYCONFIG_RATIONAL hSyncFreq;
            public DISPLAYCONFIG_RATIONAL vSyncFreq;
            public DISPLAYCONFIG_2DREGION activeSize;
            public DISPLAYCONFIG_2DREGION totalSize;
            public uint videoStandard;
            public uint scanLineOrdering;
        }
        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_TARGET_MODE { public DISPLAYCONFIG_VIDEO_SIGNAL_INFO targetVideoSignalInfo; }
        [StructLayout(LayoutKind.Sequential)]
        public struct POINTL { public int x; public int y; }
        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_SOURCE_MODE { public uint width; public uint height; public uint pixelFormat; public POINTL position; }
        [StructLayout(LayoutKind.Sequential)]
        public struct RECTL { public int left; public int top; public int right; public int bottom; }
        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_DESKTOP_IMAGE_INFO { public POINTL PathSourceSize; public RECTL DesktopImageRegion; public RECTL DesktopImageClip; }

        [StructLayout(LayoutKind.Explicit)]
        public struct DISPLAYCONFIG_MODE_INFO
        {
            [FieldOffset(0)] public uint infoType;
            [FieldOffset(4)] public uint id;
            [FieldOffset(8)] public LUID adapterId;
            [FieldOffset(16)] public DISPLAYCONFIG_TARGET_MODE targetMode;
            [FieldOffset(16)] public DISPLAYCONFIG_SOURCE_MODE sourceMode;
            [FieldOffset(16)] public DISPLAYCONFIG_DESKTOP_IMAGE_INFO desktopImageInfo;
        }
        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_PATH_SOURCE_INFO { public LUID adapterId; public uint id; public uint modeInfoIdx; public uint statusFlags; }
        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_PATH_TARGET_INFO
        {
            public LUID adapterId; public uint id; public uint modeInfoIdx; public uint outputTechnology; public uint rotation;
            public uint scaling; public DISPLAYCONFIG_RATIONAL refreshRate; public uint scanLineOrdering; public int targetAvailable; public uint statusFlags;
        }
        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_PATH_INFO { public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo; public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo; public uint flags; }
        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_DEVICE_INFO_HEADER { public uint type; public uint size; public LUID adapterId; public uint id; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
        {
            public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string viewGdiDeviceName;
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DISPLAYCONFIG_TARGET_DEVICE_NAME
        {
            public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
            public uint flags; public uint outputTechnology; public ushort edidManufactureId; public ushort edidProductCodeId; public uint connectorInstance;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string monitorFriendlyDeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string monitorDevicePath;
        }

        public const uint QDC_ALL_PATHS = 0x00000001;
        public const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;
        public const uint SDC_TOPOLOGY_INTERNAL = 0x00000001;
        public const uint SDC_TOPOLOGY_CLONE = 0x00000002;
        public const uint SDC_TOPOLOGY_EXTEND = 0x00000004;
        public const uint SDC_TOPOLOGY_EXTERNAL = 0x00000008;
        public const uint SDC_USE_SUPPLIED_DISPLAY_CONFIG = 0x00000020;
        public const uint SDC_APPLY = 0x00000080;
        public const uint SDC_SAVE_TO_DATABASE = 0x00000200;
        public const uint SDC_ALLOW_CHANGES = 0x00000400;
        public const uint OUTPUT_TECH_INTERNAL = 0x80000000;

        [DllImport("user32.dll")]
        public static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);
        [DllImport("user32.dll")]
        public static extern int QueryDisplayConfig(uint flags, ref uint numPathArrayElements, [Out] DISPLAYCONFIG_PATH_INFO[] pathArray,
            ref uint numModeInfoArrayElements, [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray, IntPtr currentTopologyId);
        [DllImport("user32.dll")]
        public static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME requestPacket);
        [DllImport("user32.dll")]
        public static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME requestPacket);
        [DllImport("user32.dll")]
        public static extern int SetDisplayConfig(uint numPathArrayElements, [In] DISPLAYCONFIG_PATH_INFO[] pathArray,
            uint numModeInfoArrayElements, [In] DISPLAYCONFIG_MODE_INFO[] modeInfoArray, uint flags);
        [DllImport("user32.dll", EntryPoint = "SetDisplayConfig")]
        public static extern int SetDisplayConfigTopology(uint numPathArrayElements, IntPtr pathArray, uint numModeInfoArrayElements, IntPtr modeInfoArray, uint flags);

        // ---------- network ----------
        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        public static extern int SendARP(uint destIp, uint srcIp, byte[] macAddr, ref uint physicalAddrLen);

        [DllImport("kernel32.dll")]
        public static extern ulong GetTickCount64();

        // ---------- window ----------
        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
    }
}
