namespace BlockMap
{
    /// <summary>方块的渲染方式 —— 决定用哪个 shader</summary>
    public enum BlockRenderMode
    {
        Opaque,        // 普通不透明
        Cutout,        // alpha 镂空（树叶 / 栅栏）
        Transparent,   // 真半透明（玻璃 / 水）
        Animated       // 序列帧动画（岩浆 / 流水）
    }
}
