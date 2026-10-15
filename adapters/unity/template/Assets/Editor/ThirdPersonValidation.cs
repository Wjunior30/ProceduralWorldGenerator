using System;
using System.IO;
using UnityEditor;
using UnityEngine;

// Runs against the live CharacterController, terrain collision and generated water.
[InitializeOnLoad]
public static class ThirdPersonValidation
{
    static int stage=-1, frames;
    static float performanceStartTime;
    static bool savedRunInBackground;
    static int savedVSyncCount, savedTargetFrameRate;
    static float startY, maxY;
    static Vector3 start, swimStart;
    static ThirdPersonExplorer player;
    static string report="";
    static ThirdPersonValidation(){EditorApplication.update+=Tick;}
    static void Check(bool condition,string label){if(!condition)throw new Exception(label);report+="PASS: "+label+"\n";}
    static void Tick()
    {
        if(!EditorApplication.isPlaying){stage=-1;return;}
        if(stage<0){if(!SessionState.GetBool("ThirdPerson.Validate",false))return;player=UnityEngine.Object.FindFirstObjectByType<ThirdPersonExplorer>();if(player==null||player.Water==null)return;SessionState.SetBool("ThirdPerson.Validate",false);savedRunInBackground=Application.runInBackground;savedVSyncCount=QualitySettings.vSyncCount;savedTargetFrameRate=Application.targetFrameRate;Application.runInBackground=true;QualitySettings.vSyncCount=0;Application.targetFrameRate=-1;player.externalControl=true;stage=0;frames=0;report="";}
        try {
            const float dt=1f/60;
            switch(stage){
                case 0:
                    player.Step(Vector2.zero,false,false,dt);
                    if(++frames<100)return;
                    Check(player.Grounded,"Spawn settles on terrain");start=player.transform.position;startY=start.y;maxY=startY;frames=0;stage=1;break;
                case 1:
                    player.Step(Vector2.zero,frames==0,false,dt);maxY=Mathf.Max(maxY,player.transform.position.y);
                    if(++frames<110)return;
                    Check(maxY>startY+1,"Jump rises at least one metre");Check(player.Grounded,"Jump lands on terrain");frames=0;stage=2;break;
                case 2:
                    player.Step(Vector2.up,false,false,dt);
                    if(++frames<90)return;
                    Check(Vector3.Distance(start,player.transform.position)>3,"Camera-relative walking moves across terrain");
                    var before=player.transform.position;player.Teleport(player.spawnPoint);Check(Vector3.Distance(player.transform.position,player.spawnPoint)<.01f,"Return to spawn resets position");
                    bool found=false;
                    foreach(var segment in player.Water.Segments){var mid=(segment.A+segment.B)*.5f;var s=player.Water.Sample(mid.X,mid.Y);if(s.Depth<1.9f||s.Velocity.Length()<.4f)continue;swimStart=new Vector3(mid.X,s.Surface-1.2f,mid.Y);found=true;break;}
                    Check(found,"Generated river contains a swimming reach");player.Teleport(swimStart);frames=0;stage=3;break;
                case 3:
                    player.Step(Vector2.zero,false,false,dt);
                    if(++frames<45)return;
                    Check(player.Swimming,"Deep water enables swimming");
                    var displacement=Vector3.ProjectOnPlane(player.transform.position-swimStart,Vector3.up);
                    Check(displacement.magnitude>.2f,"River current carries the swimmer without input");
                    var sample=player.Water.Sample(player.transform.position.x,player.transform.position.z);
                    Check(Mathf.Abs(player.transform.position.y-(sample.Surface-1.2f))<.65f,"Buoyancy stays near the water surface");
                    player.Teleport(player.spawnPoint);frames=0;stage=4;break;
                case 4:
                    player.Step(Vector2.zero,false,false,dt);if(++frames<90)return;
                    var orbit=Camera.main.GetComponent<ThirdPersonOrbitCamera>();orbit.Snap();orbit.UpdatePose(1);
                    Check(orbit.target==player.transform,"Orbit camera follows the explorer");
                    Check(!Camera.main.GetComponent<PreviewFlyCamera>().enabled,"Free camera does not compete for controls");
                    float savedYaw=orbit.yaw,savedPitch=orbit.pitch;
                    var savedRotation=Camera.main.transform.rotation;
                    orbit.ApplyMouseDelta(new Vector2(8,3));orbit.UpdatePose(1);
                    Check(Mathf.Abs(Mathf.DeltaAngle(savedYaw,orbit.yaw)-20)<.01f,"Horizontal mouse delta rotates orbit without holding a button");
                    Check(Mathf.Abs(orbit.pitch-(savedPitch-7.5f))<.01f,"Vertical mouse delta rotates orbit");
                    Check(Quaternion.Angle(savedRotation,Camera.main.transform.rotation)>10,"Mouse orbit changes actual camera transform");
                    orbit.ApplyMouseDelta(new Vector2(0,1000));Check(orbit.pitch==-12,"Camera upward angle is limited");
                    orbit.ApplyMouseDelta(new Vector2(0,-1000));Check(orbit.pitch==70,"Camera downward angle is limited");
                    orbit.yaw=savedYaw;orbit.pitch=savedPitch;orbit.Snap();orbit.UpdatePose(1);
                    var type=typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");if(type!=null)EditorWindow.GetWindow(type).Show();
                    frames=0;performanceStartTime=Time.realtimeSinceStartup;stage=5;break;
                case 5:
                    if(++frames<180)return;
                    float elapsed=Mathf.Max(.001f,Time.realtimeSinceStartup-performanceStartTime);
                    report+="METRIC: "+(frames/elapsed).ToString("F1")+" play-loop updates/s over "+frames+" updates\n";
                    Capture();File.WriteAllText(Path.Combine(Application.dataPath,"../Artifacts/third-person-validation.txt"),report);Debug.Log("THIRD_PERSON_VALIDATED\n"+report);player.externalControl=false;RestorePerformanceSettings();stage=6;break;
            }
        } catch(Exception error){if(player!=null){player.externalControl=false;player.Teleport(player.spawnPoint);}RestorePerformanceSettings();File.WriteAllText(Path.Combine(Application.dataPath,"../Artifacts/third-person-validation.txt"),report+"FAIL: "+error);Debug.LogException(error);stage=6;}
    }
    static void RestorePerformanceSettings(){Application.runInBackground=savedRunInBackground;QualitySettings.vSyncCount=savedVSyncCount;Application.targetFrameRate=savedTargetFrameRate;}
    static void Capture(){var camera=Camera.main;var target=new RenderTexture(1600,900,24);var previousTarget=camera.targetTexture;var previous=RenderTexture.active;camera.targetTexture=target;camera.Render();RenderTexture.active=target;var image=new Texture2D(1600,900,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1600,900),0,0);image.Apply();File.WriteAllBytes(Path.Combine(Application.dataPath,"../Artifacts/unity-third-person.png"),image.EncodeToPNG());camera.targetTexture=previousTarget;RenderTexture.active=previous;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(image);}
}

