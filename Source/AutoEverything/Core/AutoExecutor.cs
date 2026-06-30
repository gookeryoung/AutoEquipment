using System;
using RimWorld;
using Verse;
using AutoEverything.AutoWork;

namespace AutoEverything.Core
{
    /// <summary>
    /// 全局自动执行器：周期触发工作重配与人员评级。
    ///
    /// 设计模式：复用 SidearmAllocator/BeltAllocator 的静态门控模式，
    /// 由 CompGearManager.CompTick 每 tick 调用 TryTick()，内部静态门控每 60 tick 检查一次。
    /// 不新增 MapComponent/GameComponent，KISS 原则——CompTick 已是现成的每 tick 入口。
    ///
    /// 触发条件：
    /// - 周期触发：每 3000 tick（约 50 秒）执行一次工作重配与人员评级
    /// - 新增殖民者：殖民者数量增加时立即触发（不弹消息框）
    /// - ITab 勾选：玩家在面板勾选时立即触发一次（弹消息框反馈）
    ///
    /// 首次初始化守卫：lastWorkTick/lastTierTick &lt; 0 时设为当前 tick，不触发执行，
    /// 避免存档加载后立即执行造成卡顿。
    /// </summary>
    internal static class AutoExecutor
    {
        // 周期触发间隔：3000 tick ≈ 50 秒
        // 工作重配与人员评级均为非紧急操作，延迟可接受
        private const int ExecuteInterval = 3000;

        // 殖民者数量检查间隔：60 tick ≈ 1 秒
        // 每 tick 查询 PawnsFinder.AllMaps_FreeColonists.Count 有少量开销，60 tick 检查一次足够
        private const int CheckInterval = 60;

        private static int lastCheckTick = -9999;
        private static int lastWorkTick = -9999;
        private static int lastTierTick = -9999;

        // 殖民者数量缓存：-1 = 首次只记录不触发，避免存档加载误触发
        private static int lastColonistCount = -1;

        // 错误去重 salt：每个错误点独立，避免跨方法冲突
        private const int WorkErrorSalt = 0xA200;
        private const int TierErrorSalt = 0xA300;

        /// <summary>
        /// 由 CompGearManager.CompTick 每 tick 调用。
        /// 静态门控：每 60 tick 检查一次殖民者数量变化与周期触发。
        /// 自动周期路径不弹消息框（避免刷屏），仅走 AEDebug.Log。
        /// </summary>
        public static void TryTick()
        {
            int tick = Find.TickManager.TicksGame;

            // 静态门控：每 60 tick 才执行一次实际检查
            if (tick - lastCheckTick < CheckInterval) return;
            lastCheckTick = tick;

            // 首次初始化守卫：记录当前 tick 与殖民者数量，不触发执行
            // 避免存档加载后立即执行造成卡顿
            if (lastWorkTick < 0)
            {
                lastWorkTick = tick;
                lastTierTick = tick;
                lastColonistCount = PawnsFinder.AllMaps_FreeColonists.Count;
                return;
            }

            // 新增殖民者检测：数量增加时立即触发工作+评级（不弹消息）
            int currentCount = PawnsFinder.AllMaps_FreeColonists.Count;
            if (currentCount > lastColonistCount)
            {
                lastColonistCount = currentCount;
                ExecuteWork(tick, showMessage: false);
                ExecuteTier(tick, showMessage: false);
                return;
            }
            lastColonistCount = currentCount;

            // 周期触发：每 3000 tick 执行一次
            if (tick - lastWorkTick >= ExecuteInterval)
            {
                ExecuteWork(tick, showMessage: false);
            }
            if (tick - lastTierTick >= ExecuteInterval)
            {
                ExecuteTier(tick, showMessage: false);
            }
        }

        /// <summary>
        /// ITab 勾选时调用：立即执行工作重配并弹消息框反馈。
        /// </summary>
        public static void TriggerWorkNow()
        {
            ExecuteWork(Find.TickManager.TicksGame, showMessage: true);
        }

        /// <summary>
        /// ITab 勾选时调用：立即执行人员评级并弹消息框反馈。
        /// </summary>
        public static void TriggerTierNow()
        {
            ExecuteTier(Find.TickManager.TicksGame, showMessage: true);
        }

        /// <summary>
        /// 执行工作重配：调用 WorkAllocator.ReallocateAll()。
        /// 受 AESettings.autoWorkEnabled 开关控制，关闭时不执行。
        /// try-catch 隔离：失败时 Log.ErrorOnce 记录，不影响其他逻辑。
        /// </summary>
        private static void ExecuteWork(int tick, bool showMessage)
        {
            lastWorkTick = tick;
            if (!AESettings.autoWorkEnabled) return;

            try
            {
                int n = WorkAllocator.ReallocateAll();
                AEDebug.Log(() => $"[AutoExecutor] 工作自动配置: {n} 个殖民者 (tick={tick})");
                if (showMessage)
                {
                    Messages.Message(
                        "AE_GlobalWorkReallocateResult".Translate(n),
                        MessageTypeDefOf.TaskCompletion);
                }
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[AutoEverything] 工作自动配置失败: " + ex.Message, WorkErrorSalt);
            }
        }

        /// <summary>
        /// 执行人员评级：调用 AESettings.ApplyTierTagsWithDefaultSort()。
        /// 受 AESettings.autoTierTag 开关控制，关闭时不执行。
        /// try-catch 隔离：失败时 Log.ErrorOnce 记录，不影响其他逻辑。
        /// </summary>
        private static void ExecuteTier(int tick, bool showMessage)
        {
            lastTierTick = tick;
            if (!AESettings.autoTierTag) return;

            try
            {
                int n = AESettings.ApplyTierTagsWithDefaultSort();
                AEDebug.Log(() => $"[AutoExecutor] 人员自动评级: {n} 个殖民者 (tick={tick})");
                if (showMessage)
                {
                    Messages.Message(
                        "AE_TierTag_ApplyResult".Translate(n),
                        MessageTypeDefOf.TaskCompletion);
                }
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[AutoEverything] 人员自动评级失败: " + ex.Message, TierErrorSalt);
            }
        }
    }
}
