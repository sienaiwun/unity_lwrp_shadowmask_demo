using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.LWRP;

[ImageEffectAllowedInSceneView]
public class PlanerReflections : MonoBehaviour, IBeforeCameraRender
{

    const string k_CopyDeptyTextureTag = "Render Camera";
    
    [System.Serializable]
    public enum ResolutionMulltiplier
    {
        Full,
        Half,
        Third,
        Quarter
    }

    [System.Serializable]
    public class PlanarReflectionSettings
    {
        public ResolutionMulltiplier m_ResolutionMultiplier = ResolutionMulltiplier.Third;
        public float m_ClipPlaneOffset = 0.07f;
        public LayerMask m_ReflectLayers = -1;
    }


    [SerializeField]
    public PlanarReflectionSettings m_settings = new PlanarReflectionSettings();

    public GameObject target;
    public Material m_matCopyDepth = null;
    private Camera m_ReflectionCamera;
    private int m_TextureSize = 256;
    private RenderTexture m_ReflectionTexture = null;
    private RenderTexture m_ReflectionDepthTexture = null;

    private Vector4 reflectionPlane;
    private int m_OldReflectionTextureSize;

    // Cleanup all the objects we possibly have created
    void OnDisable()
    {
        if (m_ReflectionCamera)
        {
            m_ReflectionCamera.targetTexture = null;
            DestroyImmediate(m_ReflectionCamera.gameObject);
        }
        if (m_ReflectionTexture)
        {
            DestroyImmediate(m_ReflectionTexture);
        }
        if(m_ReflectionDepthTexture)
        {
            DestroyImmediate(m_ReflectionDepthTexture);
        }
    }

    private void UpdateCamera(Camera src, Camera dest)
    {
        if (dest == null)
            return;
        // set camera to clear the same way as current camera
        dest.clearFlags = src.clearFlags;
        dest.backgroundColor = src.backgroundColor;
        // update other values to match current camera.
        // even if we are supplying custom camera&projection matrices,
        // some of values are used elsewhere (e.g. skybox uses far plane)
        dest.farClipPlane = src.farClipPlane;
        dest.nearClipPlane = src.nearClipPlane;
        dest.orthographic = src.orthographic;
        dest.fieldOfView = src.fieldOfView;
        dest.allowHDR = src.allowHDR;
        dest.useOcclusionCulling = false;
        dest.aspect = src.aspect;
        dest.orthographicSize = src.orthographicSize;
    }


    private void UpdateReflectionCamera(Camera realCamera)
    {
        CreateTextureIfNone(realCamera);
        if (m_ReflectionCamera == null)
            m_ReflectionCamera = CreateMirrorObjects(realCamera);

        // find out the reflection plane: position and normal in world space
        Vector3 pos = Vector3.zero;
        Vector3 normal = Vector3.up;
        if (target != null)
        {
            pos = target.transform.position;
            normal = target.transform.up;
        }

        UpdateCamera(realCamera, m_ReflectionCamera);

        // Render reflection
        // Reflect camera around reflection plane
        float d = -Vector3.Dot(normal, pos) - m_settings.m_ClipPlaneOffset;
        reflectionPlane = new Vector4(normal.x, normal.y, normal.z, d);

        Matrix4x4 reflection = Matrix4x4.identity;
        reflection *= Matrix4x4.Scale(new Vector3(1, -1, 1));

        CalculateReflectionMatrix(ref reflection, reflectionPlane);
        Vector3 oldpos = realCamera.transform.position - new Vector3(0, pos.y * 2, 0);
        Vector3 newpos = ReflectPosition(oldpos);
        m_ReflectionCamera.transform.forward = Vector3.Scale(realCamera.transform.forward, new Vector3(1, -1, 1));
        m_ReflectionCamera.worldToCameraMatrix = realCamera.worldToCameraMatrix * reflection;

        // Setup oblique projection matrix so that near plane is our reflection
        // plane. This way we clip everything below/above it for free.
        Vector4 clipPlane = CameraSpacePlane(m_ReflectionCamera, pos - Vector3.up * 0.1f, normal, 1.0f);
        Matrix4x4 projection = realCamera.CalculateObliqueMatrix(clipPlane);
        m_ReflectionCamera.projectionMatrix = projection;
        m_ReflectionCamera.cullingMask = m_settings.m_ReflectLayers; // never render water layer
        m_ReflectionCamera.transform.position = newpos;
        m_ReflectionCamera.depthTextureMode = DepthTextureMode.Depth;

    }

