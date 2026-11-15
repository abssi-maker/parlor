using Meta.XR.MRUtilityKit;
using Parlor.Quest;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Parlor.EditorTools
{
    /// <summary>
    /// Builds Assets/Parlor.unity from code: the Meta camera rig with passthrough, two tracked hands, MRUK and the
    /// QuestBoot object. Idempotent: run it again after an SDK upgrade and the scene is rebuilt to the same state.
    /// unity command script eval --code "return Parlor.EditorTools.ParlorSceneBuilder.Build();"
    /// </summary>
    public static class ParlorSceneBuilder
    {
        const string ScenePath = "Assets/Parlor.unity";
        const string HandPrefab = "Packages/com.meta.xr.sdk.core/Prefabs/OVRHandPrefab.prefab";
        const string MrukPrefab = "Packages/com.meta.xr.mrutilitykit/Core/Tools/MRUK.prefab";

        [MenuItem("PARLOR/Build Scene")]
        public static string Build()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var rig = Object.FindFirstObjectByType<OVRCameraRig>();
            if (!rig) return "ERROR: no OVRCameraRig in " + ScenePath + " (run Meta > Simplified VR Setup first)";
            var log = new System.Text.StringBuilder();

            // No realtime lights: all PARLOR lighting is faked in the shaders.
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) Object.DestroyImmediate(l.gameObject);

            // Passthrough as an underlay behind the eye buffer.
            var manager = rig.GetComponent<OVRManager>();
            manager.isInsightPassthroughEnabled = true;
            var mso = new SerializedObject(manager);
            SetBool(mso, "requestScenePermissionOnStartup", true, log);
            mso.ApplyModifiedPropertiesWithoutUndo();
            var layer = rig.GetComponent<OVRPassthroughLayer>();
            if (!layer) layer = rig.gameObject.AddComponent<OVRPassthroughLayer>();
            // (v209: OVRPassthroughLayer is always composited as the background underlay.)
            layer.textureOpacity = 1f;

            // Tracked hands (OpenXR skeleton) with meshes; QuestBoot turns the meshes into occluders.
            var left = Hand(rig.leftHandAnchor, "ParlorHandL", false, log);
            var right = Hand(rig.rightHandAnchor, "ParlorHandR", true, log);

            // MR Utility Kit: device scene first; bundled prefab rooms when there is none (Editor without a headset).
            var mruk = Object.FindFirstObjectByType<MRUK>();
            if (!mruk)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MrukPrefab);
                if (!prefab) return "ERROR: MRUK prefab not found at " + MrukPrefab;
                mruk = ((GameObject)PrefabUtility.InstantiatePrefab(prefab)).GetComponent<MRUK>();
            }
            mruk.SceneSettings.DataSource = MRUK.SceneDataSource.DeviceWithPrefabFallback;
            mruk.SceneSettings.LoadSceneOnStartup = true;
            EditorUtility.SetDirty(mruk);
            log.Append("MRUK room prefabs: " + (mruk.SceneSettings.RoomPrefabs != null ? mruk.SceneSettings.RoomPrefabs.Length : 0) + "\n");

            var boot = Object.FindFirstObjectByType<QuestBoot>();
            if (!boot) boot = new GameObject("ParlorBoot").AddComponent<QuestBoot>();
            boot.Rig = rig;
            boot.LeftHand = left.GetComponent<OVRHand>(); boot.RightHand = right.GetComponent<OVRHand>();
            boot.LeftSkeleton = left.GetComponent<OVRSkeleton>(); boot.RightSkeleton = right.GetComponent<OVRSkeleton>();
            boot.LeftHandMesh = left.GetComponent<SkinnedMeshRenderer>(); boot.RightHandMesh = right.GetComponent<SkinnedMeshRenderer>();
            EditorUtility.SetDirty(boot); EditorUtility.SetDirty(manager); EditorUtility.SetDirty(layer);

            // Manifest features: hands only, passthrough, scene and anchors supported but not required, so the app
            // still launches when the user declines them (the show then uses its floating table).
            var cfg = OVRProjectConfig.CachedProjectConfig;
            cfg.handTrackingSupport = OVRProjectConfig.HandTrackingSupport.HandsOnly;
            cfg.handTrackingFrequency = OVRProjectConfig.HandTrackingFrequency.HIGH;
            cfg.insightPassthroughSupport = OVRProjectConfig.FeatureSupport.Supported;
            cfg.sceneSupport = OVRProjectConfig.FeatureSupport.Supported;
            cfg.anchorSupport = OVRProjectConfig.AnchorSupport.Enabled;
            OVRProjectConfig.CommitProjectConfig(cfg);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            log.Append("scene saved: " + ScenePath);
            return log.ToString();
        }

        static GameObject Hand(Transform anchor, string name, bool rightHand, System.Text.StringBuilder log)
        {
            var existing = anchor.Find(name);
            if (existing) Object.DestroyImmediate(existing.gameObject);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HandPrefab);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, anchor);
            go.name = name;
            go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity;

            var hand = new SerializedObject(go.GetComponent<OVRHand>());
            SetInt(hand, "HandType", rightHand ? (int)OVRHand.Hand.HandRight : (int)OVRHand.Hand.HandLeft, log);
            hand.ApplyModifiedPropertiesWithoutUndo();
            var skel = new SerializedObject(go.GetComponent<OVRSkeleton>());
            SetInt(skel, "_skeletonType", (int)(rightHand ? OVRSkeleton.SkeletonType.XRHandRight : OVRSkeleton.SkeletonType.XRHandLeft), log);
            skel.ApplyModifiedPropertiesWithoutUndo();
            var mesh = go.GetComponent<OVRMesh>();
            if (mesh)
            {
                var so = new SerializedObject(mesh);
                SetInt(so, "_meshType", (int)(rightHand ? OVRMesh.MeshType.XRHandRight : OVRMesh.MeshType.XRHandLeft), log);
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            // PARLOR draws its own occluder material; the debug skeleton lines are not wanted.
            var lines = go.GetComponent<OVRSkeletonRenderer>();
            if (lines) Object.DestroyImmediate(lines);
            log.Append(name + ": " + string.Join(", ", System.Array.ConvertAll(go.GetComponents<Component>(), c => c.GetType().Name)) + "\n");
            return go;
        }

        static void SetInt(SerializedObject so, string prop, int value, System.Text.StringBuilder log)
        {
            var p = so.FindProperty(prop);
            if (p == null) { log.Append("WARNING: no property " + prop + " on " + so.targetObject.GetType().Name + "\n"); return; }
            p.intValue = value;
        }

        static void SetBool(SerializedObject so, string prop, bool value, System.Text.StringBuilder log)
        {
            var p = so.FindProperty(prop);
            if (p == null) { log.Append("WARNING: no property " + prop + " on " + so.targetObject.GetType().Name + "\n"); return; }
            p.boolValue = value;
        }
    }
}
