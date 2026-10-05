using System.Collections.Generic;
using System.IO;
using System.Linq;
using SpaceStation.Audio;
using SpaceStation.Building;
using SpaceStation.Core;
using SpaceStation.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceStation.Editor
{
    /// <summary>
    /// 5-8 소리 일괄 설정 (메뉴 SpaceStation/Audio/Setup). 여러 번 실행해도 결과가 같다.
    /// - 가져오기 설정: UI·건설은 PCM(지연 최소), 이벤트는 Vorbis, 반복음은 압축 상태 메모리, 음악은 스트리밍
    /// - SoundLibrary 에셋의 클립을 파일 이름으로 채움 (볼륨 등 조정값은 유지)
    /// - 씬: Audio 오브젝트(AudioService + GameAudio) 생성·연결, HUD 버튼과 버튼 프리팹에 UiSound 추가
    /// 소리 파일 가공·합성은 저장소의 AudioOriginals/build_audio.py (원본은 같은 폴더에 보관).
    /// </summary>
    public static class AudioSetup
    {
        private const string AudioRoot = "Assets/_Project/Audio";
        private const string LibraryPath = "Assets/_Project/Data/Audio/SoundLibrary.asset";
        private const string ButtonPrefab = "Assets/_Project/Prefabs/UI/PF_BuildButton.prefab";
        private const string TabPrefab = "Assets/_Project/Prefabs/UI/PF_BuildTab.prefab";

        /// <summary>SoundLibrary 필드 → 파일 이름 (끝이 _01 등인 변형은 접두어로 모두 포함).</summary>
        private static readonly Dictionary<string, string[]> Map = new Dictionary<string, string[]>
        {
            { "UiClick", new[] { "ui_click_" } },
            { "UiHover", new[] { "ui_hover_" } },
            { "UiTab", new[] { "ui_tab" } },
            { "UiOpen", new[] { "ui_open" } },
            { "UiClose", new[] { "ui_close" } },
            { "UiError", new[] { "ui_error" } },
            { "UiSpeedUp", new[] { "ui_speed_up" } },
            { "UiPause", new[] { "ui_pause" } },
            { "UiResume", new[] { "ui_resume" } },
            { "BuildSelect", new[] { "build_select" } },
            { "BuildRotate", new[] { "build_rotate_" } },
            { "BuildPlace", new[] { "build_place" } },
            { "BuildRemove", new[] { "build_remove" } },
            { "BuildConnect", new[] { "build_connect" } },
            { "RepairStart", new[] { "repair_start" } },
            { "RepairDone", new[] { "repair_done" } },
            { "Maintain", new[] { "maintain" } },
            { "EventNegative", new[] { "event_negative" } },
            { "EventPositive", new[] { "event_positive" } },
            { "EventEnd", new[] { "event_end" } },
            { "EarlyWarning", new[] { "early_warning" } },
            { "MeteorIncoming", new[] { "meteor_incoming" } },
            { "MeteorImpact", new[] { "meteor_impact" } },
            { "MeteorExplode", new[] { "meteor_explode" } },
            { "TurretLaser", new[] { "turret_laser" } },
            { "ShieldDeflect", new[] { "shield_deflect" } },
            { "ModuleDamaged", new[] { "module_damaged" } },
            { "ModuleDestroyed", new[] { "module_destroyed" } },
            { "StormSpark", new[] { "storm_spark_" } },
            { "OxygenLeak", new[] { "oxygen_leak" } },
            { "SupplyArrive", new[] { "supply_arrive" } },
            { "ResourceDepleted", new[] { "resource_depleted" } },
            { "GradeUp", new[] { "grade_up" } },
            { "GradeDown", new[] { "grade_down" } },
            { "Victory", new[] { "victory" } },
            { "GameOver", new[] { "game_over" } },
            { "AlarmLoop", new[] { "alarm_loop" } },
            { "StormLoop", new[] { "solar_storm_loop" } },
            { "StationHum", new[] { "amb_station_loop" } },
            { "Footstep", new[] { "step_metal_" } },
            { "RoomLife", new[] { "amb_room_life_loop" } },
            { "RoomWater", new[] { "amb_room_water_loop" } },
            { "RoomMachine", new[] { "amb_room_machine_loop" } },
            { "RoomHeat", new[] { "amb_room_heat_loop" } },
            { "RoomElectric", new[] { "amb_room_electric_loop" } },
            { "RoomHall", new[] { "amb_room_hall_loop" } },
            { "MusicDay", new[] { "amb_in_game_loop", "amb_space_loop" } },
            { "MusicNight", new[] { "amb_night_loop" } },
            { "MusicCrisis", new[] { "amb_crisis_loop" } },
            { "MusicMenu", new[] { "amb_mainmenu_loop" } },
        };

        [MenuItem("SpaceStation/Audio/Setup")]
        public static void Run()
        {
            var clips = ImportSettings();
            var library = BuildLibrary(clips);
            WireScene(library);
            AddButtonSounds();
            Debug.Log($"[AudioSetup] 클립 {clips.Count}개, 라이브러리 {LibraryPath}, 씬 연결 완료");
        }

        // ---------------- 가져오기 설정 ----------------

        private static Dictionary<string, AudioClip> ImportSettings()
        {
            var result = new Dictionary<string, AudioClip>();
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { AudioRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = Path.GetFileNameWithoutExtension(path);
                var importer = (AudioImporter)AssetImporter.GetAtPath(path);
                bool music = path.EndsWith(".mp3") || path.EndsWith(".ogg");
                bool loop = name.EndsWith("_loop");
                bool snappy = path.Contains("/UI/") || path.Contains("/Build/");

                var s = importer.defaultSampleSettings;
                if (music)
                {
                    s.loadType = AudioClipLoadType.Streaming;
                    s.compressionFormat = AudioCompressionFormat.Vorbis;
                    s.quality = 0.6f;
                    s.preloadAudioData = false;
                }
                else if (loop)
                {
                    s.loadType = AudioClipLoadType.CompressedInMemory;
                    s.compressionFormat = AudioCompressionFormat.Vorbis;
                    s.quality = 0.75f;
                    s.preloadAudioData = true;
                }
                else
                {
                    s.loadType = AudioClipLoadType.DecompressOnLoad;
                    s.compressionFormat = snappy ? AudioCompressionFormat.PCM : AudioCompressionFormat.Vorbis;
                    s.quality = 0.8f;
                    s.preloadAudioData = true;
                }
                var old = importer.defaultSampleSettings;
                bool changed = old.loadType != s.loadType || old.compressionFormat != s.compressionFormat
                               || !Mathf.Approximately(old.quality, s.quality) || old.preloadAudioData != s.preloadAudioData
                               || importer.loadInBackground != music || importer.forceToMono != (!music && !loop);
                if (changed)
                {
                    importer.defaultSampleSettings = s;
                    importer.loadInBackground = music;
                    importer.forceToMono = !music && !loop;
                    importer.SaveAndReimport();
                }
                result[name] = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            }
            return result;
        }

        // ---------------- 라이브러리 ----------------

        private static SoundLibrary BuildLibrary(Dictionary<string, AudioClip> clips)
        {
            var library = AssetDatabase.LoadAssetAtPath<SoundLibrary>(LibraryPath);
            if (library == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LibraryPath));
                library = ScriptableObject.CreateInstance<SoundLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
            }
            var so = new SerializedObject(library);
            foreach (var pair in Map)
            {
                var found = new List<AudioClip>();
                foreach (var pattern in pair.Value)
                {
                    bool prefix = pattern.EndsWith("_");
                    found.AddRange(clips.Where(c => prefix ? c.Key.StartsWith(pattern) : c.Key == pattern)
                        .OrderBy(c => c.Key).Select(c => c.Value));
                }
                var arr = so.FindProperty(pair.Key + ".Clips");
                if (arr == null)
                {
                    Debug.LogWarning($"[AudioSetup] SoundLibrary.{pair.Key} 없음");
                    continue;
                }
                if (found.Count == 0)
                {
                    // 파일이 없으면 기존 연결을 지우지 않음 (2026-10-06: Audio/Build가 .gitignore의 **/[Bb]uild/에 걸려 이 PC에 없어,
                    // 실행할 때마다 건설 소리 연결이 비워졌음)
                    Debug.LogWarning($"[AudioSetup] {pair.Key}: 파일 없음 ({string.Join(", ", pair.Value)}) — 기존 연결 유지");
                    continue;
                }
                arr.arraySize = found.Count;
                for (int i = 0; i < found.Count; i++)
                    arr.GetArrayElementAtIndex(i).objectReferenceValue = found[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            return library;
        }

        // ---------------- 씬 ----------------

        private static void WireScene(SoundLibrary library)
        {
            var go = GameObject.Find("Audio");
            if (go == null)
                go = new GameObject("Audio");
            var service = GetOrAdd<AudioService>(go);
            Set(service, "_library", library);
            var game = GetOrAdd<GameAudio>(go);
            Set(game, "_station", Object.FindFirstObjectByType<StationController>());
            Set(game, "_build", Object.FindFirstObjectByType<BuildController>());
            Set(game, "_selection", Object.FindFirstObjectByType<ModuleSelectionController>());
            Set(game, "_actions", Object.FindFirstObjectByType<SelectionActionsPanel>());
            Set(game, "_clock", Object.FindFirstObjectByType<SimulationClock>());
            Set(game, "_meteors", Object.FindFirstObjectByType<MeteorFx>());
            Set(game, "_storm", Object.FindFirstObjectByType<SolarStormFx>());
            Set(game, "_camera", Camera.main);
            EditorSceneManager.MarkSceneDirty(go.scene);
        }

        private static void AddButtonSounds()
        {
            var hud = GameObject.Find("HUD");
            if (hud != null)
            {
                foreach (var b in hud.GetComponentsInChildren<Button>(true))
                {
                    var sound = GetOrAdd<UiSound>(b.gameObject);
                    // 결과에 전용 소리가 있는 버튼은 클릭음 생략 (배속·일시정지)
                    bool silent = b.GetComponentInParent<TimeControlPanel>(true) != null
                                  || b.GetComponent<BuildButtonView>() != null || b.GetComponent<BuildTabView>() != null;
                    sound.Mode = silent ? UiSound.ClickMode.Silent : UiSound.ClickMode.Click;
                    EditorUtility.SetDirty(sound);
                }
                EditorSceneManager.MarkSceneDirty(hud.scene);
            }
            AddToPrefab(ButtonPrefab);
            AddToPrefab(TabPrefab);
        }

        private static void AddToPrefab(string path)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var sound = GetOrAdd<UiSound>(root);
                sound.Mode = UiSound.ClickMode.Silent; // 건설 선택음·탭 전환음이 따로 난다
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // GetComponent의 "가짜 null"은 ??로 걸러지지 않으므로 명시적으로 비교
        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }

        private static void Set(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null)
            {
                Debug.LogWarning($"[AudioSetup] {target.GetType().Name}.{field} 없음");
                return;
            }
            p.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
