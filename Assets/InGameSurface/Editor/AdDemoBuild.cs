using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace InGameSurface.Editor
{
    public static class AdDemoBuild
    {
        const string Scene="Assets/Scenes/IGSDemo.unity";
        [MenuItem("In Game Surface/Открыть демонстрацию")]
        public static void Open()
        { if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;Prepare();EditorSceneManager.OpenScene(Scene); }

        public static void Prepare()
        {
            Directory.CreateDirectory("Assets/InGameSurface/Resources/IGS");
            const string mat="Assets/InGameSurface/Resources/IGS/AdSurface.mat";
            if(AssetDatabase.LoadAssetAtPath<Material>(mat)==null)
                AssetDatabase.CreateAsset(new Material(Shader.Find("Universal Render Pipeline/Unlit")),mat);
            if(!File.Exists(Scene))
            { var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);EditorSceneManager.SaveScene(scene,Scene);EditorSceneManager.CloseScene(scene,true); }
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
