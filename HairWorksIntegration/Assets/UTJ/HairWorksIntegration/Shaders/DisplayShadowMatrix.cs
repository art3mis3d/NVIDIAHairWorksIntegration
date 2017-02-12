using UnityEngine;
using System.Collections;

public class DisplayShadowMatrix : MonoBehaviour {

    Material mat;
    public RenderTexture tex;
	// Use this for initialization
	void OnEnable()
    {
        mat = new Material(Shader.Find("Hidden/MatrixPacker"));
        tex = new RenderTexture(4, 16, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear);
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.Create();
	}
	
	// Update is called once per frame
	void LateUpdate () {
        Blit(tex, mat, 0);
    }

    void OnDisable()
    {
        tex.Release();
    }

    static public void Blit(RenderTexture des, Material mat, int pass = 0)
    {
        RenderTexture oldRT = RenderTexture.active;

        Graphics.SetRenderTarget(des);

        GL.Clear(true, true, Color.clear);

        GL.PushMatrix();
        GL.LoadOrtho();

        mat.SetPass(pass);

        GL.Begin(GL.QUADS);
        GL.TexCoord2(0.0f, 0.0f); GL.Vertex3(0.0f, 0.0f, 0.1f);
        GL.TexCoord2(1.0f, 0.0f); GL.Vertex3(1.0f, 0.0f, 0.1f);
        GL.TexCoord2(1.0f, 1.0f); GL.Vertex3(1.0f, 1.0f, 0.1f);
        GL.TexCoord2(0.0f, 1.0f); GL.Vertex3(0.0f, 1.0f, 0.1f);
        GL.End();

        GL.PopMatrix();

        RenderTexture.active = oldRT;
    }
}
