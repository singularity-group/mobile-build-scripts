#if UNITY_IOS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JetBrains.Annotations;
using UnityEditor;
using System.Text.RegularExpressions;
using UnityEditor.Build;
using UnityEngine;
// this namespace requires ios build support to be installed
using UnityEditor.iOS.Xcode;

namespace Commons.Editor {

    /// <summary>
    /// Implemented here so that <see cref="PostprocessUnityIOS"/> can be tested inside this project.
    /// </summary>
    public class CommonsPostprocessor : IPostProcessIOS {
        public PBXProject EditXcodeProject(PBXProject pbx, string pathToBuiltProject) {
            pbx = ApplyGoogleResolverPatches(pbx, pbx.GetUnityFrameworkTargetGuid());
            pbx = ApplyGoogleResolverPatches(pbx, pbx.GetUnityMainTargetGuid());
            pbx = AlternativeIconsPostProcessor.EditXcodeProject(pbx, pbx.GetUnityMainTargetGuid(),
                pathToBuiltProject);
            return AddSwiftCodeEssentials(pbx, pbx.GetUnityFrameworkTargetGuid());
        }

        /// <summary>
        /// Apply patches by the 'unity-jar-resolver' project.
        /// </summary>
        /// <remarks>
        /// We were using unity-jar-resolver previously, which does these patches.
        /// Since we don't use unity-jar-resolver for iOS, I copied their source code:
        /// https://github.com/googlesamples/unity-jar-resolver/blob/7970d6900fb33a112d638b4ff1b9be9e59aadf6a/source/IOSResolver/src/IOSResolver.cs#L1708-L1711
        /// Not sure what each one does but its probably useful stuff.
        /// </remarks>
        private static PBXProject ApplyGoogleResolverPatches(PBXProject project, string targetGuid) {
            project.SetBuildProperty(targetGuid, "CLANG_ENABLE_MODULES", "YES");
            project.AddBuildProperty(targetGuid, "OTHER_LDFLAGS", "-ObjC");
            // GTMSessionFetcher requires Obj-C exceptions.
            project.SetBuildProperty(targetGuid, "GCC_ENABLE_OBJC_EXCEPTIONS", "YES");
            return project;
        }

        private static PBXProject AddSwiftCodeEssentials(PBXProject pbx, string targetGuid) {
            // all swift code shall use this version
            pbx.SetBuildProperty(targetGuid, "SWIFT_VERSION", "5.0");


            // Starting with Unity 2019.3, plugins are built into UnityFramework.framework
            // and frameworks cant have a bridging header.
            #if !UNITY_2019_3_OR_NEWER
            if (PreprocessBrigingHeader.CombinedContents != null) {
                string projectPath = PreprocessBrigingHeader.BridgingHeaderPathInXcodeproj;
                pbx.AddFile(projectPath, projectPath);
                pbx.SetBuildProperty(targetGuid, "SWIFT_OBJC_BRIDGING_HEADER",
                    "$(SRCROOT)/" + projectPath);
            } else {
                Debug.LogWarning("Swift code may not compile, there are" +
                                 $" zero '{PreprocessBrigingHeader.BridgingHeaderFilename}' files" +
                                 " in this project.");
            }
            #endif

            // value is hardcoded in objc files (.m, .mm) that call swift code
            pbx.SetBuildProperty(targetGuid, "SWIFT_OBJC_INTERFACE_HEADER_NAME",
                "SwiftInObjcBridge.h");
            // fixes a runtime crash at startup (might not be needed anymore?)
            pbx.AddBuildProperty(targetGuid, "LD_RUNPATH_SEARCH_PATHS",
                "@executable_path/Frameworks");
            return pbx;
        }

        public void EditInfoDoc(PlistDocument infoDoc) {
            AlternativeIconsPostProcessor.AddIconsToInfoDoc(infoDoc);
            
            // Some other sdks and plugins edit the NSAppTransportSecurity dictionary.
            // But for insecure http requests from C# to be allowed,
            // NSAppTransportSecurity dict must have no other entries, so we replace whatever is there.
            infoDoc.root.values["NSAppTransportSecurity"] = new PlistElementDict {
                ["NSAllowsArbitraryLoads"] = new PlistElementBoolean(true),
            };
            
            // Schemes of iOS apps whose installation status can be checked by Commons.AppDetector.IsAppInstalled.
            var schemes = new PlistElementArray();
            if (infoDoc.root.values.TryGetValue("LSApplicationQueriesSchemes", out var existingSchemes)) {
                schemes = (PlistElementArray) existingSchemes;
            }
            schemes.AddString("fb"); // Allow checking whether Facebook is installed.
            schemes.AddString("twitter"); // Allow checking whether Twitter is installed.
            schemes.AddString("paypal"); // Allow checking whether Paypal app is installed.
            infoDoc.root.values["LSApplicationQueriesSchemes"] = schemes;
        }

