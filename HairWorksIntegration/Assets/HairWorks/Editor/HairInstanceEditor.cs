using System;
using UnityEditor;
using UnityEngine;

namespace UTJ
{
    [CustomEditor(typeof(HairInstance))]
    public class HairInstanceEditor : Editor
    {
        bool showTextures = false;
        bool showRendering = false;
    

        public override void OnInspectorGUI()
        {
            var t = target as HairInstance;

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Load Hair Asset"))
            {
                var path = EditorUtility.OpenFilePanel("Select apx file in StreamingAssets directory", Application.streamingAssetsPath, "apx");
                t.LoadHairAsset(MakeRelativePath(path));
            }
            if (GUILayout.Button("Reload Hair Asset"))
            {
                t.ReloadHairAsset();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(10);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Load Hair Shader"))
            {
                var path = EditorUtility.OpenFilePanel("Select compiled shader (.cso) file in StreamingAssets directory", Application.streamingAssetsPath, "cso");
                t.LoadHairShader(MakeRelativePath(path));
            }
            if (GUILayout.Button("Reload Hair Shader"))
            {
                t.ReloadHairShader();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(10);

            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Assign Textures"))
            {
                t.AssignAllTextures();
            }

            GUILayout.EndHorizontal();

            GUILayout.Space(10);

            DrawDefaultInspector();

            showRendering = EditorGUILayout.Foldout(showRendering, "Rendering", EditorStyles.foldout);

            if (showRendering)
            {
                t.useLightProbes = EditorGUILayout.Toggle("Use Light Probes", t.useLightProbes);

                if (t.useLightProbes)
                {
                    t.lightProbeIntensity = EditorGUILayout.FloatField("Light Probe Intensity", t.lightProbeIntensity);
                }

                t.useReflectionProbes = EditorGUILayout.Toggle("Use Reflection Probes", t.useReflectionProbes);

                if (t.useReflectionProbes)
                {
                    t.reflectionProbeIntensity = EditorGUILayout.FloatField("Reflection Probe Intensity", t.reflectionProbeIntensity);
                    t.reflectionProbeSpecularity = EditorGUILayout.FloatField("Specular Strength", t.reflectionProbeSpecularity);
                }
            }

            showTextures = EditorGUILayout.Foldout(showTextures, "Textures", EditorStyles.foldout);

            if (showTextures)
            {
                t.root = (Texture2D)EditorGUILayout.ObjectField("Root Color", t.root, typeof(Texture2D), false);
                t.tip = (Texture2D)EditorGUILayout.ObjectField("Tip Color", t.tip, typeof(Texture2D), false);
                t.specular = (Texture2D)EditorGUILayout.ObjectField("Specular", t.specular, typeof(Texture2D), false);
                t.clump = (Texture2D)EditorGUILayout.ObjectField("Clump", t.clump, typeof(Texture2D), false);
                t.density = (Texture2D)EditorGUILayout.ObjectField("Density", t.density, typeof(Texture2D), false);
                t.stiffness = (Texture2D)EditorGUILayout.ObjectField("Stiffness", t.stiffness, typeof(Texture2D), false);
                t.waviness = (Texture2D)EditorGUILayout.ObjectField("Waviness", t.waviness, typeof(Texture2D), false);
                t.width = (Texture2D)EditorGUILayout.ObjectField("Width", t.width, typeof(Texture2D), false);
                t.rootStiffness = (Texture2D)EditorGUILayout.ObjectField("Root Stiffness", t.rootStiffness, typeof(Texture2D), false);
                t.clumpRoundness = (Texture2D)EditorGUILayout.ObjectField("Clump Roundness", t.clumpRoundness, typeof(Texture2D), false);
                t.waveFrequency = (Texture2D)EditorGUILayout.ObjectField("Wave Frequency", t.waveFrequency, typeof(Texture2D), false);
                t.strand = (Texture2D)EditorGUILayout.ObjectField("Strand", t.strand, typeof(Texture2D), false);
                t.length = (Texture2D)EditorGUILayout.ObjectField("Lenght", t.length, typeof(Texture2D), false);
                t.weights = (Texture2D)EditorGUILayout.ObjectField("Weights", t.waviness, typeof(Texture2D), false);
            }

            GUILayout.Space(10);

            GUILayout.Label(
                "hair shader: " + t.m_hair_shader + "\n" +
                "hair asset: " + t.m_hair_asset + "\n" +
                "shader id: " + HandleToString(t.shader_id) + "\n" +
                "asset id: " + HandleToString(t.asset_id) + "\n" +
                "instance id: " + HandleToString(t.instance_id));
        }

        static string HandleToString(uint h)
        {
            return h == 0xFFFFFFFF ? "(null)" : h.ToString();
        }

        static string MakeRelativePath(string path)
        {
            Uri path_to_assets = new Uri(Application.streamingAssetsPath + "/");
            return path_to_assets.MakeRelativeUri(new Uri(path)).ToString();
        }
    }

}
