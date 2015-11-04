using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.IO;

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
            //ps.WorkingDirectory = Project.TOOLS_BIN;
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

        static void Main(string[] args)
        {
            string smoke_path = "d:\\git\\visualize\\hoops_3df\\bin\\nt_x64_vc12\\smoke.exe";            
            string smoke_data_dir = "C:\\users\\evan\\desktop\\smoke_data";
            string driver = "dx11";
            string baseline_dir = "C:\\users\\evan\\desktop\\smoke_data\\baselines";
            Dictionary<string, int> baselines = null;

            string error;
            string tests_string;

            if (baseline_dir != null)
            {
                baselines = new Dictionary<string, int>();

                var dat_file = Directory.GetFiles(baseline_dir, "*.dat")[0];

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
                            reader.ReadLine();
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

            if (!RunCommandLineProcess(smoke_path, string.Format("-d {0} -n -l", driver), out tests_string, out error))
                throw new Exception();

            var tests = tests_string.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries).Take(10);

            string output_dir = Path.Combine(smoke_data_dir, DateTime.Now.ToString("MM.dd.yyyy.hh.mm.ss"));
            Directory.CreateDirectory(output_dir);

            List<string> failed_tests = new List<string>();
            object sync = new object();
            Action<string> one_test = (string test) =>
            {
                string test_dir = Path.Combine(output_dir, test);
                Directory.CreateDirectory(test_dir);

                if (RunCommandLineProcess(smoke_path, string.Format("-d {0} -k -D {1} -n -t {2}", driver, test_dir, test), out tests_string, out error))
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

            Stopwatch timer = new Stopwatch();
            timer.Start();

            {
                var start = timer.Elapsed;

                Parallel.ForEach(tests, one_test);
                //tests.ForEach(one_test);

                var finish = timer.Elapsed;

                Console.WriteLine("Test run time = " + (finish - start).TotalSeconds);                                
            }

            {
                var start = timer.Elapsed;
                string captures_dir = null;

                if (baselines == null)
                {
                    captures_dir = Path.Combine(output_dir, "baselines");
                    Directory.CreateDirectory(captures_dir);

                    StreamWriter writer = null;
                    string merged_dat = null;
                    foreach (string test in tests)
                    {
                        string test_dir = Path.Combine(output_dir, test);

                        var image = Directory.GetFiles(test_dir, "*.png")[0];              
                        File.Copy(image, Path.Combine(captures_dir, Path.GetFileName(image)));                        

                        var dat_file = Directory.GetFiles(test_dir, "*.dat")[0];

                        if (writer == null)
                        {
                            merged_dat = Path.Combine(captures_dir, Path.GetFileName(dat_file));
                            File.Copy(dat_file, merged_dat);
                            writer = new StreamWriter(new FileStream(merged_dat, FileMode.Append));
                        }

                        else
                        {
                            string one_dat = File.ReadAllText(dat_file);

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
                        }

                        Directory.Delete(test_dir, true);
                    }

                    writer.Close();
                }

                else
                {
                    captures_dir = Path.Combine(output_dir, "changes");
                    Directory.CreateDirectory(captures_dir);

                    var baseline_dat = Directory.GetFiles(baseline_dir, "*.dat")[0];
                    var merged_dat = Path.Combine(captures_dir, Path.GetFileName(baseline_dat));

                    File.Copy(baseline_dat, merged_dat);
                    List<string> changed_tests = new List<string>();

                    using (StreamWriter writer = new StreamWriter(new FileStream(merged_dat, FileMode.Append)))
                    {
                        foreach (string test in tests)
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
                                    string line = string_reader.ReadLine();
                                    line = line.Substring(7);
                                    int crc = int.Parse(line, System.Globalization.NumberStyles.AllowHexSpecifier);

                                    if (crc != baselines[test])
                                    {
                                        changed_tests.Add(test);
                                        writer.Write(result);

                                        var image = Directory.GetFiles(test_dir, "*.png")[0];
                                        File.Copy(image, Path.Combine(captures_dir, Path.GetFileName(image)));

                                        var match = "*" + test + ".png";
                                        var baseline_image = Directory.GetFiles(baseline_dir, match)[0];
                                        File.Copy(baseline_image, Path.Combine(captures_dir, Path.GetFileName(baseline_image)));
                                    }
                                }                               
                            }

                            Directory.Delete(test_dir, true);
                        }
                    }                    
                }

                var finish = timer.Elapsed;

                Console.WriteLine("Merge time = " + (finish - start).TotalSeconds);

                RunCommandLineProcess(smoke_path, string.Format("-d {0} -c -x * -D {1} -n", driver, captures_dir), out tests_string, out error);
            }            
        }
    }
}
