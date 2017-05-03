using System.Collections;
using System.Collections.Generic;
using UnityEngine;
[RequireComponent(typeof(Camera))]
public class DepthDebug : MonoBehaviour {
    public Shader shader;

    private Material mat;
	// Use this for initialization
	void Start ()
    {
        Camera.main.depthTextureMode = DepthTextureMode.Depth;
        mat = new Material(shader);	
	}
	
	// Update is called once per frame
	void Update () {
		
	}

    void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        Graphics.Blit(source, destination, mat);
    }
}