    // Calculates reflection matrix around the given plane
    private static void CalculateReflectionMatrix(ref Matrix4x4 reflectionMat, Vector4 plane)
    {
        reflectionMat.m00 = (1F - 2F * plane[0] * plane[0]);
        reflectionMat.m01 = (-2F * plane[0] * plane[1]);
        reflectionMat.m02 = (-2F * plane[0] * plane[2]);
        reflectionMat.m03 = (-2F * plane[3] * plane[0]);

        reflectionMat.m10 = (-2F * plane[1] * plane[0]);
        reflectionMat.m11 = (1F - 2F * plane[1] * plane[1]);
        reflectionMat.m12 = (-2F * plane[1] * plane[2]);
        reflectionMat.m13 = (-2F * plane[3] * plane[1]);

        reflectionMat.m20 = (-2F * plane[2] * plane[0]);
        reflectionMat.m21 = (-2F * plane[2] * plane[1]);
        reflectionMat.m22 = (1F - 2F * plane[2] * plane[2]);
        reflectionMat.m23 = (-2F * plane[3] * plane[2]);

        reflectionMat.m30 = 0F;
        reflectionMat.m31 = 0F;
        reflectionMat.m32 = 0F;
        reflectionMat.m33 = 1F;
    }

    private static Vector3 ReflectPosition(Vector3 pos)
    {
        Vector3 newPos = new Vector3(pos.x, -pos.y, pos.z);
        return newPos;
    }

    private float GetScaleValue()
    {
        switch (m_settings.m_ResolutionMultiplier)
        {
            case ResolutionMulltiplier.Full:
                return 1f;
            case ResolutionMulltiplier.Half:
                return 0.5f;
            case ResolutionMulltiplier.Third:
                return 0.33f;
            case ResolutionMulltiplier.Quarter:
                return 0.25f;
        }
        return 0.5f; // default to half res
    }

    // Given position/normal of the plane, calculates plane in camera space.
    private Vector4 CameraSpacePlane(Camera cam, Vector3 pos, Vector3 normal, float sideSign)
    {
        Vector3 offsetPos = pos + normal * m_settings.m_ClipPlaneOffset;
        Matrix4x4 m = cam.worldToCameraMatrix;
        Vector3 cpos = m.MultiplyPoint(offsetPos);
        Vector3 cnormal = m.MultiplyVector(normal).normalized * sideSign;
        return new Vector4(cnormal.x, cnormal.y, cnormal.z, -Vector3.Dot(cpos, cnormal));
    }

    private void CreateTextureIfNone(Camera currentCamera)
    {
        LightweightRenderPipelineAsset lwAsset = (LightweightRenderPipelineAsset)GraphicsSettings.renderPipelineAsset;
        var resMulti = lwAsset.renderScale * GetScaleValue();
        m_TextureSize = (int)Mathf.Pow(2, Mathf.RoundToInt(Mathf.Log(currentCamera.pixelWidth * resMulti, 2)));
        //m_TextureSize.y = (int)Mathf.Pow(2, Mathf.RoundToInt(Mathf.Log(currentCamera.pixelHeight * resMulti, 2)));

        // Reflection render texture
        //if (Int2Compare(m_TextureSize, m_OldReflectionTextureSize) || !m_ReflectionTexture)
        if (!m_ReflectionTexture || m_OldReflectionTextureSize != m_TextureSize)
        {
            if (m_ReflectionTexture)
                DestroyImmediate(m_ReflectionTexture);

            bool useHDR10 = Application.isMobilePlatform &&
            SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGB2101010);
            RenderTextureFormat hdrFormat = (useHDR10) ? RenderTextureFormat.ARGB2101010 : RenderTextureFormat.DefaultHDR;

            m_ReflectionTexture = new RenderTexture(m_TextureSize, m_TextureSize, 16,
                currentCamera.allowHDR ? hdrFormat : RenderTextureFormat.Default);
            m_ReflectionTexture.useMipMap = m_ReflectionTexture.autoGenerateMips = true;
            m_ReflectionTexture.autoGenerateMips = true; // no need for mips(unless wanting cheap roughness)
            m_ReflectionTexture.name = "_PlanarReflection" + GetInstanceID();
            m_ReflectionTexture.isPowerOfTwo = true;
            m_ReflectionTexture.hideFlags = HideFlags.DontSave;
            m_ReflectionTexture.filterMode = FilterMode.Trilinear;

            if (m_ReflectionDepthTexture)
                DestroyImmediate(m_ReflectionDepthTexture);
            m_ReflectionDepthTexture = new RenderTexture(m_TextureSize, m_TextureSize, 0, RenderTextureFormat.RHalf);
            // m_ReflectionDepthTexture = new RenderTexture(m_TextureSize, m_TextureSize, 0, RenderTextureFormat.R8);
            m_ReflectionDepthTexture.name = "__MirrorReflectionDepth" + GetInstanceID();
            m_ReflectionDepthTexture.isPowerOfTwo = true;
            m_ReflectionDepthTexture.hideFlags = HideFlags.DontSave;
            m_ReflectionDepthTexture.filterMode = FilterMode.Bilinear;

            m_OldReflectionTextureSize = m_TextureSize;
        }

