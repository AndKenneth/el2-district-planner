using System.Text;
using Amplitude;
using Amplitude.Mercury.Data.World;
using Amplitude.Mercury.Interop;

namespace DistrictPlanner
{
    // Signed yields with the game's inline yield symbols, e.g. "+4[MoneyColored] -3[FoodColored]".
    internal static class YieldText
    {
        public static string Format(in FimsInfo yields)
        {
            var sb = new StringBuilder();
            Append(sb, yields.Food, "FoodColored");
            Append(sb, yields.Industry, "IndustryColored");
            Append(sb, yields.Money, "MoneyColored");
            Append(sb, yields.Science, "ScienceColored");
            Append(sb, yields.Influence, "InfluenceColored");
            Append(sb, yields.Approval, "PublicOrderColored");
            return sb.ToString();
        }

        // One yield, e.g. "+6[FoodColored]".
        public static string Format(UIResourceType type, FixedPoint value)
        {
            var sb = new StringBuilder();
            Append(sb, value, Symbol(type));
            return sb.ToString();
        }

        // A cost, unsigned, e.g. "50[InfluenceColored]".
        public static string Cost(UIResourceType type, FixedPoint value) =>
            ((float)value).ToString("0.#") + "[" + Symbol(type) + "]";

        private static string Symbol(UIResourceType type)
        {
            switch (type)
            {
                case UIResourceType.Food: return "FoodColored";
                case UIResourceType.Industry: return "IndustryColored";
                case UIResourceType.Money: return "MoneyColored";
                case UIResourceType.Science: return "ScienceColored";
                case UIResourceType.Influence: return "InfluenceColored";
                case UIResourceType.Approval: return "PublicOrderColored";
                default: return type.ToString();
            }
        }

        private static void Append(StringBuilder sb, FixedPoint value, string symbol)
        {
            float amount = (float)value;
            if (amount > -0.5f && amount < 0.5f)
            {
                return;
            }
            if (sb.Length > 0)
            {
                sb.Append(' ');
            }
            sb.Append(amount > 0 ? "+" : "").Append(amount.ToString("0.#")).Append('[').Append(symbol).Append(']');
        }
    }
}
