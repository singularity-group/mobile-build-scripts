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
    /// Some packages have proguard rules that we must include.
    /// It doesn't happen automatically, so this build script does it for those packages.
    /// </summary>
    /// <remarks>
    /// This github issue tracks the problem:
    /// https://github.com/google/play-unity-plugins/issues/37
    /// </remarks>
    internal class PostprocessAndroidProguardFiles : IPostGenerateGradleAndroidProject {
        public int callbackOrder => 100;

        public void OnPostGenerateGradleAndroidProject(string unityLibraryPath) {
            Debug.Log("PostprocessAndroidMinifyFiles");
            try {
                var unityLibraryGradleFile = Path.Combine(unityLibraryPath, "build.gradle");
                
                var proguardFiles = new Dictionary<string, string> {
                    { "Packages/com.google.play.core/Proguard/common.txt", "proguard-com.google.play.core.txt"},
                    { "Packages/com.google.play.review/Proguard/review.txt", "proguard-com.google.play.review.txt"},
                };
                
                foreach (var pair in proguardFiles) {
                    var proguardFile = Path.GetFullPath(pair.Key);
                    if (File.Exists(proguardFile)) {
                        Debug.Log($"Copying {pair.Key} to the gradle project proguard files");
                    } else {
                        continue;
                    }
                    var outputFile = Path.Combine(unityLibraryPath, pair.Value);
                    File.Copy(proguardFile, outputFile, true);
                    File.AppendAllText(unityLibraryGradleFile, $@"
// Added by {nameof(PostprocessAndroidProguardFiles)}.cs
android {{
    defaultConfig {{
        consumerProguardFiles += '{pair.Value}'
    }}
}}");
                }
            } catch (BuildFailedException) {
                throw;
            } catch (Exception ex) {
                throw new BuildFailedException(ex);
            }
        }

        [MenuItem("Build/Advanced/Test Postprocess AndroidProguardFiles")]
        private static void MenuTest() {
            // local path to an android export project
            const string testExportProject = "/Users/g4g/Downloads/job_output/android/IdleGame/Bin/Android/ETC2/ARMv7";
            var unityLibraryPath = Path.Combine(testExportProject, "unityLibrary");
            new PostprocessAndroidProguardFiles().OnPostGenerateGradleAndroidProject(unityLibraryPath);
        }
    }
}