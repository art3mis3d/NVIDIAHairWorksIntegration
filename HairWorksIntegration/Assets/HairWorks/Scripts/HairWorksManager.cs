using System.Collections;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Rendering;
#endif
using UnityEngine;
using UnityEngine.Rendering;
using GameWorks;

[ExecuteInEditMode]
public class HairWorksManager : MonoBehaviour
{
    static CommandBuffer s_command_buffer;
    static HashSet<Camera> s_cameras = new HashSet<Camera>();

	public static bool HairWorksEnabled = true;

    void OnEnable()
    {  
        if (!hwi.hwLoadHairWorks())
        {
			#if UNITY_EDITOR
			EditorUtility.DisplayDialog(
                "Hair Works Integration",
                "Failed to load HairWorks (version " + hwi.hwGetSDKVersion() + ") dll. You need to get HairWorks SDK from NVIDIA website. Read document for more detail.",
                "OK");
			#endif

			HairWorksEnabled = false;
			return;
		}

		HairWorksEnabled = true;

		hwi.hwSetLogCallback();
    }

	// Use this for initialization
	void Start ()
    {
        // Change depth stencil to match reversed z-buffer in 5.5
        #if UNITY_5_5_OR_NEWER
        hwi.hwInitializeDepthStencil(true);
#endif

        Application.targetFrameRate = 30;
    }

    void LateUpdate()
    {
        hwi.hwStepSimulation(Time.deltaTime);
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


    public static bool DoesRenderToTexture(Camera cam)
    {
            #if UNITY_5_5_OR_NEWER
                    return true;
            #else

            if (UnityEngine.VR.VRSettings.enabled == true)
            {
                return true;
            }
            else
            {
                return IsDeferred(cam) || cam.targetTexture != null;
            }
            #endif
    }

    public static void Render(Camera CameraToAdd, HairInstance instance)
    {
        if (s_command_buffer == null)
        {
            s_command_buffer      = new CommandBuffer();
            s_command_buffer.name = "Hair";
            s_command_buffer.IssuePluginEvent( hwi.hwGetRenderEventFunc(), 0);
     
        }

		if (!HairWorksEnabled)
			return;

		CameraEvent s_timing;

		if (IsDeferred(CameraToAdd))
		{
			s_timing = CameraEvent.BeforeImageEffects;
		}
		else
		{
			s_timing = CameraEvent.AfterImageEffectsOpaque;
		}

		if ( CameraToAdd != null )
        {
            Matrix4x4 V = CameraToAdd.worldToCameraMatrix;
            Matrix4x4 P = GL.GetGPUProjectionMatrix(CameraToAdd.projectionMatrix, DoesRenderToTexture(CameraToAdd));
            float fov   = CameraToAdd.fieldOfView;
            hwi.hwSetViewProjection(ref V, ref P, fov);
            HairLight.AssignLightData();

            if (!s_cameras.Contains(CameraToAdd))
            {
				CameraToAdd.AddCommandBuffer(s_timing, s_command_buffer);
                s_cameras.Add(CameraToAdd);
            }
        }

        hwi.hwBeginScene();

		instance.Render();

		hwi.hwEndScene();
    }

    static public void ClearCommandBuffer()
    {
        foreach (var c in s_cameras)
        {
            if (c != null)
            {
				if (IsDeferred(c))
				{
					c.RemoveCommandBuffer(CameraEvent.BeforeImageEffects, s_command_buffer);
				}
				else
				{
					c.RemoveCommandBuffer(CameraEvent.AfterImageEffectsOpaque, s_command_buffer);
				}
			}
        }
        s_cameras.Clear();
    }
}
