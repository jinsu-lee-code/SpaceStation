using System;
using System.IO;
using UnityEngine;

namespace SpaceStation.Audio
{
    /// <summary>
    /// 사운드 검수 도구 (SOUND_LIST_NEW "사운드 작업 지침" 7, 사용자 2026-11-20 "사운드가 적절한지 한 번 더 검수하는 과정"):
    /// 오디오 리스너(메인 카메라)에 붙이면 게임에서 실제로 들리는 소리(모든 소리가 섞인 결과)를 녹음해 WAV로 저장한다.
    /// 게임 기능이 아님 — 검수 때만 붙였다 뗀다. 녹음은 오디오 스레드에서 미리 잡은 버퍼에 복사만 함.
    /// </summary>
    public sealed class ListenerTap : MonoBehaviour
    {
        private float[] _buffer;
        private int _count;
        private int _channels = 2;
        private int _rate;
        private volatile bool _recording;

        public bool Recording => _recording;
        public float Seconds => _rate > 0 ? _count / (float)(_rate * _channels) : 0f;

        public void Begin(float seconds)
        {
            _rate = AudioSettings.outputSampleRate;
            _buffer = new float[Mathf.CeilToInt(seconds * _rate) * 2];
            _count = 0;
            _recording = true;
        }

        public void End() => _recording = false;

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (!_recording || _buffer == null)
                return;
            _channels = channels;
            int n = Math.Min(data.Length, _buffer.Length - _count);
            Array.Copy(data, 0, _buffer, _count, n);
            _count += n;
            if (_count >= _buffer.Length)
                _recording = false;
        }

        /// <summary>16 bit WAV로 저장 (녹음을 멈춘 뒤).</summary>
        public void Save(string path)
        {
            _recording = false;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using var w = new BinaryWriter(File.Create(path));
            int bytes = _count * 2;
            w.Write(new[] { 'R', 'I', 'F', 'F' });
            w.Write(36 + bytes);
            w.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
            w.Write(16);
            w.Write((short)1);
            w.Write((short)_channels);
            w.Write(_rate);
            w.Write(_rate * _channels * 2);
            w.Write((short)(_channels * 2));
            w.Write((short)16);
            w.Write(new[] { 'd', 'a', 't', 'a' });
            w.Write(bytes);
            for (int i = 0; i < _count; i++)
                w.Write((short)Mathf.Clamp(Mathf.RoundToInt(_buffer[i] * 32767f), -32768, 32767));
        }
    }
}
