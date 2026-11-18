using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PharmaX.Application.Inventory.Dtos;

namespace PharmaX.Application.Assistant;

/// <summary>
/// "What do we have for a headache?" read as a question about this
/// pharmacy's shelves: the condition, the medicines the model says are
/// used for it, and those words to search the product list with.
/// </summary>
/// <remarks>
/// The model only names ingredients; the products themselves come from the
/// product list, so it can never offer something the pharmacy does not
/// stock or has run out of.
/// </remarks>
public static partial class StockForCondition
{
    /// Most ingredients searched for, so one rambling answer cannot start
    /// twenty searches.
    public const int MostIngredients = 8;

    private static readonly char[] Separators = { ',', '\n', ';' };

    /// <summary>
    /// The condition asked about, or null where the question is not about
    /// products for one.
    /// </summary>
    /// <remarks>
    /// Asked the way the counter asks: "what do we have for X", "the best
    /// medication for someone with X", "what can someone with X take",
    /// "I have a X, what can I take", "any X tablets". A question about
    /// safety, doses or interactions is left to the model as it was asked.
    /// </remarks>
    public static string? Condition(string question)
    {
        question ??= "";
        if (AboutSafety().IsMatch(question)) return null;

        foreach (var shape in new[] { ForSomething(), NamedRemedy() })
            if (shape.Match(question) is { Success: true } match
                && Tidy(match.Groups["condition"].Value) is { } condition)
                return condition;

        // "What can someone with headaches take?" - only where something is
        // being asked for, so "can patients with asthma take ibuprofen?"
        // stays a question about ibuprofen.
        if (SomeoneWith().Match(question) is { Success: true } who && AskingWhatToGive().IsMatch(question))
            return Tidy(who.Groups["condition"].Value);

        return null;
    }

    /// <summary>
    /// True for "Suggest OTC medication in the system?" or "what OTC
    /// products do we have?": the shelves asked about, with no condition
    /// named. The condition comes from earlier in the conversation, or
    /// the operator is asked for it.
    /// </summary>
    public static bool AsksForStock(string question) =>
        !AboutSafety().IsMatch(question ?? "") && AskingForStock().IsMatch(question ?? "");

    /// The answer where the shelves are asked about and nothing says what for.
    public const string WhichCondition =
        "Which condition or symptom is it for? Ask, for example: What do we have for a headache?";

    /// <summary>
    /// The question as the model is asked it, in the pharmacy's own terms:
    /// what can be sold over the counter (OTC) and what needs a script from
    /// the dispensary, each medicine by its generic name with its other
    /// common name, and when to send the customer on.
    /// </summary>
    /// <remarks>
    /// The condition is said outright, for a follow-up that does not name
    /// it, and the stock is left out: PharmaX lists that itself, and the
    /// model otherwise says it cannot see the pharmacy's system.
    /// </remarks>
    public static string CounterAdvice(string question, string condition) =>
        question.Trim() + $"\n\nThe customer's complaint: {condition}. "
        + "Answer as advice at a pharmacy counter, in a few short bullet "
        + "points. Name each medicine by its generic name and add its other common name where "
        + "there is one, for example Paracetamol (Acetaminophen) or Salbutamol (Albuterol). "
        + "Put the medicines under two headings: OTC, for those sold over the counter, and "
        + "Dispensary, for prescription medicines that need a script. Then add self-care, and "
        + "when the customer should see a pharmacist or doctor. Do not mention the pharmacy's "
        + "stock, inventory or system.";

    /// The hidden question that gets the ingredients.
    public static string IngredientsQuestion(string condition) =>
        $"List the active ingredients a pharmacy commonly gives for {condition}, over-the-counter "
        + $"ones first. Answer with at most {MostIngredients} generic names separated by commas, "
        + "for example: paracetamol, ibuprofen, aspirin. No other words.";

    /// <summary>
    /// The other names a medicine goes by, so an answer that says
    /// "acetaminophen" still finds the paracetamol on the shelf.
    /// </summary>
    private static readonly (string Said, string Shelf)[] OtherNames =
    {
        ("acetaminophen", "paracetamol"), ("albuterol", "salbutamol"), ("epinephrine", "adrenaline"),
        ("acetylsalicylic acid", "aspirin"), ("glyceryl trinitrate", "nitroglycerin"),
        ("furosemide", "frusemide"), ("amoxycillin", "amoxicillin"), ("cefalexin", "cephalexin"),
        ("lidocaine", "lignocaine"), ("chlorpheniramine", "chlorphenamine"), ("rifampin", "rifampicin")
    };

