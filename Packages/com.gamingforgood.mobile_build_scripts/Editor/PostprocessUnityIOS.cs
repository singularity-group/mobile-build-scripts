using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Commons.Editor {

    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Debug = UnityEngine.Debug;
    using UnityEditor;
    #if UNITY_IOS
    using System.IO;
    using UnityEngine;
    using UnityEditor.iOS.Xcode;
    #endif

    public class PostprocessUnityIOS: IPostprocessBuildWithReport {
        /// <summary>
        /// Give others a change to call <see cref="RegisterPostProcessor"/>.
        /// </summary>
        public int callbackOrder => 10000;

        public static void RegisterPostProcessor(IPostProcessIOS processIos) {
            postProcessors[processIos.GetType()] = processIos;
        }

        private static readonly Dictionary<Type, IPostProcessIOS> postProcessors
            = new Dictionary<Type, IPostProcessIOS>();

        public void OnPostprocessBuild(BuildReport report) {
            if (report.summary.platformGroup != BuildTargetGroup.iOS) return;
        #if UNITY_IOS
            RegisterPostProcessor(new PostprocessForSwift());
        #endif

            Debug.Log("⏩ PostprocessUnityIOS begins...");

            try {
                PostBuild.Run(report.summary.platform, report.summary.outputPath,
                    postProcessors.Values);
            } catch (Exception exc) {
                // make sure build fails due to the exception
                throw new BuildFailedException(exc);
            }

            Debug.Log("✅ PostprocessUnityIOS done.");
        }
    }

    public interface IPostProcessIOS : IOrderedCallback {
        #if UNITY_IOS

        /// You can edit the xcodeproj like this:
        /// pbx.SetBuildProperty(unityIphoneTargetGuid, "GCC_OPTIMIZATION_LEVEL", "1");
        ///
        /// Implementors must do all edits before returning the <see cref="pbx"/> object.
        PBXProject EditXcodeProject(PBXProject pbx, string pathToBuiltProject);

        /// <summary>
        /// Edit the info.plist
        /// </summary>
        void EditInfoDoc(PlistDocument infoDoc);

        // todo: move to a separate interface; this method is rarely implemented
        /// <summary>
        /// Edit the GoogleService info.plist
        /// </summary>
        /// <param name="googlePlist">edit me</param>
        void EditGoogleServicePlist(PlistDocument googlePlist);

        void LastMethod(string pathToBuiltProject);
        #endif
    }

    internal static class PostBuild {

        public static void Run(BuildTarget buildTarget, string pathToBuiltProject,
            IEnumerable<IPostProcessIOS> allImplementors) {

            if (buildTarget != BuildTarget.iOS) {
                return;
            }

            Debug.Log("!!! Post process ios build");

            // package consumers will modify the pbx etc
            // implementors sorted from lowest callbackOrder to highest callbackOrder.
            var postProcessors = allImplementors
                .OrderBy(o => o.callbackOrder)
                .ToArray();

            #if UNITY_IOS
            // load generated project file
            PBXProject pbx = LoadPbx(pathToBuiltProject);

            // implementors modify pbx
            EditXcodeProject(pbx, pathToBuiltProject, postProcessors);

            // changing pbx is done
            SavePbx(pbx, pathToBuiltProject);

            EditInfoDoc(pathToBuiltProject, postProcessors);

            // create Podfile and install pods
            var podfilePath = Path.Combine(pathToBuiltProject, "Podfile");
            IosDependencyResolver.CreatePodfile(podfilePath);
            if (File.Exists(podfilePath)) {
                // install dependencies to build xcode project (also creates xcworkspace)
                Debug.Log("Installing native ios dependencies...");
                CocoapodsHelper.Install(pathToBuiltProject);
                Debug.Log("Cocoapods installed successfully");
            }
            
            LastMethod(pathToBuiltProject, postProcessors);
            #endif
        }
        
        #if UNITY_IOS
        private static PBXProject EditXcodeProject(PBXProject project, string pathToBuiltProject, IPostProcessIOS[]
            implementors) {
            var pbxProject = project;
            foreach (var implementor in implementors) {
                var modified = implementor.EditXcodeProject(pbxProject, pathToBuiltProject);
                if (modified != pbxProject) throw new BuildFailedException(
                    $"{nameof(EditXcodeProject)} implementation must return the same PBXProject object");
                pbxProject = modified;
            }

            return pbxProject;
        }

        #region simple helpers

        private static PBXProject LoadPbx(string pathToBuiltProject) {
            var pbx = new PBXProject();
            var pbxPath = Path.Combine(pathToBuiltProject, PBXProject.GetPBXProjectPath(pathToBuiltProject));
            pbx.ReadFromFile(pbxPath);

            return pbx;
        }

        private static void SavePbx(PBXProject pbx, string pathToBuiltProject) {
            var pbxPath = Path.Combine(pathToBuiltProject, PBXProject.GetPBXProjectPath(pathToBuiltProject));
            pbx.WriteToFile(pbxPath);
        }

        private static PlistDocument LoadInfoDoc(string pathToBuiltProject) {
            var infoDoc = new PlistDocument();
            string plistPath = Path.Combine(pathToBuiltProject, "Info.plist");
            infoDoc.ReadFromFile(plistPath);
            return infoDoc;
        }

        private static void SaveInfoDoc(string pathToBuiltProject, PlistDocument infoDoc) {
            string plistPath = Path.Combine(pathToBuiltProject, "Info.plist");
            infoDoc.WriteToFile(plistPath);
        }

        private static void EditInfoDoc(string pathToBuiltProject, IPostProcessIOS[] implementors) {
            var infoDoc = LoadInfoDoc(pathToBuiltProject);

            // edit info plist
            foreach (var implementor in implementors) {
                implementor.EditInfoDoc(infoDoc);
            }

            SaveInfoDoc(pathToBuiltProject, infoDoc);
        }
        #endregion

        private static void LastMethod(string pathToBuildProject, IPostProcessIOS[] implementors) {
            foreach (var implementor in implementors) {
                implementor.LastMethod(pathToBuildProject);
            }
        }
        #endif
    }

    public static class PlistExt {
        #if UNITY_IOS
        public static void AddInfoDocValue(this PlistDocument infoDoc, string key, string value) {
            infoDoc.root.values[key] = new PlistElementString(value);
        }

        public static void AddInfoDocBoolean(this PlistDocument infoDoc, string key, bool value) {
            infoDoc.root.values[key] = new PlistElementBoolean(value);
        }

        public static void AddInfoDocArray(this PlistDocument infoDoc,
            string key,
            IEnumerable<string> values) {
            var plistArray = new PlistElementArray();
            foreach (string val in values) {
                plistArray.AddString(val);
            }

            infoDoc.root.values[key] = plistArray;
        }

        public static void AddInfoDocDict(
            this PlistDocument infoDoc,
            string dictKey,
            Dictionary<string, string> values
        ) {
            var plistDict = infoDoc.root.CreateDict(dictKey);
            foreach (var pair in values) {
                plistDict.SetString(pair.Key, pair.Value);
            }
        }
        #endif
    }
}
