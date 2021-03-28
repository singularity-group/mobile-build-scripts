using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public class PreprocessAddressSanitizer : IPreprocessBuildWithReport {

    /// Adds a lot of overhead to the app when it runs.
    /// https://developer.android.com/ndk/guides/asan#building
    public static bool EnableAsanSupport = false;
    
    public void OnPreprocessBuild(BuildReport report) {
        if (report.summary.platformGroup != BuildTargetGroup.Android) {
            // only android supported so far
            return;
        }
        
        if (!EnableAsanSupport) {
            return;
        }
        
        /* The il2cpp c++ compiler is clang (when Windows PC builds for Android at least)
         https://releases.llvm.org/3.1/tools/clang/docs/AddressSanitizer.html
           Usage:
            Simply compile and link your program with -faddress-sanitizer flag.
            To get a reasonable performance add -O1 or higher.
            To get nicer stack traces in error messages add -fno-omit-frame-pointer.
            To get perfect stack traces you may need to disable inlining (just use -O1) and tail call elimination (-fno-optimize-sibling-calls). 
         */
        var flags = new[] {
            "--compiler-flags=-fsanitize=address", // essential
            "--compiler-flags=-fno-omit-frame-pointer", // To get nicer stack traces in error messages add -fno-omit-frame-pointer. 
            "--linker-flags=-fsanitize=address", // essential
        };
        var flagsLine = string.Join(" ", flags);
        PlayerSettings.SetAdditionalIl2CppArgs(flagsLine);
        Debug.Log("Using additional il2cpp flags: " + PlayerSettings.GetAdditionalIl2CppArgs());
    }

    /// let other pre build script set <see cref="EnableAsanSupport"/>
    public int callbackOrder => 9999999; 
}
