using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using GooglePlayServices;
using UnityEngine;

namespace Commons.Editor {
    public class CocoapodsHelper {

        // Pod executable filename.
        private static string POD_EXECUTABLE = "pod";

        // Default paths to search for the "pod" command before falling back to
        // querying the Ruby Gem tool for the environment.
        private static readonly string[] POD_SEARCH_PATHS = new string[] {
            "/usr/local/bin",
            "/usr/bin",
        };

        // Ruby Gem executable filename.
        private static string GEM_EXECUTABLE = "gem";

        public static void Install(string pathToBuiltProject) {
            RunPodCommand("install", pathToBuiltProject);
        }
        
        /// <summary>
        /// Finds and executes the pod command on the command line, using the
        /// correct environment.
        /// </summary>
        /// <param name="podArgs">Arguments passed to the pod command.</param>
        /// <param name="pathToBuiltProject">The path to the unity project, given
        /// from the unity [PostProcessBuildAttribute()] function.</param>
        /// <param name="completionDelegate">Called when the command is complete.</param>
        private static void RunPodCommandAsync(
                string podArgs, string pathToBuiltProject,
                CommandLine.CompletionHandler completionDelegate) {
            string podCommand = FindPodTool();
            if (String.IsNullOrEmpty(podCommand)) {
                var result = new CommandLine.Result();
                result.exitCode = 1;
                result.stderr = "'pod' command not found; unable to generate a usable Xcode project.";
                Debug.LogError(result.stderr);
                completionDelegate(result);
            }
            RunCommandAsync(podCommand, podArgs, completionDelegate,
                            pathToBuiltProject);
        }

        /// <summary>
        /// Finds and executes the pod command on the command line, using the
        /// correct environment.
        /// </summary>
        /// <param name="podArgs">Arguments passed to the pod command.</param>
        /// <param name="pathToBuiltProject">The path to the unity project, given
        ///     from the unity [PostProcessBuildAttribute()] function.</param>
        /// <returns>The CommandLine.Result from running the command.</returns>
        private static void RunPodCommand(string podArgs,
            string pathToBuiltProject) {
            var complete = new AutoResetEvent(false);
            RunPodCommandAsync(podArgs, pathToBuiltProject,
                               asyncResult => {
                                   complete.Set();
                               });
            complete.WaitOne();
        }

        /// <summary>
        /// Find the "pod" tool.
        /// </summary>
        /// <returns>Path to the pod tool if successful, null otherwise.</returns>
        private static string FindPodTool() {
            foreach (string path in POD_SEARCH_PATHS) {
                string podPath = Path.Combine(path, POD_EXECUTABLE);
                Debug.Log("Searching for CocoaPods tool in " + podPath);
                if (File.Exists(podPath)) {
                    Debug.Log("Found CocoaPods tool in " + podPath);
                    return podPath;
                }
            }

            Debug.Log("Querying gems for CocoaPods install path");
            var environment = ReadGemsEnvironment();
            if (environment != null) {
                const string executableDir = "EXECUTABLE DIRECTORY";
                foreach (string environmentVariable in new[] { executableDir, "GEM PATHS" }) {
                    if (environment.TryGetValue(environmentVariable, out var paths)) {
                        foreach (var path in paths) {
                            var binPath = environmentVariable == executableDir
                                ? path
                                : Path.Combine(path, "bin");
                            var podPath = Path.Combine(binPath, POD_EXECUTABLE);
                            Debug.Log("Checking gems install path for CocoaPods tool " + podPath);
                            if (File.Exists(podPath)) {
                                Debug.Log("Found CocoaPods tool in " + podPath);
                                return podPath;
                            }
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Read the Gems environment.
        /// </summary>
        /// <returns>Dictionary of environment properties or null if there was a problem reading
        /// the environment.</returns>
        private static Dictionary<string, List<string>> ReadGemsEnvironment() {
            var result = RunCommand(GEM_EXECUTABLE, "environment");
            if (result.exitCode != 0) {
                return null;
            }

            // gem environment outputs YAML for all config variables.  Perform some very rough YAML
            // parsing to get the environment into a usable form.
            var gemEnvironment = new Dictionary<string, List<string>>();
            const string listItemPrefix = "- ";
            int previousIndentSize = 0;
            List<string> currentList = null;
            char[] listToken = new char[] { ':' };
            foreach (var line in result.stdout.Split(new char[] { '\r', '\n' })) {
                var trimmedLine = line.Trim();
                var indentSize = line.Length - trimmedLine.Length;
                if (indentSize < previousIndentSize) currentList = null;

                if (trimmedLine.StartsWith(listItemPrefix)) {
                    trimmedLine = trimmedLine.Substring(listItemPrefix.Length).Trim();
                    if (currentList == null) {
                        var tokens = trimmedLine.Split(listToken);
                        currentList = new List<string>();
                        gemEnvironment[tokens[0].Trim()] = currentList;
                        var value = tokens.Length == 2 ? tokens[1].Trim() : null;
                        if (!String.IsNullOrEmpty(value)) {
                            currentList.Add(value);
                            currentList = null;
                        }
                    } else if (indentSize >= previousIndentSize) {
                        currentList.Add(trimmedLine);
                    }
                } else {
                    currentList = null;
                }

                previousIndentSize = indentSize;
            }

            return gemEnvironment;
        }
        
        #region run command

        /// <summary>
        /// Run a command, optionally displaying a dialog.
        /// </summary>
        /// <param name="command">Command to execute.</param>
        /// <param name="commandArgs">Arguments passed to the command.</param>
        /// <param name="completionDelegate">Called when the command is complete.</param>
        /// <param name="workingDirectory">Where to run the command.</param>
        private static void RunCommandAsync(string command, string commandArgs,
                                            CommandLine.CompletionHandler completionDelegate,
                                            string workingDirectory = null) {
            var envVars = new Dictionary<string, string>() {
                // CocoaPods requires a UTF-8 terminal, otherwise it displays a warning.
                {
                    "LANG", (Environment.GetEnvironmentVariable("LANG") ??
                             "en_US.UTF-8").Split('.')[0] + ".UTF-8"
                }, {
                    "PATH", ("/usr/local/bin:" +
                             (Environment.GetEnvironmentVariable("PATH") ?? ""))
                },
            };
            
            Debug.Log(command);
            var result = CommandLine.RunViaShell(command, commandArgs,
                workingDirectory,
                envVars,
                useShellExecution: false);
            Debug.Log(result);
            completionDelegate(result);
        }

        /// <summary>
        /// Run a command, optionally displaying a dialog.
        /// </summary>
        /// <param name="command">Command to execute.</param>
        /// <param name="commandArgs">Arguments passed to the command.</param>
        /// <param name="workingDirectory">Where to run the command.</param>
        /// <returns>The CommandLine.Result from running the command.</returns>
        private static CommandLine.Result RunCommand(string command, string commandArgs,
                                                     string workingDirectory = null) {
            CommandLine.Result result = null;
            var complete = new AutoResetEvent(false);
            RunCommandAsync(command, commandArgs,
                            asyncResult => {
                                result = asyncResult;
                                complete.Set();
                            }, workingDirectory);
            complete.WaitOne(120_000);
            return result;

        }
        #endregion
    }
}