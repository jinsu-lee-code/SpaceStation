using System.Collections.Generic;
using SpaceStation.Audio;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// CONNECT_1121 A6 2단계 ③ 놀이 소품 흩어짐 (사용자 결정 2026-11-27, 2단계 셋 중 하나):
    /// 방 바닥 2 ~ 3곳에 놀이 소품(공 · 별 쿠션 · 고리)이 흩어지고 바구니 하나가 놓임. 소품을 주워(F) 바구니에 넣으면(F) 하나씩 담기고,
    /// 다 담으면 해결 — 바구니는 소품을 담은 채 작아지며 사라짐 (방에 쌓이지 않게). 한 번에 하나만 들 수 있음 (가져다주기 규칙).
    /// 모델 = BlenderWork/InteriorProps/stage_incident_builder.py (ToyBasket · ToyBall · ToyStar · ToyRing).
    /// </summary>
    public sealed partial class InteriorIncidents
    {
        private sealed class ToyItem
        {
            public GameObject Go;
            public Light Glow;
            public float Seed;
            public bool InBasket;
        }

        private sealed partial class Incident
        {
            public readonly List<ToyItem> Toys = new List<ToyItem>();
            public ToyItem CarriedToy;
            public float BasketScale = 1f;
        }

        /// <summary>확인용: 마지막 놀이 소품 놓기 결과.</summary>
        public static string LastToyNote { get; private set; }

        private static readonly string[] ToyNames = { "ToyBall", "ToyStar", "ToyRing" };
        private const int ToyMin = 2;
        private const int ToyMax = 3;
        private static readonly Vector3 ToyHalf = new Vector3(0.2f, 0.15f, 0.2f);
        private static readonly Vector3 BasketHalf = new Vector3(0.36f, 0.3f, 0.36f);
        private const float BasketHalfWidth = 0.32f;
        /// <summary>소품끼리 · 바구니에서 떨어진 거리 (자리가 모자라면 짧게) · 문길에서.</summary>
        private const float ToySpacing = 3f;
        private const float ToySpacingMin = 1.8f;
        private const float ToyDoorMargin = 0.8f;
        private const float BasketBubbleHeight = 0.95f;
        /// <summary>흩어진 소품 위 작은 빛 (높이 · 닿는 거리 · 세기 — 숨 쉬듯 0.5 ~ 1배).</summary>
        private const float ToyGlowHeight = 0.45f;
        private const float ToyGlowRange = 1.6f;
        private const float ToyGlowIntensity = 0.9f;
        private static readonly Color ToyGlowColor = new Color(1f, 0.88f, 0.62f);
        /// <summary>바구니 속 자리 (바구니 기준, m) — 넣은 차례대로.</summary>
        private static readonly Vector3[] BasketSlots = { new Vector3(-0.08f, 0.03f, 0.06f), new Vector3(0.09f, 0.03f, -0.05f), new Vector3(0.01f, 0.12f, 0.01f) };

        private bool PlaceScatteredToys(Incident a)
        {
            var parent = _builder.RoomParent(a.Room);
            if (_s.Toys == null || parent == null)
                return false;
            // 바구니 자리 (분전함과 같은 규칙 — 길을 막지 않고 앞에 설 바닥)
            bool basketFound = false;
            var basketSpot = Vector3.zero;
            var basketFace = Vector3.forward;
            for (int tries = 0; tries < 16 && !basketFound; tries++)
            {
                if (!InteriorSpots.Floor(_builder, a.Room, _rng, BasketHalf, null, 0f, out var s, 0.6f, 3.5f, sightHeight: 0.5f))
                    continue;
                if (InteriorSpots.NearDoor(_builder, a.Room, s + Vector3.up, DecoDoorMargin) || !PropFace(s, BasketHalfWidth, out var f))
                    continue;
                basketFound = true;
                basketSpot = s;
                basketFace = f;
            }
            // 소품 자리 (바구니 · 서로 떨어지게, 넓은 바닥 아무 데나 — 작아서 길을 막지 않음)
            int want = ToyMin + _rng.Next(ToyMax - ToyMin + 1);
            var used = new List<Vector3>();
            if (basketFound)
                used.Add(basketSpot);
            var spots = new List<Vector3>();
            for (int tries = 0; tries < 20 && basketFound && spots.Count < want; tries++)
            {
                float spacing = tries < 12 ? ToySpacing : ToySpacingMin;
                if (!InteriorSpots.Floor(_builder, a.Room, _rng, ToyHalf, used, spacing, out var s, 0.5f, 3.6f, sightHeight: 0.2f))
                    continue;
                if (InteriorSpots.NearDoor(_builder, a.Room, s + Vector3.up, ToyDoorMargin))
                    continue;
                spots.Add(s);
                used.Add(s);
            }
            LastToyNote = $"바구니 {(basketFound ? "놓음" : "자리 없음")} · 소품 {spots.Count}/{want}";
            if (!basketFound || spots.Count < ToyMin)
                return false;

            // 한 벌을 꺼내 바구니 · 소품을 방에 나눠 놓음
            var set = Instantiate(_s.Toys, parent);
            var basket = set.transform.Find("ToyBasket");
            if (basket == null)
            {
                Destroy(set);
                return false;
            }
            basket.SetParent(parent, false);
            basket.SetPositionAndRotation(basketSpot, Quaternion.LookRotation(basketFace, Vector3.up));
            basket.name = "IncidentToyBasket";
            AddMeshCollider(basket);
            a.Prop = basket.gameObject;
            var bubbleAt = new GameObject("BubbleAt").transform;
            bubbleAt.SetParent(basket, false);
            bubbleAt.localPosition = new Vector3(0f, BasketBubbleHeight, 0f);
            a.BubbleAt = bubbleAt;
            int total = spots.Count;
            var put = basket.gameObject.AddComponent<InteriorInteractable>();
            put.HoldSeconds = _s.PickupHold;
            put.Prompt = () => $"바구니에 넣기 ({ToysIn(a)}/{total})  <color=#FF8A5C>{InteriorIncidentRules.Title(a.Kind)}</color>";
            put.Blocked = () => a.CarriedToy == null ? "흩어진 소품을 먼저 주우세요" : null;
            put.Completed = () => PutToy(a, basket);

            var names = new List<string>(ToyNames);
            for (int i = names.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (names[i], names[j]) = (names[j], names[i]);
            }
            for (int i = 0; i < total; i++)
            {
                var t = set.transform.Find(names[i % names.Count]);
                if (t == null)
                    continue;
                if (i >= names.Count)
                    t = Instantiate(t.gameObject, set.transform).transform;   // 소품이 3개를 넘으면 (지금은 없음) 같은 것을 하나 더
                t.SetParent(parent, false);
                t.SetPositionAndRotation(spots[i], Quaternion.Euler(0f, (float)_rng.NextDouble() * 360f, 0f));
                t.name = "IncidentToy" + i;
                AddMeshCollider(t);
                // 작은 빛: 밤 · 어두운 방에서도 흩어진 소품을 찾게 (천천히 밝아졌다 어두워짐 — 바구니에 넣으면 끔)
                var glowGo = new GameObject("ToyGlow");
                glowGo.transform.SetParent(t, false);
                glowGo.transform.localPosition = new Vector3(0f, ToyGlowHeight, 0f);
                var glow = glowGo.AddComponent<Light>();
                glow.type = LightType.Point;
                glow.range = ToyGlowRange;
                glow.color = ToyGlowColor;
                glow.intensity = ToyGlowIntensity;
                glow.shadows = LightShadows.None;
                var toy = new ToyItem { Go = t.gameObject, Glow = glow, Seed = (float)_rng.NextDouble() * 6f };
                var pick = t.gameObject.AddComponent<InteriorInteractable>();
                pick.HoldSeconds = _s.PickupHold;
                pick.Prompt = () => $"소품 줍기  <color=#FF8A5C>{InteriorIncidentRules.Title(a.Kind)}</color>";
                pick.Blocked = () => a.CarriedToy != null ? "들고 있는 소품을 먼저 바구니에 넣으세요" : null;
                pick.Completed = () => PickToy(a, toy);
                a.Toys.Add(toy);
                a.Extra.Add(t.gameObject);
            }
            Destroy(set);   // 안 쓴 소품 · 빈 뿌리
            return a.Toys.Count >= ToyMin;
        }

        private static int ToysIn(Incident a)
        {
            int n = 0;
            foreach (var t in a.Toys)
            {
                if (t.InBasket)
                    n++;
            }
            return n;
        }

        private void PickToy(Incident a, ToyItem toy)
        {
            if (a.Done || a.CarriedToy != null || toy.InBasket || toy.Go == null)
                return;
            a.CarriedToy = toy;
            AudioService.TryPlayAt(l => l.CratePickup, toy.Go.transform.position);
            toy.Go.SetActive(false);
            Message?.Invoke("소품을 들었어요 · 바구니에 넣어 주세요", false);
        }

        private void PutToy(Incident a, Transform basket)
        {
            var toy = a.CarriedToy;
            if (a.Done || toy == null || toy.Go == null)
                return;
            int slot = ToysIn(a);
            a.CarriedToy = null;
            toy.InBasket = true;
            var t = toy.Go.transform;
            foreach (var c in t.GetComponentsInChildren<Collider>())
                Destroy(c);
            var use = t.GetComponent<InteriorInteractable>();
            if (use != null)
                Destroy(use);
            if (toy.Glow != null)
                Destroy(toy.Glow.gameObject);
            toy.Glow = null;
            t.SetParent(basket, false);
            t.localPosition = BasketSlots[Mathf.Min(slot, BasketSlots.Length - 1)];
            t.localRotation = Quaternion.Euler(slot == 2 ? 12f : 0f, slot * 70f, 0f);
            toy.Go.SetActive(true);
            AudioService.TryPlayAt(l => l.OportPlop, basket.position + Vector3.up * 0.3f);
            if (ToysIn(a) >= a.Toys.Count)
                Resolve(a);
        }

        /// <summary>흩어진 소품의 빛이 천천히 밝아졌다 어두워짐 (밝기로 눈에 띔).</summary>
        private static void AnimateToys(Incident a, float now)
        {
            foreach (var t in a.Toys)
            {
                if (t.Glow != null)
                    t.Glow.intensity = ToyGlowIntensity * (0.75f + 0.25f * Mathf.Sin(now * 2.4f + t.Seed));
            }
        }

        /// <summary>해결 뒤: 바구니가 소품을 담은 채 작아지며 사라짐 (0.9초 뒤부터).</summary>
        private void AnimateToysDone(Incident a, float dt)
        {
            if (a.Age - a.DoneAge <= 0.9f || a.Prop == null)
                return;
            a.BasketScale = Mathf.MoveTowards(a.BasketScale, 0f, dt / 0.5f);
            a.Prop.transform.localScale = Vector3.one * Mathf.SmoothStep(0f, 1f, a.BasketScale);
        }

        /// <summary>정리: 해결했으면 바구니(담긴 소품 포함)를 지움 — 놓쳤으면 End가 바구니 · 흩어진 소품을 지움.</summary>
        private void RestoreToys(Incident a)
        {
            if (a.Done && a.Prop != null)
                Destroy(a.Prop);
            a.CarriedToy = null;
        }

        /// <summary>메시 경계 상자로 충돌체 (소품 · 바구니).</summary>
        private static void AddMeshCollider(Transform t)
        {
            var mf = t.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null)
                return;
            var col = t.gameObject.AddComponent<BoxCollider>();
            col.center = mf.sharedMesh.bounds.center;
            col.size = mf.sharedMesh.bounds.size;
        }
    }
}
