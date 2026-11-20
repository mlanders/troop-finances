using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Services;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;

namespace BsaTroopFinances.Tests;

public class DonationLetterTests
{
    private static readonly TroopInfo NoCharter = TestTroop.Info;
    private static readonly DateOnly LetterDate = new(2026, 10, 30);

    private static DonationEntity Check() => new()
    {
        RowKey = "gift", DonorName = "Main Street Hardware", DonorAddress = "12 Main St\nSpringfield, CA 90000",
        ReceivedDate = new(2026, 9, 15, 0, 0, 0, TimeSpan.Zero), Amount = 300, Method = InvoicePaymentMethod.Check, Reference = "5521",
    };

    private static string Text(byte[] letter)
    {
        using var document = WordprocessingDocument.Open(new MemoryStream(letter), false);
        return string.Join("\n", document.MainDocumentPart!.Document!.Body!.Elements<Paragraph>().Select(p =>
            string.Concat(p.Descendants().Select(e => e switch { Text t => t.Text, Break => " | ", _ => "" }))));
    }

    private static IReadOnlyList<string> Highlighted(byte[] letter)
    {
        using var document = WordprocessingDocument.Open(new MemoryStream(letter), false);
        return document.MainDocumentPart!.Document!.Body!.Descendants<Run>()
            .Where(r => r.RunProperties?.Highlight is not null)
            .Select(r => r.InnerText)
            .ToList();
    }

    [Fact]
    public void A_money_gift_is_thanked_with_its_amount_date_and_how_it_was_given()
    {
        var text = Text(DonationLetter.Build(Check(), NoCharter, "Pat Treasurer", LetterDate));

        Assert.Contains("October 30, 2026", text);
        Assert.Contains("Main Street Hardware | 12 Main St | Springfield, CA 90000", text);
        Assert.Contains("Dear Main Street Hardware,", text);
        Assert.Contains("generous gift of $300.00, received on September 15, 2026 by check no. 5521.", text);
        Assert.Contains("No goods or services were provided in exchange for this contribution.", text);
        Assert.Contains("Pat Treasurer | Treasurer, Troop 52", text);
        Assert.Contains("Troop 52\nScouts BSA · treasurer@troop52.example · finance.troop52.example", text);
        Assert.Contains("Troop 52 is a Scouts BSA unit", text);
        Assert.DoesNotContain("As you asked", text);
    }

    [Fact]
    public void A_restricted_gift_says_what_it_will_be_used_for()
    {
        var gift = Check();
        gift.IsRestricted = true;
        gift.Purpose = "Summer camp scholarships";

        Assert.Contains("As you asked, it will be used for “Summer camp scholarships.”", Text(DonationLetter.Build(gift, NoCharter, "Pat", LetterDate)));
    }

    [Theory]
    [InlineData("Check 5521")]
    [InlineData("#5521")]
    [InlineData("check no. 5521")]
    public void A_check_reference_is_reduced_to_its_number(string reference)
    {
        var gift = Check();
        gift.Reference = reference;

        Assert.Contains("by check no. 5521.", Text(DonationLetter.Build(gift, NoCharter, "Pat", LetterDate)));
    }

    [Fact]
    public void A_family_is_greeted_without_the_leading_the()
    {
        var gift = Check();
        gift.DonorName = "The Nguyen family";

        Assert.Contains("Dear Nguyen family,", Text(DonationLetter.Build(gift, NoCharter, "Pat", LetterDate)));
    }

    [Fact]
    public void Goods_are_described_but_never_valued()
    {
        var gift = Check();
        gift.Method = DonationMethod.InKind;
        gift.Reference = "two 4-person tents";
        gift.Amount = 180;

        var text = Text(DonationLetter.Build(gift, NoCharter, "Pat", LetterDate));

        Assert.Contains("generous donation of two 4-person tents, received on September 15, 2026.", text);
        Assert.DoesNotContain("$", text);
    }

    [Fact]
    public void Missing_charter_details_are_highlighted_placeholders()
    {
        var letter = DonationLetter.Build(Check(), NoCharter, "Pat", LetterDate);

        Assert.Equal(["[Chartered organization]", "[EIN]"], Highlighted(letter));
    }

    [Fact]
    public void Configured_charter_details_fill_the_letter()
    {
        var letter = DonationLetter.Build(Check(), TestTroop.Info with { CharteredOrganization = "First Community Church", Ein = "12-3456789" }, "Pat", LetterDate);

        Assert.Contains("chartered to First Community Church (EIN 12-3456789).", Text(letter));
        Assert.Empty(Highlighted(letter));
    }

    [Fact]
    public void The_letter_is_a_valid_word_document()
    {
        using var document = WordprocessingDocument.Open(new MemoryStream(DonationLetter.Build(Check(), NoCharter, "Pat", LetterDate)), false);

        Assert.Empty(new OpenXmlValidator().Validate(document));
    }
}
