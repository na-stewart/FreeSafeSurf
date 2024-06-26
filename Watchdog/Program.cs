using System.Diagnostics;

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

namespace Watchdog
{
    internal static class Program
    {
        static void Main(string[] args)
        {
            using var mutex = new Mutex(false, "SSWatchdog");
            if (mutex.WaitOne(TimeSpan.Zero))
            {
                Process enforcer = Process.GetProcessById(int.Parse(args[0]));
                while (true) // Prevents closure of enforcer by immediately reopening it.
                {
                    enforcer.WaitForExit();
                    enforcer = Process.GetProcessById(StartDaemon(args[1]));
                }
            }
        }
        
        static int StartDaemon(string exePath)
        {
            var executor = Process.Start(new ProcessStartInfo(Path.Combine(exePath, "SSExecutor.exe"))
            {
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                Arguments = $"\"{Path.Combine(exePath, "SSDaemon.exe")}\" {Process.GetCurrentProcess().Id}",
            });
            var executorResponse = executor.StandardOutput.ReadLine();
            return executorResponse == null ? throw new NullReferenceException("No pid returned from executor.") : int.Parse(executorResponse);
        }
    }
}