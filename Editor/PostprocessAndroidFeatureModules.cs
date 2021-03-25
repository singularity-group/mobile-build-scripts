using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEngine;

namespace Commons.Editor {
    /// <summary>
    /// Integrates our dynamic feature modules into the exported project
    /// </summary>
    internal class PostprocessAndroidFeatureModules : IPostGenerateGradleAndroidProject {
        public int callbackOrder => 50;

        public void OnPostGenerateGradleAndroidProject(string unityLibraryPath) {
            Debug.Log("PostprocessAndroidFeatureModules");
            try {
                AddUniTranslate(unityLibraryPath);
                AddVivox(unityLibraryPath);
            } catch (BuildFailedException) {
                throw;
            } catch (Exception ex) {
                throw new BuildFailedException(ex);
            } finally {
                if (!PreprocessAndroidPlugins.UsePrebuiltAndroidArtifacts && Application.isBatchMode) {
                    // because AddUniTranslate runs AssembleAar 
                    AndroidPluginsGradleBuilder.StopGradleDaemons();
                }
            }
        }

        private static void AddUniTranslate(string unityLibraryPath) {
            // assemble aar and move it to /unityLibrary/libs/unitranslate-release.aar
            var libsFolder = Path.Combine(unityLibraryPath, "libs");
            if (PreprocessAndroidPlugins.UsePrebuiltAndroidArtifacts) {
                AndroidPluginsArtifacts.GetAar("unitranslate", libsFolder);
            } else {
                AndroidPluginsGradleBuilder.AssembleAar("unitranslate", libsFolder);
            }

            var srcModuleDirectory = Path.Combine(FileFinder.GetRepoRoot(), "MobilePlugins",
                "NativeSources", "Android", "unitranslate_dynamicfeature");
            AddDynamicFeatureModule(unityLibraryPath, srcModuleDirectory, "unitranslate_dynamicfeature");

            // com.google.mlkit:language-id caused MissingDependencyException in production (app bundle) builds
            // so we make sure it is included in the base module.
            var lines = File.ReadLines(Path.Combine(srcModuleDirectory, "build.gradle"));
            string mlkitLanguageIdDependency = String.Empty;
            foreach (var line in lines) {
                if (line.Contains("com.google.mlkit:language-id:")) {
                    mlkitLanguageIdDependency = line;
                    break;
                }
            }
            
            File.AppendAllText(Path.Combine(unityLibraryPath, "build.gradle"), $@"
dependencies {{
{mlkitLanguageIdDependency} // added by PostprocessAndroidFeatureModules.cs
}}
");
        }

        private static void AddVivox(string unityLibraryPath) {
            var libsFolder = Path.Combine(unityLibraryPath, "libs");
            var vivoxAar = Path.Combine(FileFinder.GetRepoRoot(), "MobilePlugins", "UnitySources", 
                "Packages", "VivoxSdk", "Vivox", "Plugins", "Android", "VivoxNative.aar");
            var isDemo = Application.dataPath.Contains("MobilePlugins");
            if (isDemo) {
                File.Copy(vivoxAar, Path.Combine(libsFolder, "VivoxNative.aar"), true);
                
                var unityLibraryGradle = Path.Combine(unityLibraryPath, "build.gradle");
                const string vivoxDependency = @"
dependencies {
    implementation(name: 'VivoxNative', ext:'aar') // added for this demo project
}";
                File.AppendAllText(unityLibraryGradle, vivoxDependency);
            } else {
                // IdleGame build
                Debug.Log("Will fail copy if VivoxNative.aar is accidently already there (and therefore included in the base apk)");
                File.Copy(vivoxAar, Path.Combine(libsFolder, "VivoxNative.aar"), false);
                
                var srcModuleDirectory = Path.Combine(FileFinder.GetRepoRoot(), "MobilePlugins",
                    "NativeSources", "Android", "vivox_dynamicfeature");
                AddDynamicFeatureModule(unityLibraryPath, srcModuleDirectory, "vivox_dynamicfeature");
            }
            
        }

        /// <summary>
        /// Adds a dyanmic feature module to the android project
        /// </summary>
        private static void AddDynamicFeatureModule(string unityLibraryPath, string srcModuleDirectory, string moduleName) {
            Debug.Log($"AddDynamicFeatureModule :{moduleName}");
            // copy to exported project
            var destModuleDir = new DirectoryInfo(Path.Combine(unityLibraryPath, "..", moduleName));
            if (destModuleDir.Exists) {
                destModuleDir.Delete(true);
            }
            FileUtil.CopyFileOrDirectory(srcModuleDirectory, destModuleDir.FullName);
            
            // set the abi filters of the dynamic module to avoid the build error:
            // "All modules with native libraries must support the same set of ABIs, [...]"
            var moduleGradle = Path.Combine(destModuleDir.FullName, "build.gradle");
            // abiFilters 'armeabi-v7a', 'arm64-v8a'
            var abis = GetBuildAndroidAbis();
            var gradleLines = File.ReadAllLines(moduleGradle);
            for (var i = 0; i < gradleLines.Length; i++) {
                var line = gradleLines[i];
                if (line.Contains("{{ABI_FILTERS}}")) {
                    var abiFilters =
                        $"abiFilters {String.Join(", ", abis.Select(name => $"'{name}'"))}";
                    gradleLines[i] =
                        $"{new string(' ', 12)}{abiFilters} // added by {nameof(PostprocessAndroidFeatureModules)}.cs to match :launcher abiFilters";
                    break;
                }
            }
            File.WriteAllText(moduleGradle, String.Join("\n", gradleLines));

            // add module to /settings.gradle
            var settings = Path.Combine(unityLibraryPath, "..", "settings.gradle");
            File.AppendAllText(settings, $"\ninclude ':{moduleName}'");
            
            // tell :launcher to include the feature in the app bundle
            var launcherLines = CreateDynamicFeatureLines(moduleName);
            var launcherBuildGradle =
                Path.Combine(unityLibraryPath, "..", "launcher", "build.gradle");
            File.AppendAllText(launcherBuildGradle, launcherLines);
        }

        /// <returns>List of android abis included in this unity build</returns>
        private static IEnumerable<string> GetBuildAndroidAbis() {
            var buildArchs = PlayerSettings.Android.targetArchitectures;
            var abis = new List<string>();
            var possibleArchs = Enum.GetValues(typeof(AndroidArchitecture)).Cast<AndroidArchitecture>();
            foreach (var possibleArch in possibleArchs) {
                if ((buildArchs & possibleArch) != 0) {
                    var abi = possibleArch switch {
                        AndroidArchitecture.ARM64 => "arm64-v8a",
                        AndroidArchitecture.ARMv7 => "armeabi-v7a",
                        AndroidArchitecture.All => String.Empty,
                        AndroidArchitecture.None => String.Empty,
                        _ => throw new Exception($"PlayerSettings.Android.targetArchitectures contains an unknown AndroidArchitecture value of {possibleArch}")
                    };
                    if (abi != String.Empty) {
                        abis.Add(abi);
                    }
                }
            }

            return abis;
        }

        /// <summary>
        /// Lines to add to the base module build.gradle file
        /// </summary>
        /// <param name="moduleName"></param>
        /// <returns></returns>
        private static string CreateDynamicFeatureLines(string moduleName) {
            return $@"
android {{
    dynamicFeatures += ':{moduleName}'
}}";
        }

        [MenuItem("Build/Advanced/Test Postprocess AndroidFeatureModules")]
        private static void MenuTest() {
            // local path to an android export project
            const string testExportProject = "/Users/g4g/Downloads/job_output/android/bundle_export";
            var unityLibraryPath = Path.Combine(testExportProject, "unityLibrary");
            new PostprocessAndroidFeatureModules().OnPostGenerateGradleAndroidProject(unityLibraryPath);
        }
    }
}