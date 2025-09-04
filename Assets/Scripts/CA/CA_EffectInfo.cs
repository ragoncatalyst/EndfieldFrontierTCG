using System;
using System.Collections;
using UnityEngine;

namespace EndfieldFrontierTCG.CA
{
    // 独立效果表：按 CA_ID（去左侧多余 0 后再 -1）选择对应效果，并与卡牌来源 GameObject 进行绑定
    public static class CA_EffectInfo
    {
        // 以 ArrayList 存储所有效果绑定器（每个元素是 Action<GameObject>）
        private static readonly ArrayList s_Effects = new ArrayList();

        // 初始化：在这里登记你的效果（示例放了两个，可自行扩展/替换）
        static CA_EffectInfo()
        {
            // 示例保留，但默认不在生成时绑定，避免初始自发运动
            s_Effects.Add(new Action<GameObject>(BindSpinEffect)); // index 0 → CA_ID 1
            s_Effects.Add(new Action<GameObject>(BindPulseScaleEffect)); // index 1 → CA_ID 2
            // 后续可继续 Add(...)：index n → CA_ID n+1
        }

        // 入口：根据字符串 CA_ID 绑定对应效果
        public static void BindEffectScripts(GameObject cardSource, string caId)
        {
            int index = IndexFromIdString(caId);
            InvokeEffect(index, cardSource);
        }

        // 入口：根据整型 CA_ID 绑定对应效果
        public static void BindEffectScripts(GameObject cardSource, int caId)
        {
            int index = Mathf.Max(0, caId - 1);
            InvokeEffect(index, cardSource);
        }

        // 裁剪左侧所有多余的 0，再 -1 得到索引；异常时返回 0
        private static int IndexFromIdString(string caId)
        {
            if (string.IsNullOrWhiteSpace(caId)) return 0;
            string trimmed = caId.Trim();
            // 去掉左侧多余 0
            int i = 0;
            while (i < trimmed.Length && trimmed[i] == '0') i++;
            string core = i >= trimmed.Length ? "0" : trimmed.Substring(i);
            if (!int.TryParse(core, out int idValue)) idValue = 1; // 回退为 1
            return Mathf.Max(0, idValue - 1);
        }

        // 调用指定索引的效果
        private static void InvokeEffect(int index, GameObject host)
        {
            if (host == null) return;
            if (index < 0 || index >= s_Effects.Count) return; // 越界无效果
            if (s_Effects[index] is Action<GameObject> binder)
            {
                binder?.Invoke(host);
            }
        }

        // ========== 示例效果实现（可替换/扩展） ==========
        private static void BindSpinEffect(GameObject host)
        {
            if (host.GetComponent<CA_SpinEffect>() == null)
            {
                host.AddComponent<CA_SpinEffect>();
            }
        }

        private static void BindPulseScaleEffect(GameObject host)
        {
            if (host.GetComponent<CA_PulseScaleEffect>() == null)
            {
                host.AddComponent<CA_PulseScaleEffect>();
            }
        }
    }

    // 旋转示例
    public class CA_SpinEffect : MonoBehaviour
    {
        public float speedDegPerSec = 45f;
        private void Update()
        {
            transform.Rotate(Vector3.up, speedDegPerSec * Time.deltaTime, Space.World);
        }
    }

    // 呼吸缩放示例
    public class CA_PulseScaleEffect : MonoBehaviour
    {
        public float amp = 0.05f;
        public float period = 1.2f;
        private Vector3 _base;
        private void Awake() { _base = transform.localScale == Vector3.zero ? Vector3.one : transform.localScale; }
        private void Update()
        {
            float t = Mathf.PingPong(Time.time, period) / period; // 0..1..0
            float k = 1f + (t * 2f - 1f) * amp; // [-amp, +amp]
            transform.localScale = _base * k;
        }
    }
}
