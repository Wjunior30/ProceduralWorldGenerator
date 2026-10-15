using UnityEngine;

public sealed class ThirdPersonOrbitCamera : MonoBehaviour
{
    public Transform target;
    public float distance=5.5f;
    public float yaw, pitch=18;
    public float mouseSensitivity=2.5f;
    Vector3 focus;
    bool initialized;
    public void Snap(){initialized=false;}
    void Start(){CapturePointer();Debug.Log("THIRD_PERSON_MOUSE_READY | Clique no jogo, mova o mouse; Esc libera o cursor");}
    public void CapturePointer(){Cursor.lockState=CursorLockMode.Locked;Cursor.visible=false;}
    public void ReleasePointer(){Cursor.lockState=CursorLockMode.None;Cursor.visible=true;}
    void Update()
    {
        if(Input.GetKeyDown(KeyCode.Escape)){ReleasePointer();return;}
        // A single click captures the pointer. Rotation never requires holding a button.
        bool capture=Input.GetMouseButtonDown(0)||Input.GetMouseButtonDown(1);
        if(capture){CapturePointer();return;} // Ignore the recentering delta on capture.
        if(Cursor.lockState!=CursorLockMode.Locked || !Application.isFocused)return;
        ApplyMouseDelta(new Vector2(Input.GetAxisRaw("Mouse X"),Input.GetAxisRaw("Mouse Y")));
        distance=Mathf.Clamp(distance-Input.mouseScrollDelta.y*.6f,2.5f,11);
    }
    public void ApplyMouseDelta(Vector2 delta)
    {
        // Mouse axes already represent displacement this frame, so do not multiply by deltaTime.
        yaw=Mathf.Repeat(yaw+delta.x*mouseSensitivity,360);
        pitch=Mathf.Clamp(pitch-delta.y*mouseSensitivity,-12,70);
    }
    void OnApplicationFocus(bool focused){if(!focused)ReleasePointer();}
    void OnDisable(){ReleasePointer();}
    void LateUpdate(){UpdatePose(Time.deltaTime);}
    public void UpdatePose(float dt)
    {
        if(target==null)return;
        var anchor=target.position+Vector3.up*1.4f;
        focus=initialized?Vector3.Lerp(focus,anchor,1-Mathf.Exp(-16*dt)):anchor; initialized=true;
        var rotation=Quaternion.Euler(pitch,yaw,0);var back=rotation*Vector3.back;
        float length=distance;
        if(Physics.SphereCast(focus,.24f,back,out var hit,distance,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))length=Mathf.Max(.15f,hit.distance-.12f);
        transform.SetPositionAndRotation(focus+back*length,rotation);
    }
}
