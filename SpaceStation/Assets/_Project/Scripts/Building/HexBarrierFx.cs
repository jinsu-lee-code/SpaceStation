using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceStation.Building
{
    /// <summary>
    /// 1단계 실드 매트릭스 실드 연출 (사용자 2026-10-29 "실드 계열 단계별 이펙트 — 1단계도 조금 강하게", StationArtBuilder가 자동 배선):
    /// 운석을 빗겨낼 때 기존 파란 구면 파문은 그대로 두고, 그 위로 닿은 자리에서 육각 격자 무늬가 구면을 따라 퍼지며 잠깐 비친다
    /// (셰이더 SpaceStation/HexShield — 퍼지는 앞 가장자리 밝음, 뒤로 옅어짐). 2단계 실드 돔 = 오로라 파문, 3단계 궤도 방어망 = 수정 결정 막.
    /// MeteorFx가 실드 모듈에서 이 컴포넌트를 찾으면 파란 파문과 함께 Play. 시간은 게임 시간.
    /// </summary>
    public sealed class HexBarrierFx : MonoBehaviour
    {
        private static readonly int HitDirId = Shader.PropertyToID("_HitDir");
        private static readonly int SpreadId = Shader.PropertyToID("_Spread");
        private static readonly int FadeId = Shader.PropertyToID("_Fade");

        [SerializeField] private Material _material;
        [SerializeField] private float _duration = 0.9f;
        [Tooltip("닿은 자리에서 격자가 끝까지 퍼지는 시간")]
        [SerializeField] private float _spreadTime = 0.55f;

        /// <summary>한 번의 육각 파문 (확인용으로 Begin → Apply(t) → End를 직접 불러도 됨).</summary>
        public sealed class Shot
        {
            public GameObject Go;
            public MeshRenderer Renderer;
            public MaterialPropertyBlock Block;
            public float Radius;
        }

        public float Duration => _duration;

        /// <summary>실드 구면(center, radius)의 contact 자리에서 육각 무늬를 한 번 퍼뜨린다.</summary>
        public void Play(Vector3 center, float radius, Vector3 contact)
        {
            if (_material != null && isActiveAndEnabled)
                StartCoroutine(Run(center, radius, contact));
        }

        private IEnumerator Run(Vector3 center, float radius, Vector3 contact)
        {
            var shot = Begin(center, radius, contact);
            float t = 0f;
            while (t < _duration && shot.Go != null)
            {
                t += Time.deltaTime;
                Apply(shot, t);
                yield return null;
            }
            End(shot);
        }

        public Shot Begin(Vector3 center, float radius, Vector3 contact)
        {
            var go = new GameObject("HexBarrier");
            go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(center, Quaternion.identity); // 물체 기준 = 월드 축 (닿은 방향을 그대로 넘김)
            go.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("New-Sphere.fbx");
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            var shot = new Shot { Go = go, Renderer = r, Block = new MaterialPropertyBlock(), Radius = radius };
            shot.Block.SetVector(HitDirId, (contact - center).normalized);
            Apply(shot, 0f);
            return shot;
        }

        public void Apply(Shot shot, float t)
        {
            float k = Mathf.Clamp01(t / _duration);
            shot.Go.transform.localScale = Vector3.one * shot.Radius * 2f * Mathf.Lerp(0.97f, 1.03f, k); // 기존 파문처럼 살짝 부풂 (기본 구 = 지름 1)
            float s = Mathf.Clamp01(t / _spreadTime);
            shot.Block.SetFloat(SpreadId, 0.5f * (1f - (1f - s) * (1f - s))); // 닿은 자리 → 옆쪽 (셰이더가 멀어질수록 옅게 해 사라짐)
            shot.Block.SetFloat(FadeId, Mathf.Clamp01(t / 0.05f) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1f, k))));
            shot.Renderer.SetPropertyBlock(shot.Block);
        }

        public static void End(Shot shot)
        {
            if (shot.Go == null)
                return;
            if (Application.isPlaying)
                Destroy(shot.Go);
            else
                DestroyImmediate(shot.Go);
        }
    }
}
