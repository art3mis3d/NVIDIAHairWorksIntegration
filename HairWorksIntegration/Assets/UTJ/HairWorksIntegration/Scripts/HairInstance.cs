using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Rendering;
#endif


namespace UTJ
{
    [AddComponentMenu("UTJ/Hair Works Integration/Hair Instance")]
    [ExecuteInEditMode]
    [Serializable]
    public class HairInstance : MonoBehaviour
    {
        #region static
        static List<HairInstance> s_instances;
        static int s_nth_LateUpdate;
        static int s_nth_OnWillRenderObject;

        static CommandBuffer s_command_buffer;
        static HashSet<Camera> s_cameras = new HashSet<Camera>();

        static public List<HairInstance> GetInstances()
        {
            if (s_instances == null)
            {
                s_instances = new List<HairInstance>();
            }
            return s_instances;
        }
        #endregion

        static CameraEvent s_timing = CameraEvent.BeforeImageEffects;

        public string m_hair_asset;
        public string m_hair_shader = "UTJ/HairWorksIntegration/DefaultHairShader.cso";
        public Transform m_root_bone;
        public bool m_invert_bone_x = true;
        public hwi.Descriptor m_params = hwi.Descriptor.default_value;
        hwi.HShader m_hshader = hwi.HShader.NullHandle;
        hwi.HAsset m_hasset = hwi.HAsset.NullHandle;
        hwi.HInstance m_hinstance = hwi.HInstance.NullHandle;

        public Transform[] m_bones;
        Matrix4x4[] m_inv_bindpose;
        Matrix4x4[] m_skinning_matrices = null;
        IntPtr m_skinning_matrices_ptr = IntPtr.Zero;
        Matrix4x4 m_conversion_matrix;

        public Mesh m_probe_mesh;


        public uint shader_id { get { return m_hshader; } }
        public uint asset_id { get { return m_hasset; } }
        public uint instance_id { get { return m_hinstance; } }

        [HideInInspector]
        public bool useLightProbes = true;

        [HideInInspector]
        public bool useReflectionProbes = true;

        [HideInInspector]
        public float lightProbeIntensity = 1;

        [HideInInspector]
        public float reflectionProbeIntensity = 1;

        [HideInInspector]
        public float reflectionProbeSpecularity = 1;

        [HideInInspector]
        public Texture2D root;
        [HideInInspector]
        public Texture2D tip;
        [HideInInspector]
        public Texture2D specular;
        [HideInInspector]
        public Texture2D clump;
        [HideInInspector]
        public Texture2D density;
        [HideInInspector]
        public Texture2D stiffness;
        [HideInInspector]
        public Texture2D waviness;
        [HideInInspector]
        public Texture2D width;
        [HideInInspector]
        public Texture2D rootStiffness;
        [HideInInspector]
        public Texture2D clumpRoundness;
        [HideInInspector]
        public Texture2D waveFrequency;
        [HideInInspector]
        public Texture2D strand;
        [HideInInspector]
        public Texture2D length;
        [HideInInspector]
        public Texture2D weights;

        private Dictionary<hwi.TextureType, Texture2D> textureDictionary = new Dictionary<hwi.TextureType, Texture2D>();
        private Dictionary<ReflectionProbe, IntPtr> probePointers = new Dictionary<ReflectionProbe, IntPtr>();
        private List<Texture2D> textures = new List<Texture2D>();
        private Vector4[] avCoeff = new Vector4[7];
        private ReflectionProbe[] reflectionProbes;
        private List<ReflectionProbe> probeInstances;
        private float probeBlendAmount;

        // The UpdateBones() method is very expensive. Optimized by updating fixed amount per second. Not per frame. 
        private float accumTime = 0;
        public static float boneUpdatesPerSecond = 60;
        float stepsize;
        bool updateBones;

        void RepaintWindow()
        {
#if UNITY_EDITOR
            var assembly = typeof(UnityEditor.EditorWindow).Assembly;
            var type = assembly.GetType("UnityEditor.GameView");
            var gameview = EditorWindow.GetWindow(type);
            gameview.Repaint();
#endif
        }

