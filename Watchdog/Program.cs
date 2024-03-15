using Microsoft.Win32;
using System.Diagnostics;
using System.Reflection;

namespace Watchdog
{
    internal static class Program
    {
     
        static void Main(string[] args)
        {
            using (var mutex = new Mutex(false, "cbe_watchdog"))
            {
                if (mutex.WaitOne(TimeSpan.Zero))
                {
                    Process enforcer = Process.GetProcessById(int.Parse(args[0]));
                    while (true)
                    {
                        enforcer.WaitForExit();
                        enforcer = Process.GetProcessById(StartEnforcer());
                    }
                }               
            }
        }

        static int StartEnforcer()
        {
            Process executor = new Process();
            executor.EnableRaisingEvents = true;
            executor.StartInfo.FileName = Path.Combine(Assembly.GetExecutingAssembly().Location, "CBEExecutor.exe");
            executor.StartInfo.Arguments = $"\"{Path.Combine(Assembly.GetExecutingAssembly().Location, "CBEDaemon.exe")}\" {Process.GetCurrentProcess().Id}";
            executor.StartInfo.RedirectStandardOutput = true;
            executor.Start();
            return int.Parse(executor.StandardOutput.ReadLine());
        }
    }
}