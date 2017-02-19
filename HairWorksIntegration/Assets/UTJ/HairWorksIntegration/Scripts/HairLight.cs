using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace UTJ
{

    [AddComponentMenu("UTJ/Hair Works Integration/Hair Light")]
    [RequireComponent(typeof(Light))]
    [ExecuteInEditMode]
    public class HairLight : MonoBehaviour
    {
        #region static
        static List<HairLight> s_instances;
        static hwi.LightData[] s_light_data;
        static IntPtr s_light_data_ptr;

        static public List<HairLight> GetInstances()
        {
            if (s_instances == null)
            {
                s_instances = new List<HairLight>();
            }
            return s_instances;
        }

        static public void AssignLightData()
        {
            if (s_light_data == null)
            {
                s_light_data = new hwi.LightData[hwi.LightData.MaxLights];
                s_light_data_ptr = Marshal.UnsafeAddrOfPinnedArrayElement(s_light_data, 0);
            }

            List<HairLight> instances = GetInstances();
            int n = Mathf.Min(instances.Count, hwi.LightData.MaxLights);
            for (int i = 0; i < instances.Count; i++)
            {
                if (i == n)
                    break;

                s_light_data[i] = instances[i].GetLightData();
            }
            hwi.hwSetLights(n, s_light_data_ptr);
        }
        #endregion

        struct ShadowParams
        {
           public Matrix4x4 worldToShadow0;
           public Matrix4x4 worldToShadow1;
           public Matrix4x4 worldToShadow2;
           public Matrix4x4 worldToShadow3;
           public Vector4 shadowSplitSpheres0;
           public Vector4 shadowSplitSpheres1;
           public Vector4 shadowSplitSpheres2;
           public Vector4 shadowSplitSpheres3;
           public Vector4 shadowSplitSqRadii;
        };

        public enum Type
        {
            Spot,
            Directional,
            Point,
        }

        hwi.LightData m_data;

        public bool m_copy_light_params = false;
        public LightType m_type = LightType.Directional;
        public float m_range = 10.0f;
        public Color m_color = Color.white;
        public float m_intensity = 1.0f;
        public int m_angle = 180;
        CommandBuffer m_cb;
        public bool debug;

        public RenderTexture m_ShadowmapCopy;
        Material m_CopyShadowParamsMaterial;
        ComputeBuffer m_ShadowParamsCB;
        CommandBuffer m_BufGrabShadowParams;

        public CommandBuffer GetCommandBuffer()
        {
            if (m_cb == null)
            {
                RenderTargetIdentifier shadowmap = BuiltinRenderTextureType.CurrentActive;
                m_ShadowmapCopy = new RenderTexture(1024, 1024, 0);

                m_cb = new CommandBuffer();
                m_cb.name = "Hair Shadow";

                m_cb.SetShadowSamplingMode(shadowmap, ShadowSamplingMode.RawDepth);

                m_cb.Blit(shadowmap, new RenderTargetIdentifier(m_ShadowmapCopy));

                GetComponent<Light>().AddCommandBuffer(LightEvent.AfterShadowMap, m_cb);
            }
            return m_cb;
        }

        public void GetShadowParams()
        {
            if (m_BufGrabShadowParams != null)
                m_BufGrabShadowParams.Clear();

            Graphics.SetRandomWriteTarget(1, m_ShadowParamsCB, true);
            m_CopyShadowParamsMaterial.SetBuffer("_ShadowParams", m_ShadowParamsCB);
            m_BufGrabShadowParams.DrawProcedural(Matrix4x4.identity, m_CopyShadowParamsMaterial, 0, MeshTopology.Points, 1);
            Graphics.ExecuteCommandBuffer(m_BufGrabShadowParams);
        }

        public IntPtr GetShadowMapPointer()
        {
            return m_ShadowmapCopy.GetNativeTexturePtr();
        }

        public hwi.LightData GetLightData()
        {
            var t = GetComponent<Transform>();
            m_data.type = (int)m_type;
            m_data.range = m_range;
            m_data.color = new Color(m_color.r * m_intensity, m_color.g * m_intensity, m_color.b * m_intensity, 0.0f);
            m_data.position = t.position;
            m_data.direction = t.forward;
            m_data.angle = m_angle;
            return m_data;

        }


        void OnEnable()
        {
            GetInstances().Add(this);

            if (GetInstances().Count > hwi.LightData.MaxLights)
            {
                Debug.LogWarning("Max HairLight is " + hwi.LightData.MaxLights + ". Current active HairLight is " + GetInstances().Count);
            }

            m_CopyShadowParamsMaterial = new Material(Shader.Find("Hidden/CopyShadowParams"));

            m_ShadowParamsCB = new ComputeBuffer(1, 336);
            m_BufGrabShadowParams = new CommandBuffer();
            m_BufGrabShadowParams.name = "Grab shadow params";

            GetCommandBuffer();
        }

        void OnDisable()
        {
            GetInstances().Remove(this);
            m_ShadowParamsCB.Release();
        }

        void Update()
        {
            if (m_copy_light_params)
            {
                var l = GetComponent<Light>();

                if (!l.enabled)
                {
                    m_intensity = 0;
                    return;
                }

                m_type = l.type;
                m_range = l.range;
                m_color = l.color;
                m_intensity = l.intensity;
                m_angle = (int)l.spotAngle;
            }

            //GetShadowParams();
        }

    }
}
