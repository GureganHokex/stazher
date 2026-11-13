using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace InGameSurface.Editor
{
    public static class AdDemoBuild
    {
        const string Scene="Assets/Scenes/IGSDemo.unity";
        [MenuItem("In Game Surface/Открыть демонстрацию")]
        public static void Open()
        { if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;Prepare();EditorSceneManager.OpenScene(Scene); }

        [MenuItem("In Game Surface/Открыть игру с рекламой")]
        public static void OpenGame()
        { if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;Prepare();EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity"); }

        [MenuItem("In Game Surface/Открыть игру с рекламой",true)]
        public static bool CanOpenGame() => !EditorApplication.isPlayingOrWillChangePlaymode;

        public static void Prepare()
        {
            Directory.CreateDirectory("Assets/InGameSurface/Resources/IGS");
            const string mat="Assets/InGameSurface/Resources/IGS/AdSurface.mat";
            if(AssetDatabase.LoadAssetAtPath<Material>(mat)==null)
                AssetDatabase.CreateAsset(new Material(Shader.Find("Universal Render Pipeline/Unlit")),mat);
            if(!File.Exists(Scene))
            {
                // Unity cannot add a scene alongside an unsaved Untitled scene on first import.
                bool untitled=false;
                for(int i=0;i<SceneManager.sceneCount;i++)untitled|=string.IsNullOrEmpty(SceneManager.GetSceneAt(i).path);
                if(untitled && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    throw new OperationCanceledException("Создание демосцены отменено пользователем");
                var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,untitled?NewSceneMode.Single:NewSceneMode.Additive);
                if(!EditorSceneManager.SaveScene(scene,Scene))throw new IOException("Не удалось сохранить демосцену IGS");
                if(!untitled)EditorSceneManager.CloseScene(scene,true);
            }
            AssetDatabase.SaveAssets();AssetDatabase.Refresh();
        }
        [MenuItem("In Game Surface/Собрать демо для macOS")]
        public static void BuildMac() { Build(BuildTarget.StandaloneOSX,"Builds/IGS-Mac/In Game Surface.app"); }
        [MenuItem("In Game Surface/Собрать демо для Windows")]
        public static void BuildWindows() { Build(BuildTarget.StandaloneWindows64,"Builds/IGS-Windows/InGameSurface.exe"); }
        static void Build(BuildTarget target,string output)
        {
            Prepare();Directory.CreateDirectory(Path.GetDirectoryName(output));
            string product=PlayerSettings.productName,company=PlayerSettings.companyName;
            bool background=PlayerSettings.runInBackground;var screen=PlayerSettings.fullScreenMode;
            int screenWidth=PlayerSettings.defaultScreenWidth,screenHeight=PlayerSettings.defaultScreenHeight;
            try
            {
                PlayerSettings.productName="In Game Surface";PlayerSettings.companyName="IGS Prototype";
                PlayerSettings.runInBackground=true;PlayerSettings.fullScreenMode=FullScreenMode.Windowed;
                PlayerSettings.defaultScreenWidth=1440;PlayerSettings.defaultScreenHeight=900;
                var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes=new[]{Scene},locationPathName=output,target=target,options=BuildOptions.Development });
                if(report.summary.result!=BuildResult.Succeeded)throw new Exception("IGS build failed: "+report.summary.result);
                Debug.Log("IGS_BUILD_OK "+Path.GetFullPath(output));
            }
            finally { PlayerSettings.productName=product;PlayerSettings.companyName=company;PlayerSettings.runInBackground=background;PlayerSettings.fullScreenMode=screen;PlayerSettings.defaultScreenWidth=screenWidth;PlayerSettings.defaultScreenHeight=screenHeight;AssetDatabase.SaveAssets(); }
        }
    }
}
