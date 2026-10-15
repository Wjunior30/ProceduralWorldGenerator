using System;
using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class FrameworkAutoSetup
{
    static FrameworkAutoSetup(){EditorApplication.update+=Prepare;}
    static void Prepare()
    {
        if(Application.isBatchMode || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)return;
        if(File.Exists("Assets/Scenes/FrameworkWorld.unity")){EditorApplication.update-=Prepare;return;}
        if(!File.Exists(Path.Combine(Application.streamingAssetsPath,"ProceduralWorld/world-data.json")))return;
        EditorApplication.update-=Prepare;
        try{FrameworkPreviewBuilder.Build();}catch(Exception error){Debug.LogException(error);}
    }
}
