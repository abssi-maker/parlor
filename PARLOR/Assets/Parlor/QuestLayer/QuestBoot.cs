using UnityEngine;

namespace Parlor.Quest
{
    /// <summary>
    /// The one scripted object in the scene. Wires the Meta XR rig to the show: passthrough underlay behind
    /// transparent-clearing eye cameras, hand-mesh occluders, the room source, then launches ParlorApp.
    /// The scene builder (ParlorProjectSetup.BuildScene) fills in the references.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class QuestBoot : MonoBehaviour
    {
        public OVRCameraRig Rig;
        public OVRHand LeftHand, RightHand;
        public OVRSkeleton LeftSkeleton, RightSkeleton;
        public SkinnedMeshRenderer LeftHandMesh, RightHandMesh;

        ParlorApp _app;

        /// <summary>
        /// The Quest's performance controls: the display at 90 Hz when the headset offers it (smoother hands and a
        /// steadier magician than 72 Hz, and the scene has the headroom: ~40 draw calls, ~20k triangles, no realtime
        /// lights), fixed foveated rendering at High with dynamic foveation (the edges of the view are only ever
        /// passthrough and curtain), and sustained-high CPU/GPU levels. Each step is guarded: a headset or runtime that
        /// refuses one setting keeps its default, and the show starts regardless.
        /// </summary>
        static void ConfigurePerformance()
        {
            var applied = new System.Text.StringBuilder("[PARLOR] quest performance:");
            try
            {
                float best = 0f;
                foreach (float hz in OVRPlugin.systemDisplayFrequenciesAvailable) if (hz <= 90.5f && hz > best) best = hz;
                if (best >= 72f && !Application.isEditor) { OVRPlugin.systemDisplayFrequency = best; applied.Append(" ").Append(best.ToString("F0")).Append(" Hz;"); }
            }
            catch (System.Exception e) { applied.Append(" refresh unchanged (").Append(e.GetType().Name).Append(");"); }
            try
            {
                if (OVRManager.fixedFoveatedRenderingSupported)
                {
                    OVRManager.foveatedRenderingLevel = OVRManager.FoveatedRenderingLevel.High;
                    OVRManager.useDynamicFoveatedRendering = true;
                    applied.Append(" foveation High (dynamic);");
                }
                else applied.Append(" foveation unsupported;");
            }
            catch (System.Exception e) { applied.Append(" foveation unchanged (").Append(e.GetType().Name).Append(");"); }
            try
            {
                OVRManager.suggestedCpuPerfLevel = OVRManager.ProcessorPerformanceLevel.SustainedHigh;
                OVRManager.suggestedGpuPerfLevel = OVRManager.ProcessorPerformanceLevel.SustainedHigh;
                applied.Append(" CPU/GPU SustainedHigh");
            }
            catch (System.Exception e) { applied.Append(" CPU/GPU levels unchanged (").Append(e.GetType().Name).Append(")"); }
            try { if (OVRPlugin.systemDisplayFrequency >= 30f) PerfMonitor.DisplayHz = OVRPlugin.systemDisplayFrequency; } catch { }
            Debug.Log(applied.ToString());
        }

        void Start()
        {
            ConfigurePerformance();
            // Passthrough is an underlay: the eye buffer must clear to transparent black so the room shows through
            // wherever nothing virtual is drawn.
            foreach (var cam in Rig.GetComponentsInChildren<Camera>())
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0, 0, 0, 0);
                cam.nearClipPlane = 0.05f; cam.farClipPlane = 30f;
            }

            // Hands occlude the magician and props. In the Editor (simulator) there is no real hand to see behind the
            // occluder, so a faint ghost tint is drawn instead.
            Color ghost = Application.isEditor ? new Color(0.78f, 0.62f, 0.52f, 1f) : new Color(0, 0, 0, 0);
            var handMat = Mats.Hand(ghost);
            if (LeftHandMesh) { LeftHandMesh.sharedMaterial = handMat; MeshKit.Quiet(LeftHandMesh); }
            if (RightHandMesh) { RightHandMesh.sharedMaterial = handMat; MeshKit.Quiet(RightHandMesh); }

            var input = gameObject.AddComponent<QuestInput>();
            input.CentreEye = Rig.centerEyeAnchor;
            input.LeftHand = LeftHand; input.RightHand = RightHand;
            input.LeftSkeleton = LeftSkeleton; input.RightSkeleton = RightSkeleton;
            var room = gameObject.AddComponent<QuestRoom>();

            var anchor = gameObject.AddComponent<QuestAnchor>();

            _app = ParlorApp.Launch(input, input, room);
            _app.SavedStage = () => anchor.Saved;
            _app.SavedStageSettled = () => anchor.Settled;
            _app.StagePlaced = anchor.Lock;
            _passthrough = Rig.GetComponent<OVRPassthroughLayer>();
            _app.HouseLights = level => _lightsTarget = Mathf.Clamp01(level);

            if (!Application.isEditor)
            {
                OVRManager.HMDUnmounted += Pause; OVRManager.HMDMounted += Resume;
                OVRManager.VrFocusLost += Pause; OVRManager.VrFocusAcquired += Resume;
                OVRManager.InputFocusLost += Pause; OVRManager.InputFocusAcquired += Resume;
            }
        }

        // House lights: the real room dims and warms, slowly, like an auditorium. Applied only when it changes.
        OVRPassthroughLayer _passthrough;
        float _lights = 1f, _lightsTarget = 1f, _lightsApplied = -1f;

        void Update()
        {
            if (!_passthrough) return;
            _lights = Mathf.MoveTowards(_lights, _lightsTarget, Time.deltaTime * 0.45f);
            if (Mathf.Abs(_lights - _lightsApplied) < 0.004f) return;
            _lightsApplied = _lights;
            float dark = 1f - _lights;
            if (dark < 0.004f) _passthrough.DisableColorMap();
            else _passthrough.SetBrightnessContrastSaturation(-0.42f * dark, 0.10f * dark, -0.20f * dark);
        }

        void Pause() { if (_app) _app.SetPaused(true); }
        void Resume() { if (_app) _app.SetPaused(false); }

        void OnDestroy()
        {
            OVRManager.HMDUnmounted -= Pause; OVRManager.HMDMounted -= Resume;
            OVRManager.VrFocusLost -= Pause; OVRManager.VrFocusAcquired -= Resume;
            OVRManager.InputFocusLost -= Pause; OVRManager.InputFocusAcquired -= Resume;
        }
    }
}