        public void LoadHairShader(string path_to_cso)
        {
            // release existing shader
            if (m_hshader)
            {
                hwi.hwShaderRelease(m_hshader);
                m_hshader = hwi.HShader.NullHandle;
            }

            // load shader
            if (m_hshader = hwi.hwShaderLoadFromFile(Application.streamingAssetsPath + "/" + path_to_cso))
            {
                m_hair_shader = path_to_cso;
            }
#if UNITY_EDITOR
            RepaintWindow();
#endif
        }

        public void ReloadHairShader()
        {
            hwi.hwShaderReload(m_hshader);
            RepaintWindow();
        }

        public void LoadHairAsset(string path_to_apx, bool reset_params = true)
        {
            // release existing instance & asset
            if (m_hinstance)
            {
                hwi.hwInstanceRelease(m_hinstance);
                m_hinstance = hwi.HInstance.NullHandle;
            }
            if (m_hasset)
            {
                hwi.hwAssetRelease(m_hasset);
                m_hasset = hwi.HAsset.NullHandle;
            }

            // load & create instance
            if (m_hasset = hwi.hwAssetLoadFromFile(Application.streamingAssetsPath + "/" + path_to_apx))
            {
                m_hair_asset = path_to_apx;
                m_hinstance = hwi.hwInstanceCreate(m_hasset);
                if (reset_params)
                {
                    hwi.hwAssetGetDefaultDescriptor(m_hasset, ref m_params);
                }
            }

            // update bone structure
            if (reset_params)
            {
                m_bones = null;
                m_skinning_matrices = null;
                m_skinning_matrices_ptr = IntPtr.Zero;
            }
            UpdateBones();

#if UNITY_EDITOR
            Update();
            RepaintWindow();
#endif
        }


        public void ReloadHairAsset()
        {
            hwi.hwAssetReload(m_hasset);
            hwi.hwAssetGetDefaultDescriptor(m_hasset, ref m_params);
            hwi.hwInstanceSetDescriptor(m_hinstance, ref m_params);
            RepaintWindow();
        }

        public void AssignTexture(hwi.TextureType type, Texture2D tex)
        {
            if (tex == null)
            {
                hwi.hwInstanceSetTexture(m_hinstance, type, IntPtr.Zero);
                return;
            }

            hwi.hwInstanceSetTexture(m_hinstance, type, tex.GetNativeTexturePtr());
        }

        public void AssignAllTextures()
        {
            SetTextureDictionary();

            hwi.TextureType[] types = (hwi.TextureType[])Enum.GetValues(typeof(hwi.TextureType));

            for (int i = 0; i < textures.Count; i++)
            {
                    AssignTexture(types[i], textureDictionary[types[i]]);
            }
        }

        public void UpdateBones()
        {
            int num_bones = hwi.hwAssetGetNumBones(m_hasset);

            if (num_bones == 0)
                return;

            if (m_bones == null || m_bones.Length != num_bones)
            {
                m_bones = new Transform[num_bones];

                if (m_root_bone == null)
                {
                    m_root_bone = GetComponent<Transform>();
                }

                var children = m_root_bone.GetComponentsInChildren<Transform>();
                for (int i = 0; i < num_bones; ++i)
                {
                    string name = hwi.hwAssetGetBoneNameString(m_hasset, i);
                    m_bones[i] = Array.Find(children, (a) => { return a.name == name; });
                    if (m_bones[i] == null) { m_bones[i] = m_root_bone; }
                }

            }

            if (m_skinning_matrices == null)
            {
                m_inv_bindpose = new Matrix4x4[num_bones];
                m_skinning_matrices = new Matrix4x4[num_bones];
                m_skinning_matrices_ptr = Marshal.UnsafeAddrOfPinnedArrayElement(m_skinning_matrices, 0);
                for (int i = 0; i < num_bones; ++i)
                {
                    m_inv_bindpose[i] = Matrix4x4.identity;
                    m_skinning_matrices[i] = Matrix4x4.identity;
                }

                m_conversion_matrix = Matrix4x4.identity;
                if (m_invert_bone_x)
                {
                    m_conversion_matrix *= Matrix4x4.Scale(new Vector3(-1.0f, 1.0f, 1.0f));
                }

                // m_conversion_matrix is constant, optimize by premultiplying with m_inv_bindpose
                for (int i = 0; i < num_bones; ++i)
                {
                    hwi.hwAssetGetBindPose(m_hasset, i, ref m_inv_bindpose[i]);
                    m_inv_bindpose[i] = m_conversion_matrix * m_inv_bindpose[i].inverse;
                }
            }

            for (int i = 0; i < num_bones; ++i)
            {
                var t = m_bones[i];
                if (t != null)
                {
                    m_skinning_matrices[i] = t.localToWorldMatrix *  m_inv_bindpose[i];
                }
            }
        }

