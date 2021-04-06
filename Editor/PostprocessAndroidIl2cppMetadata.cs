#nullable enable
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEngine;

public class PostprocessAndroidIl2cppMetadata : IPostGenerateGradleAndroidProject {
    public int callbackOrder => 0;
    
    public void OnPostGenerateGradleAndroidProject(string unityLibraryPath) {
        Debug.Log("PostprocessAndroidIl2cppMetadata");
            
        try {
            // demo projects can skip it
            var skipVar = Environment.GetEnvironmentVariable(
                "SKIP_GENERATE_GRADLE_ANDROID_PROJECT"
            );
            if (skipVar == "1") {
                return;
            }
            // Move metadata file to split by abi, because in cloud pipelines we run multiple export player jobs.
            // The code that moves the metadata file to where unity looks is in AndroidPlugins/util
            string abiName;
            switch (PlayerSettings.Android.targetArchitectures) {
                case AndroidArchitecture.ARM64:
                    abiName = "arm64-v8a";
                    break;
                
                case AndroidArchitecture.ARMv7:
                    abiName = "armeabi-v7a";
                    break;
                
                default: throw new NotSupportedException("ALL architecture not supported because of buzzing");
            }
            var from = Path.Combine(unityLibraryPath, "src/main/assets/bin/Data/Managed/Metadata/global-metadata.dat");
            if (File.Exists(from)) {
                var to = Path.Combine(unityLibraryPath, $"src/main/jniLibs/{abiName}/libglobal-metadata.so");
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Move(from, to);
            }
        } catch (BuildFailedException) {
            throw;
        } catch (Exception ex) {
            throw new BuildFailedException(ex);
        }
    }
}
