using System;
using System.IO;
using UnityEngine;

[Serializable] public class ProceduralWorldData { public int size; public float cellSize; public float minimumHeight; public float maximumHeight; public float[] height; public float[] waterDepth; public float[] flow; public int[] drainageReceiver; public bool[] riverChannel; public bool[] mainRiverChannel; public int[] biomes; public string fingerprint; }
public static class GeneratedWorldBootstrap
{
    [RuntimeInitializeOnLoadMethod]
    static void Build()
    {
        if(UnityEngine.Object.FindFirstObjectByType<Terrain>() != null) return;
        string path=Path.Combine(Application.streamingAssetsPath,"ProceduralWorld/world-data.json"); if(!File.Exists(path)) return;
        var data=JsonUtility.FromJson<ProceduralWorldData>(File.ReadAllText(path)); var terrainData=new TerrainData(); terrainData.heightmapResolution=data.size;
        float range=Mathf.Max(.01f,data.maximumHeight-data.minimumHeight); var heights=new float[data.size,data.size];
        for(int z=0;z<data.size;z++) for(int x=0;x<data.size;x++) heights[z,x]=(data.height[z*data.size+x]-data.minimumHeight)/range;
        terrainData.size=new Vector3((data.size-1)*data.cellSize,range,(data.size-1)*data.cellSize); terrainData.SetHeights(0,0,heights);
        var terrain=Terrain.CreateTerrainGameObject(terrainData); terrain.name="Procedural World"; terrain.transform.position=new Vector3(-terrainData.size.x*.5f,data.minimumHeight,-terrainData.size.z*.5f);
        if(Camera.main==null){var camera=new GameObject("Main Camera").AddComponent<Camera>(); camera.tag="MainCamera"; camera.transform.position=new Vector3(0,data.maximumHeight+terrainData.size.x*.6f,-terrainData.size.z*.65f); camera.transform.LookAt(Vector3.up*(data.minimumHeight+range*.35f));}
        if(UnityEngine.Object.FindFirstObjectByType<Light>()==null){var light=new GameObject("Sun").AddComponent<Light>(); light.type=LightType.Directional; light.transform.rotation=Quaternion.Euler(50,-30,0);}
    }
}