    /// <summary>
    /// The products found, as the counter reads them before the advice:
    /// what is on the shelf over the counter, what the dispensary holds,
    /// and what is on file but out of stock. Pack and price from the
    /// product list, never from the model.
    /// </summary>
    public static string Listing(string condition, IReadOnlyList<ProductListItemDto> products, int most)
    {
        ArgumentNullException.ThrowIfNull(products);

        if (products.Count == 0)
            return $"Nothing in the product list matches {condition}.";

        var text = new StringBuilder($"In the product list for {condition}:");

        void Section(string heading, IEnumerable<ProductListItemDto> items)
        {
            var list = items.Take(most).OrderBy(p => p.Description, StringComparer.OrdinalIgnoreCase).ToList();
            if (list.Count == 0) return;

            text.Append(CultureInfo.InvariantCulture, $"{Environment.NewLine}{heading}");
            foreach (var p in list)
                text.Append(
                    $"{Environment.NewLine}- {p.Description} ({p.Code}): "
                    + $"{p.QuantityOnHand.ToString("0.##", CultureInfo.InvariantCulture)} on hand, "
                    + $"{p.RetailPrice.ToString("N2", CultureInfo.InvariantCulture)}"
                    + (string.IsNullOrWhiteSpace(p.PackSize) ? "" : $" for {p.PackSize}"));
        }

        Section("OTC (over the counter):", products.Where(p => p.QuantityOnHand > 0m && p.Schedule < 3));
        Section("Dispensary (prescription, pharmacist only):", products.Where(p => p.QuantityOnHand > 0m && p.Schedule >= 3));
        Section("On file but out of stock:", products.Where(p => p.QuantityOnHand <= 0m).Take(3));

        return text.ToString();
    }

