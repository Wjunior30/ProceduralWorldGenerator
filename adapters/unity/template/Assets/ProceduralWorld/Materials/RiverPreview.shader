Shader "ProceduralWorld/RiverPreview" {
 Properties {
  _Shallow("Shallow water",Color)=(0.10,0.48,0.43,1)
  _Deep("Deep water",Color)=(0.018,0.15,0.21,1)
 }
 SubShader {
  Tags {"Queue"="Geometry+10" "RenderType"="Opaque"}
  Cull Back
  ZWrite On
  Pass {
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma target 2.0
   #pragma multi_compile_fog
   #include "UnityCG.cginc"
   fixed4 _Shallow, _Deep;
   struct appdata { float4 vertex:POSITION; fixed4 color:COLOR; };
   struct v2f { float4 position:SV_POSITION; float2 worldXZ:TEXCOORD0; fixed4 river:COLOR; UNITY_FOG_COORDS(1) };
   v2f vert(appdata v) {
    v2f o; float3 world=mul(unity_ObjectToWorld,v.vertex).xyz;
    o.position=UnityObjectToClipPos(v.vertex);o.worldXZ=world.xz;o.river=v.color;UNITY_TRANSFER_FOG(o,o.position);return o;
   }
   fixed4 frag(v2f i):SV_Target {
    float depth=i.river.b*3.35;float2 velocity=(i.river.rg-.5)/.49*3.2;
    float phase=dot(i.worldXZ,float2(.105,.071))-dot(velocity,float2(.18,.12))*_Time.y;
    float shimmer=.5+.5*sin(phase);float deepFactor=saturate(depth*.32);
    float foam=saturate(i.river.a*.7+(1-saturate(depth*2.5))*smoothstep(.82,1,shimmer)*.22);
    fixed3 color=lerp(_Shallow.rgb,_Deep.rgb,deepFactor);color=lerp(color,fixed3(.74,.88,.84),foam);color+=((shimmer-.5)*.025)*(1-foam);
    fixed4 output=fixed4(color,1);UNITY_APPLY_FOG(i.fogCoord,output);return output;
   }
   ENDCG
  }
 }
 Fallback "Unlit/Color"
}
