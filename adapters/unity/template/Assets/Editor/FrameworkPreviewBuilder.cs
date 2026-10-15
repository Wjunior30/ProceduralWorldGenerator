using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using ProceduralWorld.Core;
using Object = UnityEngine.Object;

public static class FrameworkPreviewBuilder
{
    const string ScenePath="Assets/Scenes/FrameworkWorld.unity";
    const string Generated="Assets/ProceduralWorld/Generated";
    static readonly string DataPath=Path.Combine(Application.streamingAssetsPath,"ProceduralWorld/world-data.json");
    static readonly Color[] Palette = {
        Hex("526d60"),Hex("526d60"),Hex("b5ab7e"),Hex("b99a68"),Hex("9a9e61"),Hex("6e9462"),Hex("416f52"),Hex("2d624b"),Hex("506952"),Hex("64796e"),Hex("989d96"),Hex("76736b"),Hex("e1e5e3")};
    static Color Hex(string code) { ColorUtility.TryParseHtmlString("#"+code,out var c);return c; }

    [MenuItem("Framework/Abrir mundo gerado")]
    public static void Open() { EditorSceneManager.OpenScene(ScenePath); }

    [MenuItem("Framework/Regenerar com a configuração exportada")]
    public static void Regenerate()
    {
        var configPath=Path.Combine(Application.streamingAssetsPath,"ProceduralWorld/world-config.json");
        if(!File.Exists(configPath))throw new FileNotFoundException("Exported generation settings are required",configPath);
        var config=JsonUtility.FromJson<ExportConfig>(File.ReadAllText(configPath)).settings;
        var settings=TerrainPresetCatalog.Create((TerrainStyle)Enum.Parse(typeof(TerrainStyle),config.style,true),config.size);
        settings.MountainHeight=config.mountainHeight;settings.ReliefStrength=config.reliefStrength;settings.ErosionIterations=config.erosionIterations;
        settings.RiverWidthMultiplier=config.riverWidth;settings.CaveDensity=config.caveDensity;settings.BiomeRegionScale=config.biomeScale;
        settings.WaterMode=(WaterMode)Enum.Parse(typeof(WaterMode),config.waterMode,true);
        if(config.enabledBiomes!=null && config.enabledBiomes.Length>0)settings.EnabledBiomes=Array.ConvertAll(config.enabledBiomes,x=>(Biome)Enum.Parse(typeof(Biome),x,true));
        var world=new WorldGenerator(config.seed,settings).Generate();
        var map=world.Surface;
        var data=new ProceduralWorldData {size=map.Size,cellSize=map.CellSize,height=map.Height,waterDepth=map.WaterDepth,flow=map.Flow,drainageReceiver=map.DrainageReceiver,riverChannel=map.RiverChannel,biomes=Array.ConvertAll(map.Biomes,x=>(int)x),minimumHeight=float.MaxValue,maximumHeight=float.MinValue,fingerprint=world.Fingerprint().ToString("X16")};
        foreach(float h in map.Height){data.minimumHeight=Math.Min(data.minimumHeight,h);data.maximumHeight=Math.Max(data.maximumHeight,h);}
        File.WriteAllText(DataPath,JsonUtility.ToJson(data));
        AssetDatabase.Refresh();
        Build();
    }

