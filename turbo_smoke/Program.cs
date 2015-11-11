using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Runtime.InteropServices;

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
            //ps.CreateNoWindow = true;
            ps.FileName = exe;
            //ps.RedirectStandardError = true;
            //ps.RedirectStandardOutput = true;
            //ps.WindowStyle = ProcessWindowStyle.Hidden;            

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

        struct Settings
        {
            public string SmokeExecutable;
            public string OutputDir;
            public string BaselineDir;
            public string Driver;
        }

        static void ShowHelp()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("-e full path to smoke executable.");
            Console.WriteLine("-b optional path to baselines directory to use for comparison.");
            Console.WriteLine("-o output directory path, must not already exist.");
            Console.WriteLine("-O output directory path to be overwritten.");
            Console.WriteLine("-d driver type.");
        }
        static bool ProcessArgs(string[] args, out Settings settings)
        {
            settings = new Settings();
            
            for (int i = 0; i < args.Length; ++i)
            {
                var arg = args[i];

                switch (arg)
                {
                    case "-e":
                        {
                            if (i + 1 < args.Length)
                            {
                                settings.SmokeExecutable = args[++i];

                                if (!File.Exists(settings.SmokeExecutable))
                                {
                                    Console.WriteLine(string.Format("Smoke executable '[0}' does not exist.", settings.SmokeExecutable));
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

                                if (Directory.Exists(settings.OutputDir))
                                {
                                    Console.WriteLine(string.Format("Output directory '{0}' exists, use -O instead to overwrite existing directory.", settings.OutputDir));
                                    return false;
                                }
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
                                    Console.WriteLine(string.Format("Baselines directory '{0}' does not exist.", settings.BaselineDir));
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

            return true;
        }

        static void Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            if (args.Length == 0)
            {
                ShowHelp();
                Environment.Exit(0);
            }

            Settings settings;
            if (!ProcessArgs(args, out settings))
            {
                Console.WriteLine("One or more invalid arguments specified, exiting.");
                Environment.Exit(1);
            }

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
                Console.WriteLine("Load baselines time = " + (finish - start).TotalSeconds);
            }

            string tests_string;
            {
                string error;
                if (!RunCommandLineProcess(settings.SmokeExecutable, string.Format("-d {0} -n -l", settings.Driver), out tests_string, out error))
                    throw new Exception();
            }

            var tests = tests_string.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);//.Take(100).ToList();
            //tests.Add("10736_gpu_resident_timed");
        
            string output_dir = settings.OutputDir;
            if (Directory.Exists(output_dir))
                Directory.Delete(output_dir, true);
            Directory.CreateDirectory(output_dir);

            List<string> failed_tests = new List<string>();
            object sync = new object();

            NativeMethods.SetErrorMode(NativeMethods.SetErrorMode(0) | ErrorModes.SEM_NOGPFAULTERRORBOX | ErrorModes.SEM_FAILCRITICALERRORS | ErrorModes.SEM_NOOPENFILEERRORBOX);

#if false
            Action<string> one_test = (string test) =>
            {
                string test_dir = Path.Combine(output_dir, test);
                Directory.CreateDirectory(test_dir);

                string output;
                string error;
                if (RunCommandLineProcess(smoke_path, string.Format("-d {0} -k -D {1} -n -t {2}", driver, test_dir, test), out output, out error))
                {

                }

                else
                {
                    lock (sync)
                    {
                        failed_tests.Add(test);
                    }
                }
            };

            {
                var start = timer.Elapsed;

                Parallel.ForEach(tests, one_test);
                //tests.ForEach(one_test);

                var finish = timer.Elapsed;
                Console.WriteLine("Test run time = " + (finish - start).TotalSeconds);                                
            }
#else
            {
                int in_flight_tests = Environment.ProcessorCount * 3 / 2;

                Semaphore s = new Semaphore(in_flight_tests, in_flight_tests);
                List<Process> processes = new List<Process>();
                ManualResetEvent finished = new ManualResetEvent(false);

                Action<object> monitorThread = (object o) =>
                    {
                        var timeout = TimeSpan.FromSeconds(15.0);

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
                                        if (!process.HasExited)
                                            process.Kill();
                                    }
                                }
                            }
                        }
                    };

                EventHandler exited = (object sender, EventArgs e) =>
                {
                    var process = (Process)sender;

                    lock (sync)
                    {
                        processes.Remove(process);
                    }

                    string a = process.StartInfo.Arguments;
                    string test_name = a.Substring(a.IndexOf("-t") + 3);

                    if (process.ExitCode == 0)
                    {
                        //Console.WriteLine("Passed: " + test_name);
                    }

                    else
                    {
                        if ((uint)process.ExitCode == 0xFFFFFFFF)
                        {
                            Console.WriteLine("Killed: " + test_name);
                        }                        

                        else
                        {
                            Console.WriteLine("Crashed: " + test_name);
                        }

                        lock (sync)
                        {
                            failed_tests.Add(test_name);
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
                Console.WriteLine("Test run time = " + (finish - start).TotalSeconds);
            }
#endif
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
                                                changed_tests.Add(test);
                                                writer.Write(result);

                                                var images = Directory.GetFiles(test_dir, "*.png");
                                                if (images.Length > 0)
                                                    File.Copy(images[0], Path.Combine(captures_dir, Path.GetFileName(images[0])));

                                                var match = "*" + test + ".png";
                                                var baseline_image = Directory.GetFiles(settings.BaselineDir, match)[0];
                                                File.Copy(baseline_image, Path.Combine(captures_dir, Path.GetFileName(baseline_image)));
                                            }
                                        }

                                        else
                                        {
                                            Console.WriteLine(string.Format("No baseline found for '{0}'.", test));

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

                Console.WriteLine("Merge time = " + (finish - start).TotalSeconds);

                if (captures_dir != null)
                {
                    string output;
                    string error;
                    RunCommandLineProcess(settings.SmokeExecutable, string.Format("-d {0} -c -x * -D {1} -n", settings.Driver, captures_dir), out output, out error);
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
