namespace AutoEverything.Core
{
    /// <summary>
    /// 殖民者栏默认排序方式。
    /// 设计：玩家在 Mod 选项里配置默认排序，ITab 的"全局人物评级"按钮
    /// 一键应用评级前缀并按此排序重排殖民者栏。
    /// </summary>
    public enum ColonistBarSortMode : byte
    {
        /// <summary>不排序：仅应用评级前缀，保留殖民者栏原顺序</summary>
        None = 0,
        /// <summary>按评级降序（S→A→B→C→D→X），同档内按战斗价值降序</summary>
        ByTierThenValue = 1,
        /// <summary>按角色分组（格斗者→射手→医生→工人→无暴力者→猎人→领袖），同角色内按评级降序</summary>
        ByRoleThenTier = 2,
        /// <summary>仅按战斗价值降序（不区分评级，高技能和平主义者可能挤占前列）</summary>
        ByCombatValue = 3
    }
}
