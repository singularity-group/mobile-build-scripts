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

namespace Commons.Editor {
    internal class PreprocessAndroidPlugins : IPreprocessBuildWithReport {
        public int callbackOrder => 1;

        /// In cloud builds (batch mode is true), a previous job builds the android plugins.
        public static bool UsePrebuiltAndroidArtifacts => Application.isBatchMode;

        public void OnPreprocessBuild(BuildReport report) {
            if (report.summary.platformGroup != BuildTargetGroup.Android) {
                return;
            }
            try {
                PreprocessGetAndroidPlugins.Run();
                if (UsePrebuiltAndroidArtifacts) {
                    PreprocessCheckAndroidPlugins.Run();
                }
            } catch (BuildFailedException) {
                throw;
            } catch (Exception ex) {
                // make the build fail, Unity ignores other exceptions during the build process
                throw new BuildFailedException(ex);
            }
        }

        internal const string declarationFilename = "AndroidPluginsNeeded.yml";

        /// Ensure gradle dependencies are same as declared by the xml files
        internal static void UpdateGradleTemplate(bool printGradleTemplate = false) {
            // update gradle dependencies as declared by the xml files
            GoogleExternalResolver.Resolve();
            Debug.Log("Finished resolving gradle dependencies.");
            
            // modify gradle dependencies to fix build issues
            var mainTemplateFilepath = Path.Combine(Application.dataPath, "Plugins", "Android",
                "mainTemplate.gradle");
            var gradleTemplateContents = File.ReadAllText(mainTemplateFilepath);
            gradleTemplateContents = ExcludeDuplicatedPackage(gradleTemplateContents);
            if (printGradleTemplate) {
                Debug.Log($"Contents of {mainTemplateFilepath} (for debugging purposes):\n{gradleTemplateContents}");
            }
            File.WriteAllText(mainTemplateFilepath, gradleTemplateContents);
            // make sure the updated mainTemplate.gradle is used
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        /// UnityChannel.aar which is part of Unity IAP plugin,
        /// has a common barcode scanner package ('com.google.zxing:core') packaged inside the aar. 
        /// When another plugin also uses this scanner package, android build fails due to
        /// duplicate classes.
        /// <returns>Updated contents of mainTemplate.gradle</returns>
        private static string ExcludeDuplicatedPackage(string mainTemplateGradleContents) {
            var facebookSdk = new Regex(@"implementation (['""]com.facebook.android:facebook-android-sdk:[^'""]+['""])");
            var facebookDependency = facebookSdk.Match(mainTemplateGradleContents);
            if (!facebookDependency.Success) {
                Debug.Log($"Facebook sdk gradle dependency not found, {nameof(ExcludeDuplicatedPackage)} did nothing.");
                return mainTemplateGradleContents;
            }
            
            var versionString = facebookDependency.Groups[1].Value;
            string currentFile = new System.Diagnostics.StackTrace(true).GetFrame(0).GetFileName();
            string withDuplicateExcluded = $@"
implementation({versionString}) {{
        // exclusion added by {nameof(ExcludeDuplicatedPackage)} in {currentFile}
        exclude group: 'com.google.zxing', module: 'core'
    }}
".Trim('\n');
            Debug.Log($"found: {versionString}\nreplacing with: {withDuplicateExcluded}");
            var modified = mainTemplateGradleContents.Replace(facebookDependency.Value,
                withDuplicateExcluded);
            return modified;
        }

        internal static HashSet<string> GetNeededModuleNames() {
            var declarationFiles = FileFinder.FindAssetsNamed(declarationFilename);

            CheckAtleast(1, declarationFiles, $"{declarationFilename} files");

            var moduleNames = new HashSet<string>();
            foreach (var file in declarationFiles) {
                Debug.Log($"reading declared modules from {file}");
                var declaredModules = ReadDeclarationFile(file);
                foreach (var module in declaredModules) {
                    moduleNames.Add(module);
                }
            }

            return moduleNames;
        }
        
        /// <summary>
        /// Read a file that declares some needed modules.
        /// </summary>
        /// <param name="path">
        /// Path to file.
        /// File must follow this spec.
        /// </param>
        /// <example>
        /// modules:
        ///   - commons
        ///   - golive
        /// </example>
        /// <returns>Names of needed modules</returns>
        internal static string[] ReadDeclarationFile(string path) {
            var mapping = ReadYamlFile(path);
            var modulesEntry = mapping.Children.First(entry => ((YamlScalarNode)entry.Key).Value == "modules");
            var modulesArray = modulesEntry.Value as YamlSequenceNode;
            Assert.IsTrue(modulesArray != null, "'modules' is a yaml sequence");

            return modulesArray.Children
                .Select(child => {
                    Assert.AreEqual(child.NodeType, YamlNodeType.Scalar, "child of 'modules' must be string");
                    return ((YamlScalarNode)child).Value;
                })
                .ToArray();
        }

        private static YamlMappingNode ReadYamlFile(string path) {
            var contents = File.ReadAllText(path);
            using var input = new StringReader(contents);
            var yaml = new YamlStream();
            yaml.Load(input);
            var mapping = (YamlMappingNode)yaml.Documents[0].RootNode;
            return mapping;
        }

        /// Sanity checks.
        private static void CheckAtleast(int atleast, IEnumerable<object> items, string itemsName) {
            int actualCount = items.Count();

            if (actualCount < atleast) {
                Debug.LogWarning($"found {actualCount} {itemsName}! thats strange, expected at least {atleast}.");
            }
        }
    }
}
