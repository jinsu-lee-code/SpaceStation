using UnityEngine;

namespace SpaceStation.Building
{
    /// <summary>
    /// 도는 구름 아래 빗줄기가 진주 아치를 뚫고 내려가지 않게 (사용자 2026-10-29 플레이 — 바이오스피어, StationArtBuilder가 자동 배선):
    /// 빗줄기 재질(SpaceStation/Rain)을 이 모듈 것으로 복제해 모델 좌표(구름이 도는 부품의 회전과 무관한 모듈 기준)를 넘기면,
    /// 셰이더가 가운데에서 본 각도가 아치 쪽인 빗줄기 토막만 감춘다 (아치 = 가운데에서 바깥으로 뻗은 가는 띠).
    /// 고스트(배치 미리보기)에서는 하지 않음.
    /// </summary>
    public sealed class RainShelterFx : MonoBehaviour
    {
        private static readonly int WorldToModuleId = Shader.PropertyToID("_WorldToModule");

        [Tooltip("모델 기준 좌표의 기준 (모델 \"Model\")")]
        [SerializeField] private Transform _module;
        [Tooltip("아치가 뻗어 나가는 가운데 (모델 기준 x, z)")]
        [SerializeField] private Vector2 _center = new Vector2(1f, 1f);
        [Tooltip("아치 방향 (도, 모델 기준 x축에서 z축 쪽으로) — 셋까지")]
        [SerializeField] private Vector3 _anglesDeg = new Vector3(30f, 150f, 270f);
        [Tooltip("아치 띠 반쪽 너비 (덩굴 포함)")]
        [SerializeField] private float _halfWidth = 0.06f;

        private Renderer _renderer;
        private Material _instance;

        private void Start()
        {
            var view = GetComponentInParent<ModuleView>();
            if (view != null && view.Module != null)
                Play();
        }

        /// <summary>빗줄기 재질을 이 모듈 것으로 바꾸고 아치 자리를 넘김 (Start가 부름 — 확인용으로 미리보기에서 직접 불러도 됨).</summary>
        public void Play()
        {
            if (_instance != null)
                return;
            _renderer = GetComponent<Renderer>();
            if (_renderer == null || _module == null)
                return;
            var mats = _renderer.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null || mats[i].shader.name != "SpaceStation/Rain")
                    continue;
                _instance = new Material(mats[i]) { name = mats[i].name + " (Shelter)" };
                _instance.SetFloat("_Shelter", 1f);
                _instance.SetVector("_ShelterCenter", new Vector4(_center.x, _center.y, _halfWidth, 0f));
                _instance.SetVector("_ShelterAngles", new Vector4(_anglesDeg.x * Mathf.Deg2Rad, _anglesDeg.y * Mathf.Deg2Rad, _anglesDeg.z * Mathf.Deg2Rad, 0f));
                mats[i] = _instance;
                _renderer.sharedMaterials = mats;
                break;
            }
            LateUpdate();
        }

        private void LateUpdate()
        {
            if (_instance != null)
                _instance.SetMatrix(WorldToModuleId, _module.worldToLocalMatrix);
        }

        private void OnDestroy()
        {
            if (_instance == null)
                return;
            if (Application.isPlaying)
                Destroy(_instance);
            else
                DestroyImmediate(_instance);
        }
    }
}
