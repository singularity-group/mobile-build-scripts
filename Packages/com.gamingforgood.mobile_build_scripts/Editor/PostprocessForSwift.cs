#if UNITY_IOS
using UnityEngine;
using UnityEditor.iOS.Xcode;

namespace Commons.Editor {
    /// Adds support for writing Swift plugins.
    /// Designed for iOS, and may work for Mac as well.
    public class PostprocessForSwift : IPostProcessIOS {
        public int callbackOrder => 1; // can be run at any time

        public PBXProject EditXcodeProject(PBXProject pbx, string targetGuid) {
            return AddSwiftCodeEssentials(pbx, targetGuid);
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

        public void EditInfoDoc(PlistDocument infoDoc) {}

        public void EditGoogleServicePlist(PlistDocument googlePlist) {}

        public void LastMethod(string pathToBuiltProject) {}
    }
}
#endif
