#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using YamlDotNet.Serialization;

namespace Commons.Editor {

    /// <summary>
    /// The Podfile creator that replaces 'unity-jar-resolver'.
    /// It doesn't run 'pod install' so you will have to do that yourself,
    /// on the first build and any subsequent builds where pods changed.
    /// </summary>
    public static class IosDependencyResolver {

        [MenuItem("Build/Advanced/Test IosDependencyResolver")]
        private static void MenuTest() {
            string podfilePath = Path.Combine(Application.dataPath, "..", "..", "Podfile");
            CreatePodfile(podfilePath);
        }

        [Obsolete("not implemented, please run 'pod install' after unity build", true)]
        public static void PodInstall(string podfilePath) { }

        [MenuItem("Build/Advanced/List *IosDependencies.yml files")]
        private static void MenuListFiles() {
            var depFiles = GetDeclarationFiles();
            Debug.Log("List of all *IosDependencies.yml files:" +
                      "\n    " + String.Join("\n    ", depFiles));
        }

        private static string[] GetDeclarationFiles() {
            return FileFinder.FindAssetsNamed("IosDependencies.yml");
        }

        public static void CreatePodfile(string outputPathToPodfile) {
            // 1. find all the *IosDependencies.yml
            var depFiles = GetDeclarationFiles();
            // 2. read the files and create set of PodDependency's
            var depFilesContents = depFiles.Select(File.ReadAllText);

            var deserializer = new Deserializer();
            var pods = new HashSet<IPodDependency>();
            foreach (var contents in depFilesContents) {
                // ignore the example yml file
                if (contents.Contains("example:")) {
                    continue;
                }
                var declarations = deserializer.Deserialize<Dictionary<string, PodDeclaration>>(contents);
                // collect all declared pods, dropping exact duplicates
                foreach (var declaration in declarations) {
                    var podName = declaration.Key;
                    var declaredPod = declaration.Value.ToPodDependency(podName, outputPathToPodfile);
                    pods.Add(declaredPod);
                }
            }

            if (pods.Count == 0) {
                // nothing to do here, this project doesnt require cocoapods.
                return;
            }

            // 3. ensure that none of the PodDependency's have same .name
            var errorMessages = "";
            foreach (var pod in pods) {
                Debug.Log(pod);
                var withSameName = pods.Where(p => p.name == pod.name).ToArray();
                if (withSameName.Length == 1) continue;
                // else: pod declared twice and not with same source!
                errorMessages =
                    $"The pod '{pod.name}' has been declared {withSameName.Length} times and they point to different sources."
                    + "\nThe conflicting pod declarations:\n  " +
                    String.Join("\n  ", (IEnumerable<IPodDependency>) withSameName);
                errorMessages += "\n";
            }

            // make build failed if two have same name
            if (!String.IsNullOrEmpty(errorMessages)) {
                throw new BuildFailedException(errorMessages);
            }

            // 4. create Podfile that declares the pods
            var podfileContents = CreatePodfileContents(pods);
            File.WriteAllText(outputPathToPodfile, podfileContents);
        }

        private static string CreatePodfileContents(IEnumerable<IPodDependency> pods) {
            const string templateFileName = "PodfileTemplate.rb";
            var templateFile = FileFinder.FindAssetsNamed(templateFileName)
                .First(path => path.EndsWith($"IosDependencyResolver/{templateFileName}", StringComparison.Ordinal));

            var frameworkTargetLines = new StringBuilder();
            string nseTargetContents = String.Empty;
            
            foreach (var pod in pods) {
                frameworkTargetLines.AppendLine(pod.ToDeclarationLine());
                // firebase cloud messaging pod is needed by the NSE target
                if (pod.name == "Firebase/Messaging") {
                    nseTargetContents = new string(' ', 4) + pod.ToDeclarationLine();
                }
            }

            string podfile = File.ReadAllText(templateFile);
            
            var iosVersion = PlayerSettings.iOS.targetOSVersionString;
            podfile = podfile.Replace("**IOS_VERSION**", iosVersion);

            string frameworkTargetContents = frameworkTargetLines.ToString();
            podfile = podfile.Replace("**UNITY_FRAMEWORK_TARGET_CONTENTS**", frameworkTargetContents);

            var appTargetLines = new StringBuilder();
            if (nseTargetContents != String.Empty) {
                appTargetLines.AppendLine(new string(' ', 2) + "target 'NotificationServiceExtension' do");
                appTargetLines.AppendLine(nseTargetContents);
                appTargetLines.Append(new string(' ', 2) + "end");
            }
            string appTargetContents = appTargetLines.ToString();
            // note: PodfileTemplate.rb assumes that Unity-iPhone app target does not depend on any cocoapods.
            podfile = podfile.Replace("**UNITY_APP_TARGET_CONTENTS**", appTargetContents);
            
            return podfile;
        }
    }