    // Invoked in batch mode first; leaves a saved, populated scene for the editor.
    public static void Build()
    {
        var data=JsonUtility.FromJson<ProceduralWorldData>(File.ReadAllText(DataPath));
        if(data.height==null||data.height.Length!=data.size*data.size)throw new InvalidDataException("Invalid framework world");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        PlayerSettings.productName=new DirectoryInfo(Path.GetFullPath(Path.Combine(Application.dataPath,".."))).Name;
        Directory.CreateDirectory(Generated);
        Directory.CreateDirectory("Assets/Scenes");
        AssetDatabase.Refresh();
        var map=new SurfaceMap(data.size,data.cellSize);
        Array.Copy(data.height,map.Height,data.height.Length);Array.Copy(data.flow,map.Flow,data.flow.Length);Array.Copy(data.drainageReceiver,map.DrainageReceiver,data.drainageReceiver.Length);Array.Copy(data.riverChannel,map.RiverChannel,data.riverChannel.Length);
        var field=new RiverSurfaceField(map);
        float extent=(data.size-1)*data.cellSize, half=extent*.5f, baseElevation=data.minimumHeight-32,range=Math.Max(.01f,data.maximumHeight-baseElevation);
        int fineSize=(data.size-1)*4+1;
        var terrainData=new TerrainData {heightmapResolution=fineSize,size=new Vector3(extent,range,extent),baseMapResolution=1024};
        var heights=new float[fineSize,fineSize];
        var texture=new Texture2D(fineSize,fineSize,TextureFormat.RGB24,false);
        texture.name="Framework Biomes";
        var pixels=new Color[fineSize*fineSize];
        for(int z=0;z<fineSize;z++)for(int x=0;x<fineSize;x++){
            float wx=x*field.Step-half,wz=z*field.Step-half;var sample=field.Sample(wx,wz);
            heights[z,x]=(sample.Bed-baseElevation)/range;
            Color color=GroundColor(data,x/4f,z/4f);
            color=Color.Lerp(color,Color.Lerp(Hex("91846a"),Hex("526d60"),Mathf.SmoothStep(0,1,(sample.Depth-.2f)/2)),sample.WetBank);
            pixels[z*fineSize+x]=color*Mathf.Lerp(.94f,1.06f,Mathf.PerlinNoise(wx*.42f,wz*.42f));
        }
        terrainData.SetHeights(0,0,heights);
        terrainData.alphamapResolution=512;
        texture.SetPixels(pixels);texture.Apply();texture.wrapMode=TextureWrapMode.Clamp;texture.filterMode=FilterMode.Bilinear;
        Save(texture,"BiomeColors.asset");
        var layer=new TerrainLayer {diffuseTexture=texture,tileSize=new Vector2(extent,extent),smoothness=.03f};
        Save(layer,"BiomeLayer.terrainlayer");terrainData.terrainLayers=new[]{layer};
        var alpha=new float[512,512,1];for(int z=0;z<512;z++)for(int x=0;x<512;x++)alpha[z,x,0]=1;
        terrainData.SetAlphamaps(0,0,alpha);Save(terrainData,"Terrain.asset");
        var terrainObject=Terrain.CreateTerrainGameObject(terrainData);terrainObject.name="Framework Terrain";
        terrainObject.transform.position=new Vector3(-half,baseElevation,-half);
        var terrain=terrainObject.GetComponent<Terrain>();terrain.heightmapPixelError=2;terrain.basemapDistance=5000;terrain.drawInstanced=true;
        var terrainMaterial=new Material(Shader.Find("Nature/Terrain/Standard"));Save(terrainMaterial,"TerrainMaterial.mat");terrain.materialTemplate=terrainMaterial;
        BuildWater(field);
        var sun=new GameObject("Daylight").AddComponent<Light>();sun.type=LightType.Directional;sun.color=Hex("fff0d5");sun.intensity=1.15f;sun.transform.rotation=Quaternion.Euler(34,-42,0);sun.shadows=LightShadows.Soft;
        var sky=new Material(Shader.Find("Skybox/Procedural"));sky.SetColor("_SkyTint",Hex("759cb5"));sky.SetColor("_GroundColor",Hex("6e806d"));sky.SetFloat("_AtmosphereThickness",.8f);Save(sky,"Sky.mat");
        RenderSettings.skybox=sky;RenderSettings.sun=sun;RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor=Hex("a5c3d1")*.7f;RenderSettings.ambientEquatorColor=Hex("849b88")*.6f;RenderSettings.ambientGroundColor=Hex("4a5e48")*.5f;
        RenderSettings.fog=true;RenderSettings.fogMode=FogMode.ExponentialSquared;RenderSettings.fogColor=Hex("bfd2d3");RenderSettings.fogDensity=.00032f;
        QualitySettings.antiAliasing=4;QualitySettings.shadowDistance=700;
        var camera=new GameObject("Preview Camera").AddComponent<Camera>();camera.tag="MainCamera";camera.nearClipPlane=.15f;camera.farClipPlane=10000;camera.fieldOfView=60;camera.allowHDR=true;
        var fly=camera.gameObject.AddComponent<PreviewFlyCamera>();
        fly.overviewPosition=new Vector3(-extent*.48f,data.maximumHeight+extent*.35f,-extent*.48f);
        fly.overviewEuler=Quaternion.LookRotation(new Vector3(0,data.minimumHeight+range*.30f,0)-fly.overviewPosition).eulerAngles;
        int focus=FindRiver(data);int fx=focus%data.size,fz=focus/data.size;
        var center=new Vector3(fx*data.cellSize-half,field.Sample(fx*data.cellSize-half,fz*data.cellSize-half).Surface,fz*data.cellSize-half);
        int receiver=data.drainageReceiver[focus];
        var forward=receiver>=0?new Vector3(receiver%data.size-fx,0,receiver/data.size-fz).normalized:Vector3.forward;
        if(forward.sqrMagnitude<.1f)forward=Vector3.forward;
        var side=new Vector3(-forward.z,0,forward.x);
        var eye=center-forward*65+side*38;
        eye.y=Mathf.Max(center.y+25,terrain.SampleHeight(eye)+baseElevation+10);
        fly.riverPosition=eye;fly.riverEuler=Quaternion.LookRotation(center+forward*55-eye).eulerAngles;
        camera.transform.SetPositionAndRotation(fly.riverPosition,Quaternion.Euler(fly.riverEuler));
        EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(ScenePath,true)};
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(),ScenePath);AssetDatabase.SaveAssets();
        if(SceneView.lastActiveSceneView!=null) SceneView.lastActiveSceneView.LookAtDirect(center, camera.transform.rotation,150);
        Capture(camera,"unity-river.png");camera.transform.SetPositionAndRotation(fly.overviewPosition,Quaternion.Euler(fly.overviewEuler));Capture(camera,"unity-overview.png");
        camera.transform.SetPositionAndRotation(fly.riverPosition,Quaternion.Euler(fly.riverEuler));
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(),ScenePath);
        ThirdPersonSetup.Configure();
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(),ScenePath);
        Debug.Log("FRAMEWORK_PREVIEW_READY fingerprint="+data.fingerprint+" size="+data.size+" scene="+ScenePath+" river="+focus);
    }
    [Serializable] private sealed class ExportConfig { public ExportSettings settings; }
    [Serializable] private sealed class ExportSettings {
        public int seed, size, erosionIterations;
        public string style, waterMode;
        public float mountainHeight, reliefStrength, riverWidth, caveDensity, biomeScale;
        public string[] enabledBiomes;
    }
    static void Save(Object asset,string file) {string path=Generated+"/"+file;if(AssetDatabase.LoadMainAssetAtPath(path)!=null)AssetDatabase.DeleteAsset(path);AssetDatabase.CreateAsset(asset,path);}
    static int FindRiver(ProceduralWorldData d) {
        int best=d.size*d.size/2;float score=-1;
        for(int z=d.size/4;z<d.size*3/4;z++)for(int x=d.size/4;x<d.size*3/4;x++) {int i=z*d.size+x;if(!d.riverChannel[i]||d.drainageReceiver[i]<0||d.waterDepth[i]<.15f)continue;float candidate=d.flow[i]/(1+Mathf.Abs(d.height[i]-d.height[d.drainageReceiver[i]]));if(candidate>score){score=candidate;best=i;}}
        return best;
    }
    struct WetVertex { public Vector3 p; public Color c; public float depth; }
    static void BuildWater(RiverSurfaceField field) {
        var vertices=new List<Vector3>();var colors=new List<Color>();var indices=new List<int>();
        Func<float,float,WetVertex> sample=(x,z)=>{
            var s=field.Sample(x,z);
            return new WetVertex{p=new Vector3(x,s.Surface+.018f,z),depth=s.Depth,c=new Color(.5f+s.Velocity.X/3.2f*.49f,.5f+s.Velocity.Y/3.2f*.49f,Mathf.Clamp01(s.Depth/3.35f),s.Rapids)};
        };
        foreach(var cell in field.RefinedCells)for(int z=0;z<4;z++)for(int x=0;x<4;x++){
            float px=cell.X*field.CellSize-field.HalfExtent+x*field.Step,pz=cell.Z*field.CellSize-field.HalfExtent+z*field.Step;
            var a=sample(px,pz);var b=sample(px+field.Step,pz);var c=sample(px,pz+field.Step);var d=sample(px+field.Step,pz+field.Step);
            Clip(new[]{a,c,b},vertices,colors,indices);Clip(new[]{b,c,d},vertices,colors,indices);
        }
        if(vertices.Count==0)return;
        var mesh=new Mesh{name="Framework River Surface",indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};mesh.SetVertices(vertices);mesh.SetColors(colors);mesh.SetTriangles(indices,0);
        var normals=new Vector3[vertices.Count];var tangents=new Vector4[vertices.Count];for(int i=0;i<tangents.Length;i++){normals[i]=Vector3.up;tangents[i]=new Vector4(1,0,0,1);}mesh.normals=normals;mesh.tangents=tangents;mesh.RecalculateBounds();Save(mesh,"Rivers.asset");
        var material=new Material(Shader.Find("ProceduralWorld/RiverPreview"));Save(material,"River.mat");
        var go=new GameObject("Framework Rivers");go.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
        Debug.Log("FRAMEWORK_WATER_TRIANGLES="+indices.Count/3);
    }
    static Color GroundColor(ProceduralWorldData data,float x,float z) {
        int ix=Mathf.Clamp(Mathf.FloorToInt(x),0,data.size-2),iz=Mathf.Clamp(Mathf.FloorToInt(z),0,data.size-2);
        float u=Mathf.SmoothStep(0,1,x-ix),v=Mathf.SmoothStep(0,1,z-iz);
        Func<int,Color> color=i=>data.biomes[i]<=2?Hex("6e8963"):Palette[Mathf.Clamp(data.biomes[i],0,Palette.Length-1)];
        return Color.Lerp(Color.Lerp(color(iz*data.size+ix),color(iz*data.size+ix+1),u),Color.Lerp(color((iz+1)*data.size+ix),color((iz+1)*data.size+ix+1),u),v);
    }
    static void Clip(WetVertex[] input,List<Vector3> vertices,List<Color> colors,List<int> indices) {
        var polygon=new List<WetVertex>();for(int i=0;i<3;i++){var a=input[(i+2)%3];var b=input[i];bool ai=a.depth>.025f,bi=b.depth>.025f;if(ai!=bi){float t=(.025f-a.depth)/(b.depth-a.depth);polygon.Add(new WetVertex{p=Vector3.Lerp(a.p,b.p,t),c=Color.Lerp(a.c,b.c,t),depth=.025f});}if(bi)polygon.Add(b);}
        for(int i=1;i<polygon.Count-1;i++)foreach(var v in new[]{polygon[0],polygon[i],polygon[i+1]}){indices.Add(vertices.Count);vertices.Add(v.p);colors.Add(v.c);}
    }
    static void Capture(Camera camera,string name) {
        var target=new RenderTexture(1600,900,24);camera.targetTexture=target;camera.Render();var previous=RenderTexture.active;RenderTexture.active=target;
        var image=new Texture2D(1600,900,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1600,900),0,0);image.Apply();
        string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../Artifacts"));Directory.CreateDirectory(folder);File.WriteAllBytes(Path.Combine(folder,name),image.EncodeToPNG());
        camera.targetTexture=null;RenderTexture.active=previous;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(image);
    }
}



