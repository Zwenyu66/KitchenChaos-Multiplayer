#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEditor.SceneManagement;

namespace KitchenChaos.Multiplayer {
    public sealed class MultiplayerTools : IPreprocessBuildWithReport {
        public int callbackOrder { get { return 0; } }
        [InitializeOnLoadMethod]
        public static void RefreshBuildInfo() {
            var roots=new[]{"Assets/Multiplayer/Core","Assets/Multiplayer/Runtime","Assets/Mirror/Core","Assets/Mirror/Transports/KCP"};
            var files=roots.SelectMany(root=>Directory.GetFiles(root,"*.cs",SearchOption.AllDirectories));
            string digest=BuildFingerprint.Compute(files.Select(p=>new KeyValuePair<string,string>(p,File.ReadAllText(p))));
            string json="{\"sourceHash\":\""+digest+"\"}\n";
            const string path="Assets/Resources/MultiplayerBuildInfo.json";
            Directory.CreateDirectory("Assets/Resources");
            if(!File.Exists(path)||File.ReadAllText(path)!=json) {
                File.WriteAllText(path,json,new UTF8Encoding(false));
                AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            }
        }
        [MenuItem("Kitchen Chaos/Open Multiplayer Kitchen")]
        public static void Open() { EditorSceneManager.OpenScene("Assets/Scenes/GameScene.unity"); }
        [MenuItem("Kitchen Chaos/Validate Project")]
        public static void Validate() {
            RefreshBuildInfo();
            var config=Resources.Load<TextAsset>("MultiplayerRules");
            if(config==null) throw new BuildFailedException("缺少联机配置");
            JsonUtility.FromJson<Rules>(config.text).Validate();
            var catalog=Resources.Load<MultiplayerAssets>("MultiplayerAssets");
            if(catalog==null || catalog.items==null || catalog.items.Length!=11 || catalog.items.Any(i=>i==null || i.prefab==null) || catalog.sounds==null ||
                catalog.chefBodyMaterials==null||catalog.chefBodyMaterials.Length!=4||catalog.chefBodyMaterials.Any(m=>m==null))
                throw new BuildFailedException("食材或音效目录缺失，请使用完整工程");
            foreach(var path in new[]{"Assets/Scenes/MainMenuScene.unity","Assets/Scenes/GameScene.unity","Assets/Scenes/LoadingScene.unity"})
                if(!EditorBuildSettings.scenes.Any(s=>s.enabled&&s.path==path)) throw new BuildFailedException("构建场景缺失: "+path);
            Debug.Log("Kitchen Chaos: 配置、11种物品和构建场景检查通过");
        }
        [MenuItem("Kitchen Chaos/Run Regression Tests")]
        public static void Tests() { Validate();RegressionSuite.Run(Debug.Log); }
        [MenuItem("Kitchen Chaos/Build Windows Multiplayer")]
        public static void BuildWindows() {
            Validate();Directory.CreateDirectory("Builds/Windows");
            var report=BuildPipeline.BuildPlayer(EditorBuildSettings.scenes.Where(s=>s.enabled).Select(s=>s.path).ToArray(),
                "Builds/Windows/KitchenChaos.exe",BuildTarget.StandaloneWindows64,BuildOptions.None);
            if(report.summary.result!=BuildResult.Succeeded) throw new BuildFailedException("Windows构建失败，请查看Console");
            Debug.Log("Windows联机版本位于 "+Path.GetFullPath("Builds/Windows"));
        }
        public void OnPreprocessBuild(BuildReport report) { Validate(); }
    }
}
#endif
