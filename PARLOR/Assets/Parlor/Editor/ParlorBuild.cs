using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Parlor.EditorTools
{
    /// <summary>
    /// Release APK for Meta Quest: IL2CPP, ARM64, signed with the release keystore from repo/.env.
    /// unity command script eval --timeout 3600 --code "return Parlor.EditorTools.ParlorBuild.Android();"
    /// Secrets are read from .env straight into PlayerSettings for this Editor session and are never logged.
    /// </summary>
    public static class ParlorBuild
    {
        static string Repo => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));

        static Dictionary<string, string> Env()
        {
            var d = new Dictionary<string, string>();
            string path = Path.Combine(Repo, ".env");
            if (!File.Exists(path)) return d;
            foreach (var raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                int eq = line.IndexOf('=');
                if (line.Length == 0 || line[0] == '#' || eq < 1) continue;
                d[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim().Trim('"');
            }
            return d;
        }

        [MenuItem("PARLOR/Build Android APK")]
        public static string Android()
        {
            var env = Env();
            string Get(string k, string fallback = "") => env.TryGetValue(k, out var v) && v.Length > 0 ? v : fallback;

            var target = NamedBuildTarget.Android;
            PlayerSettings.productName = "PARLOR";
            PlayerSettings.companyName = "Parlor";
            PlayerSettings.SetApplicationIdentifier(target, Get("ANDROID_PACKAGE_NAME", "com.parlor.magicshow"));
            PlayerSettings.SetScriptingBackend(target, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetManagedStrippingLevel(target, ManagedStrippingLevel.Low);
            PlayerSettings.bundleVersion = "0.1." + PlayerSettings.Android.bundleVersionCode;
            PlayerSettings.Android.bundleVersionCode += 1;

            string keystore = Get("KEYSTORE_PATH");
            bool signed = File.Exists(keystore) && Get("KEYSTORE_PASSWORD").Length > 0;
            PlayerSettings.Android.useCustomKeystore = signed;
            if (signed)
            {
                PlayerSettings.Android.keystoreName = keystore;
                PlayerSettings.Android.keystorePass = Get("KEYSTORE_PASSWORD");
                PlayerSettings.Android.keyaliasName = Get("KEY_ALIAS", "parlor");
                PlayerSettings.Android.keyaliasPass = Get("KEY_PASSWORD");
            }
            EditorUserBuildSettings.development = false;
            EditorUserBuildSettings.buildAppBundle = false;

            string outDir = Path.Combine(Repo, "Builds");
            Directory.CreateDirectory(outDir);
            string apk = Path.Combine(outDir, "PARLOR.apk");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Parlor.unity" },
                locationPathName = apk,
                target = BuildTarget.Android,
                options = BuildOptions.None,
            });
            var s = report.summary;
            var sb = new System.Text.StringBuilder();
            sb.Append("result=" + s.result + " signed=" + signed + " versionCode=" + PlayerSettings.Android.bundleVersionCode
                      + " size=" + (s.totalSize / (1024f * 1024f)).ToString("F1") + "MB time=" + s.totalTime.TotalSeconds.ToString("F0")
                      + "s errors=" + s.totalErrors + " warnings=" + s.totalWarnings + "\n");
            int shown = 0;
            foreach (var step in report.steps)
                foreach (var m in step.messages)
                    if (m.type == LogType.Error && shown++ < 12) sb.Append("ERROR " + m.content.Substring(0, Mathf.Min(300, m.content.Length)) + "\n");
            File.WriteAllText(Path.Combine(outDir, "last_build.txt"), sb.ToString());
            return sb.ToString();
        }
    }
}
