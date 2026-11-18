using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace ConsoleSwitch
{
    static class Program
    {
        [DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);

        [STAThread]
        static int Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Write("Crash: " + e.ExceptionObject);

            if (args.Length > 0) return RunCommand(args[0].Trim().ToLowerInvariant());

            bool first;
            using (var mutex = new Mutex(true, @"Local\ConsoleSwitch_Tray", out first))
            {
                if (!first)
                {
                    MessageBox.Show("Console Switch is already running. Look for its icon in the tray.", "Console Switch");
                    return 0;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Log.Write("=== Console Switch started ===");
                Application.Run(new TrayApp(Config.Load()));
            }
            return 0;
        }

        // Command line, for desktop shortcuts and Stream Deck style launchers:
        //   ConsoleSwitch.exe --console | --pc | --fix-audio | --probe
        static int RunCommand(string cmd)
        {
            var cfg = Config.Load();
            if (cmd == "--probe") { AttachConsole(-1); Probe(); return 0; }
            if (!cfg.IsComplete) { MessageBox.Show("Run Console Switch once to set it up.", "Console Switch"); return 1; }
            var sw = new Switcher(cfg);
            if (cmd == "--console") sw.SwitchToConsole();
            else if (cmd == "--pc") sw.SwitchToPc();
            else if (cmd == "--fix-audio")
            {
                bool first;
                using (new Mutex(true, @"Local\ConsoleSwitch_AudioFix", out first)) if (first) sw.FixConsoleAudio();
            }
            else { MessageBox.Show("Options: --console, --pc, --fix-audio, --probe", "Console Switch"); return 1; }
            return 0;
        }

        // Read-only diagnostics: lists monitors, inputs, volume, signal and consoles. Changes nothing.
        static void Probe()
        {
            Console.WriteLine();
            Console.WriteLine("Console Switch probe (read-only)");
            Console.WriteLine("Laptop screen present: " + Displays.HasInternalDisplay());
            var mons = Ddc.Discover();
            Console.WriteLine("DDC/CI monitors: " + mons.Count);
            foreach (var m in mons)
            {
                Console.WriteLine("  " + m.Model + "  [" + m.FriendlyName + ", " + m.GdiDevice + "]");
                Console.WriteLine("    inputs: " + string.Join(", ", m.Inputs.ConvertAll(i => Ddc.InputName(i) + " (0x" + i.ToString("X2") + ")").ToArray()));
                int cur = Ddc.Get(m.Model, Ddc.VCP_INPUT);
                Console.WriteLine("    current input: " + (cur >= 0 ? Ddc.InputName(cur) : "?"));
                Console.WriteLine("    volume: " + (m.HasVolume ? Ddc.Get(m.Model, Ddc.VCP_VOLUME).ToString() : "not supported"));
                Console.WriteLine("    picture on active input: " + Ddc.Signal(m.Model));
            }
            Console.WriteLine("Scanning network…");
            var devs = Network.Scan();
            int consoles = 0;
            foreach (var d in devs) if (d.Vendor.Length > 0) { consoles++; Console.WriteLine("  " + d); }
            Console.WriteLine("Devices answering: " + devs.Count + ", consoles among them: " + consoles);
        }
    }
}