        m_ReflectionTexture.DiscardContents();
        Shader.SetGlobalTexture("_PlanarReflectionTexture", m_ReflectionTexture);
        Shader.SetGlobalTexture("_PlanarReflectionDepth", m_ReflectionDepthTexture);
    }

    private Camera CreateMirrorObjects(Camera currentCamera)
    {
        GameObject go =
            new GameObject("Planar Refl Camera id" + GetInstanceID() + " for " + currentCamera.GetInstanceID(),
                typeof(Camera), typeof(Skybox));
        LWRPAdditionalCameraData lwrpCamData =
            go.AddComponent(typeof(LWRPAdditionalCameraData)) as LWRPAdditionalCameraData;
        lwrpCamData.renderShadows = false; // turn off shadows for the reflection camera
        lwrpCamData.requiresColorOption = CameraOverrideOption.Off;
        lwrpCamData.requiresDepthOption = CameraOverrideOption.On;
        // lwrpCamData.m_RendererOverrideOption = RendererOverrideOption.Custom;
        // lwrpCamData.GenerateRenderer();
        var reflectionCamera = go.GetComponent<Camera>();
        reflectionCamera.transform.SetPositionAndRotation(transform.position, transform.rotation);
        reflectionCamera.targetTexture = m_ReflectionTexture;
        reflectionCamera.allowMSAA = true;
        reflectionCamera.depth = -10;
        reflectionCamera.enabled = false;
        reflectionCamera.cameraType = CameraType.Reflection;
        //reflectionCamera.allowHDR = false;
        //go.hideFlags = HideFlags.HideAndDontSave;
        go.hideFlags = HideFlags.DontSave;


        return reflectionCamera;
    }
    

    public void ExecuteBeforeCameraRender(
        LightweightRenderPipeline pipelineInstance,
        ScriptableRenderContext context,
        Camera camera)
    {

        if (!enabled)
            return;

        GL.invertCulling = true;
        RenderSettings.fog = false;
        var max = QualitySettings.maximumLODLevel;
        var bias = QualitySettings.lodBias;
        QualitySettings.maximumLODLevel = 1;
        QualitySettings.lodBias = bias * 0.5f;

        UpdateReflectionCamera(camera);

        LightweightRenderPipeline.RenderSingleCamera(context, m_ReflectionCamera);

        // if (m_matCopyDepth)
        {
            CommandBuffer cmd = CommandBufferPool.Get(k_CopyDeptyTextureTag);
            using (new ProfilingSample(cmd, k_CopyDeptyTextureTag))
            {
                Graphics.Blit(m_ReflectionTexture, m_ReflectionDepthTexture, m_matCopyDepth);
                /*Graphics.SetRenderTarget(m_ReflectionDepthTexture);
                m_matCopyDepth.SetPass(0);
                DrawFullscreenQuad();
                Graphics.SetRenderTarget(null);*/

                Matrix4x4 projMatrix = GL.GetGPUProjectionMatrix(m_ReflectionCamera.projectionMatrix, false);
                Matrix4x4 viewMatrix = m_ReflectionCamera.worldToCameraMatrix;
                Matrix4x4 viewProjMatrix = projMatrix * viewMatrix;
                Matrix4x4 invViewProjMatrix = Matrix4x4.Inverse(viewProjMatrix);
                Matrix4x4 viewprojinv = viewProjMatrix.inverse;
                Shader.SetGlobalMatrix("_Reflect_ViewProjectInverse", viewprojinv);
                Shader.SetGlobalVector("_Reflect_Plane", reflectionPlane);
            }
        }
        GL.invertCulling = false;
        RenderSettings.fog = true;
        QualitySettings.maximumLODLevel = max;
        QualitySettings.lodBias = bias;
    }

    static public void DrawFullscreenQuad(float z = 1.0f)
    {
        GL.Begin(GL.QUADS);
        GL.Vertex3(0.0f, 0.0f, z);
        GL.Vertex3(1.0f, 0.0f, z);
        GL.Vertex3(1.0f, 1.0f, z);
        GL.Vertex3(0.0f, 1.0f, z);

        GL.Vertex3(0.0f, 1.0f, z);
        GL.Vertex3(1.0f, 1.0f, z);
        GL.Vertex3(1.0f, 0.0f, z);
        GL.Vertex3(0.0f, 0.0f, z);
        GL.End();
    }

}
