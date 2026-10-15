Shader "ProceduralWorld/RiverPreview" {
 Properties {
  _Shallow("Shallow water",Color)=(0.10,0.48,0.43,1)
  _Deep("Deep water",Color)=(0.018,0.15,0.21,1)
 }
 SubShader {
  Tags {"Queue"="Transparent" "RenderType"="Transparent"}
  Cull Off
  CGPROGRAM
  #pragma surface surf Standard alpha:fade vertex:vert
  #pragma target 3.0
  fixed4 _Shallow, _Deep;
  struct Input { float3 worldPos; float4 river; };
  float hash(float2 p) {return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
  float noise(float2 p) {float2 i=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(hash(i),hash(i+float2(1,0)),f.x),lerp(hash(i+float2(0,1)),hash(i+1),f.x),f.y);}
  float wave(float2 p) { return noise(p*.48)*.65+noise(p*1.2)*.23+sin(p.x*.72+p.y*.3+noise(p*.2)*3)*.12; }
  float flow(float2 p,float2 velocity) {float phase=frac(_Time.y*.25);return lerp(wave(p-velocity*phase*4),wave(p-velocity*frac(phase+.5)*4),abs(phase*2-1));}
  void vert(inout appdata_full v,out Input o) {UNITY_INITIALIZE_OUTPUT(Input,o);o.river=v.color;}
  void surf(Input IN,inout SurfaceOutputStandard o) {
   float depth=IN.river.b*3.35;
   float2 velocity=(IN.river.rg-.5)/.49*3.2;
   float2 p=IN.worldPos.xz;
   float2 grad=float2(flow(p+float2(.08,0),velocity)-flow(p-float2(.08,0),velocity),flow(p+float2(0,.08),velocity)-flow(p-float2(0,.08),velocity))/.16;
   o.Normal=normalize(float3(-grad.x*.12,-grad.y*.12,1));
   float phase=frac(_Time.y*.25);
   float pattern=lerp(noise((p-velocity*phase*4)*1.2),noise((p-velocity*frac(phase+.5)*4)*1.2),abs(phase*2-1));
   float foam=smoothstep(.65,.86,pattern)*(IN.river.a*.8+(1-smoothstep(.04,.4,depth))*.25);
   o.Albedo=lerp(lerp(_Shallow.rgb,_Deep.rgb,1-exp(-depth*.65)),float3(.83,.94,.86),foam);
   o.Smoothness=lerp(.68,.45,foam);o.Metallic=.05;
   o.Alpha=(.3+.65*(1-exp(-depth*.8)))*smoothstep(.025,.18,depth);
  }
  ENDCG
 }
 Fallback "Transparent/Diffuse"
}
