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

        void Start()
        {
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

            if (!Application.isEditor)
            {
                OVRManager.HMDUnmounted += Pause; OVRManager.HMDMounted += Resume;
                OVRManager.VrFocusLost += Pause; OVRManager.VrFocusAcquired += Resume;
                OVRManager.InputFocusLost += Pause; OVRManager.InputFocusAcquired += Resume;
            }
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