    /// <summary>
    /// The words to search the product list for: the condition itself, so
    /// a "Cough Syrup" is found for a cough, then each ingredient named.
    /// </summary>
    public static IReadOnlyList<string> SearchTerms(string condition, string answer)
    {
        // The whole phrase: the search wants every word, so "back pain"
        // finds a back-pain gel and not everything with "back" in it.
        var terms = new List<string> { condition.Trim().ToLowerInvariant() };

        // "paracetamol (acetaminophen)" is two names for one medicine.
        var said = (answer ?? "").Replace('(', ',').Replace(')', ',').Replace('/', ',');

        foreach (var piece in said.Split(Separators,
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var name = piece.Trim('-', '*', '.', ' ').ToLowerInvariant();

            // "1. paracetamol" from a model that numbered its list anyway.
            name = Regex.Replace(name, @"^\d+[.)]\s*", "");

            if (name.Length is < 3 or > 40 || name.Split(' ').Length > 3) continue;

            // Searched by the name the shelf uses as well.
            foreach (var shelf in OtherNames.Where(o => o.Said == name).Select(o => o.Shelf).Prepend(name))
                if (!terms.Contains(shelf)) terms.Add(shelf);

            if (terms.Count >= MostIngredients + 2) break;
        }

        return terms;
    }

    /// Words that follow "for" without being a condition: "for 2 weeks",
    /// "for me", "for a child" (a child with what?), "OTC medication".
    private static readonly HashSet<string> NotConditions = new(StringComparer.OrdinalIgnoreCase)
    {
        "otc", "over", "counter", "the", "any", "some", "good", "best", "other", "our", "your", "my",
        "this", "that", "these", "those", "generic", "prescription", "strong", "stronger", "common",
        "more", "all", "which", "what", "cheap", "cheapest", "natural", "herbal", "me", "you", "us",
        "him", "her", "them", "it", "sale", "now", "today", "example", "instance", "how", "long",
        "someone", "somebody", "anyone", "people", "person", "patient", "patients", "customer",
        "customers", "child", "children", "kid", "kids", "baby", "babies", "adult", "adults",
        "question", "query", "problem", "do", "we", "in", "suggest", "recommend", "new", "repeat"
    };

    /// <summary>
    /// The condition as the product list is searched for it: "someone with
    /// bad headaches in the system" read as "headaches".
    /// </summary>
    private static string? Tidy(string said)
    {
        var condition = TrailingClause().Replace(said.ToLowerInvariant(), "");
        condition = Sufferer().Replace(condition, "");
        condition = Modifiers().Replace(condition, "").Trim(' ', '?', '.', '!', '\'', '"');

        if (condition.Length is < 3 or > 40 || char.IsDigit(condition[0])) return null;

        var words = condition.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length > 4 || NotConditions.Contains(words[0]) ? null : condition;
    }

    // "what do we have for X", "suggest something for X", "products for X",
    // "the best medication for X", "what can I take for X", "good for X"
    [GeneratedRegex(
        @"\b(have|stock|stocked|products?|medicines?|medications?|meds|suggest|recommend|sell|give|offer|anything|something|options?|good|best|take|use|treat|treatment|help|helps|remedy|remedies|relief|cure|tablets?|pills?|syrups?|creams?|drops|otc)\b.*?\bfor\s+(?<condition>[^?.!,;]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ForSomething();

    // "any headache tablets?", "suggest cough medicine", "I need pain relief"
    [GeneratedRegex(
        @"\b(suggest|recommend|have|stock|sell|give|offer|need|want|looking|any|some)\b.*?\b(?<condition>[a-z]+)\s+(medications?|medicines?|meds|tablets|pills|remed(y|ies)|relief|treatments?|relievers?|syrups?|drops|creams?|sprays?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NamedRemedy();

    // "someone with X", "a customer who has X", "I have a X", "she's got X"
    [GeneratedRegex(
        @"\b(someone|somebody|anyone|person|people|patients?|customers?|clients?|child|children|kids?|baby|babies|adults?|i|he|she|they|i'm|he's|she's|they're|i've|i\s+am|he\s+is|she\s+is|they\s+are)\s+(with|who\s+(has|have|is\s+having|suffers\s+from|gets?)|that\s+(has|have)|suffering\s+from|having|has|have|got|has\s+got|have\s+got|gets?|suffers\s+from)\s+(?<condition>[^?.!,;]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SomeoneWith();

    // Something being asked for: "what can they take", "suggest", "anything".
    [GeneratedRegex(
        @"\b(what|which)\b.*\b(take|give|use|have|recommend|suggest|sell|buy|best|good|help)\b|\b(suggest|recommend|something|anything|options?|remed(y|ies)|relief|medications?|medicines?|meds|tablets|treatment|treat)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AskingWhatToGive();

    // "Suggest OTC medication in the system?", "what OTC products do we have?"
    [GeneratedRegex(
        @"^(?=.*\b(suggest|recommend|list|show|what|which|any|give|offer|options?)\b)(?=.*\b(otc|over\s+the\s+counter|medications?|medicines?|meds|products?|items|something|anything)\b)(?=.*\b(in\s+the\s+system|in\s+stock|on\s+the\s+shel(f|ves)|we\s+(have|stock|sell|carry)|our|available|otc|over\s+the\s+counter)\b)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AskingForStock();

    // A question about using a medicine rather than choosing one.
    [GeneratedRegex(
        @"\b(safe|safely|safety|interacts?|interactions?|side\s+effects?|doses?|dosage|avoid|contraindicat\w*|overdose|mix|combine)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AboutSafety();

    // " in stock", " in the system", " please", " - what can I give?",
    // "headaches take" from "what can someone with headaches take?"
    [GeneratedRegex(
        @"\s+(in\s+(stock|the\s+(system|shop|store|pharmacy)|our\s+system)|on\s+(the|our)\s+shel(f|ves)|that\s+we|which\s+we|we\s|do\s+we|please|what\s|which\s|can\s|could\s|should\s|would\s|today|right\s+now|at\s+the\s+moment|available|over\s+the\s+counter|otc\b|(take|use|get|buy|try|need|have)\b).*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex TrailingClause();

    // "someone with ", "a customer who has ", "my son suffering from "
    [GeneratedRegex(
        @"^((a|an|the|my|our|your)\s+)?(someone|somebody|anyone|person|people|patients?|customers?|clients?|child|children|kids?|baby|babies|adults?|elderly|woman|women|man|men|mother|mum|son|daughter|husband|wife)\s+(with|who\s+(has|have|is\s+having|suffers\s+from|gets?)|that\s+(has|have)|suffering\s+from|having)\s+",
        RegexOptions.CultureInvariant)]
    private static partial Regex Sufferer();

    // "a bad ", "the ", "a bit of a "
    [GeneratedRegex(
        @"^((an?|the|my|his|her|their|bad|severe|mild|terrible|slight|minor|really|very|nasty|persistent|constant|bit\s+of\s+an?|bit\s+of|little)\s+)+",
        RegexOptions.CultureInvariant)]
    private static partial Regex Modifiers();
}
