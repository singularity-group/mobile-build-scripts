using System.IO;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEngine;

public class PostprocessAddressSanitizer : IPostGenerateGradleAndroidProject {
    public int callbackOrder => 1;

    private string AddressSanitizerDir => 
        Path.Combine(Path.Combine(Path.GetFullPath("Packages/com.gamingforgood.mobile_build_scripts"),
        "Editor", "AddressSanitizer"));

    // note: assumes android:extractNativeLibs="true" is set in the app manifest, see https://developer.android.com/ndk/guides/asan#running
    public void OnPostGenerateGradleAndroidProject(string unityLibraryPath) {
        if (!PreprocessAddressSanitizer.EnableAsanSupport) {
            return;
        }
        
        var launcherDir = Path.GetFullPath(Path.Combine(unityLibraryPath, "..", "launcher"));
        // \launcher\src\main\resources\lib\armeabi-v7a\wrap.sh
        var wrapScriptOutput = Path.Combine(launcherDir, "src", "main", "resources", "lib", "armeabi-v7a", "wrap.sh");
        
        // create wrap.sh
        var repoWrapScript = Path.Combine(AddressSanitizerDir, "Android", "wrap.sh");
        Directory.CreateDirectory(Path.GetDirectoryName(wrapScriptOutput)!);
        File.Copy(repoWrapScript, wrapScriptOutput, true);
        
        // copy Asan libs
        if (Application.unityVersion != "2019.4.16f1") {
            // note for later: instead of include in repo, copy from Android NDK included with Unity installation
            throw new BuildFailedException("Unity version has updated. The Asan libs that would be copied come from android NDK that is packaged" +
                                           "with Unity 2019.4.16f1. If Unity has updated their NDK version, then the jniLibs/ need to be updated.");
        }

        var repoAsanJniLibs = Path.Combine(AddressSanitizerDir, "Android", "jniLibs");
        var outputJniLibs = Path.Combine(launcherDir, "src", "main", "jniLibs");
        DirectoryUtil.DirectoryCopy(repoAsanJniLibs, outputJniLibs);
    }
}