        public void EditGoogleServicePlist(PlistDocument googlePlist) { }

        public void LastMethod(string pathToBuiltProject) {
            var podfilePath = Path.Combine(pathToBuiltProject, "Podfile");
            IosDependencyResolver.CreatePodfile(podfilePath);
            UnityWebRequestPatch.ApplyPatch(pathToBuiltProject);
            UnityKeyboardPatch.ApplyPatch(pathToBuiltProject);
            UnityDisplayManagerPatch.ApplyPatch(pathToBuiltProject);
        }

        // first
        public int callbackOrder => -1;
    }

    public static class AlternativeIconsPostProcessor {
        private const string CFBundleIcons = "CFBundleIcons";
        private const string CFBundleAlternateIcons = "CFBundleAlternateIcons";
        private const string CFBundleIconFiles = "CFBundleIconFiles";
        private const string UIPrerenderedIcon = "UIPrerenderedIcon";

        /// <summary>
        /// Put all source pngs in this folder before iOS build reaches the post-processing step.
        /// </summary>
        /// <remarks>
        /// @2x.png files are 120x120 pixels in size
        /// @3x.png files are 180x180 pixels in size
        /// 
        /// Example folder structure:
        ///  Assets/Plugins/iOS/AltIcons/
        ///   ├─ sodapoppin/
        ///   │  ├─ sodapoppin@2x.png
        ///   │  └─ sodapoppin@3x.png
        ///   └─ AtheneLive/
        ///      ├─ AtheneLive@2x.png
        ///      └─ AtheneLive@3x.png
        /// </remarks>
        public static string AlternativeIconsFolder => Path.Combine(Application.dataPath, "Plugins",
            "iOS",
            "cvs", // unity should not make textures out of the png files
            "AltIcons");

        // icon names without the @2x.png suffix
        private static readonly HashSet<string> altIcons = new HashSet<string>();
        
        /// <summary>
        /// 
        /// </summary>
        /// <param name="pbx">Null when testing</param>
        /// <param name="unityIphoneTargetGuid"></param>
        /// <param name="pathToBuiltProject"></param>
        /// <returns></returns>
        public static PBXProject EditXcodeProject([CanBeNull] PBXProject pbx,
            string unityIphoneTargetGuid,
            string pathToBuiltProject) {
            altIcons.Clear();
            // list of all files in source folder
            if (!Directory.Exists(AlternativeIconsFolder)) {
                // nothing to be done
                Debug.Log("No alt icons directory found, skipping this step.");
                return pbx;
            }
            
            var iconFiles = Directory.GetFiles(AlternativeIconsFolder, "*.png", SearchOption.AllDirectories);
            Debug.Log($"These {iconFiles.Length} files will be used as iOS alternative app icons:" +
                $"\n{String.Join("\n  ", iconFiles)}");
            
            foreach (var iconFile in iconFiles) {
                var filename = Path.GetFileName(iconFile);
                // "{iconName}@2x.png"
                var iconName = Regex.Match(filename, "[^@]+").Value;
                altIcons.Add(iconName);
                // copy file and add to pbx with path AltIcons/{iconName}/{iconName}@2x.png
                var projectRelativePath = Path.Combine("AltIcons", iconName, filename);
                var projectPath = Path.Combine(pathToBuiltProject, projectRelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(projectPath));
                File.Copy(iconFile, projectPath, true);
                pbx?.AddFileToBuild(unityIphoneTargetGuid, pbx.AddFile(projectRelativePath, projectRelativePath));
            }
            return pbx;
        }

