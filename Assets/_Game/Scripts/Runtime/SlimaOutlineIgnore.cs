using UnityEngine;

namespace SlimaPuzzle.Rendering
{
    /// <summary>
    /// 挂在物体上（含子物体）= 该物体**不参与描边遮罩采样**：
    ///   · 不会被描边
    ///   · 也不会因为描边而占用自己的像素
    ///
    /// 适合起点 / 出口这类在屏幕上只有 2~3 像素的特殊物件 —— 描边会盖住本体的那种。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SlimaOutlineIgnore : MonoBehaviour
    {
    }
}
