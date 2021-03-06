// Copyright (c) https://github.com/sidiandi 2016
// 
// This file is part of tta.
// 
// tta is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// tta is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with Foobar.  If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ttaenc
{
    class SubProcess
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public static Task Cmd(CancellationToken cancellationToken, params string[] command)
        {
            return CheckedCall(cancellationToken,
                System.Environment.GetEnvironmentVariable("COMSPEC"), new[] { "/c" }.Concat(command).ToArray());
        }

        static void CopyTo(TextReader r, Action<string> w)
        {
            for (;;)
            {
                var line = r.ReadLine();
                if (line == null)
                {
                    break;
                }
                w(line);
            }
        }

        public async static Task CheckedCall(CancellationToken cancellationToken, 
            string fileName, 
            params string[] commands)
        {
            var p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    FileName = fileName,
                    Arguments = String.Join(" ", commands),
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                }
            };

            p.Start();

            var outputs = new[] {
                Task.Factory.StartNew(() =>
                {
                    CopyTo(p.StandardError, _ => log.DebugFormat("{0}:out: {1}", p.Id, _));
                }, TaskCreationOptions.LongRunning),
                Task.Factory.StartNew(() =>
                {
                    CopyTo(p.StandardError, _ => log.DebugFormat("{0}:err: {1}", p.Id, _));
                }, TaskCreationOptions.LongRunning)
            };

            var stopwatch = Stopwatch.StartNew();
            log.DebugFormat("Process {0} started. {1}", p.Id, Details(p));

            await p.WaitForExitAsync(cancellationToken);
            Task.WaitAll(outputs);

            log.DebugFormat("Process {0} finished. Exit code: {1}. Run time: {2}. {3}",
                p.Id, p.ExitCode, stopwatch.Elapsed, Details(p));


            if (p.ExitCode != 0)
            {
                throw new Exception(String.Format("Exit code {0}: {1}", p.ExitCode, Details(p)));
            }
        }

        public async static Task<string> GetOutput(CancellationToken cancellationToken,
            string fileName,
            params string[] commands)
        {
            var p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    FileName = fileName,
                    Arguments = String.Join(" ", commands),
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                }
            };

            p.Start();

            var output = new StringWriter();

            var outputs = new[] {
                Task.Factory.StartNew(() =>
                {
                    CopyTo(p.StandardError, _ => {
                        log.DebugFormat("{0}:out: {1}", p.Id, _);
                        output.WriteLine(_);
                    });
                }, TaskCreationOptions.LongRunning),
                Task.Factory.StartNew(() =>
                {
                    CopyTo(p.StandardError, _ => log.DebugFormat("{0}:err: {1}", p.Id, _));
                }, TaskCreationOptions.LongRunning)
            };

            var stopwatch = Stopwatch.StartNew();
            log.DebugFormat("Process {0} started. {1}", p.Id, Details(p));

            await p.WaitForExitAsync(cancellationToken);
            Task.WaitAll(outputs);

            log.DebugFormat("Process {0} finished. Exit code: {1}. Run time: {2}. {3}",
                p.Id, p.ExitCode, stopwatch.Elapsed, Details(p));


            if (p.ExitCode != 0)
            {
                throw new Exception(String.Format("Exit code {0}: {1}", p.ExitCode, Details(p)));
            }

            return output.ToString();
        }

        public static string Details(Process p)
        {
            return String.Format("{0} {1}", p.StartInfo.FileName, p.StartInfo.Arguments);
        }
    }

    public static class ProcessExtensions
    {
        /// <summary>
        /// Waits asynchronously for the process to exit.
        /// </summary>
        /// <param name="process">The process to wait for cancellation.</param>
        /// <param name="cancellationToken">A cancellation token. If invoked, the task will return 
        /// immediately as canceled and the process will be killed.</param>
        /// <returns>A Task representing waiting for the process to end.</returns>
        public static Task WaitForExitAsync(this Process process,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var tcs = new TaskCompletionSource<object>();
            process.EnableRaisingEvents = true;
            process.Exited += (sender, args) => tcs.TrySetResult(null);
            if (cancellationToken != default(CancellationToken))
                cancellationToken.Register(() =>
            {
                process.Kill();
                tcs.SetCanceled();
            });

            return tcs.Task;
        }
    }
}
