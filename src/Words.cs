using System.Collections.Generic;
using Amplitude.Framework;
using Amplitude.Framework.Localization;
using Amplitude.Mercury.UI;

namespace DistrictPlanner
{
    // Every word the mod shows, in the game's language. Wherever the game has the word, its own translation is used, so
    // terms match the rest of the UI; the English fallbacks only show if a game update removes a key. Unity thread only.
    internal static class Words
    {
        public static string Foundation => Game("%FoundationTitle", "Foundation");

        public static string Tile => Game("%NestedTooltipGameplay_GameTile_Title", "Tile");

        public static string ConstructionQueue => Game("%CityWindow_ConstructionQueueTitle", "Construction Queue");

        public static string BuyTitle => Game("%CityConstructionModeFoundationTitle", "Foundation");

        // "Foundation Cost: 50[Influence]".
        public static string FoundationCost(string cost) =>
            Utils.TextUtils.StartLocalize("%ConstructionMenu_HoverConstructibleFoundation").AddParam(cost).Translate("Foundation Cost: {0}");

        public static string CannotAfford(string cost) =>
            Game("%FailureFlagsNotEnoughInfluence", "Insufficient Influence.") + " " + FoundationCost(cost);

        // The one sentence the game has no words for; {0} is the cost. Written with the game's own terms for Foundation,
        // District, Influence and Tile in each language, and its formal or informal address (from its cancel-construction
        // warning, %CancelQueuedConstructionDescription).
        public static string BuyQuestion(string cost)
        {
            string question = Plugin.TextBuyQuestion.Value;
            if (string.IsNullOrEmpty(question) && !BuyQuestionByLanguage.TryGetValue(Language, out question))
            {
                question = BuyQuestionByLanguage["en-US"];
            }
            return string.Format(question, cost);
        }

        private static readonly Dictionary<string, string> BuyQuestionByLanguage = new Dictionary<string, string>
        {
            ["en-US"] = "Buy a Foundation on this tile for {0} and place the District there? The Foundation is bought right away: if you cancel the District, it stays and the Influence is not refunded.",
            ["cs-CZ"] = "Koupit základy na tomto poli za {0} a umístit sem distrikt? Základy se koupí okamžitě: pokud distrikt zrušíš, zůstanou a vliv nebude navrácen.",
            ["de-DE"] = "Ein Fundament auf dieser Kachel für {0} kaufen und den Bezirk dort errichten? Das Fundament wird sofort gekauft: Wenn du den Bezirk abbrichst, bleibt es bestehen und der Einfluss wird nicht zurückerstattet.",
            ["es-ES"] = "¿Comprar un cimiento en esta casilla por {0} y colocar allí el distrito? El cimiento se compra al instante: si cancelas el distrito, se mantiene y no recuperarás la influencia.",
            ["fr-FR"] = "Acheter des fondations sur cette case pour {0} et y construire le quartier ? Les fondations sont achetées immédiatement : si vous annulez le quartier, elles restent et l'influence ne sera pas remboursée.",
            ["hu-HU"] = "Megveszed az alapot ezen a mezőn {0} áron, és ide helyezed a körzetet? Az alapot azonnal megvásárolod: ha visszavonod a körzetet, az alap megmarad, és a befolyás nem térül vissza.",
            ["it-IT"] = "Vuoi acquistare le fondamenta su questa casella per {0} e costruirvi il distretto? Le fondamenta vengono acquistate subito: se annulli il distretto, restano e l'Influenza non ti verrà rimborsata.",
            ["ja-JP"] = "{0}でこのタイルに基礎区画を購入し、区域を配置しますか？基礎区画はすぐに購入されます。区域の建設を中止しても基礎区画は残り、影響力は払い戻されません。",
            ["ko-KR"] = "{0}에 이 타일의 부지를 구입하고 그곳에 구역을 배치할까요? 부지는 즉시 구입됩니다. 구역을 취소해도 부지는 남으며 영향력은 돌려받을 수 없습니다.",
            ["pl-PL"] = "Kupić fundamenty na tym polu za {0} i umieścić tam dzielnicę? Fundamenty zostaną kupione od razu: jeśli anulujesz dzielnicę, pozostaną, a wpływy nie zostaną zwrócone.",
            ["pt-BR"] = "Deseja comprar uma Fundação nesta área por {0} e posicionar o Distrito nela? A Fundação é comprada na hora: se você cancelar o Distrito, ela permanece e a Influência não será reembolsada.",
            ["ru-RU"] = "Купить фундамент на этой плитке за {0} и возвести там постройку? Фундамент покупается сразу: если вы отмените постройку, он останется, а влияние не вернётся.",
            ["tr-TR"] = "Bu levhada {0} karşılığında bir temel satın alıp mıntıkayı oraya yerleştirmek istiyor musun? Temel hemen satın alınır: mıntıkayı iptal edersen temel kalır ve nüfuz iade edilmez.",
            ["uk-UA"] = "Купити фундамент на цій клітинці за {0} і побудувати там район? Фундамент купується одразу: якщо ви скасуєте район, він залишиться, а вплив не відшкодовується.",
            ["zh-CN"] = "花费{0}在此地块购买地基并在此放置城区？地基会立即购买：如果取消该城区，地基仍会保留，影响力不会退还。",
            ["zh-TW"] = "花費{0}在此格位購買地基並在此放置地區？地基會立即購買：如果你取消該地區，地基仍會保留，影響力將不會退還。",
        };

        private static string Language => Services.GetService<ILocalizationService>()?.CurrentLanguage ?? "en-US";

        private static string Game(string key, string fallback) => Utils.TextUtils.StartLocalize(key).Translate(fallback);
    }
}
