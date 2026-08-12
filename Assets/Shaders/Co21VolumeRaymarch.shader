Shader "CO21/VolumeRaymarch"
{
    Properties
    {
        _VolumeTex ("Volume", 3D) = "white" {}
        _Threshold ("Threshold", Range(0, 1)) = 0.04
        _Opacity ("Opacity", Range(0, 5)) = 1.2
        _Steps ("Ray steps", Range(64, 512)) = 256
        _SliceZ ("Velocity slice", Range(0, 1)) = 0.5
        _SliceWidth ("Velocity slice width", Range(0.01, 1)) = 0.08
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE3D(_VolumeTex);
            SAMPLER(sampler_VolumeTex);
            float _Threshold;
            float _Opacity;
            float _Steps;
            float _SliceZ;
            float _SliceWidth;

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionOS : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionOS = input.positionOS.xyz;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            bool IntersectBox(float3 origin, float3 direction, out float enter, out float exit)
            {
                float3 inv = 1.0 / max(abs(direction), 1e-5) * sign(direction);
                float3 t0 = (-0.5 - origin) * inv;
                float3 t1 = ( 0.5 - origin) * inv;
                float3 lo = min(t0, t1);
                float3 hi = max(t0, t1);
                enter = max(max(lo.x, lo.y), lo.z);
                exit = min(min(hi.x, hi.y), hi.z);
                return exit > max(enter, 0.0);
            }

            float3 Ramp(float value)
            {
                float3 blue = float3(0.01, 0.05, 0.20);
                float3 cyan = float3(0.02, 0.65, 0.95);
                float3 yellow = float3(1.0, 0.82, 0.12);
                float3 red = float3(0.95, 0.08, 0.03);
                return value < 0.35 ? lerp(blue, cyan, value / 0.35) :
                       value < 0.70 ? lerp(cyan, yellow, (value - 0.35) / 0.35) :
                       lerp(yellow, red, (value - 0.70) / 0.30);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 cameraOS = TransformWorldToObject(_WorldSpaceCameraPos);
                float3 direction = normalize(input.positionOS - cameraOS);
                float enter, exit;
                if (!IntersectBox(cameraOS, direction, enter, exit)) discard;
                enter = max(enter, 0.0);
                float distance = exit - enter;
                float stepSize = distance / _Steps;
                float4 result = float4(0, 0, 0, 0);
                [loop]
                for (int i = 0; i < 512; i++)
                {
                    if (i >= _Steps || result.a > 0.97) break;
                    float t = enter + (i + 0.5) * stepSize;
                    float3 samplePosition = cameraOS + direction * t;
                    float3 uvw = samplePosition + 0.5;
                    float value = SAMPLE_TEXTURE3D(_VolumeTex, sampler_VolumeTex, uvw).r;
                    float sliceDistance = abs(uvw.z - _SliceZ);
                    float sliceVisibility = smoothstep(_SliceWidth, _SliceWidth * 0.35, sliceDistance);
                    // Use a soft transfer function so faint structure is
                    // visible without turning the whole cube into fog.
                    float density = smoothstep(_Threshold, 0.18, value);
                    density *= smoothstep(0.015, 0.55, value);
                    density *= sliceVisibility;
                    float alpha = 1.0 - exp(-density * _Opacity * stepSize * 18.0);
                    float3 color = Ramp(smoothstep(_Threshold, 1.0, value));
                    result.rgb += (1.0 - result.a) * color * alpha;
                    result.a += (1.0 - result.a) * alpha;
                }
                return result;
            }
            ENDHLSL
        }
    }
}
