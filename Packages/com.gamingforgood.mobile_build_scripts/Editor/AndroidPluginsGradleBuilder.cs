using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Commons.Editor {
    public static class AndroidPluginsGradleBuilder {

        /// Build android modules, single-thread blocking.
        ///
        /// <param name="outputPluginsFolder">
        /// Path to a folder like Assets/{anything}/Plugins
        /// The built android libs (.aar files) will be copied into {outputPluginsFolder}/Android.
        /// The modulename-Dependencies.xml files will be written to {outputPluginsFolder}/Editor.
        /// </param>
        /// <param name="neededModules">names of needed modules e.g. ["golive", "util"]</param>
        public static void AssembleAars(HashSet<string> neededModules, string outputFolder) {
            var first = true;
            foreach (var projectName in neededModules) {
                // the first one can take a lot longer on cloud build
                // because it triggers "Install NDK ...", idk why.
                var timeout = 60 * 5 + (first ? 60 * 5 : 0);
                AssembleAar(projectName, outputFolder, timeout);
                first = false;
            }
        }

        /// Run the build+copy task for the aar (See AndroidPlugins/build.gradle)
        public static void AssembleAar(string moduleName, string outputFolder, int timeout = 60 * 5) {
            string absolutePath = Path.GetFullPath(outputFolder);
            Debug.Assert(Directory.Exists(outputFolder));
            RunGradlew($"{moduleName}_aar --outputPath=\"{absolutePath}\"", timeout);
        }

        public static void StopGradleDaemons() {
            RunGradlew("gradle --stop", 30);
        }

        /// Generate all the *Dependencies.xml files
        public static void GenerateDependenciesXml(string outputFolder, IEnumerable<string> neededModules) {
            string absoluteOutputDir = Path.GetFullPath(outputFolder);
            Debug.Assert(Directory.Exists(absoluteOutputDir));

            var projectNames = String.Join(",", neededModules);
            var flags =
                $"--includedProjectNames={projectNames} --outputPath=\"{absoluteOutputDir}\"";
            RunGradlew("generateDepsXmls " + flags, 60 * 2);
        }

        /// <summary>
        /// Run a gradle task inside the /AndroidPlugins/ gradle project
        /// </summary>
        private static void RunGradlew(string arguments, int timeoutSeconds) {
            if (Application.isBatchMode) {
                throw new Exception("Coder mistake, should not call RunGradlew in cloud build,"
                    + " because docker windows runner cannot reliably run gradle.");
            }
            // Removed retry because it caused deadlock in demo project
            //Retries.DoActionWithRetry(() => _RunGradlew(arguments, timeoutSeconds), 3);
            _RunGradlew(arguments, timeoutSeconds);
        }
        
        /// Dont call directly, use <see cref="RunGradlew"/>. 
        private static void _RunGradlew(string arguments, int timeoutSeconds) {
            string pathToAndroidPlugins = Path.Combine(FileFinder.GetRepoRoot(), "MobilePlugins");
            bool isWinEditor = Application.platform == RuntimePlatform.WindowsEditor;
            // in batch mode, gradle daemon does not let the process exit.
            // if (Application.isBatchMode) {
            //     arguments += " --no-daemon";
            // }

            var gradlew = Path.Combine(pathToAndroidPlugins,
                isWinEditor ? "gradlew.bat" : "gradlew");
            // on cloud machines, the android sdk path is not known to gradle
            //sdk.dir=/Users/g4g/Library/Android/sdk
            string localPropertiesFile = Path.Combine(pathToAndroidPlugins, "local.properties");
            string fileContents;
            if (File.Exists(localPropertiesFile)) {
                fileContents = File.ReadAllText(localPropertiesFile);
            } else {
                fileContents = "";
            }

            // set path to android sdk if needed
            // setting ANDROID_SDK_ROOT env var might also work
            if (fileContents.Contains("sdk.dir=")) {
                // already set, print the path for debugging purposes
                var start = fileContents.IndexOf("sdk.dir=", StringComparison.Ordinal);
                var sdkDir = fileContents.Substring(start + "sdk.dir=".Length);
                Debug.Log($"using sdk.dir={sdkDir} (local.properties file already had it)");
            } else {
                var sdkDir = TryGetAndroidSdkDirectory();
                if (sdkDir == default) {
                    throw new BuildFailedException("Android sdk could not be found on this machine. It was not installed with unity, not set as environment variable and not in AppData.");
                }

                // note: gradle's sdk.dir path must use forward slashes '/' and must NOT be surrounded by quotes
                sdkDir = sdkDir.Replace(@"\", "/");
                
                Debug.Log($"using sdk.dir={sdkDir} (created local.properties file)");
                fileContents += $"\nsdk.dir={sdkDir}";
                File.WriteAllText(localPropertiesFile, fileContents);
            }

            ProcessFactory.RunProcess(gradlew, arguments, timeoutSeconds);
        }

        private static string TryGetAndroidSdkDirectory() {
            // prefer the SDK installed on this machine
            // on windows this looks like C:\Users\G4G\AppData\Local\Android\Sdk
            var sdkInAppData =
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Android", "Sdk");
            var androidSdkLocations = new[] {
                // Build/Build/Program.cs sets this env var, we want to use the same installation  
                Environment.GetEnvironmentVariable("ANDROID_HOME"),
                sdkInAppData,
                // editor sets AndroidSdkRoot, see https://answers.unity.com/answers/495988/view.html
                EditorPrefs.GetString("AndroidSdkRoot"),
            };
            return androidSdkLocations.FirstOrDefault(Directory.Exists);
        }
    }
}