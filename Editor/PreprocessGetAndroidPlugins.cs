using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Assertions;
using YamlDotNet.RepresentationModel;
using Debug = UnityEngine.Debug;

namespace Commons.Editor {

    /// <summary>
    /// Before unity build, this class assembles the needed Android plugins and updates
    /// the unity-jar-resolver '*Dependencies.xml' files.
    /// </summary>
    internal static class PreprocessGetAndroidPlugins {

        private static string outputFolder => Path.Combine(Application.dataPath, "Plugins", "MobilePlugins");

        [MenuItem("Build/Advanced/Test AndroidPlugins prebuilder")]
        public static void Run() {
            var daemonStarted = AssembleAarsAndEnsureDeps(true);
            if (daemonStarted && Application.isBatchMode) {
                // Unity Editor running in batch mode can be stuck open due to gradle daemons
                // left running that were started as a side effect of running gradle tasks.
                AndroidPluginsGradleBuilder.StopGradleDaemons();
            }
        }

        /// <summary>
        /// 1. Builds the needed /AndroidPlugins/ aars
        /// 2. Generates *Dependencies.xml files with dependencies of /AndroidPlugins/ project
        /// 3. Updates `mainTemplate.gradle` by running `unity-jar-resolver`
        /// </summary>
        /// <returns>Whether a gradle daemon was started</returns>
        private static bool AssembleAarsAndEnsureDeps(bool printGradleTemplate = false) {
            // removed cache check because android plugins are now always built in a previous job. 
            // if (File.Exists("AndroidPluginsCachedFlag.txt")) {
            // }
                
            // In cloud builds (batch mode is true), a previous job builds the android plugins.
            // Because there was problems building android modules on a docker windows runner.
            var usePrebuiltArtifacts = PreprocessAndroidPlugins.UsePrebuiltAndroidArtifacts;
            var moduleNames = PreprocessAndroidPlugins.GetNeededModuleNames();

            string outputFolderAars = Path.Combine(outputFolder, "Android");
            if (usePrebuiltArtifacts) {
                Debug.Log($"Getting android modules: {String.Join(", ", moduleNames)}");
                AndroidPluginsArtifacts.GetAars(moduleNames, outputFolderAars);
            } else {
                Debug.Log($"Building android modules: {String.Join(", ", moduleNames)}");
                AndroidPluginsGradleBuilder.AssembleAars(moduleNames, outputFolderAars);
            }

            // generate xmls
            string outputFolderXmls = Path.Combine(outputFolder, "Editor");
            if (usePrebuiltArtifacts) {
                AndroidPluginsArtifacts.GetDependenciesXmls(outputFolderXmls);
            } else {
                AndroidPluginsGradleBuilder.GenerateDependenciesXml(outputFolderXmls, moduleNames);
            }

            Debug.Log("Finished gathering android modules.");

            // make sure unity knows the updates to xmls and aars
            // (asset database might be used by Google's dep resolver)
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            PreprocessAndroidPlugins.UpdateGradleTemplate(printGradleTemplate);
            // gradle daemon is only started if we dont use prebuilt artifacts
            return !usePrebuiltArtifacts;
        }
    }

    internal static class AndroidPluginsArtifacts {

        // Directory path is copied from Build/Build source code
        internal static string ArtifactsDirectory =
            Path.Combine(FileFinder.GetRepoRoot(), "AndroidPlugins", "build", "built_modules");

        public static void GetAars(HashSet<string> neededModules, string outputFolder) {
            foreach (var moduleName in neededModules) {
                GetAar(moduleName, outputFolder);
            }
        }

        public static void GetAar(string moduleName, string outputFolder) {
            var filename = $"{moduleName}-release.aar";
            var prebuiltAar = Path.Combine(ArtifactsDirectory, filename);
            File.Copy(prebuiltAar, Path.Combine(outputFolder, filename), true);
        }

        /// <summary>
        /// Get all of the *Dependencies.xml. Some modules dont have any external dependencies,
        /// so those modules do not have an xml file.
        /// </summary>
        /// <param name="outputFolder"></param>
        /// <param name="neededModules"></param>
        public static void GetDependenciesXmls(string outputFolder) {
            var xmlFiles = Directory.GetFiles(ArtifactsDirectory, "*.xml");
            // just make sure there is at least one
            Assert.IsTrue(xmlFiles.Length >= 1, "one or more *Dependencies.xml files should exist");
            foreach (var xmlFilepath in xmlFiles) {
                var filename = Path.GetFileName(xmlFilepath);
                File.Copy(xmlFilepath, Path.Combine(outputFolder, filename), true);
            }
        }
    }

    internal static class AndroidPluginsGradleBuilder {

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
            string pathToAndroidPlugins = Path.Combine(FileFinder.GetRepoRoot(), "AndroidPlugins");
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

    internal static class GoogleExternalResolver {

        /// <summary>
        /// Update mainTemplate.gradle with the latest dependencies declared in **/*Dependencies.xml
        /// Assumption: Assets > Play Services Resolver > Android Resolver > Settings >
        ///  'Patch mainTemplate.gradle' is enabled.
        /// </summary>
        /// <exception cref="Exception">When resolving failed for any reason</exception>
        internal static void Resolve() {
            if(!Google.VersionHandler.Enabled) return;
            // recommended by https://github.com/googlesamples/unity-jar-resolver/issues/153
            Type androidResolverClass = Google.VersionHandler.FindClass(
                "Google.JarResolver", "GooglePlayServices.PlayServicesResolver");
            bool success = (bool) Google.VersionHandler.InvokeStaticMethod(
                androidResolverClass, "ResolveSync", args: new object[] { true },
                namedArgs: null);
            if (!success) {
                throw new Exception(
                    "failed to resolve external android dependencies (Google's dep resolver returned false)");
            }
        }

    }
}
