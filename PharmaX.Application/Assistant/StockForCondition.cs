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
    public static string? Condition(string question)
    {
        var match = AskingForProducts().Match(question ?? "");
        if (!match.Success) return null;

        var condition = match.Groups["condition"].Value.Trim(' ', '?', '.', '!');
        return condition.Length is >= 3 and <= 60 ? condition : null;
    }

    /// <summary>
    /// The question as the model is asked it, in the pharmacy's own terms:
    /// what can be sold over the counter (OTC) and what needs a script from
    /// the dispensary, each medicine by its generic name with its other
    /// common name, and when to send the customer on.
    /// </summary>
    public static string CounterAdvice(string question) =>
        question.Trim() + "\n\nAnswer as advice at a pharmacy counter, in a few short bullet "
        + "points. Name each medicine by its generic name and add its other common name where "
        + "there is one, for example Paracetamol (Acetaminophen) or Salbutamol (Albuterol). "
        + "Put the medicines under two headings: OTC, for those sold over the counter, and "
        + "Dispensary, for prescription medicines that need a script. Then add self-care, and "
        + "when the customer should see a pharmacist or doctor.";

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

    // "what do we have for X", "suggest something for X",
    // "products for X", "anything in stock for X", "what can I sell for X"
    [GeneratedRegex(
        @"\b(have|stock|stocked|products?|medicines?|medication|meds|suggest|recommend|sell|give|offer|anything|something|options?)\b.*?\bfor\s+(an?\s+|the\s+|my\s+)?(?<condition>[^?.!]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AskingForProducts();
}
