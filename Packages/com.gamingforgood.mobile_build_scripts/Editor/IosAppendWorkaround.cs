using System.IO;
using JetBrains.Annotations;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Commons.Editor {

    /// <summary>
    /// Workaround for Unity issue that broke iOS append builds.
    /// </summary>
    /// <remarks>
    /// Since Unity 2019.3 or 2019.4, iOS append builds can fail with "DirectoryNotFoundException:
    /// Could not find a part of the path '[...]/Unity-iPhone/Images.xcassets/LaunchImage.launchimage'"
    /// This class prevents that exception so the build can continue.
    /// https://issuetracker.unity3d.com/issues/ios-appending-an-ios-build-fails-due-to-missing-launchimage-path-error
    /// </remarks>
    public class IosAppendWorkaround : IPreprocessBuildWithReport {
        public int callbackOrder => 0;

        [UsedImplicitly]
        public void OnPreprocessBuild(BuildReport report) {
            if (report.summary.platform != BuildTarget.iOS) return;

            var buildPath = report.summary.outputPath;
            var catalogPath = Path.Combine(buildPath, "Unity-iPhone", "Images.xcassets");
            if (!Directory.Exists(catalogPath)) {
                // not an append build, no need to do anything
                return;
            }

            var launchimagePath = Path.Combine(catalogPath, "LaunchImage.launchimage");
            Directory.CreateDirectory(launchimagePath);
        }
    }

}