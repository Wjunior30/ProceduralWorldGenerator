using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class ThirdPersonSetup
{
    static readonly string Request=Path.GetFullPath(Path.Combine(Application.dataPath,"../Artifacts/install-third-person.request"));
    static ThirdPersonSetup(){EditorApplication.update+=Poll;}
    static void Poll()
    {
        if(!File.Exists(Request)||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
        if(EditorApplication.isPlayingOrWillChangePlaymode){EditorApplication.isPlaying=false;return;}
        File.Delete(Request);
        try {
            Install();
            SessionState.SetBool("ThirdPerson.Validate",true);
            EditorApplication.isPlaying=true;
        } catch(Exception e){Debug.LogException(e);}
    }
    [MenuItem("Framework/Explorar em terceira pessoa")]
    public static void Install()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Exit Play mode before installing the controller.");
        if(UnityEngine.SceneManagement.SceneManager.GetActiveScene().path!="Assets/Scenes/FrameworkWorld.unity")EditorSceneManager.OpenScene("Assets/Scenes/FrameworkWorld.unity");
        Configure();
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        Debug.Log("THIRD_PERSON_INSTALLED");
    }
    public static void Configure()
    {
        var camera=Camera.main;var terrain=Terrain.activeTerrain;
        if(camera==null||terrain==null)throw new InvalidOperationException("Generated terrain and camera required.");
        var fly=camera.GetComponent<PreviewFlyCamera>();
        Vector3 seed=fly!=null?fly.riverPosition:camera.transform.position;
        Vector3 spawn=FindSpawn(terrain,seed);
        var player=UnityEngine.Object.FindFirstObjectByType<ThirdPersonExplorer>();
        if(player==null){var go=new GameObject("Explorer • Third Person");go.layer=2;var controller=go.AddComponent<CharacterController>();controller.height=1.85f;controller.radius=.32f;controller.center=new Vector3(0,.94f,0);controller.stepOffset=.38f;controller.slopeLimit=48;controller.skinWidth=.04f;controller.minMoveDistance=0;player=go.AddComponent<ThirdPersonExplorer>();}
        player.transform.position=spawn;player.spawnPoint=spawn;player.view=camera.transform;
        if(fly!=null)fly.enabled=false;
        var orbit=camera.GetComponent<ThirdPersonOrbitCamera>();if(orbit==null)orbit=camera.gameObject.AddComponent<ThirdPersonOrbitCamera>();
        orbit.target=player.transform;orbit.yaw=fly!=null?fly.riverEuler.y:camera.transform.eulerAngles.y;orbit.pitch=18;orbit.distance=5.5f;
        camera.nearClipPlane=.08f;orbit.Snap();orbit.UpdatePose(1);
    }
    static Vector3 FindSpawn(Terrain terrain,Vector3 seed)
    {
        var origin=terrain.transform.position;var size=terrain.terrainData.size;
        // Prefer a walkable bank near the original river viewpoint.
        for(int r=0;r<=80;r+=4)for(int angle=0;angle<360;angle+=30){float a=angle*Mathf.Deg2Rad;var p=seed+new Vector3(Mathf.Cos(a)*r,0,Mathf.Sin(a)*r);float u=(p.x-origin.x)/size.x,v=(p.z-origin.z)/size.z;if(u<.02f||u>.98f||v<.02f||v>.98f)continue;if(terrain.terrainData.GetSteepness(u,v)>25)continue;p.y=terrain.SampleHeight(p)+origin.y+.12f;return p;}
        seed.y=terrain.SampleHeight(seed)+origin.y+.2f;return seed;
    }
}
