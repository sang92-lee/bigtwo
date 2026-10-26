using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;

// 웹(WebGL) 빌드. 메뉴: Tools → 웹 빌드
public static class WebBuild
{
    public const string ScenePath = "Assets/Scenes/Main.unity";
    const string OutDir = "Builds/WebGL";

    [MenuItem("Tools/메인 씬 만들기")]
    public static void MakeScene()
    {
        // 장면은 BigTwoGame이 실행할 때 코드로 만든다. 씬은 비워 둔다
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
    }

    [MenuItem("Tools/웹 빌드")]
    public static void Build()
    {
        // 코드로 머티리얼을 만들기 때문에 셰이더가 빌드에서 빠지지 않게 한다
        AddAlwaysIncluded("Standard");
        if (!File.Exists(ScenePath)) MakeScene();

        PlayerSettings.productName = "빅 투";
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
        PlayerSettings.WebGL.decompressionFallback = true;
        PlayerSettings.WebGL.dataCaching = false;
        PlayerSettings.stripEngineCode = false;
        PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, ManagedStrippingLevel.Low);

        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = OutDir,
            target = BuildTarget.WebGL,
        });
        var s = report.summary;
        Debug.Log($"[WebBuild] result={s.result} size={s.totalSize / 1024 / 1024}MB errors={s.totalErrors}");
    }

    static void AddAlwaysIncluded(params string[] names)
    {
        var gs = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
        var list = gs.FindProperty("m_AlwaysIncludedShaders");
        foreach (var n in names)
        {
            var shader = Shader.Find(n);
            if (shader == null) continue;
            bool exists = false;
            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == shader) exists = true;
            if (exists) continue;
            list.InsertArrayElementAtIndex(list.arraySize);
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
        }
        gs.ApplyModifiedProperties();
    }
}
