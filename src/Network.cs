// Finds consoles on the local network. A console is recognised by its exact MAC address,
// so a laptop that later gets the console's old IP from DHCP is never mistaken for it.
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace ConsoleSwitch
{
    public class LanDevice
    {
        public string Ip;
        public string Mac;     // 12 hex chars, upper case
        public string Vendor;  // "PlayStation", "Xbox / Microsoft", "Nintendo" or ""
        public override string ToString()
        {
            string v = Vendor.Length > 0 ? Vendor : "Unknown device";
            return string.Format("{0}   {1}   ({2})", v, Ip, Network.FormatMac(Mac));
        }
    }

    public static class Network
    {
        // MAC prefixes (OUIs) registered to Sony Interactive Entertainment, Microsoft and Nintendo. Used only to
        // label scan results; the chosen console is always matched by its full MAC.
        static readonly HashSet<string> Sony = Set(
            "00041F","001315","0015C1","0019C5","001D0D","001FA7","00248D","00D9D1","00E421","04F778",
            "0C7043","0CFE45","280DFC","2840DD","2C9E00","2CCC44","50B03B","54E6FD","5C843C","5C9666",
            "68286C","70662A","709E29","78C881","84E657","904748","98FA2E","9C37CB","A8E3EE","B40AD8",
            "B41F4D","BC3329","BC60A7","C0151B","C84AA0","C863F1","D4F7D5","E86E3A","EC748C","F46412",
            "F8461C","F8D0AC","FC0FE6","FCCA40");
        static readonly HashSet<string> Microsoft = Set(
            "0017FA","001DD8","002248","0025AE","0050F2","00125A","003DE8","04D3B0","0C3526","1C1ADF",
            "28187B","2C5491","30595B","3C8375","485073","4C0BBE","5882A8","6045BD","60455E","62455E",
            "7C1E52","7CED8D","84EAF8","949AA9","985FD3","98DE07","B4AE2B","B83859","BC8385","C0335E",
            "C49DED","C83F26","D8E2DF","DCB4C4","E0E8BB","EC59E7","F01DBC");
        static readonly HashSet<string> Nintendo = Set(
            "0009BF","001656","0017AB","00191D","0019FD","001AE9","001B7A","001BEA","001CBE","001DBC",
            "001E35","001EA9","001F32","001FC5","002147","0021BD","00224C","0022AA","0022D7","002331",
            "0023CC","00241E","002444","0024F3","0025A0","002659","00265C","04031D","2C10C1","34AF2C",
            "40D28A","40F407","582F40","58BDA3","5C521E","606BFF","64B5C6","78A2A0","7CBB8A","8CCDE8",
            "98415C","98B6E9","9CE635","A438CC","A45C27","A4C0E1","B8AE6E","CC9E00","CCFB65","D86BF7",
            "DC68EB","E00C7F","E0E751","E84ECE","ECC40D");

        static HashSet<string> Set(params string[] items) { return new HashSet<string>(items, StringComparer.OrdinalIgnoreCase); }

        public static string VendorOf(string mac)
        {
            if (mac == null || mac.Length < 6) return "";
            string oui = mac.Substring(0, 6);
            if (Sony.Contains(oui)) return "PlayStation";
            if (Nintendo.Contains(oui)) return "Nintendo";
            if (Microsoft.Contains(oui)) return "Xbox / Microsoft";
            return "";
        }

        public static string NormalizeMac(string mac)
        {
            if (string.IsNullOrEmpty(mac)) return "";
            return mac.Replace(":", "").Replace("-", "").Replace(" ", "").ToUpperInvariant();
        }

        public static string FormatMac(string mac)
        {
            if (mac == null || mac.Length != 12) return mac ?? "";
            var parts = new string[6];
            for (int i = 0; i < 6; i++) parts[i] = mac.Substring(i * 2, 2);
            return string.Join(":", parts);
        }

        public static string MacOf(string ip)
        {
            IPAddress addr;
            if (!IPAddress.TryParse(ip, out addr) || addr.AddressFamily != AddressFamily.InterNetwork) return "";
            try
            {
                byte[] mac = new byte[6];
                uint len = 6;
                if (Native.SendARP(BitConverter.ToUInt32(addr.GetAddressBytes(), 0), 0, mac, ref len) != 0 || len < 6) return "";
                string s = BitConverter.ToString(mac, 0, 6).Replace("-", "");
                return s == "000000000000" ? "" : s;
            }
            catch { return ""; }
        }

        public static bool Ping(string host, int timeoutMs)
        {
            try
            {
                using (var p = new Ping())
                {
                    var r = p.Send(host, timeoutMs);
                    return r != null && r.Status == IPStatus.Success;
                }
            }
            catch { return false; }
        }

        public static HashSet<string> LocalIps()
        {
            var set = new HashSet<string>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                        if (ua.Address.AddressFamily == AddressFamily.InterNetwork) set.Add(ua.Address.ToString());
            }
            catch { }
            return set;
        }

        /// <summary>The /24 prefix ("192.168.1.") of every adapter that has a default gateway.</summary>
        public static List<string> LanPrefixes()
        {
            var list = new List<string>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    var props = ni.GetIPProperties();
                    bool gateway = false;
                    foreach (var g in props.GatewayAddresses)
                        if (g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)) gateway = true;
                    if (!gateway) continue;
                    foreach (var ua in props.UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        string s = ua.Address.ToString();
                        string prefix = s.Substring(0, s.LastIndexOf('.') + 1);
                        if (!list.Contains(prefix)) list.Add(prefix);
                    }
                }
            }
            catch { }
            return list;
        }

        /// <summary>Pings the whole local /24 network in parallel and returns every device that answered.</summary>
        public static List<LanDevice> Scan(int timeoutMs = 600)
        {
            var result = new List<LanDevice>();
            var locals = LocalIps();
            var pings = new List<Ping>();
            var tasks = new List<Task<PingReply>>();
            foreach (string prefix in LanPrefixes())
            {
                for (int i = 1; i < 255; i++)
                {
                    string ip = prefix + i;
                    if (locals.Contains(ip)) continue;
                    var p = new Ping();
                    pings.Add(p);
                    try { tasks.Add(p.SendPingAsync(ip, timeoutMs)); } catch { }
                }
            }
            try { Task.WaitAll(tasks.ToArray(), timeoutMs + 2500); } catch { }
            foreach (var t in tasks)
            {
                if (t.Status != TaskStatus.RanToCompletion || t.Result == null || t.Result.Status != IPStatus.Success) continue;
                string ip = t.Result.Address.ToString();
                string mac = MacOf(ip);
                if (mac.Length != 12) continue;
                result.Add(new LanDevice { Ip = ip, Mac = mac, Vendor = VendorOf(mac) });
            }
            foreach (var p in pings) p.Dispose();
            // Consoles first, then by address.
            result.Sort((a, b) =>
            {
                int va = a.Vendor.Length > 0 ? 0 : 1, vb = b.Vendor.Length > 0 ? 0 : 1;
                if (va != vb) return va - vb;
                return LastOctet(a.Ip) - LastOctet(b.Ip);
            });
            return result;
        }

        static int LastOctet(string ip)
        {
            int n;
            return int.TryParse(ip.Substring(ip.LastIndexOf('.') + 1), out n) ? n : 0;
        }

        /// <summary>Finds the device with this MAC on the local network; returns its IP or null.</summary>
        public static string FindByMac(string mac)
        {
            foreach (var d in Scan(400))
                if (d.Mac == mac) return d.Ip;
            return null;
        }
    }
}
