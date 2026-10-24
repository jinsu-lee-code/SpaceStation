using System;
using TMPro;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 12-5 템플릿 안 고정 표지 글자 (여객 도킹 포트 탑승구 · 심사 부스 · 방열판 열교환기 이름판 등).
    /// 모델에는 어두운 화면판만 두고 글자는 여기서 그림 (Blender 빌더 ANCHORS["labels"]). 모듈이 꺼지면 어둡게 (밝기로 구분 — 색약).
    /// </summary>
    public sealed class InteriorSignLabels : InteriorRoomDisplay
    {
        [Serializable]
        public struct Label
        {
            public Vector3 Center;
            public Vector3 Normal;
            public Vector3 Up;
            public float Width;
            public float Height;
            public string Text;
        }

        [SerializeField] private Label[] _labels = Array.Empty<Label>();

        private TextMeshPro[] _texts;

        public void Configure(Label[] labels) => _labels = labels ?? Array.Empty<Label>();

        protected override void Refresh()
        {
            if (_texts == null)
            {
                _texts = new TextMeshPro[_labels.Length];
                for (int i = 0; i < _labels.Length; i++)
                {
                    var l = _labels[i];
                    _texts[i] = CreateText("Label", l.Center, l.Normal, l.Up, l.Width, l.Height, 2f, TextAlignmentOptions.Center);
                }
            }
            var color = Active ? ScreenText : ScreenDim;
            for (int i = 0; i < _texts.Length; i++)
                SetText(_texts[i], _labels[i].Text, color);
        }
    }
}
