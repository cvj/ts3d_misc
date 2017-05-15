using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Drawing.Imaging;

namespace turbo_smoke
{
    class Program
    { 
        static bool RunCommandLineProcess(string exe, string args, out string stdout, out string stderror)
        {
            ProcessStartInfo ps = new ProcessStartInfo();

            ps.Arguments = args;
            ps.UseShellExecute = false;
            ps.CreateNoWindow = true;            
            ps.FileName = exe;
            ps.RedirectStandardError = true;
            ps.RedirectStandardOutput = true;

            using (Process process = Process.Start(ps))
            {
                stdout = process.StandardOutput.ReadToEnd().TrimEnd('\r', '\n');
                stderror = process.StandardError.ReadToEnd().TrimEnd('\r', '\n');

                process.WaitForExit();

                return process.ExitCode == 0;
            }            
        }

        static Process RunCommandLineProcess(string exe, string args, EventHandler exited)
        {
            ProcessStartInfo ps = new ProcessStartInfo();

            ps.Arguments = args;
            ps.UseShellExecute = false;
            ps.CreateNoWindow = true;
            ps.FileName = exe;
            //ps.RedirectStandardError = true;
            //ps.RedirectStandardOutput = true;
            ps.WindowStyle = ProcessWindowStyle.Hidden;
            ps.EnvironmentVariables.Add("HOOPS_DRIVER_OPTIONS", "debug=0x00040000");

            Process process = new Process();
            process.StartInfo = ps;
            process.Exited += exited;
            process.EnableRaisingEvents = true;                

            process.Start();
            return process;
        }

        [Flags]
        internal enum ErrorModes : uint
        {
            SYSTEM_DEFAULT = 0x0,
            SEM_FAILCRITICALERRORS = 0x0001,
            SEM_NOALIGNMENTFAULTEXCEPT = 0x0004,
            SEM_NOGPFAULTERRORBOX = 0x0002,
            SEM_NOOPENFILEERRORBOX = 0x8000
        }

        internal static class NativeMethods
        {
            [DllImport("kernel32.dll")]
            internal static extern ErrorModes SetErrorMode(ErrorModes mode);
        }

        class Settings
        {
            public string SmokeExecutable;
            public string OutputDir;
            public string BaselineDir;
            public string Driver;
            public string ExcludeList;
            public int InFlightTests = Environment.ProcessorCount;
        }

        static void ShowHelp()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("-e full path to smoke executable.");
            Console.WriteLine("-b optional path to baselines directory to use for comparison.");
            Console.WriteLine("-o output directory path, must not already exist.");
            Console.WriteLine("-O output directory path to be overwritten.");
            Console.WriteLine("-d driver type.");
            Console.WriteLine("-X path to file containing tests to exclude.");
            Console.WriteLine("-i max number of in flight tests.");
        }
        static bool ProcessArgs(string[] args, out Settings settings)
        {
            settings = new Settings();
            
            for (int i = 0; i < args.Length; ++i)
            {
                var arg = args[i];

                switch (arg)
                {
                    case "-i":
                        {
                            settings.InFlightTests = int.Parse(args[++i]);
                        }
                        break;

                    case "-e":
                        {
                            if (i + 1 < args.Length)
                            {
                                settings.SmokeExecutable = args[++i];

                                if (!File.Exists(settings.SmokeExecutable))
                                {
                                    Console.WriteLine("Smoke executable '{0}' does not exist.", settings.SmokeExecutable);
                                    return false;
                                }
                            }

                            else
                            {
                                Console.WriteLine("No smoke executable path specified with -e option.");
                                return false;
                            }
                        }
                        break;

                    case "-o":
                        {
                            if (i + 1 < args.Length)
                            {
                                settings.OutputDir = args[++i];

                                //if (Directory.Exists(settings.OutputDir))
                                //{
                                  //  Console.WriteLine("Output directory '{0}' exists, use -O instead to overwrite existing directory.", settings.OutputDir);
                                    //return false;
                                //}
                            }

                            else
                            {
                                Console.WriteLine("No output directory specified with -o option.");
                                return false;
                            }
                        }
                        break;

                    case "-O":
                        {
                            if (i + 1 < args.Length)
                            {
                                settings.OutputDir = args[++i];
                            }

                            else
                            {
                                Console.WriteLine("No output directory specified with -O option.");
                                return false;
                            }
                        }
                        break;

                    case "-b":
                        {
                            if (i + 1 < args.Length)
                            {
                                settings.BaselineDir = args[++i];

                                if (!Directory.Exists(settings.BaselineDir))
                                {
                                    Console.WriteLine("Baselines directory '{0}' does not exist.", settings.BaselineDir);
                                    return false;
                                }
                            }

                            else
                            {
                                Console.Write("No baselines directory specified with -b option.");
                                return false;
                            }
                        }
                        break;

                    case "-d":
                        {
                            if (i + 1 < args.Length)
                            {
                                settings.Driver = args[++i];
                            }

                            else
                            {
                                Console.WriteLine("No driver specified with -d option.");
                                return false;
                            }
                        }
                        break;

                    case "-X":
                        {
                            if (i + 1 < args.Length)
                            {
                                settings.ExcludeList = args[++i];
                            }

                            else
                            {
                                Console.WriteLine("No exclude list file specified with -X option.");
                                return false;
                            }
                        }
                        break;
                }
            }

            if (settings.OutputDir == null)
            {
                Console.WriteLine("No output directory specified.");
                return false;
            }            

            if (settings.SmokeExecutable == null)
            {
                Console.WriteLine("No smoke executable path specified.");
                return false;
            }

            if (settings.Driver == null)
            {
                Console.WriteLine("No driver specified.");
                return false;
            }

            if (settings.OutputDir != null)
                settings.OutputDir = Path.Combine(settings.OutputDir, settings.Driver);

            if (settings.BaselineDir != null)
                settings.BaselineDir = Path.Combine(settings.BaselineDir, settings.Driver);

            return true;
        }

