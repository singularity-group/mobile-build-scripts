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

            if (moduleNames.Count == 0) {
                return false; // nothing to be done
            }

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

    public static class AndroidPluginsArtifacts {

        // Directory path is copied from Build/Build source code
        internal static string ArtifactsDirectory =
            Path.Combine(FileFinder.GetRepoRoot(), "MobilePlugins", "build", "built_modules");

        public static void GetAars(HashSet<string> neededModules, string outputFolder) {
            var aarFiles = Directory.GetFiles(ArtifactsDirectory, "*.aar");
            foreach (var aarFile in aarFiles) {
                foreach (var moduleName in neededModules) {
                    if (aarFile.Contains(moduleName)) {
                        // expected
                    } else {
                        Debug.Log($"Copying aar {aarFile} (that is not explicitly depended on)");
                    }
                }

                var filename = Path.GetFileName(aarFile);
                File.Copy(aarFile, Path.Combine(outputFolder, filename), true);
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

    public static class GoogleExternalResolver {

        /// <summary>
        /// Update mainTemplate.gradle with the latest dependencies declared in **/*Dependencies.xml
        /// Assumption: Assets > Play Services Resolver > Android Resolver > Settings >
        ///  'Patch mainTemplate.gradle' is enabled.
        /// </summary>
        /// <exception cref="Exception">When resolving failed for any reason</exception>
        public static void Resolve() {
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
