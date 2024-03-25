using Microsoft.Win32.TaskScheduler;
using System.Diagnostics;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using Task = System.Threading.Tasks.Task;


namespace Enforcer
{
    public partial class Main : Form
    {
        [DllImport("user32.dll")]
        public extern static bool ShutdownBlockReasonCreate(IntPtr hWnd, [MarshalAs(UnmanagedType.LPWStr)] string pwszReason);
        bool isLockActive = true;
        string exePath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        Config config = Config.Instance;
        List<FileStream> filePadlocks = new List<FileStream>();

        public Main(string[] args)
        {
            this.InitializeComponent();
            SetHosts();
            if (config.Read("days-enforced").Equals("0"))
            {
                RegisterStartupTask();
                SetCleanBrowsingDNS();
            }
            else
            {
                ShutdownBlockReasonCreate(Handle, "Enforcer is active.");
                InitializeWatchdog(args);
                InitializeLock();
            }
            Environment.Exit(0);
        }

        DateTime? GetNetworkTime()
        {
            DateTime? networkDateTime = null;
            try
            {
                var ntpData = new byte[48];
                ntpData[0] = 0x1B;
                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.Connect(new IPEndPoint(Dns.GetHostEntry("pool.ntp.org").AddressList[0], 123));
                socket.Send(ntpData);
                socket.Receive(ntpData);
                socket.Close();
                var intPart = (ulong)ntpData[40] << 24 | (ulong)ntpData[41] << 16 | (ulong)ntpData[42] << 8 | (ulong)ntpData[43];
                var fractPart = (ulong)ntpData[44] << 24 | (ulong)ntpData[45] << 16 | (ulong)ntpData[46] << 8 | (ulong)ntpData[47];
                var milliseconds = (intPart * 1000) + ((fractPart * 1000) / 0x100000000L);
                networkDateTime = (new DateTime(1900, 1, 1)).AddMilliseconds((long)milliseconds);
            }
            catch (SocketException) { }
            return networkDateTime;
        }

        bool IsExpired()
        {
            DateTime.TryParse(config.Read("date-enforced"), out DateTime parsedDateLocked);
            var networkTime = GetNetworkTime();
            var expirationDate = parsedDateLocked.AddDays(int.Parse(config.Read("days-enforced")));
            return networkTime == null ? false : networkTime >= expirationDate;
        }

        void InitializeLock()
        {
            filePadlocks.Add(new FileStream(config.ConfigFile, FileMode.Open, FileAccess.Read, FileShare.Read));
            filePadlocks.Add(new FileStream("C:\\WINDOWS\\System32\\drivers\\etc\\hosts", FileMode.Open, FileAccess.Read, FileShare.Read));
            foreach (var file in Directory.GetFiles(exePath))
                filePadlocks.Add(new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read));
            while (isLockActive)
            {
                if (IsExpired())
                {
                    RemoveStartupTask();
                    foreach (var filePadlock in filePadlocks)
                        filePadlock.Close();
                    isLockActive = false;
                }
                else
                {
                    SetCleanBrowsingDNS();
                    RegisterStartupTask();
                    if (config.Read("disable-powershell").Equals("yes"))
                        DisablePowerShell();
                }
                Thread.Sleep(3000);
            }
        }

        int StartWatchdog()
        {
            using (Process executor = new Process())
            {
                executor.StartInfo.FileName = Path.Combine(exePath, "SSExecutor.exe");
                executor.StartInfo.Arguments = $"\"{Path.Combine(exePath, "svchost.exe")}\" {Process.GetCurrentProcess().Id}";
                executor.StartInfo.RedirectStandardOutput = true;
                executor.Start();
                return int.Parse(executor.StandardOutput.ReadLine());
            }
        }

        void InitializeWatchdog(string[] args)
        {
            Process watchdog = Process.GetProcessById(args.Length > 0 ? int.Parse(args[0]) : StartWatchdog());
            Task.Run(() =>
            {
                while (isLockActive)
                {
                    watchdog.WaitForExit();
                    watchdog.Close();
                    watchdog = Process.GetProcessById(StartWatchdog());
                }
            });
        }

        void RegisterStartupTask()
        {
            using (var taskService = new TaskService())
            {
                var task = taskService.GetTask("Safe Surf");
                if (task == null)
                {
                    var taskDefinition = taskService.NewTask();
                    taskDefinition.Settings.DisallowStartIfOnBatteries = false;
                    taskDefinition.RegistrationInfo.Description = "Runs Safe Surf on startup.";
                    taskDefinition.RegistrationInfo.Author = "github.com/na-stewart";
                    taskDefinition.Principal.RunLevel = TaskRunLevel.Highest;
                    taskDefinition.Triggers.Add(new LogonTrigger());
                    taskDefinition.Triggers.Add(new TimeTrigger()
                    {
                        StartBoundary = DateTime.Now,
                        Repetition = new RepetitionPattern(TimeSpan.FromMinutes(1), TimeSpan.Zero)
                    });
                    taskDefinition.Actions.Add(new ExecAction(Path.Combine(exePath, "SSDaemon.exe")));
                    taskService.RootFolder.RegisterTaskDefinition("Safe Surf", taskDefinition);
                }
                else
                    task.Enabled = true;
            }
        }

        void RemoveStartupTask()
        {
            using (var taskService = new TaskService())
            {
                taskService.RootFolder.DeleteTask("CleanBrowsing Enforcer");
            }
        }

        void SetCleanBrowsingDNS()
        {
            try
            {
                string[]? dns;
                if (config.Read("cleanbrowsing-dns-filter").Equals("off"))
                    dns = null;
                else if (config.Read("cleanbrowsing-dns-filter").Equals("family"))
                    dns = ["185.228.168.168", "185.228.169.168"];
                else
                    dns = ["185.228.168.10", "185.228.169.11"];
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

        void SetHosts()
        {
            if (config.Read("hosts-filter") == "off")
                File.WriteAllText("C:\\WINDOWS\\System32\\drivers\\etc\\hosts", "");
            else
                File.WriteAllText("C:\\WINDOWS\\System32\\drivers\\etc\\hosts", File.ReadAllText(Path.Combine(exePath, $"{config.Read("hosts-filter")}.hosts")));
        }

        void DisablePowerShell()
        {
            foreach (Process process in Process.GetProcesses())
            {
                if (!string.IsNullOrEmpty(process.MainWindowTitle) && process.MainWindowTitle.Contains("Windows PowerShell"))
                    process.Kill();
            }
        }

        protected override void WndProc(ref Message aMessage)
        {
            if (aMessage.Msg == 0x0011)
                return;
            base.WndProc(ref aMessage);
        }
    }
}
