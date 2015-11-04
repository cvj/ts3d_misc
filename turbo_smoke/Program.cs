﻿using System;
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
            string smoke_path = "C:\\git\\visualize\\hoops_3df\\bin\\nt_x64_vc14\\smoke.exe";
            string smoke_data_dir = "C:\\users\\evan\\desktop\\smoke_data/";
            string driver = "dx11";

            string error;
            string tests_string;

            if (!RunCommandLineProcess(smoke_path, string.Format("-d {0} -n -l", driver), out tests_string, out error))
                throw new Exception();

            var tests = tests_string.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries).ToList();

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

            var start = timer.Elapsed;
            
            Parallel.ForEach(tests, one_test);
            //tests.ForEach(one_test);

            var finish = timer.Elapsed;

            Console.WriteLine("Test run time = " + (finish - start).TotalSeconds);

            string captures_dir = Path.Combine(output_dir, "captures");
            Directory.CreateDirectory(captures_dir);
            foreach (string test in tests)
            {
                string test_dir = Path.Combine(output_dir, test);
                var images = Directory.GetFiles(test_dir, "*.png");

                foreach (string image in images)
                {
                    File.Copy(image, Path.Combine(captures_dir, Path.GetFileName(image)));
                }
            }

            Console.Read();
        }
    }
}