        static void Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            if (args.Length == 0)
            {
                ShowHelp();
                Console.ReadKey();
                Environment.Exit(0);
            }

            Settings settings;
            if (!ProcessArgs(args, out settings))
            {
                Console.WriteLine("One or more invalid arguments specified, exiting.");
                Console.ReadKey();
                Environment.Exit(1);
            }

            string output_dir = settings.OutputDir;
            if (Directory.Exists(output_dir))
                Directory.Delete(output_dir, true);
            Directory.CreateDirectory(output_dir);

            Dictionary<string, int> baselines = null;

            Stopwatch timer = new Stopwatch();
            timer.Start();

            if (settings.BaselineDir != null)
            {
                var start = timer.Elapsed;

                baselines = new Dictionary<string, int>();

                var dat_file = Directory.GetFiles(settings.BaselineDir, "*.dat")[0];

                using (StreamReader reader = new StreamReader(File.OpenRead(dat_file)))
                {
                    while (!reader.EndOfStream)
                    {
                        string line = reader.ReadLine();

                        if (line.StartsWith("TEST:"))
                        {
                            string test_name = line.Substring(6);
                            reader.ReadLine();
                            reader.ReadLine();
                            reader.ReadLine();
                            line = reader.ReadLine();
                            if (line.Contains("TYPE: CAPTURE"))
                            {
                                reader.ReadLine();
                                reader.ReadLine();
                                line = reader.ReadLine();
                                line = line.Substring(7);
                                int crc = int.Parse(line, System.Globalization.NumberStyles.AllowHexSpecifier);
                                baselines.Add(test_name, crc);
                            }
                        }
                    }
                }

                var finish = timer.Elapsed;
                Console.WriteLine("Load baselines time = {0:F2} sec.", (finish - start).TotalSeconds);
            }

            string tests_string;
            {
                string error;
                if (!RunCommandLineProcess(settings.SmokeExecutable, string.Format("-d {0} -n -l", settings.Driver), out tests_string, out error))
                    throw new Exception();
            }

