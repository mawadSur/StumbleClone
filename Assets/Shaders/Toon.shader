Shader "StumbleClone/Toon"
{
    // Cartoon cel shader for the characters: keeps the model's base texture/colour, lights it with
    // a few hard bands instead of smooth PBR, and wraps it in a black inverted-hull outline. Two
    // passes (outline first, then the cel-lit body) so a single shader swap outlines every submesh.
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _Steps ("Shade Steps", Float) = 2
        _ShadowTint ("Shadow Floor", Range(0,1)) = 0.62
        _Ambient ("Ambient Lift", Range(0,1)) = 0.5
        _BrightGamma ("Brighten (gamma)", Range(0.2,1)) = 0.45
        _Saturation ("Saturation", Range(0,2)) = 1.45
        _OutlineColor ("Outline Color", Color) = (0.05,0.05,0.08,1)
        _OutlineWidth ("Outline Width", Float) = 0.022
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        // ---- Pass 1: inverted-hull outline ---------------------------------------
        Pass
        {
            Name "Outline"
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float _Steps;
                float _ShadowTint;
                float _Ambient;
                float _BrightGamma;
                float _Saturation;
                float4 _OutlineColor;
                float _OutlineWidth;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionHCS : SV_POSITION; };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                float3 p = IN.positionOS.xyz + normalize(IN.normalOS) * _OutlineWidth;
                OUT.positionHCS = TransformObjectToHClip(p);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target { return _OutlineColor; }
            ENDHLSL
        }

        // ---- Pass 2: cel-lit body -------------------------------------------------
        Pass
        {
            Name "ToonLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float _Steps;
                float _ShadowTint;
                float _Ambient;
                float _BrightGamma;
                float _Saturation;
                float4 _OutlineColor;
                float _OutlineWidth;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varyings { float4 positionHCS : SV_POSITION; float2 uv : TEXCOORD0; float3 normalWS : TEXCOORD1; float4 color : COLOR; };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.color = IN.color;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                // Quaternius characters bake their palette into VERTEX COLOURS (no texture), so the
                // base colour is map * tint * vertexColour. For textured models the vertex colour is
                // white and drops out; for vertex-coloured ones it's what gives skin/clothes colour.
                half4 baseCol = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor * IN.color;

                // The Quaternius palette is baked dark/muted into _BaseColor (skin ~0.01, clothes
                // ~0.07). Pop it for the cartoon look: gamma-lift the darks toward mid, then push
                // saturation. Brights (face/highlights) stay near where they are.
                baseCol.rgb = pow(saturate(baseCol.rgb), _BrightGamma);
                float luma = dot(baseCol.rgb, float3(0.299, 0.587, 0.114));
                baseCol.rgb = saturate(lerp(luma.xxx, baseCol.rgb, _Saturation));

                float3 N = normalize(IN.normalWS);

                Light mainLight = GetMainLight();
                float ndotl = saturate(dot(N, mainLight.direction));

                // Hard cel bands, then lift the darkest band well off black so the cartoon reads
                // bright and flat (a shadow is a tint, not a void).
                float steps = max(1.0, _Steps);
                float banded = floor(ndotl * steps + 0.5) / steps;
                banded = lerp(_ShadowTint, 1.0, banded);

                // Ambient is a floor; the banded light fills the rest up toward full. Keeps the
                // whole character in a tight bright range (no near-black shadow side).
                float3 lightTerm = banded * mainLight.color;
                float3 col = baseCol.rgb * (_Ambient + (1.0 - _Ambient) * lightTerm);
                return half4(saturate(col), baseCol.a);
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Lit"
}
