using UnityEngine;

public sealed class PreviewFlyCamera : MonoBehaviour
{
    public float speed = 28f;
    public Vector3 riverPosition;
    public Vector3 riverEuler;
    public Vector3 overviewPosition;
    public Vector3 overviewEuler;
    private bool instructions = true;
    void Start() { Debug.Log("FRAMEWORK_INTERACTIVE_READY | 1 panorama, 2 rio, WASD explorar"); }
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F1)) instructions = !instructions;
        if (Input.GetKeyDown(KeyCode.Alpha1)) { transform.position=overviewPosition; transform.eulerAngles=overviewEuler; }
        if (Input.GetKeyDown(KeyCode.Alpha2)) { transform.position=riverPosition; transform.eulerAngles=riverEuler; }
        bool look = Input.GetMouseButton(1);
        Cursor.lockState = look ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !look;
        if (look)
        {
            var angles = transform.eulerAngles;
            float pitch = angles.x > 180 ? angles.x-360 : angles.x;
            transform.rotation = Quaternion.Euler(Mathf.Clamp(pitch-Input.GetAxis("Mouse Y")*2,-85,85),angles.y+Input.GetAxis("Mouse X")*2,0);
        }
        var movement = Vector3.zero;
        if(Input.GetKey(KeyCode.W)) movement += transform.forward;
        if(Input.GetKey(KeyCode.S)) movement -= transform.forward;
        if(Input.GetKey(KeyCode.D)) movement += transform.right;
        if(Input.GetKey(KeyCode.A)) movement -= transform.right;
        if(Input.GetKey(KeyCode.E)) movement += Vector3.up;
        if(Input.GetKey(KeyCode.Q)) movement -= Vector3.up;
        transform.position += movement.normalized*speed*(Input.GetKey(KeyCode.LeftShift)?4:1)*Time.unscaledDeltaTime;
    }
    void OnDisable() { Cursor.lockState=CursorLockMode.None; Cursor.visible=true; }
    void OnGUI()
    {
        if(!instructions)return;
        GUI.Box(new Rect(18,18,640,82), "");
        GUI.Label(new Rect(32,25,610,24),"PROCEDURAL WORLD • UNITY | Mundo gerado pelo framework");
        GUI.Label(new Rect(32,51,610,24),"WASD mover • Q/E altura • Botão direito olhar • Shift acelerar");
        GUI.Label(new Rect(32,73,610,22),"1 panorama • 2 rio • F1 ocultar instruções");
    }
}
