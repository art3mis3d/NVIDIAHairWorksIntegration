using System.Collections;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Rendering;
#endif
using UnityEngine;
using UnityEngine.Rendering;
using GameWorks;

[ExecuteAlways]
public class HairWorksManager : MonoBehaviour
{
    static CommandBuffer    CmdBuffer_HairRender;
 
    static HashSet<Camera> s_cameras = new HashSet<Camera>();

    public static bool HairWorksEnabled = true;

    void OnEnable()
    {  
        if (!Hwi.hwLoadHairWorks())
        {
            #if UNITY_EDITOR
            EditorUtility.DisplayDialog(
                "Hair Works Integration",
                "Failed to load HairWorks (version " + Hwi.hwGetSDKVersion() + ") dll. You need to get HairWorks SDK from NVIDIA website. Read document for more detail.",
                "OK");
            #endif

            HairWorksEnabled = false;
            return;
        }

        HairWorksEnabled = true;

        Hwi.hwSetLogCallback();
    }

    // Use this for initialization
    void Start ()
    {
        // Change depth stencil to match reversed z-buffer in 5.5
        Hwi.hwInitializeDepthStencil(true);
    }

    void LateUpdate()
    {
        Hwi.hwStepSimulation(Time.deltaTime);
    }

    void OnDisable()
    {
        HairWorksEnabled = false;
    }

    void OnApplicationQuit()
    {
        ClearCommandBuffer();
    }

    public static bool IsDeferred(Camera cam)
    {
        if (cam.renderingPath == RenderingPath.DeferredShading)
            return true;

#if UNITY_EDITOR
        if (cam.renderingPath == RenderingPath.UsePlayerSettings && EditorGraphicsSettings.GetTierSettings(BuildTargetGroup.Standalone, GraphicsTier.Tier3).renderingPath == RenderingPath.DeferredShading)
            return true;
#endif
        return false;
    }


    public static bool DoesRenderToTexture(Camera cam)
    {
        return true;
    }

    public static void Render( Camera CameraToAdd, HairInstance instance )
    {
        if (CmdBuffer_HairRender == null)
        {
            CmdBuffer_HairRender      = new CommandBuffer();
            CmdBuffer_HairRender.name = "Hair";
            CmdBuffer_HairRender.IssuePluginEvent( Hwi.hwGetRenderEventFunc(), 0); 
        }

        if (!HairWorksEnabled)
            return;

        if ( CameraToAdd != null )
        {
            CameraEvent s_timing;

            if (IsDeferred(CameraToAdd))
            {
                s_timing = CameraEvent.BeforeImageEffects;
            }
            else
            {
                s_timing = CameraEvent.AfterImageEffectsOpaque;
            }

            Matrix4x4 V = CameraToAdd.worldToCameraMatrix;
            Matrix4x4 P = GL.GetGPUProjectionMatrix(CameraToAdd.projectionMatrix, DoesRenderToTexture(CameraToAdd));
            float fov   = CameraToAdd.fieldOfView;
            Hwi.hwSetViewProjection(ref V, ref P, fov);
            HairLight.AssignLightData();

            if (!s_cameras.Contains(CameraToAdd))
            { 
                CameraToAdd.AddCommandBuffer(s_timing, CmdBuffer_HairRender);
                s_cameras.Add(CameraToAdd);
            }
        }

        Hwi.hwBeginScene();

        instance.Render();

        Hwi.hwEndScene();
    }

    static public void ClearCommandBuffer()
    {
        foreach (var c in s_cameras)
        {
            if (c != null)
            {
                if (IsDeferred(c))
                {
                    c.RemoveCommandBuffer(CameraEvent.BeforeImageEffects, CmdBuffer_HairRender);
                }
                else
                {
                    c.RemoveCommandBuffer(CameraEvent.AfterImageEffectsOpaque, CmdBuffer_HairRender);
                }
            }
        }
        s_cameras.Clear();
    }
}