            var tests = tests_string.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries).AsEnumerable();

            if (settings.ExcludeList != null)
            {
                List<string> exclude = new List<string>();
                using (var reader = File.OpenText(settings.ExcludeList))
                {
                    while (!reader.EndOfStream)
                    {
                        var line = reader.ReadLine();
                        var test = line.Split(' ')[0];
                        exclude.Add(test);
                    }
                }

                tests = tests.Except(exclude);
            }            

            NativeMethods.SetErrorMode(NativeMethods.SetErrorMode(0) | ErrorModes.SEM_NOGPFAULTERRORBOX | ErrorModes.SEM_FAILCRITICALERRORS | ErrorModes.SEM_NOOPENFILEERRORBOX);
                        
            List<string> failed_tests = new List<string>();

            {
                int in_flight_tests = settings.InFlightTests;

                if (Environment.ProcessorCount <= 4 || settings.Driver.Contains("opengl"))
                    in_flight_tests = Math.Min(settings.InFlightTests, 4);
                
                bool record_test_times = false;
                Dictionary<string, TimeSpan> test_times = null;
                if (record_test_times)
                    test_times = new Dictionary<string, TimeSpan>();

                object sync = new object();
                Semaphore s = new Semaphore(in_flight_tests, in_flight_tests);
                List<Process> processes = new List<Process>();
                ManualResetEvent finished = new ManualResetEvent(false);

                Action<object> monitorThread = (object o) =>
                    {
                        var timeout = TimeSpan.FromSeconds(100);

                        while (!finished.WaitOne(1000))
                        {
                            var now = DateTime.Now;

                            lock (sync)
                            {
                                // Kill below will synchronously call exit handler and modify collection
                                for (int i = processes.Count - 1; i >= 0; --i)
                                {
                                    var process = processes[i];

                                    if (now - process.StartTime > timeout)
                                    {
                                        string a = process.StartInfo.Arguments;
                                        string test_name = a.Substring(a.IndexOf("-t") + 3);

                                        //if (!process.HasExited)
                                        try
                                        {                                            
                                            Console.WriteLine("Killing: " + test_name);
                                            process.Kill();
                                        }

                                        catch (Exception e)
                                        {
                                            Console.WriteLine("Couldn't kill {0}: '{1}'.", test_name, e.Message);
                                        }
                                    }
                                }
                            }
                        }
                    };

                EventHandler exited = (object sender, EventArgs e) =>
                {
                    var process = (Process)sender;
                    string a = process.StartInfo.Arguments;
                    string test_name = a.Substring(a.IndexOf("-t") + 3);

                    lock (sync)
                    {
                        processes.Remove(process);

                        if (process.ExitCode == 0)
                        {
       
                        }

                        else
                        {
                            failed_tests.Add(test_name);

                            if ((uint)process.ExitCode == 0xFFFFFFFF)
                                Console.WriteLine("Killed: " + test_name);

                            else
                                Console.WriteLine("Crashed: " + test_name);
                        }

                        if (record_test_times)
                        {
                            TimeSpan time = DateTime.Now - process.StartTime;
                            test_times.Add(test_name, time);
                        }
                    }

                    s.Release();
                };

                var start = timer.Elapsed;

                ThreadPool.QueueUserWorkItem(new WaitCallback(monitorThread));

                foreach (var test in tests)
                {
                    string test_dir = Path.Combine(output_dir, test);
                    Directory.CreateDirectory(test_dir);

                    s.WaitOne();
                    var process = RunCommandLineProcess(settings.SmokeExecutable, string.Format("-d {0} -k -D {1} -n -t {2}", settings.Driver, test_dir, test), exited);

                    lock(sync)
                    {
                        processes.Add(process);                        
                    }
                }

                for (int i = 0; i < in_flight_tests; ++i)
                    s.WaitOne();

                Debug.Assert(processes.Count == 0);

                finished.Set();

                var finish = timer.Elapsed;
                Console.WriteLine("Test run time = {0:F2} sec.", (finish - start).TotalSeconds);

                if (record_test_times)
                {
                    const string times_filename = "c:/users/evan/desktop/smoke_data/test_times.txt";
                    using (StreamWriter writer = new StreamWriter(new FileStream(times_filename, FileMode.Create)))
                    {
                        foreach (var test in test_times.OrderByDescending(a => a.Value))
                        {
                            writer.WriteLine("{0} {1}", test.Key, test.Value.TotalSeconds);
                        }
                    }
                }
            }

            {
                var start = timer.Elapsed;
                string captures_dir = null;

                if (baselines == null)
                {
                    captures_dir = output_dir;
                    Directory.CreateDirectory(captures_dir);

                    StreamWriter writer = null;
                    foreach (string test in tests.Except(failed_tests))
                    {
                        string test_dir = Path.Combine(output_dir, test);

                        var dat_file = Directory.GetFiles(test_dir, "*.dat")[0];

                        string one_dat = File.ReadAllText(dat_file);

                        if (writer == null)
                        {
                            string merged_dat = Path.Combine(captures_dir, Path.GetFileName(dat_file));
                            writer = new StreamWriter(new FileStream(merged_dat, FileMode.Append));
                            writer.Write(one_dat);
                        }

                        if (one_dat.Contains("RESULT: CAPTURE"))
                        {
                            var images = Directory.GetFiles(test_dir, "*.png");
                            if (images.Length > 0)
                                File.Copy(images[0], Path.Combine(captures_dir, Path.GetFileName(images[0])));
                        }

                        using (StringReader dat_reader = new StringReader(one_dat))
                        {
                            dat_reader.ReadLine();
                            dat_reader.ReadLine();
                            dat_reader.ReadLine();
                            dat_reader.ReadLine();
                            dat_reader.ReadLine();
                            dat_reader.ReadLine();
                            dat_reader.ReadLine();

                            var the_rest = dat_reader.ReadToEnd();

                            writer.Write(the_rest);
                        }

                        Directory.Delete(test_dir, true);
                    }

                    writer.Close();
                }

                else
                {
                    captures_dir = output_dir;
                    Directory.CreateDirectory(captures_dir);

                    var baseline_dat = Directory.GetFiles(settings.BaselineDir, "*.dat")[0];
                    var merged_dat = Path.Combine(captures_dir, Path.GetFileName(baseline_dat));

                    File.Copy(baseline_dat, merged_dat);
                    List<string> changed_tests = new List<string>();

                    using (StreamWriter writer = new StreamWriter(new FileStream(merged_dat, FileMode.Append)))
                    {
                        foreach (string test in tests.Except(failed_tests))
                        {
                            string test_dir = Path.Combine(output_dir, test);

                            var dat_file = Directory.GetFiles(test_dir, "*.dat")[0];
                            using (StreamReader dat_reader = new StreamReader(File.OpenRead(dat_file)))
                            {
                                dat_reader.ReadLine();
                                dat_reader.ReadLine();
                                dat_reader.ReadLine();
                                dat_reader.ReadLine();
                                dat_reader.ReadLine();
                                dat_reader.ReadLine();
                                dat_reader.ReadLine();

                                // the whole test result block
                                string result = dat_reader.ReadToEnd();

                                if (result.Contains("TYPE: CAPTURE"))
                                {
                                    // now parse the result block to find the crc value.
                                    using (StringReader string_reader = new StringReader(result))
                                    {
                                        string_reader.ReadLine();
                                        string_reader.ReadLine();
                                        string_reader.ReadLine();
                                        string_reader.ReadLine();
                                        string_reader.ReadLine();
                                        string_reader.ReadLine();
                                        string_reader.ReadLine();

                                        var line = string_reader.ReadLine();
                                        line = line.Substring(7);
                                        int crc = int.Parse(line, System.Globalization.NumberStyles.AllowHexSpecifier);

                                        int baseline_crc;
                                        if (baselines.TryGetValue(test, out baseline_crc))
                                        {
                                            if (baseline_crc != crc)
                                            {
                                                string baseline_image = Directory.GetFiles(settings.BaselineDir, "*" + test + ".png")[0];
                                                string new_image = null;
                                                {
                                                    var images = Directory.GetFiles(test_dir, "*.png");
                                                    if (images.Length > 0)
                                                        new_image = Path.Combine(test_dir, Path.GetFileName(images[0]));
                                                }
                                                
                                                if (new_image != null)
                                                {
                                                    int diff_count = 0;
                                                    using (var before = (Bitmap)Bitmap.FromFile(baseline_image))
                                                    {
                                                        using (var after = (Bitmap)Bitmap.FromFile(new_image))
                                                        {
                                                            var beforeData = before.LockBits(new Rectangle(0, 0, before.Width, before.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
                                                            var afterData = after.LockBits(new Rectangle(0, 0, after.Width, after.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);

                                                            for (int y = 0, height = beforeData.Height; y < height; ++y)
                                                            {
                                                                for (int x = 0, width = beforeData.Width; x < width; ++x)
                                                                {
                                                                    unsafe 
                                                                    {
                                                                        var p1 = (int*)(beforeData.Scan0 + y * beforeData.Stride + x * 4);
                                                                        var p2 = (int*)(afterData.Scan0 + y * afterData.Stride + x * 4);

                                                                        if (*p1 != *p2)
                                                                            ++diff_count;                                                               
                                                                    }
                                                                }
                                                            }

                                                            after.UnlockBits(afterData);
                                                            before.UnlockBits(beforeData);
                                                        }
                                                    }

                                                    const int dust_threshold = 1500;

                                                    if (diff_count > dust_threshold)
                                                    {
                                                        changed_tests.Add(test);
                                                        writer.Write(result);

                                                        File.Copy(new_image, Path.Combine(captures_dir, Path.GetFileName(new_image)));
                                                        File.Copy(baseline_image, Path.Combine(captures_dir, Path.GetFileName(baseline_image)));
                                                    }
                                                }
                                            }
                                        }

                                        else
                                        {
                                            Console.WriteLine("No baseline found for '{0}'.", test);

                                            changed_tests.Add(test);
                                            writer.Write(result);

                                            var images = Directory.GetFiles(test_dir, "*.png");
                                            if (images.Length > 0)
                                                File.Copy(images[0], Path.Combine(captures_dir, Path.GetFileName(images[0])));
                                        }
                                    }
                                }
                            }

                            Directory.Delete(test_dir, true);
                        }
                    }

                    if (changed_tests.Count == 0)
                    {
                        Directory.Delete(captures_dir, true);
                        captures_dir = null;
                        Console.WriteLine("No changed images.");
                    }
                }

                var finish = timer.Elapsed;

                Console.WriteLine("Merge time = {0:F2} sec.", (finish - start).TotalSeconds);

                if (captures_dir != null)
                {
                    string output;
                    string error;
                    RunCommandLineProcess(settings.SmokeExecutable, string.Format("-d {0} -c -M 50 -x * -D {1} -n", settings.Driver, captures_dir), out output, out error);
                }

                Console.WriteLine("Press any key to exit.");
                Console.ReadKey();
            }
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
                        
        }
    }
}
