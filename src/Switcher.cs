// The switching engine. Watches the console on the network and the monitor's signal, and moves the
// monitor between the PC input and the console input. Runs on a background thread.
using System;
using System.Diagnostics;
using System.Threading;

namespace ConsoleSwitch
{
    public enum Mode { Unknown, Pc, Console }

    public class Switcher
    {
        const int PingTimeoutMs = 350;
        const int IdleTickMs = 500;          // console off: check twice a second
        const int ConsoleTickMs = 200;       // console on: watch the picture closely
        const int OfflineThreshold = 4;      // missed pings before the console counts as off
        const int SweepEveryMs = 5000;       // console off and not at its last IP: rescan the network
        const int PauseAfterAudioFixMs = 15000;

        readonly Config cfg;
        readonly object switchLock = new object();
        Thread thread;
        volatile bool stop;
        volatile bool paused;                // Settings dialog open: don't touch anything
        DateTime signalPauseUntil = DateTime.MinValue;

        public Mode Mode { get; private set; }
        public bool ConsoleOnline { get; private set; }
        public event Action StateChanged;

        public Switcher(Config cfg) { this.cfg = cfg; Mode = Mode.Unknown; }

        bool UseLayout
        {
            get
            {
                if (cfg.Layout == LayoutMode.DontTouch) return false;
                if (cfg.Layout == LayoutMode.Laptop) return true;
                return Displays.HasInternalDisplay();
            }
        }

        public void Start()
        {
            stop = false;
            thread = new Thread(Run) { IsBackground = true, Name = "Switcher" };
            thread.Start();
        }

        public void Stop() { stop = true; }
        public bool Paused { get { return paused; } set { paused = value; } }

        void Changed() { var h = StateChanged; if (h != null) try { h(); } catch { } }

        // ---------- manual actions (also used by the loop) ----------

        volatile bool switching; // a switch is in progress: the loop must not react to its own input changes

        public void SwitchToConsole()
        {
            lock (switchLock)
            {
                switching = true;
                try { DoSwitchToConsole(); } finally { switching = false; }
            }
            Changed();
        }

        void DoSwitchToConsole()
        {
            Log.Write("Switching to the console (" + Ddc.InputName(cfg.ConsoleInput) + ").");
            // Input first so the console picture shows right away, then the Windows layout.
            SetInputIfNeeded(cfg.ConsoleInput);
            if (UseLayout)
            {
                int err = Displays.Clone();
                if (err != 0)
                {
                    Log.Write("Mirroring failed (code " + err + "); using the laptop screen only.");
                    Thread.Sleep(150);
                    Displays.LaptopOnly();
                }
                else
                {
                    // Re-attaching the monitor makes it fall back to the PC input; catch that fast.
                    Ddc.ClearCache();
                    HoldInput(cfg.ConsoleInput, cfg.PcInput);
                }
            }
            else SetInputVerified(cfg.ConsoleInput, false);
            Mode = Mode.Console;
        }

        public void SwitchToPc()
        {
            lock (switchLock)
            {
                switching = true;
                try
                {
                    Log.Write("Switching to the PC (" + Ddc.InputName(cfg.PcInput) + ").");
                    SetInputIfNeeded(cfg.PcInput);
                    if (UseLayout)
                    {
                        Displays.ExtendWithExternalPrimary();
                        Ddc.ClearCache();
                        HoldInput(cfg.PcInput, cfg.ConsoleInput);
                    }
                    else SetInputVerified(cfg.PcInput, false);
                    Mode = Mode.Pc;
                }
                finally { switching = false; }
            }
            Changed();
        }

        /// <summary>Console has no sound: re-select its input once. The input change makes the console re-read
        /// the monitor's audio formats (an HDMI hot-plug), which brings the sound back.</summary>
        public void FixConsoleAudio()
        {
            lock (switchLock)
            {
                Log.Write("Audio fix: re-selecting the console input.");
                signalPauseUntil = DateTime.Now.AddMilliseconds(PauseAfterAudioFixMs);
                int volume = Ddc.Get(cfg.MonitorModel, Ddc.VCP_VOLUME);
                bool restore = volume > 0 && volume <= 100;
                // Speakers to 0 while the input flips, so they don't pop. (VCP 0x8D "mute" is avoided on purpose:
                // on some monitors it shares a code with screen blanking and garbles the picture.)
                if (restore) Ddc.Set(cfg.MonitorModel, Ddc.VCP_VOLUME, 0);
                Ddc.Set(cfg.MonitorModel, Ddc.VCP_INPUT, cfg.PcInput);
                var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 4000 && Ddc.Get(cfg.MonitorModel, Ddc.VCP_INPUT) != cfg.PcInput) Thread.Sleep(200);
                SetInputVerified(cfg.ConsoleInput, true);
                if (restore)
                {
                    WaitForStableSignal();
                    Thread.Sleep(800);
                    for (int i = 0; i < 5; i++)
                    {
                        Ddc.Set(cfg.MonitorModel, Ddc.VCP_VOLUME, volume);
                        Thread.Sleep(300);
                        if (Ddc.Get(cfg.MonitorModel, Ddc.VCP_VOLUME) == volume) break;
                        Thread.Sleep(500);
                    }
                }
                signalPauseUntil = DateTime.Now.AddMilliseconds(PauseAfterAudioFixMs);
                Log.Write("Audio fix done.");
            }
        }