        static public void Swap<T>(ref T a, ref T b)
        {
            T tmp = a;
            a = b;
            b = tmp;
        }

        void SetTextureList()
        {
            textures = new List<Texture2D>();
            textures.Add(density);
            textures.Add(root);
            textures.Add(tip);
            textures.Add(width);
            textures.Add(stiffness);
            textures.Add(rootStiffness);
            textures.Add(clump);
            textures.Add(clumpRoundness);
            textures.Add(waviness);
            textures.Add(waveFrequency);
            textures.Add(strand);
            textures.Add(length);
            textures.Add(specular);
            textures.Add(weights);

        }

        void SetTextureDictionary()
        {
            SetTextureList();

            textureDictionary = new Dictionary<hwi.TextureType, Texture2D>();

            hwi.TextureType[] types = (hwi.TextureType[])Enum.GetValues(typeof(hwi.TextureType));

            for (int i = 0; i < textures.Count; i++)
            {
                textureDictionary.Add(types[i], textures[i]);
            }
        }

        void UpdateLightProbes()
        {
            if (LightmapSettings.lightProbes.count > 0 && useLightProbes)
            {
                SphericalHarmonicsL2 aSample;    // SH sample consists of 27 floats   
                LightProbes.GetInterpolatedProbe(this.transform.position, this.GetComponent<MeshRenderer>(), out aSample);

                for (int iC = 0; iC < 3; iC++)
                {
                    avCoeff[iC] = new Vector4((float)aSample[iC, 3], aSample[iC, 1], aSample[iC, 2], aSample[iC, 0] - aSample[iC, 6]);
                }
                for (int iC = 0; iC < 3; iC++)
                {
                    avCoeff[iC + 3].x = aSample[iC, 4];
                    avCoeff[iC + 3].y = aSample[iC, 5];
                    avCoeff[iC + 3].z = 3.0f * aSample[iC, 6];
                    avCoeff[iC + 3].w = aSample[iC, 7];
                }
                avCoeff[6].x = aSample[0, 8];
                avCoeff[6].y = aSample[1, 8];
                avCoeff[6].y = aSample[2, 8];
                avCoeff[6].w = 1.0f;

            }
            else
            {
                avCoeff[0] = Vector4.zero;
                avCoeff[1] = Vector4.zero;
                avCoeff[2] = Vector4.zero;
                avCoeff[3] = Vector4.zero;
                avCoeff[4] = Vector4.zero;
                avCoeff[5] = Vector4.zero;
                avCoeff[6] = Vector4.zero;
            }
    }

        Texture GetProbeTexture(ReflectionProbe probe)
        {
            if (probe.customBakedTexture != null)
                return probe.customBakedTexture;

            if (probe.bakedTexture != null)
                return probe.bakedTexture;

            return null;
        }

