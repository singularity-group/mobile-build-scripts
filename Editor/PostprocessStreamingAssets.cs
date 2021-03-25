using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Callbacks;
using UnityEngine;

namespace Commons.Editor {
    
    /// <summary>
    /// Unity does not support StreamingAssets folders inside unity Packages.
    /// This class adds support for them by copying all files into the exported project.
    /// </summary>
    internal class PostprocessStreamingAssets : IPostGenerateGradleAndroidProject {
        
        /// ios only
        [PostProcessBuild]
        private static void CopyStreamingAssetsIos(BuildTarget target, string pathToBuildProject) {
            if (target != BuildTarget.iOS) return;
            
            var outputStreamingAssetsPath = Path.Combine(pathToBuildProject, "Data", "Raw");
            // streaming asset files only required by ios
            CopyAllDirectoriesToOutputPath("StreamingAssets", outputStreamingAssetsPath);
            CopyAllDirectoriesToOutputPath("StreamingAssetsIOS", outputStreamingAssetsPath);
        }

        public int callbackOrder => 100;

        /// Android only
        public void OnPostGenerateGradleAndroidProject(string path) {
            var outputStreamingAssetsPath = Path.Combine(path,
                "src", "main", "assets");
            CopyAllDirectoriesToOutputPath("StreamingAssets", outputStreamingAssetsPath);
            CopyAllDirectoriesToOutputPath("StreamingAssetsAndroid", outputStreamingAssetsPath);
        }

        /// <summary>
        /// Copy contents of all folders with special name into the output folder.
        /// Folder heirarchy is preserved.
        /// </summary>
        /// <param name="foldersNamed"></param>
        /// <param name="outputStreamingAssetsPath"></param>
        private static void CopyAllDirectoriesToOutputPath(string foldersNamed, string
            outputStreamingAssetsPath) {
            var foldersInPackages = FileFinder.FindPackageDirectories(foldersNamed)
                .ToArray();

            Debug.Log($"Count of {foldersNamed} folders in Packages/ is {foldersInPackages.Length}");

            // destination
            Directory.CreateDirectory(outputStreamingAssetsPath);

            foreach (var dir in foldersInPackages) {
                Debug.Log("streamingAssetsInPackages " + dir);
                // copy each file to the xcode project
                var files = Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories);
                foreach (var file in files) {
                    // meta files are not wanted
                    if (file.EndsWith(".meta")) continue;

                    // support nested folders:
                    // get path as "Folder/filename.ext" inside StreamingAssets/
                    var filenameWithFolders = file.Substring(dir.Length + 1);
                    var dest = Path.Combine(outputStreamingAssetsPath, filenameWithFolders);
                    // make sure directory exists
                    var destDir = Path.GetDirectoryName(dest);
                    Directory.CreateDirectory(destDir);

                    File.Copy(file, dest, true);
                }
            }
        }

        [MenuItem("Build/Advanced/Test StreamingAssets fix")]
        private static void EditorMenu() {
            var dummmyPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Bin", "TestStreamingAssets"));
            // ios
            CopyStreamingAssetsIos(BuildTarget.iOS, dummmyPath);
            // android
            new PostprocessStreamingAssets().OnPostGenerateGradleAndroidProject(dummmyPath);
        }
    }
}