    internal class PodDeclaration {
        // these fields are assigned by yaml deserialize
        #pragma warning disable 0649
        public string? version;
        public string? path;
        public string? git;
        public string? commit;
        #pragma warning restore 0649
        
        public IPodDependency ToPodDependency(string name, string outputPathToPodfile) {
            if (version != null) {
                return new PodStandard(name, version);
            }

            if (path != null) {
                // find where 'path' is pointing to
                // assume 'path' is relative to repo root
                var resolvedPath = Path.Combine(FileFinder.GetRepoRoot(), path);
                var exists = Directory.Exists(resolvedPath) || File.Exists(resolvedPath);
                if (!exists) {
                    throw new BuildFailedException($"Don't know where else to look. Couldn't resolve path to pod '{name}'" +
                        $" with only the specified path of '{path}'");
                }

                // resolve any relative stuff like '/..'
                resolvedPath = Path.GetFullPath(resolvedPath);
                var buildDirectory = Path.GetDirectoryName(outputPathToPodfile);
                return new PodLocal(name, resolvedPath, buildDirectory!);
            }

            if (git != null && commit != null) {
                return new PodGit(name, git, commit);
            } else {
                throw new Exception("'git' and 'commit' must both be declared when 'version' and 'path' are not.");
            }
        }
    }
    
    internal interface IPodDependency {
        /// Pod name
        string name { get; }
        /// Podfile line that goes 'pod ...'
        string ToDeclarationLine();
    }

    /// Pod that exists in cocoapods centralized repository
    internal readonly struct PodStandard : IPodDependency {
        private readonly string version;
        public string name { get; }

        public PodStandard(string name, string version) {
            this.version = version;
            this.name = name;
        }

        public string ToDeclarationLine() {
            return $"pod '{name}', '{version}'";
        }

        public override string ToString() {
            return $"{nameof(IPodDependency)} {{ name={name}, version={version} }}";
        }
    }

    /// Pod that has source files in a (public/accessible) git repository
    internal readonly struct PodGit : IPodDependency {

        public PodGit(string name, string git, string commit) {
            this.git = git;
            // We only care about the commit being a unique identifier.
            // This makes sure that two files can declare 'abc12345' and 'abc12345678906789'
            // and they are treated at the same source.
            if (commit.Length < 8) throw new BuildFailedException("commit hash must be unique (at least 8 characters)!" +
                                                                  $" '{commit}' is too short. Declared pod is '{name}' with git '{git}'");
            var shortCommit = commit.Substring(0, 8);
            this.commit = shortCommit;
            this.name = name;
        }

        private readonly string git;
        private readonly string commit;
        public string name { get; }

        public string ToDeclarationLine() {
            return $"pod '{name}', :git => '{git}', :commit => '{commit}'";
        }

        public override string ToString() {
            return $"{nameof(IPodDependency)} {{ name={name}, git={git}, commit={commit} }}";
        }
    }

    internal readonly struct PodLocal : IPodDependency {
        private readonly string path;
        public string name { get; }

        public PodLocal(string name, string absolutePath, string buildDirectory) {
            this.name = name;
            // create relative path from the build directory to the local pod path
            var relative = DirectoryUtil.GetRelativePath(absolutePath, buildDirectory);
            path = relative;
        }

        public string ToDeclarationLine() {
            return $"pod '{name}', :path => '{path}'";
        }

        public override string ToString() {
            return $"{nameof(IPodDependency)} {{ name={name}, path={path} }}";
        }
    }
}