        // After a Windows layout change the monitor re-syncs and may jump to the other input on its own.
        // Poll quickly and push it back the moment it reports the wrong input, until it settles.
        void HoldInput(int target, int other)
        {
            var sw = Stopwatch.StartNew();
            var sinceSend = Stopwatch.StartNew();
            var steady = new Stopwatch();   // how long the monitor has reported the target without a break
            int sends = 0;
            while (sw.ElapsedMilliseconds < 8000)
            {
                int now = Ddc.Get(cfg.MonitorModel, Ddc.VCP_INPUT);
                if (now == target)
                {
                    if (!steady.IsRunning) steady.Restart();
                    // The fallback to the other input can come ~1 s after the layout change, so wait it out.
                    if (steady.ElapsedMilliseconds >= 1800)
                    {
                        if (sends > 0) Log.Write("Monitor fell back to " + Ddc.InputName(other) + "; pushed " + Ddc.InputName(target) + " again (" + sends + "x) after " + sw.ElapsedMilliseconds + " ms.");
                        return;
                    }
                }
                else
                {
                    if (now == other) steady.Reset();
                    if (now == other && sends < 4 && sinceSend.ElapsedMilliseconds > 500)
                    {
                        Ddc.Set(cfg.MonitorModel, Ddc.VCP_INPUT, target);
                        sends++;
                        sinceSend.Restart();
                    }
                }
                Thread.Sleep(120);
            }
            Log.Write("Input didn't settle on " + Ddc.InputName(target) + " within 8 s.");
        }

        // Re-selecting the input the monitor is already on makes it re-sync, which can cut the console's sound.
        void SetInputIfNeeded(int input)
        {
            if (Ddc.Get(cfg.MonitorModel, Ddc.VCP_INPUT) == input) return;
            Ddc.Set(cfg.MonitorModel, Ddc.VCP_INPUT, input);
        }

        void WaitForStableSignal()
        {
            var sw = Stopwatch.StartNew();
            int ok = 0;
            while (sw.ElapsedMilliseconds < 8000)
            {
                if (Ddc.Signal(cfg.MonitorModel) == 1) { if (++ok >= 3) return; } else ok = 0;
                Thread.Sleep(300);
            }
        }

        void WaitMonitorReady()
        {
            Thread.Sleep(400);
            var sw = Stopwatch.StartNew();
            int stable = 0;
            while (sw.ElapsedMilliseconds < 5000)
            {
                int v = Ddc.Get(cfg.MonitorModel, Ddc.VCP_INPUT);
                if (v == cfg.PcInput || v == cfg.ConsoleInput) { if (++stable >= 2) return; } else stable = 0;
                Thread.Sleep(250);
            }
            Log.Write("Monitor wasn't ready after 5 s; continuing anyway.");
        }

