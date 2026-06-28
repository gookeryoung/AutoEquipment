using System.Collections.Generic;
using RimWorld;
using Verse;

namespace AutoEquipment.Scoring
{
    /// <summary>
    /// 评分管线：按顺序执行所有 IScorer 策略，累加分数到 ScoreBreakdown。
    /// 设计模式：责任链/管线——每个 Scorer 独立处理，结果累加。
    /// 否决（Veto）会短路后续 Scorer（除耐久修正外）。
    /// </summary>
    /// <typeparam name="TThing">被评分对象类型</typeparam>
    public class ScoringPipeline<TThing> where TThing : Thing
    {
        private readonly List<IScorer<TThing>> scorers;

        public ScoringPipeline(List<IScorer<TThing>> scorers)
        {
            this.scorers = scorers ?? new List<IScorer<TThing>>();
        }

        /// <summary>
        /// 执行评分管线，返回带明细的 ScoreBreakdown。
        /// </summary>
        public ScoreBreakdown Evaluate(Pawn pawn, TThing gear, Role role,
                                       GearContext context, GearWeights weights)
        {
            var breakdown = new ScoreBreakdown();

            // 按顺序执行所有 Scorer
            for (int i = 0; i < scorers.Count; i++)
            {
                // 否决后短路：跳过后续所有 Scorer
                // 耐久修正 Scorer 即使在管线末尾也不会执行——
                // 否决分已是 -9000，再乘以耐久系数无意义
                if (breakdown.Vetoed)
                {
                    break;
                }

                try
                {
                    scorers[i].Score(pawn, gear, role, context, weights, breakdown);
                }
                catch (System.Exception ex)
                {
                    // 单个 Scorer 失败不应中断整个管线
                    Log.WarningOnce($"[AutoEquipment] 评分器 {scorers[i].Name} 失败: {ex.Message}",
                        pawn.thingIDNumber ^ gear.thingIDNumber ^ i);
                }
            }

            return breakdown;
        }
    }
}
