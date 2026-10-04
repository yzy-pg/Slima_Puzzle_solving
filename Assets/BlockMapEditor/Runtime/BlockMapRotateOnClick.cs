using UnityEngine;

namespace BlockMap
{
    /// <summary>
    /// 挂到任何模型上：**鼠标左键单击 → 原地旋转 90°**。
    ///
    /// ── 用法（就一步）────────────────────────────────────────────
    ///   把这个脚本挂到你的门（或任何物件）Prefab 的根节点上即可，不用动编辑器。
    ///
    /// ── 三个注意点 ───────────────────────────────────────────────
    ///   1. 要点得到它，物体上必须有 **Collider**（没有碰撞体点不到）
    ///   2. 旋转绕**自身原点**：门请把 Prefab 原点放在**门轴**上
    ///   3. 在**地图编辑器**里（Play 且编辑器开启）鼠标会被编辑器吃掉 →
    ///      这时用下面的 hotkey（默认 `R`）转，或者先刷好朝向再进正式场景
    ///
    /// ── 和 BlockMapDoor 的分工 ───────────────────────────────────
    ///   · 本脚本 = 改**朝向**（每次 90°）
    ///   · BlockMapDoor = 开 / 关（绕门轴摆动一个角度）
    ///   两个可以同时挂在一个门上。
    /// </summary>
    public class BlockMapRotateOnClick : MonoBehaviour
    {
        [Tooltip("每次旋转的角度（度）")]
        public float step = 90f;

        [Tooltip("绕哪个轴转：默认自身 Y 轴（水平旋转）")]
        public Vector3 axis = Vector3.up;

        [Tooltip("键盘快捷键（地图编辑器会吃掉鼠标时用它）；None = 不响应键盘")]
        public KeyCode hotkey = KeyCode.R;

        [Tooltip("旋转用时（0 = 瞬间转到位）")]
        public float duration = 0f;

        Quaternion _from;
        Quaternion _to;
        float _t = 1f;

        void Update()
        {
            if (hotkey != KeyCode.None && Input.GetKeyDown(hotkey)) Rotate();

            if (_t < 1f)
            {
                _t = duration <= 0f ? 1f : Mathf.Min(1f, _t + Time.deltaTime / duration);
                transform.localRotation = Quaternion.Slerp(_from, _to, _t);
            }
        }

        void OnMouseDown()
        {
            Rotate();
        }

        /// <summary>原地转一个 step（默认 90°）。别的脚本也可以直接调。</summary>
        public void Rotate()
        {
            _from = transform.localRotation;
            _to = Quaternion.AngleAxis(step, axis.normalized) * _from;
            _t = duration <= 0f ? 1f : 0f;
            if (_t >= 1f) transform.localRotation = _to;      // 瞬间模式直接落位
        }
    }
}
