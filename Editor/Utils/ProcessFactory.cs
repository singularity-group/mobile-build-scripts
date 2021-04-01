using System;
using System.Diagnostics;
using System.IO;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.Assertions;
using Debug = UnityEngine.Debug;

namespace Commons.Editor {

    internal static class ProcessFactory {

        public static Process RunGitCommand(string command, string arguments, bool redirectOutput = false) {
            var proc = StartProcess("git", $"{command} {arguments}", redirectOutput: redirectOutput);
            proc.WaitForExit();
            return proc;
        }

        /// <summary>
        /// Start a process and return it.
        /// </summary>
        /// <param name="fileName"></param>
        /// <param name="arguments"></param>
        /// <param name="useShell"></param>
        /// <param name="redirectOutput"></param>
        /// <param name="workingDirectory"></param>
        /// <param name="tag"></param>
        /// <param name="logOut"></param>
        /// <returns></returns>
        public static Process StartProcess(string fileName, string arguments, bool useShell = false,
            bool redirectOutput = false, string workingDirectory = null, string tag = null, bool logOut = false) {
            var process = new Process {
                StartInfo = ProcessInfoFactory.Create(fileName, arguments, useShell, redirectOutput, workingDirectory)
            };

            if (redirectOutput) {
                process.OutputDataReceived += (sender, e) => {
                    var data = e.Data;
                    if (data != null && logOut) {
                        Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null,$"[{tag ?? fileName}] {data}");
                    }
                };
                process.ErrorDataReceived += (sender, e) => {
                    var data = e.Data;
                    if (data != null) {
                        Debug.LogFormat(LogType.Error, LogOption.NoStacktrace, null,$"[{tag ?? fileName}] {data}");
                    }
                };
            }

            process.Start();

            if (redirectOutput) {
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }
            return process;
        }

        /// <summary>
        /// Run a process, blocking until it has finished or <paramref name="timeoutSeconds"/> is
        /// reached.
        /// </summary>
        /// <param name="filename"></param>
        /// <param name="arguments"></param>
        /// <param name="timeoutSeconds"></param>
        /// <exception cref="Exception">Timed out waiting for process to finish, it had to be killed.</exception>
        public static void RunProcess([NotNull] string filename, [NotNull] string arguments, int timeoutSeconds, bool logStdout = true) {
            Debug.Log($"Running process '{filename} {arguments}'");
            var process = StartProcess(filename, arguments,
                workingDirectory: Path.GetDirectoryName(filename), redirectOutput: true,
                logOut: logStdout, tag: Path.GetFileName(filename));

            Assert.IsNotNull(process, nameof(process) + " != null");

            process.WaitForExit(timeoutSeconds * 1000);
            var timedOut = !process.HasExited;

            if (timedOut) process.Kill();

            bool success = !timedOut && process.ExitCode == 0;

            if (success) return;

            if (timedOut) {
                Debug.LogError(
                    $"timed out executing '{filename} {arguments}'\nWaited for {timeoutSeconds} seconds.");
                Debug.Log($"exit code was {process.ExitCode}");
            }

            throw new Exception($"process {filename} {arguments} failed");
        }
    }

    public static class ProcessInfoFactory {

        public static ProcessStartInfo Create(string fileName, string arguments, bool useShell, bool redirectOutput,
            string workingDirectory) {

            var info = new ProcessStartInfo {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = useShell && !redirectOutput,
                CreateNoWindow = !(useShell && !redirectOutput),
                RedirectStandardError = redirectOutput,
                RedirectStandardOutput = redirectOutput,
            };

            if (!String.IsNullOrEmpty(workingDirectory)) {
                info.WorkingDirectory = workingDirectory;
            }

            return info;
        }

    }
}