        void GetReflectionProbeData()
        {
            // Copy a List of all reflection probes in scene
            probeInstances.Clear();

            probeBlendAmount = 0;
            float dist1;
            float dist2;

            // remove inactive probes
            for (int i = 0; i < reflectionProbes.Length; i++)
            {
                if (reflectionProbes[i] != null && reflectionProbes[i].enabled && reflectionProbes[i].gameObject.activeInHierarchy)
                {
                    probeInstances.Add(reflectionProbes[i]);
                }
            }

            // If no active reflection probes in scene then return
            if (probeInstances.Count <= 0 || !useReflectionProbes)
            {
                hwi.hwSetReflectionProbe(IntPtr.Zero, IntPtr.Zero);
                return;
            }

            if (probeInstances.Count == 1)
            {
                if (GetProbeTexture(probeInstances[0]) == null)
                    return;
                
                    if (!probePointers.ContainsKey(probeInstances[0]))
                        probePointers.Add(probeInstances[0], GetProbeTexture(probeInstances[0]).GetNativeTexturePtr());

                    hwi.hwSetReflectionProbe(probePointers[probeInstances[0]], probePointers[probeInstances[0]]);
                

                return;
            }

            if (probeInstances.Count == 2)
            {
                if (GetProbeTexture(probeInstances[0]) == null || GetProbeTexture(probeInstances[1]) == null)
                    return;

                    if (!probePointers.ContainsKey(probeInstances[0]))
                        probePointers.Add(probeInstances[0], GetProbeTexture(probeInstances[0]).GetNativeTexturePtr());

                if (!probePointers.ContainsKey(probeInstances[1]))
                    probePointers.Add(probeInstances[1], GetProbeTexture(probeInstances[1]).GetNativeTexturePtr());

                dist1 = Vector3.Distance(transform.position, probeInstances[0].transform.position);
                dist2 = Vector3.Distance(transform.position, probeInstances[1].transform.position);

                if (dist2 > dist1)
                {
                    hwi.hwSetReflectionProbe(probePointers[probeInstances[0]], probePointers[probeInstances[1]]);
                    probeBlendAmount = 0.5f * (1.0f / (dist2 / (dist1 + 0.01f)));
                }
                else
                {
                    hwi.hwSetReflectionProbe(probePointers[probeInstances[1]], probePointers[probeInstances[0]]);
                    probeBlendAmount = 0.5f * (1.0f / (dist1 / (dist2 + 0.01f)));
                }

                return;
            }

            // send closest two probes or two probes with highest importance
           
                float closestDistance1 = Mathf.Infinity;
                float closestDistance2 = Mathf.Infinity;

                float mostImportant1 = -Mathf.Infinity;
                float mostImportant2 = -Mathf.Infinity;

                int distanceIdx1 = -1;
                int distanceIdx2 = -1;

                int importanceIdx1 = -1;
                int importanceIdx2 = -1;

                // Get distances to hair and importance
                for (int i = 0; i < probeInstances.Count; i++)
                {
                    float distance = Vector3.Distance(this.transform.position, probeInstances[i].transform.position);
                    float importance = probeInstances[i].importance;

                    if (distance < closestDistance1)
                    {
                        closestDistance1 = distance;
                        distanceIdx1 = i;
                    }
                    else
                    {
                        if (distance < closestDistance2)
                        {
                            closestDistance2 = distance;
                            distanceIdx2 = i;
                        }
                    }

                    if (mostImportant1 < importance)
                    {
                        mostImportant1 = importance;
                        importanceIdx1 = i;
                    }
                    else
                    {
                        if (mostImportant2 < importance)
                        {
                            mostImportant2 = importance;
                            importanceIdx2 = i;
                        }
                    }
                }

                // get 2 closest
                ReflectionProbe probe1 = probeInstances[distanceIdx1];

                ReflectionProbe probe2 = probeInstances[distanceIdx2];

                if (GetProbeTexture(probe1) == null || GetProbeTexture(probe2) == null)
                        return;

            // if there are more important probes then switch to them
            if (probe1.importance < probeInstances[importanceIdx1].importance)
            {
                probe1 = probeInstances[importanceIdx1];
            }

            if (probe2.importance < probeInstances[importanceIdx2].importance)
            {
                probe2 = probeInstances[importanceIdx2];
            }

                if (GetProbeTexture(probe1) == null || GetProbeTexture(probe2) == null)
                    return;

                if (!probePointers.ContainsKey(probe1))
                    probePointers.Add(probe1, GetProbeTexture(probe1).GetNativeTexturePtr());

                if (!probePointers.ContainsKey(probe2))
                    probePointers.Add(probe2, GetProbeTexture(probe2).GetNativeTexturePtr());

            dist1 = Vector3.Distance(transform.position, probe1.transform.position);
            dist2 = Vector3.Distance(transform.position, probe2.transform.position);

            //send probes
            if (dist2 > dist1)
            {
                hwi.hwSetReflectionProbe(probePointers[probe1], probePointers[probe2]);
                probeBlendAmount = 0.5f * (1.0f / (dist2 / (dist1 + 0.01f)));
            }
            else
            {
                hwi.hwSetReflectionProbe(probePointers[probe2], probePointers[probe1]);
                probeBlendAmount = 0.5f * (1.0f / (dist1 / (dist2 + 0.01f)));
            }
        }


#if UNITY_EDITOR
        void Reset()
        {
            var skinned_mesh_renderer = GetComponent<SkinnedMeshRenderer>();
            m_root_bone = skinned_mesh_renderer != null ? skinned_mesh_renderer.rootBone : GetComponent<Transform>();

            var renderer = GetComponent<Renderer>();
            if (renderer == null)
            {
                m_probe_mesh = new Mesh();
                m_probe_mesh.name = "Probe";
                m_probe_mesh.vertices = new Vector3[1] { Vector3.zero };
                m_probe_mesh.SetIndices(new int[1] { 0 }, MeshTopology.Points, 0);

                var mesh_filter = gameObject.AddComponent<MeshFilter>();
                mesh_filter.sharedMesh = m_probe_mesh;
                renderer = gameObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = new Material[0] { };
            }
        }
#endif

