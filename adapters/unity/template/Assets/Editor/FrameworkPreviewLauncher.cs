using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering;

public static class FrameworkPreviewLauncher
{
    public static void ValidateAndConfigure()
    {
        if(!File.Exists("Assets/Scenes/FrameworkWorld.unity"))FrameworkPreviewBuilder.Build();
        EditorSceneManager.OpenScene("Assets/Scenes/FrameworkWorld.unity");
        PlayerSettings.productName=new DirectoryInfo(Path.GetFullPath(Path.Combine(Application.dataPath,".."))).Name;
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64,false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,new[]{GraphicsDeviceType.Direct3D11});
        var terrain=UnityEngine.Object.FindFirstObjectByType<Terrain>();
        var camera=Camera.main;
        var water=GameObject.Find("Framework Rivers")?.GetComponent<MeshFilter>()?.sharedMesh;
        if(terrain==null||camera==null)throw new Exception("Incomplete preview scene");
        if(water!=null)foreach(var v in water.vertices)if(float.IsNaN(v.y)||float.IsInfinity(v.y))throw new Exception("Non-finite water vertex");
        var shader=Shader.Find("ProceduralWorld/RiverPreview");
        if(shader==null||ShaderUtil.ShaderHasError(shader))throw new Exception("Water material failed to compile");
        string source=Path.Combine(Application.streamingAssetsPath,"ProceduralWorld/world-data.json");
        string original=Path.GetFullPath(Path.Combine(Application.dataPath,"../ProceduralWorld"));
        var data=JsonUtility.FromJson<ProceduralWorldData>(File.ReadAllText(source));
        if(terrain.terrainData.heightmapResolution!=(data.size-1)*4+1)throw new Exception("River refinement not applied");
        File.WriteAllText(Path.Combine(original,"world-data.json"),File.ReadAllText(source));
        // Keep exported raw data synchronized with the world generated inside Unity.
        var bytes=new byte[data.height.Length*2];float span=Math.Max(.001f,data.maximumHeight-data.minimumHeight);
        for(int i=0;i<data.height.Length;i++){ushort value=(ushort)Mathf.Clamp(Mathf.RoundToInt((data.height[i]-data.minimumHeight)/span*65535),0,65535);bytes[i*2]=(byte)(value&255);bytes[i*2+1]=(byte)(value>>8);}
        File.WriteAllBytes(Path.Combine(original,"heightmap.r16"),bytes);
        File.WriteAllBytes(Path.Combine(original,"biomes.raw8"),Array.ConvertAll(data.biomes,x=>(byte)x));
        var configuration=JsonUtility.FromJson<GenerationConfig>(File.ReadAllText(Path.Combine(Application.streamingAssetsPath,"ProceduralWorld/world-config.json")));
        File.WriteAllText(Path.Combine(original,"unity-generation.json"),JsonUtility.ToJson(new GenerationMetadata {generator="ProceduralWorld.Core",runtime="Unity "+Application.unityVersion,seed=configuration.settings.seed,fingerprint=data.fingerprint,sourceGrid=data.size,terrainResolution=terrain.terrainData.heightmapResolution},true));
        AssetDatabase.SaveAssets();
        Debug.Log("UNITY_PREVIEW_VALIDATED vertices="+(water!=null?water.vertexCount:0)+" terrain="+terrain.terrainData.heightmapResolution+" fingerprint="+data.fingerprint);
    }
    [Serializable] private sealed class GenerationConfig { public SeedSettings settings; }
    [Serializable] private sealed class SeedSettings { public int seed; }
    [Serializable] private sealed class GenerationMetadata { public string generator,runtime,fingerprint; public int seed,sourceGrid,terrainResolution; }
    public static void OpenPreview()
    {
        if(!File.Exists("Assets/Scenes/FrameworkWorld.unity"))FrameworkPreviewBuilder.Build();
        EditorSceneManager.OpenScene("Assets/Scenes/FrameworkWorld.unity");
        EditorApplication.delayCall+=()=>{
            var type=typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            if(type!=null)EditorWindow.GetWindow(type).Show();
            EditorApplication.isPlaying=true;
        };
    }
}


