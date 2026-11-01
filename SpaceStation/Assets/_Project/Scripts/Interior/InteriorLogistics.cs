using System;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 진화 물류 허브 운반 (사용자 2026-10-31 물류 허브 시안 1 "이대로 유니티로 옮겨줘"):
    /// ① 자동 창고 칸마다 스태커 크레인 — 기둥이 바닥 레일을 따라 가고 짐판이 오르내리며, 나르는 상자 둘을 비워 둔 칸 셋 사이로
    ///    밀어 넣고 꺼낸다 (짐판에 상자를 실은 채 빈 칸에 넣음 → 다른 찬 칸에서 꺼냄 → 반복).
    /// ② 머리 위 셔틀 레일 — 셔틀이 하역문 쪽 덮개 속에서 상자를 받아 레일 끝 덮개 속에 내려놓고 빈 채로 돌아옴
    ///    (상자는 덮개 속에서만 켜고 꺼서 허공에서 생기거나 사라지지 않게).
    /// 모듈이 멈추면(비활성 · 전력 부족) 그 자리에 멈춤. 좌표는 모두 템플릿 루트 로컬 (모델 자세 그대로 = 0).
    /// </summary>
    public sealed class InteriorLogistics : InteriorRoomDisplay
    {
        private const float MastSpeed = 0.9f;     // m/s
        private const float CarSpeed = 0.5f;      // m/s
        private const float SlideTime = 0.8f;
        private const float StackerPause = 0.4f;
        private const float PodSpeed = 0.7f;
        private const float PodPause = 1.2f;

        [Serializable]
        public struct Stacker
        {
            public Transform Mast;      // 레일을 따라 x (모델 자세 x = RestX)
            public Transform Car;       // Mast 자식 — 짐판 윗면 높이 (모델 = RestCarY)
            public float RestX;
            public float RestCarY;
            public float CarZ;          // 짐판 위 상자 가운데 z
            public Transform[] Crates;  // 나르는 상자 (피벗 = 바닥 가운데). 처음: 0 = 짐판 위, 나머지 = Slots에서 FirstFull부터
            public Vector3[] Slots;     // 넣고 빼는 칸 (상자 바닥 가운데)
            public int FirstFull;       // 처음 Crates[1]이 든 칸
        }

        [Serializable]
        public struct Pod
        {
            public Transform Body;      // 레일을 따라 x (모델 자세 x = RestX)
            public Transform Crate;     // Body 자식 — 덮개 속에서 켜고 끔
            public float RestX;
            public float PickX;         // 상자를 받는 덮개 (하역문 쪽)
            public float DropX;         // 상자를 내리는 덮개 (레일 끝 = 창고 쪽)
        }

        [SerializeField] private Stacker[] _stackers = Array.Empty<Stacker>();
        [SerializeField] private Pod[] _pods = Array.Empty<Pod>();

        private sealed class StackerState
        {
            public Vector3 MastRest, CarRest;
            public float X, Y, FromX, FromY, ToX, ToY;
            public float T, Duration, Pause;
            public int Held = -1;           // 짐판 위 상자 (Crates 번호)
            public int[] SlotCrate;         // 칸마다 든 상자 (−1 = 빈 칸)
            public int Target = -1;         // 가는 칸
            public int LastFilled = -1;     // 방금 넣은 칸 (바로 다시 꺼내지 않음)
            public int LastEmptied = -1;    // 방금 꺼낸 칸 (바로 다시 넣지 않음)
            public bool Moving, Sliding, Pushing;
            public int SlideCrate;
        }

        private sealed class PodState
        {
            public Vector3 Rest;
            public float X, FromX, ToX, T, Duration, Pause;
            public bool Loaded, Going;
        }

        private StackerState[] _s;
        private PodState[] _p;
        private bool _ready;

        public void Configure(Stacker[] stackers, Pod[] pods)
        {
            _stackers = stackers ?? Array.Empty<Stacker>();
            _pods = pods ?? Array.Empty<Pod>();
        }

        private void Setup()
        {
            if (_ready)
                return;
            _ready = true;
            _s = new StackerState[_stackers.Length];
            for (int i = 0; i < _stackers.Length; i++)
            {
                var d = _stackers[i];
                var st = _s[i] = new StackerState
                {
                    MastRest = d.Mast != null ? d.Mast.localPosition : Vector3.zero,
                    CarRest = d.Car != null ? d.Car.localPosition : Vector3.zero,
                    X = d.RestX,
                    Y = d.RestCarY,
                    SlotCrate = new int[d.Slots != null ? d.Slots.Length : 0],
                };
                for (int k = 0; k < st.SlotCrate.Length; k++)
                    st.SlotCrate[k] = -1;
                if (d.Crates != null && d.Crates.Length > 0 && d.Car != null && d.Crates[0] != null)
                {
                    st.Held = 0;
                    d.Crates[0].SetParent(d.Car, true);
                }
                for (int c = 1; d.Crates != null && c < d.Crates.Length; c++)
                {
                    int slot = (d.FirstFull + c - 1) % Mathf.Max(1, st.SlotCrate.Length);
                    if (slot < st.SlotCrate.Length)
                        st.SlotCrate[slot] = c;
                }
                st.Pause = 0.5f + i * 1.3f;   // 두 칸이 같은 박자로 움직이지 않게
            }
            _p = new PodState[_pods.Length];
            for (int i = 0; i < _pods.Length; i++)
            {
                var d = _pods[i];
                _p[i] = new PodState { Rest = d.Body != null ? d.Body.localPosition : Vector3.zero, X = d.RestX, Loaded = d.Crate != null && d.Crate.gameObject.activeSelf };
                if (d.Crate != null && d.Body != null)
                    d.Crate.SetParent(d.Body, true);
                BeginPod(i);
            }
        }

        protected override void Refresh()
        {
            Setup();
        }

        private void Update()
        {
            if (!_ready || !Active)
                return;   // 모듈이 멈추면 그 자리에 멈춤
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            for (int i = 0; i < _s.Length; i++)
                UpdateStacker(i, dt);
            for (int i = 0; i < _p.Length; i++)
                UpdatePod(i, dt);
        }

        // ---------------- 스태커 ----------------

        private void UpdateStacker(int i, float dt)
        {
            var d = _stackers[i];
            var st = _s[i];
            if (d.Mast == null || d.Car == null || st.SlotCrate.Length == 0)
                return;
            if (st.Pause > 0f)
            {
                st.Pause -= dt;
                return;
            }
            if (st.Moving)
            {
                st.T = Mathf.Min(1f, st.T + dt / st.Duration);
                float u = Smooth(st.T);
                st.X = Mathf.Lerp(st.FromX, st.ToX, u);
                st.Y = Mathf.Lerp(st.FromY, st.ToY, u);
                d.Mast.localPosition = st.MastRest + Vector3.right * (st.X - d.RestX);
                d.Car.localPosition = st.CarRest + Vector3.up * (st.Y - d.RestCarY);
                if (st.T >= 1f)
                {
                    st.Moving = false;
                    StartSlide(d, st);
                }
                return;
            }
            if (st.Sliding)
            {
                st.T = Mathf.Min(1f, st.T + dt / SlideTime);
                var crate = d.Crates[st.SlideCrate];
                var slot = d.Slots[st.Target];
                var onCar = new Vector3(slot.x, slot.y, d.CarZ);
                float u = Smooth(st.T);
                crate.localPosition = st.Pushing ? Vector3.Lerp(onCar, slot, u) : Vector3.Lerp(slot, onCar, u);
                if (st.T >= 1f)
                {
                    st.Sliding = false;
                    if (st.Pushing)
                    {
                        st.SlotCrate[st.Target] = st.SlideCrate;
                        st.LastFilled = st.Target;
                        st.Held = -1;
                    }
                    else
                    {
                        crate.SetParent(d.Car, true);
                        st.Held = st.SlideCrate;
                        st.LastEmptied = st.Target;
                    }
                    st.Pause = StackerPause;
                    st.Target = -1;
                }
                return;
            }
            // 다음 칸: 실었으면 방금 꺼낸 칸이 아닌 빈 칸, 비었으면 방금 채운 칸이 아닌 찬 칸 (제자리 넣고 빼기 없이)
            st.Target = PickSlot(st, st.Held >= 0);
            if (st.Target < 0)
            {
                st.Pause = 1f;
                return;
            }
            var to = d.Slots[st.Target];
            st.FromX = st.X;
            st.FromY = st.Y;
            st.ToX = to.x;
            st.ToY = to.y;
            st.Duration = Mathf.Max(0.3f, Mathf.Abs(st.ToX - st.FromX) / MastSpeed, Mathf.Abs(st.ToY - st.FromY) / CarSpeed);
            st.T = 0f;
            st.Moving = true;
        }

        private static int PickSlot(StackerState st, bool wantEmpty)
        {
            int count = 0, pick = -1;
            int avoid = wantEmpty ? st.LastEmptied : st.LastFilled;
            int candidates = wantEmpty ? st.SlotCrate.Length - Occupied(st) : Occupied(st);
            for (int k = 0; k < st.SlotCrate.Length; k++)
            {
                bool empty = st.SlotCrate[k] < 0;
                if (empty != wantEmpty || (k == avoid && candidates > 1))
                    continue;
                count++;
                if (UnityEngine.Random.Range(0, count) == 0)
                    pick = k;   // 고른 칸 중 하나를 고르게
            }
            return pick;
        }

        private static int Occupied(StackerState st)
        {
            int n = 0;
            foreach (int c in st.SlotCrate)
            {
                if (c >= 0)
                    n++;
            }
            return n;
        }

        private void StartSlide(Stacker d, StackerState st)
        {
            st.T = 0f;
            st.Sliding = true;
            st.Pushing = st.Held >= 0;
            if (st.Pushing)
            {
                st.SlideCrate = st.Held;
                d.Crates[st.Held].SetParent(transform, true);   // 선반 칸으로 밀어 넣음 (루트 로컬로 움직임)
            }
            else
            {
                st.SlideCrate = st.SlotCrate[st.Target];
                st.SlotCrate[st.Target] = -1;
            }
        }

        // ---------------- 셔틀 ----------------

        private void BeginPod(int i)
        {
            var d = _pods[i];
            var p = _p[i];
            p.FromX = p.X;
            p.ToX = p.Loaded ? d.DropX : d.PickX;   // 실었으면 창고 쪽 끝 덮개로, 비었으면 하역문 쪽 덮개로
            p.Duration = Mathf.Max(0.3f, Mathf.Abs(p.ToX - p.FromX) / PodSpeed);
            p.T = 0f;
            p.Going = true;
        }

        private void UpdatePod(int i, float dt)
        {
            var d = _pods[i];
            var p = _p[i];
            if (d.Body == null)
                return;
            if (p.Pause > 0f)
            {
                p.Pause -= dt;
                if (p.Pause <= 0f)
                    BeginPod(i);
                return;
            }
            if (!p.Going)
                return;
            p.T = Mathf.Min(1f, p.T + dt / p.Duration);
            p.X = Mathf.Lerp(p.FromX, p.ToX, Smooth(p.T));
            d.Body.localPosition = p.Rest + Vector3.right * (p.X - d.RestX);
            if (p.T < 1f)
                return;
            p.Going = false;
            p.Loaded = !p.Loaded;   // 덮개 속: 내려놓거나 받음
            if (d.Crate != null)
                d.Crate.gameObject.SetActive(p.Loaded);
            p.Pause = PodPause;
        }

        private static float Smooth(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }
    }
}
