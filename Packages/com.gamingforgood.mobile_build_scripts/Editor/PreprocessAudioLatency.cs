using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Commons.Editor {
    /// <summary>
    /// Modifies 'Project Settings > Audio > DSP Buffer Size'
    /// There is no Editor API for this audio setting.
    /// </summary>
    public class PreprocessAudioLatency : IPreprocessBuildWithReport {
        public int callbackOrder => 0;

        // constants used by the dropdown: Project Settings > Audio > DSP Buffer Size
        private const int bestLatency = 256;
        private const int goodLatency = 512;
        private const int bestPerformance = 1024;

        static bool enabled = false;
        
        public void OnPreprocessBuild(BuildReport report) {
            if (!enabled) {
                return;
            }
            var isCloudBuild = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITLAB_CI"));

            if (isCloudBuild) {
                var audioManager = AssetDatabase.LoadMainAssetAtPath("ProjectSettings/AudioManager.asset");
         
                var serObj = new SerializedObject(audioManager);
                serObj.Update();
         
                var m_DSPBufferSize = serObj.FindProperty("m_DSPBufferSize");
                var m_RequestedDSPBufferSize = serObj.FindProperty("m_RequestedDSPBufferSize");

                if (report.summary.platformGroup == BuildTargetGroup.Android) {
                    m_DSPBufferSize.intValue = bestPerformance;
                    m_RequestedDSPBufferSize.intValue = bestPerformance;
                } else {
                    m_DSPBufferSize.intValue = bestLatency;
                    m_RequestedDSPBufferSize.intValue = bestLatency;
                }
                serObj.ApplyModifiedProperties();
                
                Debug.Log($"Set m_DSPBufferSize to {m_DSPBufferSize.intValue}");
                Debug.Log($"Set m_RequestedDSPBufferSize to {m_RequestedDSPBufferSize.intValue}");
            }
        }
    }
}