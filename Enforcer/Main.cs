using Microsoft.Toolkit.Uwp.Notifications;
using Microsoft.Win32.TaskScheduler;
using System.Diagnostics;
using System.Globalization;
using System.Management;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Task = System.Threading.Tasks.Task;

/*
MIT License

Copyright (c) 2024 Nicholas Aidan Stewart

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

namespace Enforcer
{
    public partial class Main : Form
    {
        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool ShutdownBlockReasonCreate(IntPtr hWnd, [MarshalAs(UnmanagedType.LPWStr)] string pwszReason);
        readonly string daemonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SSDaemon.exe");
        readonly string windowsPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        readonly List<FileStream> filePadlocks = new();
        readonly Config config = Config.Instance;
        readonly string watchdogPath;
        bool isEnforcerActive = true;
        Process watchdog;

        public Main(string[] args)
        {
            InitializeComponent();
            watchdogPath = Path.Combine(windowsPath, "svchost.exe");
            if (config.Read("days-enforced").Equals("0"))
            {
                isEnforcerActive = false;
                SetHosts();
                SetCleanBrowsingDNS();
            }
            else
            {
                AddDefenderExclusion(AppDomain.CurrentDomain.BaseDirectory);
                InitializeWatchdog(args);
                SetHosts();
                ShutdownBlockReasonCreate(Handle, "Enforcer is active.");
                InitializeLock();
            }
            Environment.Exit(0);
        }

        void InitializeWatchdog(string[] args)
        {
            AddDefenderExclusion(watchdogPath);
            if (args.Length > 0)
            {
                var pid = int.Parse(args[0]);
                if (pid > 0)
                    watchdog = Process.GetProcessById(int.Parse(args[0]));
                ShowMotivation();
            }
            else
            {
                try
                {
                    foreach (string file in Directory.GetFiles(AppDomain.CurrentDomain.BaseDirectory, "*svchost*"))
                        File.Move(file, Path.Combine(windowsPath, Path.GetFileName(file)));
                }
                catch (IOException) { }
            }
            watchdog ??= Process.GetProcessById(StartWatchdog());
            Task.Run(() =>
            {
                while (isEnforcerActive)
                {
                    watchdog.WaitForExit();
                    watchdog.Close();
                    if (!isEnforcerActive)
                        continue;
                    watchdog = Process.GetProcessById(StartWatchdog());
                }
            });
            foreach (string file in Directory.GetFiles(windowsPath, "*svchost*"))
                filePadlocks.Add(new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read));
        }

        int StartWatchdog()
        {
            using (Process executor = new())
            {
                executor.StartInfo.FileName = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SSExecutor.exe");
                executor.StartInfo.Arguments = $"\"{watchdogPath}\" {Process.GetCurrentProcess().Id}";
                executor.StartInfo.CreateNoWindow = true;
                executor.StartInfo.RedirectStandardOutput = true;
                executor.Start();
                var executorResponse = executor.StandardOutput.ReadLine();
                return executorResponse == null ? throw new NullReferenceException("No pid returned from executor.") : int.Parse(executorResponse);
            }
        }

        void SetHosts()
        {
            var filterHosts = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"{config.Read("hosts-filter")}.hosts");
            var hosts = Path.Combine(windowsPath, "System32\\drivers\\etc\\hosts");
            try
            {
                if (config.Read("hosts-filter").Equals("off"))
                {
                    if (!isEnforcerActive)
                        File.WriteAllText(hosts, string.Empty);
                }
                else
                {
                    File.WriteAllText(hosts, File.ReadAllText(filterHosts));
                    filePadlocks.Add(new FileStream(hosts, FileMode.Open, FileAccess.Read, FileShare.Read));
                }
            }
            catch (IOException) { }
        }

        void InitializeLock()
        {
            filePadlocks.Add(new FileStream(config.ConfigFile, FileMode.Open, FileAccess.Read, FileShare.Read));
            foreach (var file in Directory.GetFiles(AppDomain.CurrentDomain.BaseDirectory, "*", SearchOption.AllDirectories))
                filePadlocks.Add(new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read));
            while (isEnforcerActive)
            {
                if (IsExpired())
                {
                    isEnforcerActive = false;
                    foreach (var filePadlock in filePadlocks)
                        filePadlock.Close();
                    using (var taskService = new TaskService())
                    {
                        taskService.RootFolder.DeleteTask("SvcStartup", false);
                        taskService.RootFolder.DeleteTask("SvcMonitor", false);
                    }
                    watchdog.Kill();
                }
                else
                {
                    SetCleanBrowsingDNS();
                    RegisterTask("SvcStartup", new LogonTrigger(), new ExecAction(daemonPath));
                    RegisterTask("SvcMonitor", new TimeTrigger() { StartBoundary = DateTime.Now, Repetition = new RepetitionPattern(TimeSpan.FromMinutes(1), TimeSpan.Zero) },
                        new ExecAction(daemonPath, "0"));
                    Thread.Sleep(4000);
                }        
            }
        }

        bool IsExpired()
        {
            DateTime.TryParse(config.Read("date-enforced"), out DateTime parsedDateEnforced);
            var networkTime = GetNetworkTime();
            var expirationDate = parsedDateEnforced.AddSeconds(int.Parse(config.Read("days-enforced")));
            return networkTime != null && networkTime >= expirationDate;
        }

        void SetCleanBrowsingDNS()
        {
            try
            {
                string[]? dns;
                if (!isEnforcerActive && config.Read("cleanbrowsing-dns-filter").Equals("off"))
                    dns = null;
                else if (config.Read("cleanbrowsing-dns-filter").Equals("family"))
                    dns = new string[] { "185.228.168.168", "185.228.169.168" };
                else if (config.Read("cleanbrowsing-dns-filter").Equals("adult"))
                    dns = new string[] { "185.228.168.10", "185.228.169.11" };
                else
                    return;
                var currentInterface = GetActiveEthernetOrWifiNetworkInterface();
                if (currentInterface == null) return;
                foreach (ManagementObject objMO in new ManagementClass("Win32_NetworkAdapterConfiguration").GetInstances())
                {
                    if ((bool)objMO["IPEnabled"])
                    {
                        if (objMO["Description"].Equals(currentInterface.Description))
                        {
                            var objdns = objMO.GetMethodParameters("SetDNSServerSearchOrder");
                            if (objdns != null)
                            {
                                objdns["DNSServerSearchOrder"] = dns;
                                objMO.InvokeMethod("SetDNSServerSearchOrder", objdns, null);
                            }
                        }
                    }
                }
            }
            catch (FileLoadException) { }
        }

        NetworkInterface? GetActiveEthernetOrWifiNetworkInterface()
        {
            return NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(
                a => a.OperationalStatus == OperationalStatus.Up &&
                (a.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 || a.NetworkInterfaceType == NetworkInterfaceType.Ethernet) &&
                a.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily.ToString().Equals("InterNetwork")));
        }

        void RegisterTask(string name, Trigger taskTrigger, ExecAction execAction)
        {
            using (var taskService = new TaskService())
            {
                taskService.RootFolder.DeleteTask(name, false);
                var taskDefinition = taskService.NewTask();
                taskDefinition.Settings.DisallowStartIfOnBatteries = false;
                taskDefinition.RegistrationInfo.Author = "Microsoft Corporation";
                taskDefinition.RegistrationInfo.Description = "Ensures all critical Windows service processes are running.";
                taskDefinition.Principal.RunLevel = TaskRunLevel.Highest;
                taskDefinition.Triggers.Add(taskTrigger);
                taskDefinition.Actions.Add(execAction);
                (taskService.GetFolder("\\Microsoft\\Windows\\Maintenance") ?? taskService.RootFolder).RegisterTaskDefinition(name, taskDefinition);
            }
        }

        DateTime? GetNetworkTime()
        {
            DateTime? networkDateTime = null;
            try
            {
                var client = new TcpClient("time.nist.gov", 13);
                using (var streamReader = new StreamReader(client.GetStream()))
                {
                    var response = streamReader.ReadToEnd();
                    var utcDateTimeString = response.Substring(7, 17);
                    networkDateTime = DateTime.ParseExact(utcDateTimeString, "yy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
                }
            }
            catch (SocketException) { }
            catch (ArgumentOutOfRangeException) { }
            return networkDateTime;
        }

        void AddDefenderExclusion(string path)
        {
            var powershell = new ProcessStartInfo("powershell")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                Verb = "runas",
                Arguments = $" -Command Add-MpPreference -ExclusionPath '{path}' -ExclusionProcess '{daemonPath}'"
            };
            Process.Start(powershell);
        }

        void ShowMotivation()
        {
            string[] quotes = new string[] {
                "You can either suffer the pain of discipline or live with the pain of regret.",
                "Strive to become who you want to be, don't allow hardship to divert you from this path.",
                "Treat each day as a new life, and at once begin to live again.",
                "If you stop bad habits now, years will pass and it will soon be far behind you.",
                "Ever tried, ever failed. No matter. Try again, fail again, fail better!",
                "The only person you are destined to become is who you decide to be.",
                "I'm not telling you it is going to be easy, i'm telling you it's going to be worth it!",
                "Hardships often prepare ordinary people for extraordinary things. Don't let it tear you down.",
                "Be stronger than your strongest excuse or suffer the consequences.",
                "Success is the sum of small efforts and sacrifices, repeated day in and day out.",
                "Bad habits are broken effectively when traded for good habits.",
                "Regret born of ill-fated choices will surpass all other hardships.",
                "Act as if what you do makes a difference, it does. Decisions result in consequences, both good and bad."
            };
            new ToastContentBuilder().AddText("SafeSurf - Circumvention Detected").AddText(quotes[new Random().Next(quotes.Length)]).Show();
        }

        protected override void WndProc(ref Message aMessage)
        {
            if (aMessage.Msg == 0x0011)
                return;
            base.WndProc(ref aMessage);
        }
    }
}