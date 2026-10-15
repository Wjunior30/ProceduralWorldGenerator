using System;
using System.IO;
using UnityEngine;
using ProceduralWorld.Core;

[RequireComponent(typeof(CharacterController))]
public sealed class ThirdPersonExplorer : MonoBehaviour
{
    public Transform view;
    public Vector3 spawnPoint;
    public float walkSpeed=5.5f, runSpeed=10f, jumpHeight=1.4f;
    public bool externalControl;
    public bool Swimming { get; private set; }
    public bool Grounded => motor != null && motor.isGrounded;
    public RiverSurfaceField Water { get; private set; }
    CharacterController motor;
    Transform model, leftArm, rightArm, leftLeg, rightLeg;
    Vector3 horizontal;
    float vertical, phase, jumpBuffer, coyote, fps=0, fpsTime=0;
    int fpsFrames=0;
    bool instructions=true;
    Terrain terrain;

    void Awake()
    {
        motor=GetComponent<CharacterController>();
        terrain=Terrain.activeTerrain;
        if(view==null && Camera.main!=null)view=Camera.main.transform;
        var path=Path.Combine(Application.streamingAssetsPath,"ProceduralWorld/world-data.json");
        if(File.Exists(path)) {
            var d=JsonUtility.FromJson<ProceduralWorldData>(File.ReadAllText(path));
            var map=new SurfaceMap(d.size,d.cellSize);
            Array.Copy(d.height,map.Height,d.height.Length); Array.Copy(d.flow,map.Flow,d.flow.Length);
            Array.Copy(d.waterDepth,map.WaterDepth,d.waterDepth.Length); Array.Copy(d.riverChannel,map.RiverChannel,d.riverChannel.Length); if(d.mainRiverChannel!=null&&d.mainRiverChannel.Length==d.height.Length)Array.Copy(d.mainRiverChannel,map.MainRiverChannel,d.mainRiverChannel.Length); Array.Copy(d.drainageReceiver,map.DrainageReceiver,d.drainageReceiver.Length);
            Water=new RiverSurfaceField(map);
        }
        BuildAvatar();
    }
    void Start() { Debug.Log("THIRD_PERSON_READY | WASD, Shift correr, Espaco saltar, mouse camera, R retornar"); }
    void Update()
    {
        fpsTime+=Time.unscaledDeltaTime;fpsFrames++;
        if(fpsTime>=.5f){fps=fpsFrames/fpsTime;fpsTime=0;fpsFrames=0;}
        if(Input.GetKeyDown(KeyCode.F1))instructions=!instructions;
        if(externalControl)return;
        if(Input.GetKeyDown(KeyCode.R)){Teleport(spawnPoint);return;}
        Vector2 input=new Vector2((Input.GetKey(KeyCode.D)?1:0)-(Input.GetKey(KeyCode.A)?1:0),(Input.GetKey(KeyCode.W)?1:0)-(Input.GetKey(KeyCode.S)?1:0));
        Step(input,Input.GetKeyDown(KeyCode.Space),Input.GetKey(KeyCode.LeftShift)||Input.GetKey(KeyCode.RightShift),Mathf.Min(Time.deltaTime,.05f));
    }
    public void Teleport(Vector3 point)
    {
        motor.enabled=false;transform.position=point;motor.enabled=true;
        horizontal=Vector3.zero;vertical=0;Swimming=false;jumpBuffer=coyote=0;
        if(view!=null && view.TryGetComponent<ThirdPersonOrbitCamera>(out var orbit))orbit.Snap();
    }
    public void Step(Vector2 input,bool jump,bool run,float dt)
    {
        if(dt<=0 || terrain==null)return;
        input=Vector2.ClampMagnitude(input,1);
        var forward=Vector3.ProjectOnPlane(view!=null?view.forward:Vector3.forward,Vector3.up).normalized;
        if(forward.sqrMagnitude<.01f)forward=Vector3.forward;
        var desired=forward*input.y+Vector3.Cross(Vector3.up,forward)*input.x;
        var sample=Water!=null?Water.Sample(transform.position.x,transform.position.z):default;
        float submersion=sample.Depth>.05f?Mathf.Max(0,sample.Surface-transform.position.y):0;
        Swimming=sample.Depth>1.35f && submersion>1.05f;
        float speed=Swimming?3.3f:(run?runSpeed:walkSpeed)*Mathf.Lerp(1,.55f,Mathf.Clamp01(submersion));
        horizontal=Vector3.MoveTowards(horizontal,desired*speed,(Swimming?9:24)*dt);
        coyote=Grounded?.12f:Mathf.Max(0,coyote-dt);
        jumpBuffer=jump?.14f:Mathf.Max(0,jumpBuffer-dt);
        if(Swimming) {
            // Feet float 1.2 m below the same surface that drives the rendered river.
            vertical=Mathf.MoveTowards(vertical,Mathf.Clamp((sample.Surface-1.2f-transform.position.y)*6,-3,3),12*dt);
            jumpBuffer=0;coyote=0;
        } else {
            if(Grounded&&vertical<0)vertical=-2;
            if(jumpBuffer>0&&coyote>0){vertical=Mathf.Sqrt(2*24*jumpHeight);jumpBuffer=0;coyote=0;}
            vertical=Mathf.Max(-45,vertical-24*dt);
        }
        var current=new Vector3(sample.Velocity.X,0,sample.Velocity.Y)*Mathf.Clamp01(submersion/1.2f);
        var flags=motor.Move((horizontal+current+Vector3.up*vertical)*dt);
        if((flags&CollisionFlags.Above)!=0&&vertical>0)vertical=0;
        var pos=transform.position;var origin=terrain.transform.position;var size=terrain.terrainData.size;
        var bounded=new Vector3(Mathf.Clamp(pos.x,origin.x+1,origin.x+size.x-1),pos.y,Mathf.Clamp(pos.z,origin.z+1,origin.z+size.z-1));
        if(bounded!=pos)motor.Move(bounded-pos);
        if(pos.y<origin.y-20)Teleport(spawnPoint);
        if(desired.sqrMagnitude>.01f)model.rotation=Quaternion.Slerp(model.rotation,Quaternion.LookRotation(desired),1-Mathf.Exp(-14*dt));
        Animate(dt,horizontal.magnitude);
    }
    void Animate(float dt,float speed)
    {
        phase+=dt*speed*2.4f;float swing=Mathf.Sin(phase)*Mathf.Clamp01(speed/3)*32;
        leftLeg.localRotation=Quaternion.Euler(swing,0,0);rightLeg.localRotation=Quaternion.Euler(-swing,0,0);
        leftArm.localRotation=Quaternion.Euler(Swimming?-65-swing:-swing,0,Swimming?-25:0);
        rightArm.localRotation=Quaternion.Euler(Swimming?-65+swing:swing,0,Swimming?25:0);
        model.localPosition=new Vector3(0,Grounded?Mathf.Abs(Mathf.Sin(phase))*.025f:0,0);
    }
    void BuildAvatar()
    {
        model=new GameObject("Explorer Visual").transform;model.SetParent(transform,false);
        var jacket=Material(new Color(.83f,.34f,.12f));var trousers=Material(new Color(.13f,.20f,.24f));var skin=Material(new Color(.76f,.55f,.37f));var pack=Material(new Color(.22f,.28f,.20f));
        Part("Jacket",model,new Vector3(0,1.16f,0),new Vector3(.54f,.64f,.31f),jacket);
        Part("Head",model,new Vector3(0,1.68f,0),new Vector3(.32f,.36f,.32f),skin,PrimitiveType.Sphere);
        Part("Nose",model,new Vector3(0,1.67f,.17f),new Vector3(.09f,.08f,.10f),skin);
        Part("Backpack",model,new Vector3(0,1.18f,-.23f),new Vector3(.39f,.47f,.20f),pack);
        leftArm=Limb("Left Arm",new Vector3(-.36f,1.43f,0),new Vector3(.19f,.56f,.22f),jacket);
        rightArm=Limb("Right Arm",new Vector3(.36f,1.43f,0),new Vector3(.19f,.56f,.22f),jacket);
        leftLeg=Limb("Left Leg",new Vector3(-.16f,.85f,0),new Vector3(.23f,.78f,.26f),trousers);
        rightLeg=Limb("Right Leg",new Vector3(.16f,.85f,0),new Vector3(.23f,.78f,.26f),trousers);
    }
    static Material Material(Color c){return new Material(Shader.Find("Standard")){color=c};}
    Transform Limb(string name,Vector3 position,Vector3 scale,Material material){var pivot=new GameObject(name).transform;pivot.SetParent(model,false);pivot.localPosition=position;Part(name+" Mesh",pivot,new Vector3(0,-scale.y*.5f,0),scale,material);return pivot;}
    static void Part(string name,Transform parent,Vector3 position,Vector3 scale,Material material,PrimitiveType type=PrimitiveType.Cube){var go=GameObject.CreatePrimitive(type);go.name=name;go.layer=2;go.transform.SetParent(parent,false);go.transform.localPosition=position;go.transform.localScale=scale;go.GetComponent<Collider>().enabled=false;Destroy(go.GetComponent<Collider>());go.GetComponent<Renderer>().sharedMaterial=material;}
    void OnGUI()
    {
        if(!instructions)return;
        GUI.Box(new Rect(18,18,700,102),"");
        GUI.Label(new Rect(32,26,670,22),"EXPLORAR EM TERCEIRA PESSOA  |  "+(Swimming?"NADANDO • a correnteza carrega você":"EM TERRA • explore o mundo do framework")+"  |  "+fps.ToString("F0")+" FPS");
        GUI.Label(new Rect(32,50,670,22),"WASD mover • Shift correr • Espaço saltar • Mouse girar câmera (clique no jogo para ativar)");
        GUI.Label(new Rect(32,74,670,22),"R voltar ao início • Roda do mouse aproximar/afastar • Esc liberar mouse • Clique retomar • F1 ajuda");
    }
}