        // Monitors can swallow a command while they change input. Re-send only if the monitor clearly reports
        // the other input; re-sending blindly re-triggers the input and can break the console's sound.
        void SetInputVerified(int input, bool sendFirst)
        {
            if (sendFirst) Ddc.Set(cfg.MonitorModel, Ddc.VCP_INPUT, input);
            int sends = 1;
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 6000)
            {
                Thread.Sleep(700);
                int now = Ddc.Get(cfg.MonitorModel, Ddc.VCP_INPUT);
                if (now == input) return;
                if ((now == cfg.PcInput || now == cfg.ConsoleInput) && sends < 3)
                {
                    Log.Write(string.Format("Monitor reports {0}, expected {1}; sending again.", Ddc.InputName(now), Ddc.InputName(input)));
                    Ddc.Set(cfg.MonitorModel, Ddc.VCP_INPUT, input);
                    sends++;
                }
            }
            Log.Write("Monitor didn't confirm the input within 6 s.");
        }

        // ---------- detection ----------

        bool ConsoleAtKnownIp(bool verifyMac)
        {
            if (string.IsNullOrEmpty(cfg.ConsoleIp)) return false;
            if (Network.LocalIps().Contains(cfg.ConsoleIp)) return false; // this PC got the console's old address
            if (!Network.Ping(cfg.ConsoleIp, PingTimeoutMs)) return false;
            return !verifyMac || Network.MacOf(cfg.ConsoleIp) == cfg.ConsoleMac;
        }

        bool FindConsole()
        {
            string ip = Network.FindByMac(cfg.ConsoleMac);
            if (ip == null) return false;
            if (ip != cfg.ConsoleIp)
            {
                Log.Write("Console found at a new address: " + ip + " (was " + (cfg.ConsoleIp.Length > 0 ? cfg.ConsoleIp : "unknown") + ").");
                cfg.ConsoleIp = ip;
                cfg.Save();
            }
            return true;
        }

        void Run()
        {
            Log.Write(string.Format("Watching {0} for {1}. Monitor: {2}, PC = {3}, console = {4}.",
                Network.FormatMac(cfg.ConsoleMac), cfg.ConsoleName, cfg.MonitorModel, Ddc.InputName(cfg.PcInput), Ddc.InputName(cfg.ConsoleInput)));

            bool online = ConsoleAtKnownIp(true) || FindConsole();
            ConsoleOnline = online;
            int input = Ddc.Get(cfg.MonitorModel, Ddc.VCP_INPUT);
            if (online && input == cfg.ConsoleInput && Ddc.Signal(cfg.MonitorModel) == 1)
            {
                // Restarted while playing (e.g. the app was updated): leave the console picture and sound alone.
                Mode = Mode.Console;
                Log.Write("Console already on screen; not touching anything.");
            }
            else if (online && input == cfg.PcInput && Native.GetTickCount64() > 10UL * 60 * 1000)
            {
                // Restarted mid-session (not right after Windows booted) while someone is using the PC and the
                // console happens to be on: don't yank them over. Right after boot the PC input is only showing
                // because Windows just started, so the console wins then.
                Mode = Mode.Pc;
                Log.Write("Console is on but the PC is on screen; staying on the PC.");
            }
            else if (cfg.AutoSwitch) { if (online) SwitchToConsole(); else SwitchToPc(); }
            else Mode = input == cfg.ConsoleInput ? Mode.Console : Mode.Pc;
            Changed();

            int failures = 0, ticks = 0;
            bool signalSeen = online && Ddc.Signal(cfg.MonitorModel) == 1;
            bool waitForOffline = false;     // picture went away and we went to the PC; wait until the console leaves the network
            var noSignal = new Stopwatch();
            var sinceSweep = Stopwatch.StartNew();

            while (!stop)
            {
                try
                {
                    Thread.Sleep(online ? ConsoleTickMs : IdleTickMs);
                    if (paused || !cfg.AutoSwitch || switching) { noSignal.Reset(); continue; }

                    // Console mode: the picture disappears seconds before the console leaves the network.
                    if (online && Mode == Mode.Console)
                    {
                        if (DateTime.Now < signalPauseUntil) noSignal.Reset();
                        else
                        {
                            int signal = Ddc.Signal(cfg.MonitorModel);
                            if (signal == 1) { signalSeen = true; noSignal.Reset(); }
                            else if (signal == 0 && signalSeen)
                            {
                                if (!noSignal.IsRunning) noSignal.Restart();
                                if (noSignal.ElapsedMilliseconds >= cfg.NoSignalSeconds * 1000)
                                {
                                    Log.Write("Console picture gone for " + noSignal.ElapsedMilliseconds + " ms.");
                                    online = false; waitForOffline = true; failures = 0; noSignal.Reset();
                                    ConsoleOnline = false;
                                    SwitchToPc();
                                    continue;
                                }
                                continue; // keep watching the picture; skip the ping this tick
                            }
                        }
                        // Every ~1 s: did someone switch the monitor to the PC with its own buttons?
                        if (ticks % 5 == 4 && Ddc.Get(cfg.MonitorModel, Ddc.VCP_INPUT) == cfg.PcInput)
                        {
                            Log.Write("Monitor was switched to the PC by hand; restoring the PC layout.");
                            SwitchToPc(); // console stays 'online', so we won't jump back until it turns off and on again
                            ticks++;
                            continue;
                        }
                        if (++ticks % 3 != 0) continue; // ping every ~0.6 s
                    }

                    bool seen = ConsoleAtKnownIp(!online);
                    if (!seen && !online && !waitForOffline && sinceSweep.ElapsedMilliseconds >= SweepEveryMs)
                    {
                        sinceSweep.Restart();
                        seen = FindConsole();
                    }

                    if (waitForOffline)
                    {
                        if (seen) { failures = 0; continue; }
                        if (++failures >= OfflineThreshold)
                        {
                            waitForOffline = false; failures = 0; sinceSweep.Restart();
                            Log.Write("Console left the network.");
                        }
                        continue;
                    }

                    if (seen)
                    {
                        failures = 0;
                        if (!online)
                        {
                            online = true; signalSeen = false; noSignal.Reset();
                            ConsoleOnline = true;
                            Log.Write("Console turned on (" + cfg.ConsoleIp + ").");
                            SwitchToConsole();
                        }
                    }
                    else if (online && ++failures >= OfflineThreshold)
                    {
                        online = false; failures = 0;
                        ConsoleOnline = false;
                        Log.Write("Console stopped answering.");
                        SwitchToPc();
                        sinceSweep.Restart();
                    }
                }
                catch (Exception ex)
                {
                    Log.Write("Loop error: " + ex);
                }
            }
        }
    }
}