        public static void AddIconsToInfoDoc(PlistDocument infoDoc) {
            var altIconsDict = new PlistElementDict();
            foreach (var iconName in altIcons) {
                var namedFile = new PlistElementArray();
                namedFile.AddString(iconName);
                // the dict key here is used when changing to this icon
                altIconsDict.values[iconName] = new PlistElementDict {
                    [CFBundleIconFiles] = namedFile,
                    [UIPrerenderedIcon] = new PlistElementBoolean(false)
                };
            }
            infoDoc.root.values[CFBundleIcons] = new PlistElementDict {
                [CFBundleAlternateIcons] = altIconsDict
            };
        }

        private static void GetListOfAltIcons() {
        }

        [MenuItem("Build/Advanced/Test iOS alt icons")]
        private static void MenuTest() {
            var dummyProjectPath =
                Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Bin",
                    "DummyXcodeProject"));
            Directory.CreateDirectory(dummyProjectPath);
            EditXcodeProject(null, "guid", dummyProjectPath);
        }
    }

    /// <summary>
    /// Sometimes the whole app was freezing (deadlock) after playing the game for 30 minutes or so.
    /// This patch fixes the deadlock.
    /// </summary>
    /// <remarks>
    /// On iOS, UnityWebRequests were consuming an excessive number of threads (20-30 threads),
    /// which exhausts the Grand Central Dispatch thread pool (max 64 threads) and that leads to
    /// app freeze (deadlock).
    /// This patches the native UnityWebRequest implementation to force a limited number of threads.
    /// </remarks>
    internal static class UnityWebRequestPatch {
        
        // https://developer.apple.com/documentation/foundation/operationqueue/1414982-maxconcurrentoperationcount
        private const string codeToBeAdded = @"
// G4G: method added by UnityWebRequestPatch (in CommonsPostprocessor.cs)
extern ""C"" bool COS_UWRSetMaxConcurrentOperations(int32_t maxConcurrentOps) {
    if (webOperationQueue == NULL) {
        return false;
    }
    // C# code passes -1 to indicate the default value
    if (maxConcurrentOps == -1) {
        webOperationQueue.maxConcurrentOperationCount = NSOperationQueueDefaultMaxConcurrentOperationCount;
    } else {
        webOperationQueue.maxConcurrentOperationCount = (NSInteger)maxConcurrentOps;
    }
    return true;
}";

        private const string initLine = "webOperationQueue = [[NSOperationQueue alloc] init];";

        /// https://forum.unity.com/threads/unitywebrequests-on-ios-sometimes-get-stuck-indefinitely-even-with-timeout-set.1012276/page-3#post-6723370
        /// Using NSQualityOfServiceUserInitiated because we dont want web requests to cause game lag.
        private const string stuckFix = "webOperationQueue.qualityOfService = NSQualityOfServiceUserInitiated;"
            + " // G4G: line added by UnityWebRequestPatch (in CommonsPostprocessor.cs)";

        /// Modifies Unity ObjC source code file
        public static void ApplyPatch(string pathToBuiltProject) {
            var sourceFile = Path.Combine(pathToBuiltProject, "Classes", "Unity", "UnityWebRequest.mm");
            var lines = File.ReadAllLines(sourceFile).ToList();
            var codeToBeAddedFirstLine = codeToBeAdded.Split('\n')[1];
            var matchLineIndex = lines.FindIndex(line => line == codeToBeAddedFirstLine);

            if (matchLineIndex >= 0) {
                // already patched, this is probably an append build
                Debug.Log("[UnityWebRequestPatch] xcode project is already patched.");
            } else {
                Debug.Log("[UnityWebRequestPatch] ApplyPatch cus we didnt find\n" +
                          codeToBeAddedFirstLine);
                lines.Add(codeToBeAdded);
                
                var initMatchLineIndex = lines.FindIndex(line => line.Trim() == initLine);
                if (initMatchLineIndex >= 0) {
                    lines.Insert(initMatchLineIndex + 1, stuckFix);
                } else {
                    throw new BuildFailedException("did not find the line where UnityWebRequest patch should be added");
                }
                File.WriteAllLines(sourceFile, lines);
            }
        }

        [MenuItem("Build/Advanced/Test iOS UnityWebRequestPatch")]
        private static void MenuTest() {
            ApplyPatch("/Users/g4g/Downloads/job_output/ios/old/IdleGame/Bin/iOS");
        }
    }
    
    /// <summary>
    /// iOS dark mode breaks Unity's native multiline text input view.
    /// </summary>
    /// <remarks>
    /// On iOS 14.2 (may also affect other iOS versions), with dark mode enabled, the textView is rendering transparent when
    /// it is empty (textView.text == "").
    /// Setting non-default background color fixes that.
    /// </remarks>
    internal static class UnityKeyboardPatch {
        private const string textViewInitLine =
            "textView = [[UITextView alloc] initWithFrame: CGRectMake(0, 840, 480, 30)];";
        
        private static readonly string codeToBeAdded = @"
        // G4G: if block added by UnityKeyboardPatch (in CommonsPostprocessor.cs)
        if (@available(iOS 13.0, *)) {
            // On iOS 14.2 (may also affect other iOS versions), with dark mode enabled, the textView is rendering transparent when
            // it is empty.
            // Setting non-default background color fixes that.
            textView.backgroundColor = [UIColor secondarySystemBackgroundColor];
        }".TrimStart();

        /// Modifies Unity ObjC source code file
        public static void ApplyPatch(string pathToBuiltProject) {
            var sourceFile = Path.Combine(pathToBuiltProject, "Classes", "UI", "Keyboard.mm");
            var lines = File.ReadAllLines(sourceFile).ToList();
            var matchLineIndex = lines.FindIndex(line => line.Trim() == textViewInitLine);
            if (matchLineIndex < 0) {
                throw new Exception("failed to patch Keyboard.mm, has the Unity source code updated?");
            }

            if (codeToBeAdded.StartsWith(lines[matchLineIndex + 1])) {
                // already patched, this is probably an append build
                Debug.Log("[UnityKeyboardPatch] xcode project is already patched.");
            } else {
                Debug.Log("[UnityKeyboardPatch] ApplyPatch");
                lines.Insert(matchLineIndex + 1, codeToBeAdded);
                File.WriteAllLines(sourceFile, lines);
            }
        }

        [MenuItem("Build/Advanced/Test iOS UnityKeyboardPatch")]
        private static void MenuTest() {
            ApplyPatch("/Users/g4g/Downloads/job_output/ios/old/IdleGame/Bin/iOS");
        }
    }

    /// <summary>
    /// Since updating to Unity 2019.4.16f1, we saw a crash during launch.
    /// This patch works around that crash.
    /// </summary>
    /// <remarks>
    /// https://trello.com/c/9Oo5iOYU/2255-ios-immediately-crashes-on-startup
    /// This patch checks unity version because we reported a bug to them, so we're expecting the
    /// patched file code to change when the fix the bug.
    /// Patch should be reviewed when we update to next Unity version.
    /// </remarks>
    internal static class UnityDisplayManagerPatch {
        // we add our patch after this line
        private const string lineBefore = "surf->drawableCommandQueue = [surf->device newCommandQueueWithMaxCommandBufferCount: UnityCommandQueueMaxCommandBufferCountMTL()];";

        private static readonly string lineToBeAdded = new string(' ', 8) + "_surface = surf;"
                                                       + " // G4G: line added by UnityDisplayManagerPatch (in CommonsPostprocessor.cs)";

        /// Modifies Unity ObjC source code file
        public static void ApplyPatch(string pathToBuiltProject) {
            if (Application.unityVersion != "2019.4.16f1") {
                throw new BuildFailedException("New Unity version - G4G code patch may cause bugs now, Troy should check if we still need the patch");
            }
            var sourceFile =
                Path.Combine(pathToBuiltProject, "Classes", "Unity", "DisplayManager.mm");
            var lines = File.ReadAllLines(sourceFile).ToList();
            var matchLineIndex = lines.FindIndex(line => line.Trim() == lineBefore);
            if (matchLineIndex < 0) {
                throw new Exception(
                    "failed to patch DisplayManager.mm, has the Unity source code updated?");
            }

            if (lines[matchLineIndex + 1] == lineToBeAdded) {
                // already patched, this is probably an append build
                Debug.Log("[UnityDisplayManagerPatch] xcode project is already patched.");
            } else {
                Debug.Log("[UnityDisplayManagerPatch] ApplyPatch");
                lines.Insert(matchLineIndex + 1, lineToBeAdded);
                File.WriteAllLines(sourceFile, lines);
            }
        }

        [MenuItem("Build/Advanced/Test iOS UnityDisplayManagerPatch")]
        private static void MenuTest() {
            ApplyPatch("/Users/g4g/Downloads/job_output/ios/old/IdleGame/Bin/iOS");
        }
    }
}
#endif
