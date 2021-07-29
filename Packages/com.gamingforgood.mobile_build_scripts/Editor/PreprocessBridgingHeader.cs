using System;
using System.Collections.Generic;
using System.IO;
using JetBrains.Annotations;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Callbacks;
#if UNITY_IOS
using UnityEditor.iOS.Xcode;
#endif
using UnityEngine;

// Starting with Unity 2019.3, plugins are built into UnityFramework.framework
// and frameworks cant have a bridging header.
#if !UNITY_2019_3_OR_NEWER
namespace Commons.Editor {

    /// <summary>
    /// Combines the 'Bridging-Header.h' files into one file.
    /// </summary>
    /// <remarks>
    /// Build fails when two plugin files are named the same:
    /// 105 [10:06:16 AM] Plugin 'Bridging-Header.h' is used from several locations:
    /// 106 [10:06:16 AM]  Packages/com.gamingforgood.golive/Runtime/Plugins/iOS/Bridging-Header.h would be copied to <PluginPath>/Bridging-Header.h
    /// 107 [10:06:16 AM]  Assets/Plugins/iOS/Bridging-Header.h would be copied to <PluginPath>/Bridging-Header.h
    /// 108 [10:06:16 AM] Please fix plugin settings and try again.
    /// </remarks>
    internal class PreprocessBrigingHeader : IPreprocessBuildWithReport {
        public void OnPreprocessBuild(BuildReport report) {
            if (report.summary.platform != BuildTarget.iOS) return;

            CombinedContents = CombineObjcBridgingHeaders();
        }

        /// <summary>
        /// Because unity reloads assemblies between pre and post build, the values of
        /// static fields cannot be trusted to store data.
        /// </summary>
        private static string ContentsCacheFilepath =>
            Path.Combine(Application.temporaryCachePath, "CombinedContents.h");
        public static string CombinedContents {
            get => File.Exists(ContentsCacheFilepath)
                    ? File.ReadAllText(ContentsCacheFilepath)
                    : null;
            private set => File.WriteAllText(ContentsCacheFilepath, value);
        }

        [PostProcessBuild(99999)]
        private static void AddToXcodeProject(BuildTarget target, string pathToBuildProject) {
            if (target != BuildTarget.iOS) return;

            try {
                // postbuild cleanup
                RespawnDeletedFiles();

                // save the generated bridging header to file
                var generatedOutputPath = Path.Combine(pathToBuildProject, BridgingHeaderPathInXcodeproj);
                Directory.CreateDirectory(Path.GetDirectoryName(generatedOutputPath));
                File.WriteAllText(generatedOutputPath, CombinedContents);
                Debug.Log(
                    $"wrote generated bridging header to {generatedOutputPath}");

                // add the generated file to xcode project
                #if UNITY_IOS
                var pbx = new PBXProject();
                pbx.ReadFromFile(PBXProject.GetPBXProjectPath(pathToBuildProject));

                if (!pbx.ContainsFileByProjectPath(BridgingHeaderPathInXcodeproj)) {
                    var targetGuid = pbx.GetUnityMainTargetGuid();
                    pbx.AddFileToBuild(targetGuid,
                        pbx.AddFile(BridgingHeaderPathInXcodeproj, BridgingHeaderPathInXcodeproj));
                }
                #endif
            } catch (Exception ex) {
                throw new BuildFailedException(ex);
            }
        }

        /// Combines the contents of all bridging header files and deletes
        /// the original files.
        /// <returns>Combined contents of all bridging header files</returns>
        [CanBeNull]
        private static string CombineObjcBridgingHeaders() {
            bridgingHeaders = new Dictionary<string, string>();

            var bridgingHeaderFiles = FileFinder.FindAssetsNamed(BridgingHeaderFilename);
            Debug.Log($"found {bridgingHeaderFiles.Length} bridging header files");
            if (bridgingHeaderFiles.Length == 0) return null;

            HashSet<string> uniqueLines = new HashSet<string>();
            foreach (var filepath in bridgingHeaderFiles) {
                var lines = File.ReadAllLines(filepath);
                Debug.Log($"adding {filepath}");
                uniqueLines.Add($"\n/// {filepath}");
                foreach (var line in lines) {
                    var trimmed = line.Trim();
                    if (!String.IsNullOrEmpty(trimmed)) {
                        uniqueLines.Add(trimmed);
                    }
                }
                bridgingHeaders.Add(filepath, File.ReadAllText(filepath));
                bridgingHeaders.Add(filepath + ".meta", File.ReadAllText(filepath + ".meta"));
                AssetDatabase.DeleteAsset(filepath);
            }

            return String.Join("\n", uniqueLines);
        }

        #region recreate the original bridging header files

        private static void RespawnDeletedFiles() {
            if (bridgingHeaders == null) return;

            foreach (var pair in bridgingHeaders) {
                var filepath = pair.Key;
                var contents = pair.Value;
                Debug.Log($"respawning {contents.Length} chars to: " + filepath);
                File.WriteAllText(filepath, contents);
            }

            bridgingHeaders.Clear();
        }

        /// <summary>
        /// Filepath -> file contents
        /// </summary>
        private static Dictionary<string, string> bridgingHeaders;

        #endregion

        /// package consumers (e.g. IdleGame) should name their swift->objc briding header like this
        public const string BridgingHeaderFilename = "Bridging-Header.h";

        /// <summary>
        /// Relative path to the output bridging header file inside the generated xcode project.
        /// </summary>
        public const string BridgingHeaderPathInXcodeproj = BridgingHeaderFilename;


        /// Doesnt really matter
        public int callbackOrder => 10;

        [MenuItem("Build/Advanced/Test create Bridging-Header.h")]
        private static void MenuTest() {
            var contents = CombineObjcBridgingHeaders();

            var dummyFile = Path.Combine(Application.dataPath, "..", $"test_create_{BridgingHeaderFilename}");
            File.WriteAllText(dummyFile, contents);

            RespawnDeletedFiles();
        }
    }

}
#endif
