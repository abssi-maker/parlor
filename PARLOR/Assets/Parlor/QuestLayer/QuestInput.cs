using UnityEngine;

namespace Parlor.Quest
{
    /// <summary>
    /// Real hands and head, reduced to HandSnapshot. Reads OVRHand (tracking state, pinch strength) and OVRSkeleton
    /// (joint positions) from the Meta XR Core SDK, using the OpenXR hand skeleton (XRHand_* joints), which is the
    /// default for new projects (OVRRuntimeSettings.HandSkeletonVersion).
    ///
    /// Palm direction is derived from joint positions (wrist, knuckles), not from a joint's rotation, so it does not
    /// depend on any joint-axis convention. Everything is forgiving by design: the simulator cannot validate fine
    /// finger tracking, so low-confidence frames keep the last good snapshot for a moment instead of dropping the hand.
    /// </summary>
    public sealed class QuestInput : MonoBehaviour, IHandSource, IHeadSource
    {
        public Transform CentreEye;
        public OVRHand LeftHand, RightHand;
        public OVRSkeleton LeftSkeleton, RightSkeleton;

        const float HoldLost = 0.25f;   // seconds a snapshot survives a tracking dropout
        readonly HandSnapshot[] _snap = new HandSnapshot[2];
        readonly float[] _lastGood = { -10f, -10f };
        readonly Transform[][] _joints = { null, null };
        int _frame = -1;

        // OpenXR hand joints (OVRPlugin.BoneId.XRHand_*): palm, wrist, then 4 or 5 joints per finger ending in the tip.
        const int Palm = (int)OVRSkeleton.BoneId.XRHand_Palm, Wrist = (int)OVRSkeleton.BoneId.XRHand_Wrist;
        const int ThumbTip = (int)OVRSkeleton.BoneId.XRHand_ThumbTip;
        const int IndexProximal = (int)OVRSkeleton.BoneId.XRHand_IndexProximal, IndexTip = (int)OVRSkeleton.BoneId.XRHand_IndexTip;
        const int MiddleProximal = (int)OVRSkeleton.BoneId.XRHand_MiddleProximal, MiddleTip = (int)OVRSkeleton.BoneId.XRHand_MiddleTip;
        const int RingProximal = (int)OVRSkeleton.BoneId.XRHand_RingProximal, RingTip = (int)OVRSkeleton.BoneId.XRHand_RingTip;
        const int LittleProximal = (int)OVRSkeleton.BoneId.XRHand_LittleProximal, LittleTip = (int)OVRSkeleton.BoneId.XRHand_LittleTip;
        const int JointCount = (int)OVRSkeleton.BoneId.XRHand_Max;

        public Pose Head => CentreEye ? new Pose(CentreEye.position, CentreEye.rotation) : new Pose(new Vector3(0, 1.15f, 0), Quaternion.identity);

        public HandSnapshot Get(Handed hand)
        {
            if (_frame != Time.frameCount) { _frame = Time.frameCount; Refresh(0, LeftHand, LeftSkeleton); Refresh(1, RightHand, RightSkeleton); }
            return _snap[(int)hand];
        }

        void Refresh(int i, OVRHand hand, OVRSkeleton skeleton)
        {
            bool live = hand && skeleton && hand.IsTracked && skeleton.IsInitialized && skeleton.IsDataValid && Map(i, skeleton);
            if (!live)
            {
                if (Time.time - _lastGood[i] > HoldLost) _snap[i] = default;
                return;
            }
            var j = _joints[i];
            Vector3 wrist = j[Wrist].position;
            Vector3 knuckles = (j[IndexProximal].position + j[MiddleProximal].position + j[RingProximal].position + j[LittleProximal].position) * 0.25f;
            Vector3 forward = (j[MiddleProximal].position - wrist).normalized;
            Vector3 across = j[IndexProximal].position - j[LittleProximal].position;
            // Right hand, palm down, fingers away: the index knuckle is on the left, so forward × across points down,
            // out of the palm. The left hand is mirrored.
            Vector3 normal = Vector3.Cross(forward, across).normalized * (i == 1 ? 1f : -1f);

            var s = new HandSnapshot
            {
                Tracked = true,
                PalmPosition = j[Palm] ? j[Palm].position : (wrist + knuckles) * 0.5f,
                PalmNormal = normal,
                PalmForward = forward,
                IndexTip = j[IndexTip].position,
                ThumbTip = j[ThumbTip].position,
                Pinch = hand.GetFingerPinchStrength(OVRHand.HandFinger.Index),
                Curl = (Curl(j, IndexProximal, IndexTip, wrist) + Curl(j, MiddleProximal, MiddleTip, wrist)
                        + Curl(j, RingProximal, RingTip, wrist) + Curl(j, LittleProximal, LittleTip, wrist)) * 0.25f,
            };
            _snap[i] = s; _lastGood[i] = Time.time;
        }

        // 0 = finger straight, 1 = tip folded back to the palm. Compares the tip's distance from the wrist with the
        // finger fully extended (knuckle distance + finger length), which needs no joint rotations.
        static float Curl(Transform[] j, int proximal, int tip, Vector3 wrist)
        {
            float length = 0f;
            for (int b = proximal; b < tip; b++) length += Vector3.Distance(j[b].position, j[b + 1].position);
            float knuckle = Vector3.Distance(j[proximal].position, wrist);
            float extended = knuckle + length, folded = Mathf.Max(0.01f, knuckle - length * 0.15f);
            return Mathf.Clamp01(Mathf.InverseLerp(extended * 0.97f, folded, Vector3.Distance(j[tip].position, wrist)));
        }

        bool Map(int i, OVRSkeleton skeleton)
        {
            if (_joints[i] != null) return true;
            var type = skeleton.GetSkeletonType();
            if (type != OVRSkeleton.SkeletonType.XRHandLeft && type != OVRSkeleton.SkeletonType.XRHandRight)
            {
                Debug.LogWarning("[PARLOR] hand skeleton is " + type + "; PARLOR needs the OpenXR hand skeleton (Meta > Tools > Project Settings).");
                return false;
            }
            var map = new Transform[JointCount];
            foreach (var b in skeleton.Bones)
            {
                int id = (int)b.Id;
                if (id >= 0 && id < JointCount) map[id] = b.Transform;
            }
            foreach (int need in new[] { Wrist, ThumbTip, IndexProximal, IndexTip, MiddleProximal, MiddleTip, RingProximal, RingTip, LittleProximal, LittleTip })
                if (!map[need]) return false;
            for (int b = IndexProximal; b <= LittleTip; b++) if (!map[b]) return false;
            _joints[i] = map;
            return true;
        }
    }
}