        void OnApplicationQuit()
        {
            ClearCommandBuffer();
        }

        void Awake()
        {
#if UNITY_EDITOR
            if(!hwi.hwLoadHairWorks())
            {
                EditorUtility.DisplayDialog(
                    "Hair Works Integration",
                    "Failed to load HairWorks (version " + hwi.hwGetSDKVersion() + ") dll. You need to get HairWorks SDK from NVIDIA website. Read document for more detail.",
                    "OK");
            }
#endif
            hwi.hwSetLogCallback();
        }

        void OnDestroy()
        {
            hwi.hwInstanceRelease(m_hinstance);
            hwi.hwAssetRelease(m_hasset);
        }

        void OnEnable()
        {
            GetInstances().Add(this);
            m_params.m_enable = true;
            reflectionProbes = FindObjectsOfType<ReflectionProbe>();
            probeInstances = new List<ReflectionProbe>(reflectionProbes.Length);
            stepsize = 1.0f / boneUpdatesPerSecond;
        }

        void OnDisable()
        {
            m_params.m_enable = false;
            GetInstances().Remove(this);
        }

        void Start()
        {
            LoadHairShader(m_hair_shader);
            LoadHairAsset(m_hair_asset, false);
#if UNITY_5_5_OR_NEWER
            hwi.hwInitializeDepthStencil(true);
#endif
            AssignAllTextures();
        }

        void Update()
        {
            if (!m_hasset) { return; }

            if(accumTime + Time.deltaTime > stepsize)
            {
                accumTime = 0;
                updateBones = true;
            }
            else
            {
                accumTime += Time.deltaTime;
            }
           

            if (m_probe_mesh != null)
            {
                var bmin = Vector3.zero;
                var bmax = Vector3.zero;
                hwi.hwInstanceGetBounds(m_hinstance, ref bmin, ref bmax);

                var center = (bmin + bmax) * 0.5f;
                var size = bmax - center;
                m_probe_mesh.bounds = new Bounds(center, size);
            }

            s_nth_LateUpdate = 0;
        }

