// Labo 3D : une gemme taillee, eclairee par le shader lui-meme.
//
// Pas de lumiere de scene, pas d'ombres temps reel, pas de paquet : l'eclairage est calcule ici, avec une lumiere
// principale fixe en haut a gauche et une lumiere de rappel. C'est ce qui garantit que le rendu est le meme sur
// tous les telephones, et que rien ne depend d'un reglage de pipeline qu'on ne controle pas.
//
// Le style vise le jeu peint plutot que le realisme : un ombrage en trois paliers (cartoon), des reflets nets en
// forme de facette, un liseré lumineux sur les bords, et un scintillement qui passe de facette en facette.
Shader "CrushRoyale/Gem3D"
{
    Properties
    {
        _Color ("Couleur", Color) = (1, 0.2, 0.2, 1)
        _Alpha ("Opacite", Range(0, 1)) = 1
        _Glow ("Eclat (speciales)", Range(0, 1)) = 0
        _Seed ("Graine du scintillement", Float) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Cull Off
        ZWrite On
        ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _Alpha;
            float _Glow;
            float _Seed;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 normal : TEXCOORD0;
                float3 local : TEXCOORD1;
                float gold : TEXCOORD2;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.local = v.vertex.xyz;
                o.gold = v.color.r;
                return o;
            }

            // Un nombre pseudo-aleatoire stable par facette : toutes les facettes ont la meme normale par
            // construction (ombrage plat), donc la normale suffit a les distinguer.
            float facetHash(float3 n)
            {
                return frac(sin(dot(n, float3(12.9898, 78.233, 37.719)) + _Seed) * 43758.5453);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 n = normalize(i.normal);
                // Camera orthographique : la direction de vue est celle de la camera, pas celle du pixel.
                float3 v = -normalize(mul((float3x3)unity_CameraToWorld, float3(0, 0, 1)));
                if (dot(n, v) < 0) n = -n;

                float3 key = normalize(float3(-0.55, 0.75, -0.6));
                float3 fill = normalize(float3(0.6, -0.35, -0.5));

                // Trois paliers, legerement adoucis : c'est ce qui donne le cote dessine plutot que plastique.
                float ndl = dot(n, key);
                float band = smoothstep(-0.25, -0.15, ndl) * 0.45 + smoothstep(0.35, 0.45, ndl) * 0.55;
                // Le cerclage est en or, la pierre garde sa couleur.
                float3 base = lerp(_Color.rgb, float3(1.0, 0.76, 0.3), i.gold);
                float3 dark = base * 0.42;
                float3 light = min(base * 1.18 + 0.06, 1.0);
                float3 col = lerp(dark, light, band);
                col += base * saturate(dot(n, fill)) * 0.22;

                // La table s'eclaire en degrade, plus vive vers la lumiere : c'est le faux reflet interne qui
                // donne de la profondeur a la pierre au lieu d'une surface plate.
                float tableGlow = saturate(-i.local.z * 2.2) * saturate(0.6 - i.local.x * 0.5 + i.local.y * 0.5);
                col += base * tableGlow * 0.35 * (1.0 - i.gold);

                // Reflet net : une facette tournee vers la lumiere s'allume, sans effacer la couleur de la pierre.
                float3 h = normalize(key + v);
                float spec = smoothstep(0.86, 0.93, dot(n, h));
                col = lerp(col, float3(1, 1, 1), spec * lerp(0.55, 0.85, i.gold));

                // Le scintillement : de temps en temps une facette s'illumine, jamais la meme.
                float phase = facetHash(n);
                float twinkle = pow(saturate(sin(_Time.y * 1.7 + phase * 40.0)), 24.0) * step(0.55, phase);
                col += twinkle * 0.8;

                // Liseré lumineux sur le pourtour, la signature des pierres taillees en jeu.
                float rim = pow(1.0 - saturate(dot(n, v)), 3.0);
                col += lerp(_Color.rgb, float3(1, 1, 1), 0.5) * rim * 0.55;

                // Les speciales respirent : elles s'eclairent de l'interieur.
                col += _Color.rgb * _Glow * (0.35 + 0.25 * sin(_Time.y * 6.0));

                return fixed4(col, _Alpha);
            }
            ENDCG
        }
    }
}
