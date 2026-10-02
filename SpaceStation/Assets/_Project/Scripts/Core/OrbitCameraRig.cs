using UnityEngine;

namespace SpaceStation.Core
{
    /// <summary>
    /// 궤도 카메라 상태와 계산 (MonoBehaviour 없음).
    /// 초점(Focus)을 중심으로 Yaw/Pitch/Distance로 카메라 위치를 정한다.
    /// </summary>
    public sealed class OrbitCameraRig
    {
        public Vector3 Focus { get; private set; }
        public float Yaw { get; private set; }
        public float Pitch { get; private set; }
        public float Distance { get; private set; }

        public float MinPitch { get; set; } = -85f;
        public float MaxPitch { get; set; } = 85f;
        public float MinDistance { get; set; } = 3f;
        public float MaxDistance { get; set; } = 80f;

        public OrbitCameraRig(Vector3 focus, float yaw, float pitch, float distance)
        {
            Focus = focus;
            Yaw = yaw;
            Pitch = pitch;
            Distance = distance;
            Clamp();
        }

        public Quaternion Rotation => Quaternion.Euler(Pitch, Yaw, 0f);
        public Vector3 Position => Focus - Rotation * Vector3.forward * Distance;

        /// <summary>상태를 직접 설정 (세이브 복원). 한계값으로 잘린다.</summary>
        public void Set(Vector3 focus, float yaw, float pitch, float distance)
        {
            Focus = focus;
            Yaw = Mathf.Repeat(yaw, 360f);
            Pitch = pitch;
            Distance = distance;
            Clamp();
        }

        public void Orbit(float deltaYaw, float deltaPitch)
        {
            Yaw = Mathf.Repeat(Yaw + deltaYaw, 360f);
            Pitch += deltaPitch;
            Clamp();
        }

        /// <summary>줌. amount &gt; 0 이면 가까워진다. 거리에 비례해 배율로 적용한다.</summary>
        public void Zoom(float amount)
        {
            Distance *= Mathf.Pow(0.9f, amount);
            Clamp();
        }

        /// <summary>화면 기준(오른쪽/위) 평행 이동. 거리가 멀수록 크게 움직인다.</summary>
        public void PanScreen(float right, float up)
        {
            var rot = Rotation;
            Focus += (rot * Vector3.right * right + rot * Vector3.up * up) * Distance;
        }

        /// <summary>수평면 이동 (카메라 Yaw 기준 전후좌우). 높이는 바뀌지 않는다.</summary>
        public void PanHorizontal(float right, float forward)
        {
            var yawRot = Quaternion.Euler(0f, Yaw, 0f);
            Focus += yawRot * new Vector3(right, 0f, forward);
        }

        public void MoveVertical(float up)
        {
            Focus += Vector3.up * up;
        }

        private void Clamp()
        {
            Pitch = Mathf.Clamp(Pitch, MinPitch, MaxPitch);
            Distance = Mathf.Clamp(Distance, MinDistance, MaxDistance);
        }
    }
}
