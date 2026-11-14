using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FlightRecord.Experiments.Editor
{
    /// <summary>
    /// Creates experiment 06's scene (a camera and a <see cref="BridgeDemo"/>) and builds it as
    /// a standalone player, from the menu or in batch mode:
    /// <c>Unity -batchmode -quit -projectPath unity -executeMethod FlightRecord.Experiments.Editor.BridgeSceneBuilder.BuildPlayer</c>.
    /// The player lands in unity/Builds/Bridge/ (git-ignored).
    /// </summary>
    public static class BridgeSceneBuilder
    {
        public const string ScenePath = "Assets/Experiments/Bridge/Bridge.unity";

        [MenuItem("Flight Record/Experiments/Create Bridge Scene")]
        public static void CreateScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Main Camera") { tag = "MainCamera" };
            var c = camera.AddComponent<Camera>();
            c.orthographic = true;
            c.orthographicSize = 5;
            c.clearFlags = CameraClearFlags.SolidColor;
            c.backgroundColor = Color.black;
            camera.transform.position = new Vector3(0, 0, -10);
            new GameObject("Bridge").AddComponent<BridgeDemo>();
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"Saved {ScenePath}");
        }

        [MenuItem("Flight Record/Experiments/Build Bridge Player")]
        public static void BuildPlayer()
        {
            if (!File.Exists(ScenePath)) CreateScene();
            var target = Application.platform == RuntimePlatform.OSXEditor ? BuildTarget.StandaloneOSX
                : Application.platform == RuntimePlatform.LinuxEditor ? BuildTarget.StandaloneLinux64
                : BuildTarget.StandaloneWindows64;
            string exe = target == BuildTarget.StandaloneWindows64 ? "Bridge.exe" : "Bridge";
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = Path.Combine("Builds", "Bridge", exe),
                target = target,
                options = BuildOptions.None,
            });
            Debug.Log($"Bridge player build: {report.summary.result}, {report.summary.totalErrors} errors");
            if (Application.isBatchMode && report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                EditorApplication.Exit(1);
        }
    }
}
