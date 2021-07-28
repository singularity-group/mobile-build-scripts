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
    /// Check that all required AndroidPlugins modules have been built
    /// </summary>
    internal static class PreprocessCheckAndroidPlugins {

        private static string outputFolder => Path.Combine(Application.dataPath, "Plugins", "MobilePlugins");

        [MenuItem("Build/Advanced/Test AndroidPlugins checks")]
        public static void Run() {
            Debug.Log(nameof(PreprocessCheckAndroidPlugins));
            EnsureAndroidDependencies();
        }

        /// Windows docker container has difficult to fix issues when running gradle build tasks.
        /// As a precaution, this makes sure linux gradle job built everything we need.
        private static void EnsureAndroidDependencies() {
            var builtPluginFiles = Directory.GetFiles(outputFolder, "*", SearchOption.AllDirectories);
            Debug.Log($"Contents of '{outputFolder}/':\n{String.Join("\n", builtPluginFiles)}");
            
            // check that correct declaration files were used and all modules were built
            var correctDeclarationFiles = FileFinder.FindAssetsNamed(PreprocessAndroidPlugins.declarationFilename);
            Debug.Log($"Correct list of {PreprocessAndroidPlugins.declarationFilename} files:\n{String.Join("\n", correctDeclarationFiles)}");
            
            var builtInfoFilepath = Path.Combine(AndroidPluginsArtifacts.ArtifactsDirectory, "BuiltPluginsInfo.yml");
            Debug.Log($"Contents of {builtInfoFilepath}:\n" + File.ReadAllText(builtInfoFilepath));
            
            var actualBuiltModules = PreprocessAndroidPlugins.ReadDeclarationFile(builtInfoFilepath);
            var correctModuleNames = PreprocessAndroidPlugins.GetNeededModuleNames();
            Debug.Log($"actualBuiltModules = {String.Join(", ", actualBuiltModules)}");
            Debug.Log($"correctModuleNames = {String.Join(", ", correctModuleNames)}");
            
            const string errorMessage = "actualBuiltModules is not same as correctModuleNames! see logs above, you will need to change something";
            if (actualBuiltModules.Length != correctModuleNames.Count) {
                throw new BuildFailedException(errorMessage);
            }
            
            var found = 0;
            foreach (var correctModuleName in correctModuleNames) {
                if (actualBuiltModules.Contains(correctModuleName)) {
                    found++;
                } else {
                    throw new BuildFailedException(errorMessage);
                }
            }
            if (found != correctModuleNames.Count) {
                throw new BuildFailedException(errorMessage);
            }
        }
    }
}
