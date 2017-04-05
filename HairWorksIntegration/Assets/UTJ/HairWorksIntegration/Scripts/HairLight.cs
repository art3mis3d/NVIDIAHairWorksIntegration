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
    [Serializable]
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

        public enum ShadowResolution
        {
            x512,
            x1024,
            x2048,
            x4096
        }

        hwi.LightData m_data;

        public bool m_copy_light_params = false;
        public LightType m_type = LightType.Directional;
        public float m_range = 10.0f;
        public Color m_color = Color.white;
        public float m_intensity = 1.0f;
        public int m_angle = 180;
        public bool enableShadow;
        public ShadowResolution shadowResolution = ShadowResolution.x1024;
        CommandBuffer m_cb = null;

        RenderTexture m_ShadowmapCopy;
        Material m_CopyShadowParamsMaterial;
        ComputeBuffer m_ShadowParamsCB;
        CommandBuffer m_BufGrabShadowParams;

        IntPtr shadowMapPointer = IntPtr.Zero;
        IntPtr shadowParamsPointer = IntPtr.Zero;

        public CommandBuffer GetCommandBuffer()
        {
            if (m_cb == null)
            {
                RenderTargetIdentifier shadowmap = BuiltinRenderTextureType.CurrentActive;
              
                CreateShadowTexture();

                m_cb = new CommandBuffer();
                m_cb.name = "Hair Shadow";

                m_cb.SetShadowSamplingMode(shadowmap, ShadowSamplingMode.RawDepth);

                m_cb.Blit(shadowmap, new RenderTargetIdentifier(m_ShadowmapCopy));

                GetComponent<Light>().AddCommandBuffer(LightEvent.AfterShadowMap, m_cb);
            }
            return m_cb;
        }

        public void CreateShadowTexture()
        {
            switch(shadowResolution)
            {
                case ShadowResolution.x512: m_ShadowmapCopy = new RenderTexture(512, 512, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear);
                    break;
                case ShadowResolution.x1024: m_ShadowmapCopy = new RenderTexture(1024, 1024, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear);
                    break;
                case ShadowResolution.x2048: m_ShadowmapCopy = new RenderTexture(2048, 2048, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear);
                    break;
                case ShadowResolution.x4096: m_ShadowmapCopy = new RenderTexture(4096, 4096, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear);
                    break;
                default: break;
            }

            m_ShadowmapCopy.Create();
        }

        public void UpdateShadowParams()
        {
            if (m_BufGrabShadowParams == null)
            {
                m_BufGrabShadowParams = new CommandBuffer();
                m_BufGrabShadowParams.name = "Grab shadow params";
            }

            m_BufGrabShadowParams.Clear();
            Graphics.SetRandomWriteTarget(2, m_ShadowParamsCB, true);
           
            m_BufGrabShadowParams.DrawProcedural(Matrix4x4.identity, m_CopyShadowParamsMaterial, 0, MeshTopology.Points, 1);
            Graphics.ExecuteCommandBuffer(m_BufGrabShadowParams);
            
            Graphics.ClearRandomWriteTargets();
        }

        public IntPtr GetShadowMapPointer()
        {
            if (m_ShadowmapCopy == null || !m_ShadowmapCopy.IsCreated())
            {
                shadowMapPointer = IntPtr.Zero;
                return IntPtr.Zero;
            }

            if (shadowMapPointer == IntPtr.Zero)
                shadowMapPointer = m_ShadowmapCopy.GetNativeTexturePtr();

            return shadowMapPointer;
        }

        public IntPtr GetShadowParamsPointer()
        {
            if (m_ShadowParamsCB == null)
                return IntPtr.Zero;

            if (shadowParamsPointer == IntPtr.Zero)
                shadowParamsPointer = m_ShadowParamsCB.GetNativeBufferPtr();

            return shadowParamsPointer;
        }

        public hwi.LightData GetLightData()
        {
            var t = GetComponent<Transform>();
            m_data.type = (int)m_type;
            m_data.range = m_range;
            m_data.color = new Color(m_color.r * m_intensity, m_color.g * m_intensity, m_color.b * m_intensity, 0.0f);
            m_data.position = t.position;
            m_data.direction = t.forward;

            if (m_type == LightType.Directional)
            {
                m_data.direction = -m_data.direction;
            }

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

            Shader shadowCopyShader = Shader.Find("Hidden/CopyShadowParams");

            m_CopyShadowParamsMaterial = new Material(shadowCopyShader);

            m_ShadowParamsCB = new ComputeBuffer(1, 368, ComputeBufferType.Default);
            m_BufGrabShadowParams = new CommandBuffer();
            m_BufGrabShadowParams.name = "Grab shadow params";

            GetCommandBuffer();

            Shader.EnableKeyword("SHADOWS_SPLIT_SPHERES");
        }

        void OnDisable()
        {
            GetInstances().Remove(this);
            hwi.hwSetShadowTexture(IntPtr.Zero);
            hwi.hwSetShadowParams(IntPtr.Zero);
            shadowMapPointer = IntPtr.Zero;
            shadowParamsPointer = IntPtr.Zero;
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

           if (!enableShadow)
            {
                shadowMapPointer = IntPtr.Zero;
                shadowParamsPointer = IntPtr.Zero;
                hwi.hwSetShadowTexture(IntPtr.Zero);
                hwi.hwSetShadowParams(IntPtr.Zero);
                return;
            }

            UpdateShadowParams();

            if (shadowParamsPointer == IntPtr.Zero)
            {
                hwi.hwSetShadowParams(GetShadowParamsPointer());
                
            }

            if (shadowMapPointer == IntPtr.Zero)
            {
                hwi.hwSetShadowTexture(GetShadowMapPointer());
               
            }
        }

    }
}
