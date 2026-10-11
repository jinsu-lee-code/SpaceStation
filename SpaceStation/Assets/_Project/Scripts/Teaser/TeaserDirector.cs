#if UNITY_EDITOR
// 에디터 녹화 전용 (Editor 어셈블리의 MonoBehaviour는 오브젝트에 붙일 수 없어 런타임 어셈블리에 두고, 빌드에서는 빠지게)
using System.Collections;
using System.Collections.Generic;
using SpaceStation.Audio;
using SpaceStation.Building;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Interior;
using SpaceStation.Simulation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceStation.Teaser
{
    /// <summary>
    /// 11-19 티저 영상 연출 (에디터 플레이 모드 전용, 빌드에 들어가지 않음). 녹화는 Editor/Teaser/TeaserRecorder (메뉴 SpaceStation/Teaser/Record).
    /// slot1(쇼케이스 정거장)을 불러온 게임 씬에서: 코어만 남기고 모듈을 지운 뒤 다시 하나씩 붙이는 "성장" →
    /// 근접 회전 → 내부 휴게실(동물 주민) → 코어 방 화재 → 패드 홀로그램 → 제목 카드. 약 43초.
    /// 시간은 녹화기의 고정 프레임(30fps)을 따르도록 Time.deltaTime으로만 진행한다. HUD는 숨기고 자막 · 레터박스는 따로 그린다.
    /// </summary>
    public sealed class TeaserDirector : MonoBehaviour
    {
        public const float Length = 43f;

        /// <summary>장면 확인용 캡처 시각 (초) — 녹화와 함께 PNG로 저장.</summary>
        private static readonly float[] Stills = { 3f, 10f, 17f, 23f, 29.5f, 34f, 40f };

        public string StillFolder { get; set; }
        public System.Action Finished;

        private StationController _station;
        private InteriorMode _interior;
        private OrbitCameraController _orbit;
        private Canvas _hud;
        private TMP_FontAsset _font;

        private Image _fade;
        private TMP_Text _subtitle;
        private TMP_Text _title;
        private TMP_Text _titleSub;
        private TMP_Text _titleMeta;
        private float _t;
        private int _still;

        private readonly List<(ModuleData Data, Vector3Int Origin, int Rotation)> _plan = new List<(ModuleData, Vector3Int, int)>();
        private readonly List<(Transform View, float Start)> _pops = new List<(Transform, float)>();

        private StationSimulation Sim => _station.Simulation;

        private IEnumerator Start()
        {
            _station = FindFirstObjectByType<StationController>();
            _interior = FindFirstObjectByType<InteriorMode>();
            _orbit = FindFirstObjectByType<OrbitCameraController>();
            var hudGo = GameObject.Find("HUD");
            _hud = hudGo != null ? hudGo.GetComponent<Canvas>() : null;
            var anyText = hudGo != null ? hudGo.GetComponentInChildren<TMP_Text>(true) : null;
            _font = anyText != null ? anyText.font : null;

            var clock = FindFirstObjectByType<SimulationClock>();
            if (clock != null)
                clock.Clock.SetPaused(true); // 무작위 이벤트 · 자원 변화 없이
            FindFirstObjectByType<ModuleSelectionController>()?.Select(null);
            if (_hud != null)
                _hud.enabled = false;
            BuildOverlay();
            SetFade(1f);
            PrepareGrowth();
            yield return null; // 지운 모듈 정리 한 프레임

            _t = 0f;
            yield return Exterior();
            yield return Lounge();
            yield return Fire();
            yield return Pad();
            yield return Title();
            Finished?.Invoke();
        }

        private void Update()
        {
            if (AudioService.Instance != null)
                AudioService.Instance.PlayMusic(AudioService.Instance.Library.MusicMenu); // 게임 음악이 끼어들지 않게 (같은 곡이면 무시됨)
            for (int i = _pops.Count - 1; i >= 0; i--)
            {
                var (view, start) = _pops[i];
                if (view == null)
                {
                    _pops.RemoveAt(i);
                    continue;
                }
                float k = Mathf.Clamp01((_t - start) / 0.35f);
                float s = k >= 1f ? 1f : EaseOutBack(k);
                view.localScale = Vector3.one * Mathf.Max(0.01f, s);
                if (k >= 1f)
                    _pops.RemoveAt(i);
            }
            if (_still < Stills.Length && _t >= Stills[_still])
            {
                if (!string.IsNullOrEmpty(StillFolder))
                    ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(StillFolder, $"teaser_{_still + 1}_{Stills[_still]:0.0}s.png"));
                _still++;
            }
        }

        // ---------------- 장면 ----------------

        private IEnumerator Exterior()
        {
            var focus = new Vector3(0.5f, 0f, 0.5f);
            // A. 코어 하나 (0 ~ 6초)
            float end = 6f;
            while (_t < end)
            {
                float k = Smooth(_t / end);
                _orbit.Restore(focus, Mathf.Lerp(200f, 232f, k), Mathf.Lerp(6f, 18f, k), Mathf.Lerp(26f, 9f, k));
                SetFade(1f - Mathf.Clamp01(_t / 1.4f));
                Subtitle("작은 코어 하나에서", 1.2f, 5.4f);
                yield return Step();
            }
            // B. 성장 (6 ~ 15초): 모듈을 하나씩 다시 붙임
            float growStart = 6.3f, growEnd = 13.3f;
            int placed = 0;
            while (_t < 15f)
            {
                float k = Smooth((_t - 6f) / 9f);
                _orbit.Restore(focus, Mathf.Lerp(232f, 300f, k), Mathf.Lerp(18f, 30f, k), Mathf.Lerp(9f, 12f, k));
                int want = Mathf.Clamp(Mathf.FloorToInt((_t - growStart) / (growEnd - growStart) * _plan.Count) + 1, 0, _plan.Count);
                while (placed < want)
                    PlaceNext(placed++);
                Subtitle("칸을 이어 붙여, 정거장을 키운다", 7f, 12.6f);
                yield return Step();
            }
            while (placed < _plan.Count)
                PlaceNext(placed++);
            // C. 근접 회전 (15 ~ 19.5초)
            while (_t < 19.5f)
            {
                float k = Smooth((_t - 15f) / 4.5f);
                _orbit.Restore(focus, Mathf.Lerp(300f, 345f, k), Mathf.Lerp(12f, 7f, k), Mathf.Lerp(7.5f, 6.2f, k));
                SetFade(Mathf.Clamp01((_t - 19f) / 0.5f));
                Subtitle("전력 · 산소 · 물 · 식량, 하나라도 무너지면 끝", 15.4f, 19f);
                yield return Step();
            }
        }

        private IEnumerator Lounge()
        {
            SetFade(1f);
            Subtitle(null, 0f, 0f);
            _interior.Enter(Find("Recreation"));
            float wait = 0f;
            while (!_interior.IsInside || wait < 0.6f)
            {
                wait += Time.deltaTime;
                yield return null; // 들어가는 동안은 시간을 세지 않음 (페이드 중)
            }
            var player = FindFirstObjectByType<FirstPersonController>();
            float y = player.transform.position.y;
            var spawn = player.transform.position;
            // 휴게실 주민들의 가운데를 비춤 (주민은 돌아다니므로 들어간 순간 위치로)
            var room = Find("Recreation");
            var residents = FindFirstObjectByType<InteriorResidents>();
            var aim = spawn + player.transform.forward * 3f;
            if (residents != null)
            {
                var sum = Vector3.zero;
                int n = 0;
                foreach (var f in residents.Figures)
                {
                    if (f != null && f.Room == room)
                    {
                        sum += f.transform.position;
                        n++;
                    }
                }
                if (n > 0)
                    aim = sum / n;
            }
            var toAim = aim - spawn;
            toAim.y = 0f;
            float aimYaw = Mathf.Atan2(toAim.x, toAim.z) * Mathf.Rad2Deg;
            var back = -toAim.normalized * 0.5f; // 조금 뒤에서 시작해 다가감
            _t = 20.3f;
            // D. 휴게실 (20.3 ~ 26.3초)
            while (_t < 26.3f)
            {
                float k = Smooth((_t - 20.3f) / 6f);
                player.Teleport(spawn + back * (1f - k), aimYaw + Mathf.Lerp(-14f, 10f, k), 6f);
                SetFade(1f - Mathf.Clamp01((_t - 20.3f) / 0.6f) + Mathf.Clamp01((_t - 25.9f) / 0.4f));
                Subtitle("그 안에는, 누군가 살고 있다", 21f, 25.8f);
                yield return Step();
            }
        }

        private IEnumerator Fire()
        {
            SetFade(1f);
            Subtitle(null, 0f, 0f);
            var player = FindFirstObjectByType<FirstPersonController>();
            float y = player.transform.position.y;
            player.Teleport(new Vector3(8.7f, y, 5.1f), 90f); // 코어 방 (slot1 배치 기준)
            yield return Step();
            yield return Step();
            var incidents = FindFirstObjectByType<InteriorIncidents>();
            if (incidents != null && !incidents.ForceIncident(IncidentKind.Fire))
                incidents.ForceIncident();
            yield return Step();
            Transform prop = null;
            foreach (var ps in FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
            {
                if (ps.name == "Flames")
                    prop = ps.transform.parent;
            }
            var target = prop != null ? prop.position : player.transform.position + Vector3.right * 2f;
            var normal = prop != null ? prop.forward : Vector3.left;
            normal.y = 0f;
            normal.Normalize();
            _t = 26.8f;
            // E. 화재 (26.8 ~ 31.5초): 천천히 다가감
            while (_t < 31.5f)
            {
                float k = Smooth((_t - 26.8f) / 4.7f);
                var from = target + normal * Mathf.Lerp(2.4f, 1.8f, k); // 더 멀면 문틀이 가림
                from.y = y;
                var d = target - from;
                player.Teleport(from, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, Mathf.Lerp(4f, 8f, k));
                SetFade(1f - Mathf.Clamp01((_t - 26.8f) / 0.4f) + Mathf.Clamp01((_t - 31.2f) / 0.3f));
                Subtitle("작은 사고도 그냥 지나치지 않게", 27.3f, 31f);
                yield return Step();
            }
        }

        private IEnumerator Pad()
        {
            var pad = FindFirstObjectByType<InteriorPad>();
            var player = FindFirstObjectByType<FirstPersonController>();
            if (pad != null)
            {
                pad.Raise();
                pad.SetZoom(true);
            }
            // 화재 반대편(방 안쪽)을 보며 패드
            float baseYaw = player.transform.eulerAngles.y + 180f;
            var pos = player.transform.position;
            player.Teleport(pos, baseYaw, 0f);
            float wait = 0f;
            while (wait < 0.5f)
            {
                wait += Time.deltaTime;
                yield return null; // 패드가 올라오는 동안
            }
            _t = 31.9f;
            // F. 패드 홀로그램 (31.9 ~ 36.3초)
            while (_t < 36.3f)
            {
                float k = Smooth((_t - 31.9f) / 4.4f);
                player.Teleport(pos, baseYaw + Mathf.Lerp(-4f, 4f, k), 0f);
                SetFade(1f - Mathf.Clamp01((_t - 31.9f) / 0.4f) + Mathf.Clamp01((_t - 35.5f) / 0.8f));
                Subtitle("손 안의 정거장", 32.4f, 35.6f);
                yield return Step();
            }
        }

        private IEnumerator Title()
        {
            SetFade(1f);
            Subtitle(null, 0f, 0f);
            // G. 제목 카드 (36.3 ~ 43초)
            while (_t < Length)
            {
                SetAlpha(_title, Mathf.Clamp01((_t - 37.0f) / 1.2f) - Mathf.Clamp01((_t - 42.2f) / 0.8f));
                SetAlpha(_titleSub, Mathf.Clamp01((_t - 38.4f) / 1.0f) - Mathf.Clamp01((_t - 42.2f) / 0.8f));
                SetAlpha(_titleMeta, Mathf.Clamp01((_t - 39.4f) / 1.0f) - Mathf.Clamp01((_t - 42.2f) / 0.8f));
                _title.characterSpacing = Mathf.Lerp(30f, 12f, Smooth(Mathf.Clamp01((_t - 37f) / 5f)));
                if (_t > 41.5f && AudioService.Instance != null)
                    AudioService.Instance.DuckMusic(0f, 0.7f);
                yield return Step();
            }
        }

        // ---------------- 성장 ----------------

        /// <summary>코어 말고 모두 지우고, 다시 붙일 순서를 정함 (이미 붙은 칸에 면으로 닿는 것부터, 코어에서 가까운 순).</summary>
        private void PrepareGrowth()
        {
            var core = Sim.Core;
            var all = new List<ModuleInstance>();
            foreach (var m in Sim.Grid.Modules)
            {
                if (m != core)
                    all.Add(m);
            }
            var occupied = new HashSet<Vector3Int>(core.Cells);
            var center = core.Origin;
            all.Sort((a, b) => (a.Origin - center).sqrMagnitude.CompareTo((b.Origin - center).sqrMagnitude));
            var remaining = new List<ModuleInstance>(all);
            while (remaining.Count > 0)
            {
                int pick = 0;
                for (int i = 0; i < remaining.Count; i++)
                {
                    if (Touches(remaining[i], occupied))
                    {
                        pick = i;
                        break;
                    }
                }
                var m = remaining[pick];
                remaining.RemoveAt(pick);
                foreach (var c in m.Cells)
                    occupied.Add(c);
                _plan.Add((m.Data, m.Origin, m.Rotation));
            }
            foreach (var m in all)
                Sim.Grid.Remove(m);
        }

        private static readonly Vector3Int[] Dirs = { Vector3Int.right, Vector3Int.left, Vector3Int.up, Vector3Int.down, new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1) };

        private static bool Touches(ModuleInstance m, HashSet<Vector3Int> occupied)
        {
            foreach (var c in m.Cells)
            {
                foreach (var d in Dirs)
                {
                    if (occupied.Contains(c + d))
                        return true;
                }
            }
            return false;
        }

        private void PlaceNext(int index)
        {
            var (data, origin, rotation) = _plan[index];
            if (!Sim.Grid.TryPlace(data, origin, rotation, out var module))
                return;
            if (_station.TryGetView(module, out var view) && view != null)
            {
                view.transform.localScale = Vector3.one * 0.01f;
                _pops.Add((view.transform, _t));
            }
            AudioService.TryPlay(l => l.BuildPlace, 0.5f);
        }

        private ModuleInstance Find(string dataName)
        {
            foreach (var m in Sim.Grid.Modules)
            {
                if (m.Data != null && m.Data.name.Contains(dataName))
                    return m;
            }
            return Sim.Core;
        }

        // ---------------- 화면 ----------------

        private void BuildOverlay()
        {
            var go = new GameObject("TeaserOverlay", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
            var root = (RectTransform)go.transform;

            // 레터박스 (2.2:1 느낌)
            Bar(root, "BarTop", new Vector2(0f, 1f), 96f);
            Bar(root, "BarBottom", new Vector2(0f, 0f), 96f);

            // 자막은 아래 검은 띠 안 (패드 화면 안내 글자 등 장면 위 글자와 겹치지 않게)
            _subtitle = Label(root, "Subtitle", 40f, new Vector2(0.5f, 0f), new Vector2(0f, 48f), new Vector2(1600f, 64f));
            _subtitle.fontStyle = FontStyles.Bold;

            var fadeGo = new GameObject("Fade", typeof(RectTransform));
            fadeGo.transform.SetParent(root, false);
            var frt = (RectTransform)fadeGo.transform;
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = Vector2.one;
            frt.offsetMin = frt.offsetMax = Vector2.zero;
            _fade = fadeGo.AddComponent<Image>();
            _fade.color = Color.black;
            _fade.raycastTarget = false;

            _title = Label(root, "Title", 132f, new Vector2(0.5f, 0.5f), new Vector2(0f, 60f), new Vector2(1800f, 170f));
            _title.fontStyle = FontStyles.Bold;
            _title.color = new Color(0.87f, 0.97f, 1f);
            _title.text = "ANOTHER EARTH";
            if (_title.fontMaterial != null)
            {
                var mat = new Material(_title.fontMaterial);
                mat.EnableKeyword("UNDERLAY_ON");
                mat.SetColor("_UnderlayColor", new Color(0.31f, 0.85f, 1f, 0.75f));
                mat.SetFloat("_UnderlaySoftness", 0.6f);
                mat.SetFloat("_UnderlayDilate", 0.4f);
                _title.fontMaterial = mat;
            }
            _titleSub = Label(root, "TitleSub", 40f, new Vector2(0.5f, 0.5f), new Vector2(0f, -70f), new Vector2(1600f, 60f));
            _titleSub.text = "우주정거장 건설 경영  ·  1인칭 정거장 탐험";
            _titleMeta = Label(root, "TitleMeta", 26f, new Vector2(0.5f, 0.5f), new Vector2(0f, -150f), new Vector2(1600f, 40f));
            _titleMeta.text = "v0.11  ·  Windows  ·  2026";
            _titleMeta.color = new Color(0.56f, 0.64f, 0.72f);
            SetAlpha(_title, 0f);
            SetAlpha(_titleSub, 0f);
            SetAlpha(_titleMeta, 0f);
        }

        private static void Bar(RectTransform root, string name, Vector2 anchor, float height)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(root, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, anchor.y);
            rt.anchorMax = new Vector2(1f, anchor.y);
            rt.pivot = new Vector2(0.5f, anchor.y);
            rt.sizeDelta = new Vector2(0f, height);
            var img = go.AddComponent<Image>();
            img.color = Color.black;
            img.raycastTarget = false;
        }

        private TMP_Text Label(RectTransform root, string name, float size, Vector2 anchor, Vector2 pos, Vector2 box)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(root, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = box;
            var text = go.AddComponent<TextMeshProUGUI>();
            if (_font != null)
                text.font = _font;
            text.fontSize = size;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            return text;
        }

        /// <summary>자막: start ~ end 동안 보이고 앞뒤 0.4초 페이드. text가 null이면 숨김.</summary>
        private void Subtitle(string text, float start, float end)
        {
            if (text == null)
            {
                SetAlpha(_subtitle, 0f);
                return;
            }
            if (_subtitle.text != text)
                _subtitle.text = text;
            float a = Mathf.Clamp01((_t - start) / 0.4f) * (1f - Mathf.Clamp01((_t - (end - 0.4f)) / 0.4f));
            SetAlpha(_subtitle, a);
        }

        private void SetFade(float a)
        {
            var c = _fade.color;
            c.a = Mathf.Clamp01(a);
            _fade.color = c;
        }

        private static void SetAlpha(TMP_Text text, float a)
        {
            var c = text.color;
            c.a = Mathf.Clamp01(a);
            text.color = c;
        }

        private IEnumerator Step()
        {
            yield return null;
            _t += Time.deltaTime;
        }

        private static float Smooth(float k)
        {
            k = Mathf.Clamp01(k);
            return k * k * (3f - 2f * k);
        }

        private static float EaseOutBack(float k)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float x = k - 1f;
            return 1f + c3 * x * x * x + c1 * x * x;
        }
    }
}
#endif
