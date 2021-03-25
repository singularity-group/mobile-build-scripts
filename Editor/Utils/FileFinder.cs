#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using JetBrains.Annotations;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.PackageManager;
using UnityEngine;

namespace Commons.Editor {

    public static class FileFinder {

        private static string? gitRepoRoot = null;

        /// <summary>
        /// When this project is inside a git repository, this methods gets the path to the repo root.
        /// When not inside a git repository, the behaviour is undefined.
        /// </summary>
        /// <returns>The absolute path to the root of this git repository</returns>
        [NotNull]
        public static string GetRepoRoot() {
            if (gitRepoRoot == null) {
                var repoRootTask = ProcessFactory.RunGitCommand("rev-parse", "--show-toplevel");
                repoRootTask.WaitForExit();
                gitRepoRoot = repoRootTask.StandardOutput.ReadToEnd().Trim();
            }
            return gitRepoRoot;
        }

        /// <param name="endsOfFilename">The end of the filename with or without extension.
        /// e.g. "elloWorld" or "Menu.unity"</param>
        public static string[] FindAssetsNamed([NotNull] string endsOfFilename) {
            string nameWithoutExtension;
            var isCompleteFilename = Path.HasExtension(endsOfFilename);
            if (isCompleteFilename) {
                nameWithoutExtension = Path.GetFileNameWithoutExtension(endsOfFilename);
            } else {
                nameWithoutExtension = endsOfFilename;
            }
            var results = AssetDatabase.FindAssets(nameWithoutExtension);
            for (var i = 0; i < results.Length; i++) {
                var guid = results[i];
                results[i] = AssetDatabase.GUIDToAssetPath(guid);
            }

            if (isCompleteFilename) {
                // partial name did contain extension
                // only return the results that match that complete filename
                return results
                    .Where(filepath => filepath.EndsWith(endsOfFilename, StringComparison.Ordinal))
                    .ToArray();
            }

            return results;
        }

        /// <summary>
        /// Finds all directories in Packages/ that have a certain name.
        /// </summary>
        /// <param name="withSpecialName">Find directories with this exact name, e.g. 'Editor'.</param>
        /// <returns></returns>
        public static IEnumerable<string> FindPackageDirectories(string withSpecialName) {
            var allMatchingDirs = new List<string>();

            // declared in this project's manifest.json
            var localPackagesReq = Client.List(true, true);

            var timeWaited = 0;
            while (!localPackagesReq.IsCompleted) {
                Thread.Sleep(16);
                timeWaited += 16;
                if (timeWaited > 10 * 1000) {
                    Debug.LogWarning("rip package manager request took too long");
                    break;
                }
            }
            
            if (localPackagesReq.Error != null) {
                throw new BuildFailedException(localPackagesReq.Error.message);
            }
            
            foreach (var packageInfo in localPackagesReq.Result) {
                var localFound = Directory.GetDirectories(packageInfo.resolvedPath, withSpecialName,
                    SearchOption.AllDirectories);
                allMatchingDirs.AddRange(localFound);
            }
            
            return allMatchingDirs;
        }
    }
}