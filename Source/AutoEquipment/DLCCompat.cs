using RimWorld;
using Verse;

namespace AutoEquipment
{
    /// <summary>
    /// DLC 兼容性检测工具：集中包装所有 DLC 特定 API，
    /// 避免未加载 DLC 时直接调用导致 TypeLoadException。
    /// </summary>
    public static class DLCCompat
    {
        // 缓存 DLC 加载状态，避免每次访问 ModsConfig 反复读取
        private static readonly bool Anomaly = ModsConfig.AnomalyActive;
        private static readonly bool Biotech = ModsConfig.BiotechActive;

        /// <summary>
        /// 判断是否为食尸鬼（Anomaly DLC 的变异体）。
        /// 食尸鬼无法使用武器与装备，必须完全排除在装备管理外。
        /// </summary>
        public static bool IsGhoul(Pawn pawn)
        {
            if (!Anomaly || pawn == null) return false;
            try { return pawn.IsGhoul; }
            catch { return false; }
        }

        /// <summary>
        /// 判断是否为奴隶（Biotech DLC 引入的 FromDlc 概念）。
        /// </summary>
        public static bool IsSlave(Pawn pawn)
        {
            if (!Biotech || pawn == null) return false;
            try { return pawn.IsSlave; }
            catch { return false; }
        }

        /// <summary>
        /// 判断是否为非成年（Biotech DLC 的发育阶段）。
        /// </summary>
        public static bool IsChild(Pawn pawn)
        {
            if (!Biotech || pawn == null) return false;
            try { return !pawn.DevelopmentalStage.Adult(); }
            catch { return false; }
        }
    }
}