       void LateUpdate()
        {
            if (s_nth_LateUpdate++ == 0)
            {
                hwi.hwStepSimulation(Time.deltaTime);
            }
        }

       void OnWillRenderObject()
        {
            if (updateBones)
            {
                UpdateBones();
                updateBones = false;
            }

            hwi.hwInstanceSetDescriptor(m_hinstance, ref m_params);

            if (m_skinning_matrices != null)
             hwi.hwInstanceUpdateSkinningMatrices(m_hinstance, m_skinning_matrices.Length, m_skinning_matrices_ptr);

            GetReflectionProbeData();
            UpdateLightProbes();
            hwi.hwSetSphericalHarmonics(ref avCoeff[0], ref avCoeff[1], ref avCoeff[2], ref avCoeff[3], ref avCoeff[4], ref avCoeff[5], ref avCoeff[6]);

            Vector4 giparams = new Vector4(lightProbeIntensity, reflectionProbeIntensity, reflectionProbeSpecularity, probeBlendAmount);
            hwi.hwSetGIParameters(ref giparams);

            if (s_nth_OnWillRenderObject++ == 0)
            {
                BeginRender();
                
                for (int i = 0; i < GetInstances().Count; i++)
                {
                    GetInstances()[i].Render();
                }
                EndRender();
            }
        }

        void OnRenderObject()
        {
            s_nth_OnWillRenderObject = 0;
        }

        static public bool IsDeferred(Camera cam)
        {
            if (cam.renderingPath == RenderingPath.DeferredShading
#if UNITY_EDITOR
#if !UNITY_5_5_OR_NEWER
            || (cam.renderingPath == RenderingPath.UsePlayerSettings && PlayerSettings.renderingPath == RenderingPath.DeferredShading)
#else
            || (cam.renderingPath == RenderingPath.UsePlayerSettings && EditorGraphicsSettings.GetTierSettings(BuildTargetGroup.Standalone, GraphicsTier.Tier3).renderingPath == RenderingPath.DeferredShading)
#endif

#endif
            )
            {
                return true;
            }
            return false;
        }

        static public bool DoesRenderToTexture(Camera cam)
        {
            return IsDeferred(cam) || cam.targetTexture != null;
        }


        static public void ClearCommandBuffer()
        {
            foreach (var c in s_cameras)
            {
                if (c != null)
                {
                    c.RemoveCommandBuffer(s_timing, s_command_buffer);
                }
            }
            s_cameras.Clear();
        }

        static void BeginRender()
        {
            if (s_command_buffer == null)
            {
                s_command_buffer = new CommandBuffer();
                s_command_buffer.name = "Hair";
                s_command_buffer.IssuePluginEvent(hwi.hwGetRenderEventFunc(), 0);
            }

            Camera cam = Camera.current;

            if (cam != null)
            {
                Matrix4x4 V = cam.worldToCameraMatrix;
                //DoesRenderToTexture does not account for image effects or msaa. Force true for now. False only useful in forward without image effects or msaa, unlikely use case.
                Matrix4x4 P = GL.GetGPUProjectionMatrix(cam.projectionMatrix, /*DoesRenderToTexture(cam)*/ true);
                float fov = cam.fieldOfView;
                hwi.hwSetViewProjection(ref V, ref P, fov);
                HairLight.AssignLightData();

                if (!s_cameras.Contains(cam))
                {
                    cam.AddCommandBuffer(s_timing, s_command_buffer);
                    s_cameras.Add(cam);
                }
            }

            hwi.hwBeginScene();
        }

        void Render()
        {
            if (!m_hasset) { return; }

            hwi.hwSetShader(m_hshader);
            hwi.hwRender(m_hinstance);
        }

        static void EndRender()
        {
            hwi.hwEndScene();
        }

    }

}
