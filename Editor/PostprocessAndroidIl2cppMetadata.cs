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
            var metadataFile = Path.Combine(unityLibraryPath, "src/main/assets/bin/Data/Managed/Metadata/global-metadata.dat");
            // for mono builds, the file does not exist
            if (!File.Exists(metadataFile)) {
                return;
            }
            
            // Move metadata file to split by abi, because in cloud pipelines we run multiple export player jobs.
            // The code that moves the metadata file to where unity looks is in AndroidPlugins/util
            string abiName;
            switch (PlayerSettings.Android.targetArchitectures) {
                case AndroidArchitecture.ARMv7:
                    abiName = "armeabi-v7a";

                    var metadataAsLib = Path.Combine(unityLibraryPath, $"src/main/jniLibs/{abiName}/libglobal-metadata.so");
                    Directory.CreateDirectory(Path.GetDirectoryName(metadataAsLib)!);
                    File.Copy(metadataFile, metadataAsLib);
                    break;
                
                case AndroidArchitecture.ARM64:
                    abiName = "arm64-v8a";
                    
                    // when bundling both archs in gradle project there must be a 64bit lib for every 32bit lib
                    var metadataDummyLib = Path.Combine(unityLibraryPath, $"src/main/jniLibs/{abiName}/libglobal-metadata.so");
                    Directory.CreateDirectory(Path.GetDirectoryName(metadataDummyLib)!);
                    // create dummy file (might have to be > 0 bytes)
                    File.WriteAllBytes(metadataDummyLib, new byte[]{ 0x00, 0x00, 0x00, 0x00 });
                    break;
                
                default: throw new NotSupportedException("ALL architecture not supported because of buzzing");
            }
        } catch (BuildFailedException) {
            throw;
        } catch (Exception ex) {
            throw new BuildFailedException(ex);
        }
    }
}
