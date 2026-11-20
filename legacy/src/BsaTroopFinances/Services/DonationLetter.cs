using System.Globalization;
using System.Text.RegularExpressions;
using BsaTroopFinances.Data.Entities;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace BsaTroopFinances.Services;

// The troop's name, contact and charter details come from TroopInfo (the "Troop" settings). The
// charter details may be empty; the letter then shows a highlighted placeholder to fill in.

/// <summary>
/// Builds a thank-you / acknowledgment letter for one donation as an editable Word document, so
/// the treasurer can adjust it, sign it and mail it. Gifts of goods are described but not valued:
/// valuing a non-cash gift is the donor's job, not the troop's.
/// </summary>
public static class DonationLetter
{
    private const string Navy = "003F87";
    private const string Muted = "5B6577";

    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");

    /// <summary>A piece of a paragraph. A placeholder is highlighted so it stands out as something
    /// to fill in before the letter goes out.</summary>
    private sealed record Part(string Text, bool Bold = false, bool Placeholder = false);

    public static byte[] Build(DonationEntity donation, TroopInfo troop, string signerName, DateOnly letterDate)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = document.AddMainDocumentPart();
            main.AddNewPart<StyleDefinitionsPart>().Styles = DefaultStyles();
            var body = new Body();

            body.Append(Paragraph([new(troop.Name)], color: Navy, size: 36, bold: true, after: 0));
            body.Append(Paragraph([new(string.Join(" · ", new[] { "Scouts BSA", troop.ContactEmail, troop.SiteHost }.Where(s => s.Length > 0)))], color: Muted, size: 18, after: 480));

            body.Append(Paragraph([new(letterDate.ToString("MMMM d, yyyy", Us))], after: 360));
            body.Append(Lines([donation.DonorName, .. AddressLines(donation.DonorAddress)], after: 360));
            body.Append(Paragraph([new($"Dear {Greeting(donation.DonorName)},")]));

            var received = donation.ReceivedDate.UtcDateTime.ToString("MMMM d, yyyy", Us);
            if (donation.IsInKind)
            {
                body.Append(Paragraph([
                    new("Thank you for your generous donation of "), new(donation.Reference, Bold: true),
                    new($", received on {received}. "), .. Designation(donation),
                    new("Gifts like yours help give our scouts the camping trips, equipment and activities where they learn, lead and grow."),
                ]));
            }
            else
            {
                body.Append(Paragraph([
                    new("Thank you for your generous gift of "), new(donation.Amount.ToString("C", Us), Bold: true),
                    new($", received on {received} {HowGiven(donation)}. "), .. Designation(donation),
                    new("Gifts like yours help give our scouts the camping trips, equipment and activities where they learn, lead and grow."),
                ]));
            }

            body.Append(Paragraph([
                new($"No goods or services were provided in exchange for this contribution. {troop.Name} is a Scouts BSA unit chartered to "),
                Fill(troop.CharteredOrganization, "[Chartered organization]"),
                new(" (EIN "), Fill(troop.Ein, "[EIN]"),
                new("). Please keep this letter for your tax records."),
            ]));

            body.Append(Paragraph([new("With our thanks,")], after: 960));
            body.Append(Lines([signerName, $"Treasurer, {troop.Name}"], after: 0));

            body.Append(new SectionProperties(
                new PageSize { Width = 12240U, Height = 15840U },
                new PageMargin { Top = 1440, Right = 1440U, Bottom = 1440, Left = 1440U, Header = 720U, Footer = 720U, Gutter = 0U }));
            main.Document = new Document(body);
        }

        return stream.ToArray();
    }

    private static Part Fill(string value, string placeholder) =>
        string.IsNullOrWhiteSpace(value) ? new(placeholder, Placeholder: true) : new(value.Trim());

    private static IEnumerable<string> AddressLines(string address) =>
        address.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string HowGiven(DonationEntity donation) => donation.Method switch
    {
        InvoicePaymentMethod.Check => CheckNumber(donation.Reference) is { Length: > 0 } number ? $"by check no. {number}" : "by check",
        InvoicePaymentMethod.Cash => "in cash",
        InvoicePaymentMethod.Venmo => "by Venmo",
        _ => string.IsNullOrEmpty(donation.Reference) ? "" : $"by {donation.Reference}",
    };

    private static IEnumerable<Part> Designation(DonationEntity donation) => donation.IsRestricted
        ? [new($"As you asked, it will be used for “{donation.Purpose}.” ")]
        : [];

    /// <summary>"The Rivera family" reads as "Dear Rivera family".</summary>
    private static string Greeting(string donorName) =>
        donorName.StartsWith("The ", StringComparison.OrdinalIgnoreCase) && donorName.Length > 4 ? donorName[4..] : donorName;

    /// <summary>The number from a check reference typed as "1042", "#1042" or "Check no. 1042".</summary>
    private static string CheckNumber(string reference) =>
        Regex.Replace(reference, @"^\s*(check)?\s*(no\.?|number|#)?\s*", "", RegexOptions.IgnoreCase).Trim();

    private static Paragraph Paragraph(IEnumerable<Part> parts, string? color = null, int? size = null, bool bold = false, int after = 240)
    {
        var paragraph = new Paragraph(new ParagraphProperties(new SpacingBetweenLines { After = after.ToString(CultureInfo.InvariantCulture) }));
        foreach (var part in parts.Where(p => p.Text.Length > 0))
        {
            paragraph.Append(Run(part, color, size, bold));
        }

        return paragraph;
    }

    /// <summary>One paragraph with a line break between each line, as for an address block.</summary>
    private static Paragraph Lines(IReadOnlyList<string> lines, int after)
    {
        var paragraph = new Paragraph(new ParagraphProperties(new SpacingBetweenLines { After = after.ToString(CultureInfo.InvariantCulture) }));
        for (var i = 0; i < lines.Count; i++)
        {
            var run = new Run();
            if (i > 0)
            {
                run.Append(new Break());
            }

            run.Append(new Text(lines[i]) { Space = SpaceProcessingModeValues.Preserve });
            paragraph.Append(run);
        }

        return paragraph;
    }

    private static Run Run(Part part, string? color, int? size, bool bold)
    {
        // Child order follows the schema: bold, color, size, highlight.
        var properties = new RunProperties();
        if (bold || part.Bold)
        {
            properties.Append(new Bold());
        }
        if (color is not null)
        {
            properties.Append(new Color { Val = color });
        }
        if (size is not null)
        {
            properties.Append(new FontSize { Val = size.Value.ToString(CultureInfo.InvariantCulture) });
        }
        if (part.Placeholder)
        {
            properties.Append(new Highlight { Val = HighlightColorValues.Yellow });
        }

        var run = new Run();
        if (properties.HasChildren)
        {
            run.Append(properties);
        }

        run.Append(new Text(part.Text) { Space = SpaceProcessingModeValues.Preserve });
        return run;
    }

    /// <summary>Calibri 11pt with comfortable line spacing for every paragraph.</summary>
    private static Styles DefaultStyles() => new(
        new DocDefaults(
            new RunPropertiesDefault(new RunPropertiesBaseStyle(
                new RunFonts { Ascii = "Calibri", HighAnsi = "Calibri", ComplexScript = "Calibri", EastAsia = "Calibri" },
                new FontSize { Val = "22" },
                new FontSizeComplexScript { Val = "22" },
                new Languages { Val = "en-US" })),
            new ParagraphPropertiesDefault(new ParagraphPropertiesBaseStyle(
                new SpacingBetweenLines { After = "240", Line = "276", LineRule = LineSpacingRuleValues.Auto }))));
}
