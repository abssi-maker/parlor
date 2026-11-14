using UnityEngine;

namespace Parlor
{
    /// <summary>
    /// What survives between sessions: how many nights have been completed, last night's card, and where the
    /// stage was. PlayerPrefs only; the spatial anchor itself is owned by StageAnchorStore.
    /// </summary>
    public static class NightStore
    {
        const string KNights = "parlor.nightsCompleted";
        const string KCard = "parlor.lastCard";
        const string KAnchor = "parlor.anchorUuid";
        const string KTable = "parlor.tableUuid";
        const string KOffset = "parlor.tableOffset";
        const string KOneHand = "parlor.oneHanded";

        public static int NightsCompleted => PlayerPrefs.GetInt(KNights, 0);
        /// <summary>The night to play now: one more than the nights already completed.</summary>
        public static int CurrentNight => NightsCompleted + 1;
        public static int LastCard => PlayerPrefs.GetInt(KCard, -1);

        public static void CompleteNight(int night, int card)
        {
            if (night > NightsCompleted) PlayerPrefs.SetInt(KNights, night);
            PlayerPrefs.SetInt(KCard, card);
            PlayerPrefs.Save();
        }

        public static string AnchorUuid
        {
            get => PlayerPrefs.GetString(KAnchor, "");
            set { PlayerPrefs.SetString(KAnchor, value ?? ""); PlayerPrefs.Save(); }
        }

        /// <summary>Second restore path: the scene anchor (table) the stage sat on, and the stage pose in its space.</summary>
        public static void SaveTableOffset(string tableUuid, Vector3 localPos, float localYaw)
        {
            PlayerPrefs.SetString(KTable, tableUuid ?? "");
            PlayerPrefs.SetString(KOffset, string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0:R};{1:R};{2:R};{3:R}", localPos.x, localPos.y, localPos.z, localYaw));
            PlayerPrefs.Save();
        }

        public static bool TryGetTableOffset(out string tableUuid, out Vector3 localPos, out float localYaw)
        {
            tableUuid = PlayerPrefs.GetString(KTable, ""); localPos = default; localYaw = 0f;
            var parts = PlayerPrefs.GetString(KOffset, "").Split(';');
            if (string.IsNullOrEmpty(tableUuid) || parts.Length != 4) return false;
            var c = System.Globalization.CultureInfo.InvariantCulture;
            var f = new float[4];
            for (int i = 0; i < 4; i++) if (!float.TryParse(parts[i], System.Globalization.NumberStyles.Float, c, out f[i])) return false;
            localPos = new Vector3(f[0], f[1], f[2]); localYaw = f[3];
            return true;
        }

        public static bool OneHanded
        {
            get => PlayerPrefs.GetInt(KOneHand, 0) == 1;
            set { PlayerPrefs.SetInt(KOneHand, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>Hidden reset (Encore token, 3-second pinch held twice) and tests.</summary>
        public static void ResetAll()
        {
            foreach (var k in new[] { KNights, KCard, KAnchor, KTable, KOffset, KOneHand }) PlayerPrefs.DeleteKey(k);
            PlayerPrefs.Save();
        }
    }
}
