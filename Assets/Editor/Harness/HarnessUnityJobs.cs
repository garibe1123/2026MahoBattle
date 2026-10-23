// harness-unity-jobs v1
// 신호등(하네스)이 넣고 관리하는 파일이에요. 직접 고치지 마세요 (업데이트 때 덮어씀).
// Unity가 열릴 때 Assets/Resources/HarnessFonts/ 의 .ttf/.otf 중 아직 TMP 에셋이 없는 것을 찾아
// 같은 폴더에 "<파일이름> SDF.asset" (Dynamic, 한글처럼 글자가 많아도 아틀라스가 커지지 않음)으로 만든다.
// 결과는 HarnessReports/unity-jobs.json 에 남겨서 하네스가 다음 작업 때 읽는다.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace Harness.EditorJobs
{
    [InitializeOnLoad]
    internal static class HarnessUnityJobs
    {
        private const string FontRoot = "Assets/Resources/HarnessFonts";
        private const string Report = "HarnessReports/unity-jobs.json";

        static HarnessUnityJobs()
        {
            EditorApplication.delayCall += () => Run(false);
        }

        [MenuItem("Tools/Harness/Unity 작업 다시 실행 (TMP 폰트)")]
        private static void Menu() => Run(true);

        private static void Run(bool fromMenu)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += () => Run(fromMenu);
                return;
            }
            if (!Directory.Exists(FontRoot)) { if (fromMenu) Debug.Log("[Harness] 받은 폰트가 없어요."); return; }
            var items = new List<string>();
            int made = 0, failed = 0;
            foreach (string f in Directory.GetFiles(FontRoot, "*.*", SearchOption.AllDirectories))
            {
                string path = f.Replace('\\', '/');
                if (!(path.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".otf", StringComparison.OrdinalIgnoreCase))) continue;
                string target = path.Substring(0, path.Length - 4) + " SDF.asset";
                if (File.Exists(target)) continue;
                try
                {
                    var font = AssetDatabase.LoadAssetAtPath<Font>(path);
                    if (font == null) { AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate); font = AssetDatabase.LoadAssetAtPath<Font>(path); }
                    if (font == null) throw new Exception("폰트 파일을 Font로 불러오지 못했어요 (가져오기 설정 확인).");
                    var fa = TMP_FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
                    if (fa == null) throw new Exception("TMP_FontAsset.CreateFontAsset 이 null 을 돌려줬어요.");
                    fa.name = Path.GetFileNameWithoutExtension(target);
                    AssetDatabase.CreateAsset(fa, target);
                    if (fa.atlasTextures != null && fa.atlasTextures.Length > 0 && fa.atlasTextures[0] != null)
                    {
                        fa.atlasTextures[0].name = fa.name + " Atlas";
                        AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa);
                    }
                    if (fa.material != null)
                    {
                        fa.material.name = fa.name + " Material";
                        AssetDatabase.AddObjectToAsset(fa.material, fa);
                    }
                    EditorUtility.SetDirty(fa);
                    made++;
                    items.Add(Item("ok", target, ""));
                }
                catch (Exception e)
                {
                    failed++;
                    items.Add(Item("fail", target, e.Message));
                    Debug.LogError("[Harness] TMP 폰트 에셋을 못 만들었어요: " + target + " · " + e.Message);
                }
            }
            if (items.Count == 0) { if (fromMenu) Debug.Log("[Harness] 새로 만들 TMP 폰트 에셋이 없어요."); return; }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            try
            {
                Directory.CreateDirectory("HarnessReports");
                File.WriteAllText(Report, "{\"at\":\"" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\",\"unity\":\"" + Esc(Application.unityVersion) + "\",\"jobs\":[" + string.Join(",", items) + "]}");
            }
            catch (Exception e) { Debug.LogWarning("[Harness] 결과 파일을 못 썼어요: " + e.Message); }
            Debug.Log("[Harness] TMP 폰트 에셋 " + made + "개 만듦" + (failed > 0 ? " · 실패 " + failed + "개 (콘솔 오류 참고)" : ""));
        }

        private static string Item(string status, string path, string error) =>
            "{\"kind\":\"tmp_font\",\"status\":\"" + status + "\",\"path\":\"" + Esc(path) + "\",\"error\":\"" + Esc(error) + "\"}";

        private static string Esc(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ");
    }
}
#endif