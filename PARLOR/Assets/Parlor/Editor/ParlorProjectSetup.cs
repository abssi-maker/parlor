using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Parlor.EditorTools
{
    /// <summary>
    /// Project settings PARLOR depends on, applied from code so they are reviewable and repeatable.
    /// Run from the menu or with: unity command script eval --code "Parlor.EditorTools.ParlorProjectSetup.Apply();"
    /// </summary>
    public static class ParlorProjectSetup
    {
        const string Dir = "Assets/Parlor/Settings";
        const string RendererPath = Dir + "/ParlorRenderer.asset";
        const string PipelinePath = Dir + "/ParlorURP.asset";

        [MenuItem("PARLOR/Apply Project Settings")]
        public static string Apply()
        {
            var pipeline = EnsurePipeline();
            GraphicsSettings.defaultRenderPipeline = pipeline;
            // Every quality level uses the same pipeline asset: there is one budget, the Quest one.
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
                QualitySettings.shadows = UnityEngine.ShadowQuality.Disable;
                QualitySettings.antiAliasing = 4;
                QualitySettings.vSyncCount = 0;
            }
            QualitySettings.SetQualityLevel(current, false);
            PlayerSettings.colorSpace = ColorSpace.Linear;
            AssetDatabase.SaveAssets();
            return "pipeline: " + AssetDatabase.GetAssetPath(pipeline);
        }

        /// <summary>The fixed PARLOR render budget (SUPERPROMPT §5.7): forward, MSAA 4x, no shadows, no HDR, no post, no depth/opaque copies.</summary>
        static UniversalRenderPipelineAsset EnsurePipeline()
        {
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (!renderer)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, RendererPath);
                ResourceReloader.ReloadAllNullIn(renderer, UniversalRenderPipelineAsset.packagePath);
            }
            renderer.renderingMode = RenderingMode.Forward;
            renderer.postProcessData = null;
            EditorUtility.SetDirty(renderer);

            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (!asset)
            {
                asset = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(asset, PipelinePath);
            }
            asset.msaaSampleCount = 4;
            asset.supportsHDR = false;
            asset.supportsCameraDepthTexture = false;
            asset.supportsCameraOpaqueTexture = false;
            asset.renderScale = 1f;
            asset.shadowDistance = 0f;
            asset.useSRPBatcher = true;
            var so = new SerializedObject(asset);
            Set(so, "m_MainLightShadowsSupported", false);
            Set(so, "m_AdditionalLightShadowsSupported", false);
            Set(so, "m_AdditionalLightsRenderingMode", 0);
            Set(so, "m_SoftShadowsSupported", false);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            return asset;
        }

        static void Set(SerializedObject so, string name, bool v) { var p = so.FindProperty(name); if (p != null) p.boolValue = v; }
        static void Set(SerializedObject so, string name, int v) { var p = so.FindProperty(name); if (p != null) p.intValue = v; }
    }
}
