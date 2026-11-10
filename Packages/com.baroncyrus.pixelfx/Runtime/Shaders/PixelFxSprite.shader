// Draws a PixelFX sprite sheet.
//
// The sheets are premultiplied with the glow baked in:
//
//     rgb = color.rgb * color.a + light.rgb        a = color.a
//
// so one premultiplied blend puts the color over the background and adds the light on top, the way pixelfx
// composites its two buffers. A glow-only pixel has alpha 0 and rgb > 0, which is why these sprites need this
// shader (or any other premultiplied one) and look wrong with Sprites/Default.
//
// Unlit, and written against the built-in sprite include, so it works with SpriteRenderer (tint, flip,
// sorting) in URP, with either renderer, and in the Built-in Render Pipeline.
Shader "PixelFX/Sprite"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Intensity ("Intensity", Range(0, 8)) = 1
        _HueShift ("Hue Shift (degrees)", Range(-180, 180)) = 0
        [MaterialToggle] PixelSnap ("Pixel snap", Float) = 0
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _Flip ("Flip", Vector) = (1,1,1,1)
        [PerRendererData] _AlphaTex ("External Alpha", 2D) = "white" {}
        [PerRendererData] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex SpriteVert
            #pragma fragment PixelFxFrag
            #pragma target 2.0
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ PIXELSNAP_ON
            #include "UnitySprites.cginc"

            half _Intensity;
            half _HueShift;

            // Turn a color round the grey axis. It is linear, so it is safe on premultiplied color.
            half3 PixelFxHue(half3 c, half degrees)
            {
                half a = radians(degrees), s = sin(a), k = cos(a);
                const half3 axis = half3(0.57735, 0.57735, 0.57735);
                return c * k + cross(axis, c) * s + axis * dot(axis, c) * (1.0 - k);
            }

            half4 PixelFxFrag(v2f IN) : SV_Target
            {
                half4 c = tex2D(_MainTex, IN.texcoord);
                c.rgb = max(PixelFxHue(c.rgb, _HueShift), 0.0);
                // the tint's alpha fades the whole thing, glow included
                c.rgb *= IN.color.rgb * (IN.color.a * _Intensity);
                c.a *= IN.color.a;
                return c;
            }
            ENDCG
        }
    }

    Fallback Off
}
