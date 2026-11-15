using System;
using System.Collections.Generic;
using UnityEngine;

namespace Parlor.Quest
{
    /// <summary>
    /// "The theatre lives in your table." The stage pose is locked with a spatial anchor (OVRSpatialAnchor) for the
    /// length of the show and saved, so the next launch can put the trapdoor back on the same spot even where there
    /// is no labelled table to hang it on. Entirely best-effort: every call is guarded, nothing here can delay or
    /// break the show, and with no saved anchor the stage is simply placed afresh.
    /// (The first restore path, a saved offset on the table's own scene anchor, lives in StagePlacer.)
    /// </summary>
    public sealed class QuestAnchor : MonoBehaviour
    {
        /// <summary>Pose of last session's stage, once (and if) its anchor has been found again.</summary>
        public Pose? Saved { get; private set; }
        public bool Settled { get; private set; }

        OVRSpatialAnchor _anchor;
        Transform _stage;

        async void Start()
        {
            try
            {
                if (Guid.TryParse(NightStore.AnchorUuid, out Guid id))
                {
                    var found = new List<OVRSpatialAnchor.UnboundAnchor>();
                    var result = await OVRSpatialAnchor.LoadUnboundAnchorsAsync(new[] { id }, found);
                    if (result.Success && found.Count > 0 && await found[0].LocalizeAsync(2.0) && found[0].TryGetPose(out Pose pose))
                    {
                        Saved = pose;
                        Debug.Log("[PARLOR] saved stage anchor found");
                    }
                }
            }
            catch (Exception e) { Debug.Log("[PARLOR] anchor restore skipped: " + e.Message); }
            Settled = true;
        }

        /// <summary>Anchor the freshly placed stage and remember it for next time.</summary>
        public async void Lock(Transform stage, StageSite site)
        {
            try
            {
                _stage = null;
                if (_anchor) Destroy(_anchor.gameObject);
                var go = new GameObject("StageAnchor");
                go.transform.SetPositionAndRotation(stage.position, stage.rotation);
                var anchor = go.AddComponent<OVRSpatialAnchor>();
                _anchor = anchor;
                if (!await anchor.WhenLocalizedAsync() || anchor != _anchor) return;
                _stage = stage;
                var saved = await anchor.SaveAnchorAsync();
                if (saved.Success)
                {
                    NightStore.AnchorUuid = anchor.Uuid.ToString();
                    Debug.Log("[PARLOR] stage anchor saved");
                }
            }
            catch (Exception e) { Debug.Log("[PARLOR] anchor lock skipped: " + e.Message); }
        }

        // The runtime keeps the anchor glued to the real world; the stage rides on it.
        void LateUpdate()
        {
            if (_stage && _anchor && _anchor.Localized)
                _stage.SetPositionAndRotation(_anchor.transform.position, _anchor.transform.rotation);
        }
    }
}
