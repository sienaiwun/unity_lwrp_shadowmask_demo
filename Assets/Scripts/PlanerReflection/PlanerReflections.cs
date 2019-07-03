using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.LWRP;

namespace UnityEngine.Rendering.LWRP
{
    [ImageEffectAllowedInSceneView]
    public class PlanerReflections : MonoBehaviour, IBeforeCameraRender
    {

        const string k_Reflection_process = "Reflection PostProcess";
        const string k_glossy_enable = "_GLOSSY_REFLECTION";

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
            [HideInInspector]
            public ResolutionMulltiplier m_GlossyMultiplier = ResolutionMulltiplier.Half;
            public float m_ClipPlaneOffset = 0.07f;
            public LayerMask m_ReflectLayers = -1;
            public bool m_glossy_enalbe = true;
            [HideInInspector]
            public float m_fade_dis = 1.0f;
            [HideInInspector]
            public float m_cubemap_fade_dis_ratio = 1.13f;
            public bool enableSelfCullingDistance = false;
            [HideInInspector]
            public float[] layerCullingDistances = new float[32];
        }


        [SerializeField]
        public PlanarReflectionSettings m_settings = new PlanarReflectionSettings();

        public GameObject target;
        public Material m_matCopyDepth = null;
        public Material m_matReflectionBlur = null;
        private Camera m_ReflectionCamera;
        private int m_TextureSize = 256;
        private RenderTexture m_ReflectionTexture = null;
        private RenderTexture m_ReflectionDepthTexture = null;
        private RenderTexture m_ReflectionBlurTexture = null;

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
            if (m_ReflectionDepthTexture)
            {
                DestroyImmediate(m_ReflectionDepthTexture);
            }
            if (m_ReflectionBlurTexture)
            {
                DestroyImmediate(m_ReflectionBlurTexture);
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
            m_ReflectionCamera.transform.SetPositionAndRotation(transform.position, transform.rotation);
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
            m_ReflectionCamera.useOcclusionCulling = false;

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

        private float GetGlossyScaleValue()
        {
            switch (m_settings.m_GlossyMultiplier)
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

        private void CreateGlossyTexture(Camera currentCamera, float reflectionBufferScale)
        {
            LightweightRenderPipelineAsset lwAsset = (LightweightRenderPipelineAsset)GraphicsSettings.renderPipelineAsset;
            var resMulti = reflectionBufferScale * GetGlossyScaleValue();
            int glossyTextureSize = (int)Mathf.Pow(2, Mathf.RoundToInt(Mathf.Log(currentCamera.pixelWidth * resMulti, 2)));
            if (!m_ReflectionBlurTexture)
            {
                DestroyImmediate(m_ReflectionBlurTexture);
            }
            if (!m_ReflectionDepthTexture)
            {
                DestroyImmediate(m_ReflectionDepthTexture);
            }

            bool useHDR10 = Application.isMobilePlatform &&
            SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGB2101010);
            RenderTextureFormat hdrFormat = (useHDR10) ? RenderTextureFormat.ARGB2101010 : RenderTextureFormat.DefaultHDR;

            if (m_ReflectionBlurTexture)
                DestroyImmediate(m_ReflectionBlurTexture);
            m_ReflectionBlurTexture = new RenderTexture(m_TextureSize, m_TextureSize, 16,
                currentCamera.allowHDR ? hdrFormat : RenderTextureFormat.Default);
            m_ReflectionBlurTexture.useMipMap = m_ReflectionBlurTexture.autoGenerateMips = true;
            m_ReflectionBlurTexture.autoGenerateMips = true; // no need for mips(unless wanting cheap roughness)
            m_ReflectionBlurTexture.name = "_PlanarReflection blur" + GetInstanceID();
            m_ReflectionBlurTexture.isPowerOfTwo = true;
            m_ReflectionBlurTexture.hideFlags = HideFlags.DontSave;
            m_ReflectionBlurTexture.filterMode = FilterMode.Trilinear;

            if (m_ReflectionDepthTexture)
                DestroyImmediate(m_ReflectionDepthTexture);
            m_ReflectionDepthTexture = new RenderTexture(m_TextureSize, m_TextureSize, 0, RenderTextureFormat.RHalf);
            // m_ReflectionDepthTexture = new RenderTexture(m_TextureSize, m_TextureSize, 0, RenderTextureFormat.R8);
            m_ReflectionDepthTexture.name = "__MirrorReflectionDepth" + GetInstanceID();
            m_ReflectionDepthTexture.isPowerOfTwo = true;
            m_ReflectionDepthTexture.hideFlags = HideFlags.DontSave;
            m_ReflectionDepthTexture.filterMode = FilterMode.Bilinear;
            Shader.SetGlobalTexture("_PlanarReflectionBlurTexture", m_ReflectionBlurTexture);
            Shader.SetGlobalTexture("_PlanarReflectionDepth", m_ReflectionDepthTexture);
        }

        private void DestroyGlossyBuffers()
        {
            if (!m_ReflectionBlurTexture)
            {
                DestroyImmediate(m_ReflectionBlurTexture);
            }
            if (!m_ReflectionDepthTexture)
            {
                DestroyImmediate(m_ReflectionDepthTexture);
            }
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

                m_OldReflectionTextureSize = m_TextureSize;
                if (m_settings.m_glossy_enalbe)
                    CreateGlossyTexture(currentCamera, resMulti);
                else
                    DestroyGlossyBuffers();
            }
            m_ReflectionTexture.DiscardContents();
            Shader.SetGlobalTexture("_PlanarReflectionTexture", m_ReflectionTexture);

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
            reflectionCamera.allowHDR = false;
            if (!m_settings.enableSelfCullingDistance)
            {
                for (int i = 0, length = m_settings.layerCullingDistances.Length; i < length; ++i)
                {
                    m_settings.layerCullingDistances[i] = 0;
                }
            }
            else
            {
                reflectionCamera.layerCullDistances = m_settings.layerCullingDistances;
            }
            go.hideFlags = HideFlags.HideAndDontSave;
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

            CommandBuffer cmd = CommandBufferPool.Get(k_Reflection_process);
            using (new ProfilingSample(cmd, k_Reflection_process))
            {
                CoreUtils.SetKeyword(cmd, k_glossy_enable, m_settings.m_glossy_enalbe);
                if (m_matCopyDepth && m_matReflectionBlur && m_settings.m_glossy_enalbe && m_ReflectionDepthTexture)
                {
                    Graphics.Blit(m_ReflectionTexture, m_ReflectionDepthTexture, m_matCopyDepth);
                    m_matReflectionBlur.SetTexture("_MainTex", m_ReflectionTexture);
                    Graphics.Blit(m_ReflectionTexture, m_ReflectionBlurTexture, m_matReflectionBlur);
                    Matrix4x4 reflectionProjection = m_ReflectionCamera.projectionMatrix;
                    Matrix4x4 projMatrix = GL.GetGPUProjectionMatrix(reflectionProjection, false);
                    Matrix4x4 viewMatrix = m_ReflectionCamera.worldToCameraMatrix;
                    Matrix4x4 viewProjMatrix = projMatrix * viewMatrix;
                    Matrix4x4 invViewProjMatrix = Matrix4x4.Inverse(viewProjMatrix);
                    Matrix4x4 viewprojinv = viewProjMatrix.inverse;
                    Shader.SetGlobalMatrix("_Reflect_ViewProjectInverse", viewprojinv);
                    Shader.SetGlobalVector("_Reflect_Plane", reflectionPlane);
                    Shader.SetGlobalFloat("_Fade_Dis", m_settings.m_fade_dis);
                    Shader.SetGlobalFloat("_Cubemap_Fade_Dis_Radio", m_settings.m_cubemap_fade_dis_ratio);
                }
                context.ExecuteCommandBuffer(cmd);
            }
            GL.invertCulling = false;
            RenderSettings.fog = true;
            QualitySettings.maximumLODLevel = max;
            QualitySettings.lodBias = bias;
        }


    }
}
