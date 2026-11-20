using BsaTroopFinances.Data.Entities;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using static BsaTroopFinances.Services.PdfStyle;

namespace BsaTroopFinances.Services;

/// <summary>
/// Draws invoices as PDF files (US Letter): a single invoice with its payments, combined family
/// invoices, one family per page, or family status sheets. Made with QuestPDF under its Community licence (free for
/// non-profits and small organisations), which brings its own font, so the result is the same on
/// a developer's Windows machine and on the Linux App Service.
/// </summary>
public static class InvoicePdf
{
    static InvoicePdf() => QuestPDF.Settings.License = LicenseType.Community;

    public static byte[] Invoice(TroopInfo troop, InvoiceSheet sheet, DateTime printed)
    {
        var invoice = sheet.Invoice;
        // Everything not covered by the scout account or still owed has been paid by the family
        // (including any scout-funds payments made after issue, which also count as "applied").
        var paymentsReceived = invoice.StatusValue == InvoiceStatus.Voided ? 0
            : Math.Max(0, invoice.TotalAmount - invoice.AmountAppliedFromAccount - invoice.AmountDueFromFamily);

        return Document.Create(document => document.Page(page =>
        {
            Setup(page, $"Printed {printed.ToString("MMM d, yyyy", Us)}");
            page.Content().Column(column =>
            {
                column.Item().Element(c => Letterhead(c, troop, "Invoice", invoice.StatusValue == InvoiceStatus.Outstanding ? null : invoice.StatusValue.ToUpperInvariant()));

                column.Item().PaddingVertical(16).Row(row =>
                {
                    row.RelativeItem().Column(billTo =>
                    {
                        billTo.Item().Element(Label).Text("Bill to");
                        if (invoice.IsForGuardian)
                        {
                            billTo.Item().Text(invoice.GuardianName ?? string.Empty);
                        }
                        else
                        {
                            foreach (var name in sheet.GuardianNames.Count == 0 ? [$"Family of {invoice.ScoutName}"] : sheet.GuardianNames)
                            {
                                billTo.Item().Text(name);
                            }

                            billTo.Item().PaddingTop(10).Element(Label).Text("Scout");
                            billTo.Item().Text(invoice.ScoutName);
                        }
                    });
                    row.ConstantItem(200).Element(c => Facts(c,
                        ("Invoice date", invoice.IssuedUtc.ToLocalTime().ToString("MMM d, yyyy", Us)),
                        ("Due date", Date(invoice.DueDate)),
                        ("Status", invoice.StatusValue)));
                });

                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn();
                        columns.ConstantColumn(110);
                    });
                    table.Header(header =>
                    {
                        header.Cell().Element(HeaderCell).Text("Description");
                        header.Cell().Element(HeaderCell).AlignRight().Text("Amount");
                    });

                    void Line(string text, string amount, bool total = false)
                    {
                        table.Cell().Element(c => total ? TotalCell(c) : Cell(c)).Text(text);
                        table.Cell().Element(c => total ? TotalCell(c) : Cell(c)).AlignRight().Text(amount);
                    }

                    Line(invoice.Description, Money(invoice.TotalAmount));
                    Line("Total", Money(invoice.TotalAmount));
                    if (invoice.AmountAppliedFromAccount > 0)
                    {
                        Line($"Paid from {invoice.ScoutName}'s scout account", "-" + Money(invoice.AmountAppliedFromAccount));
                    }
                    if (paymentsReceived > 0)
                    {
                        Line("Payments received", "-" + Money(paymentsReceived));
                    }
                    Line("Amount due", invoice.StatusValue == InvoiceStatus.Voided ? "—" : Money(invoice.AmountDueFromFamily), total: true);
                });

                if (sheet.Payments.Count > 0)
                {
                    column.Item().PaddingTop(22).PaddingBottom(4).Text("Payments").FontSize(13).Bold().FontColor(Navy);
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(95);
                            columns.ConstantColumn(90);
                            columns.RelativeColumn();
                            columns.ConstantColumn(90);
                        });
                        table.Header(header =>
                        {
                            header.Cell().Element(HeaderCell).Text("Date");
                            header.Cell().Element(HeaderCell).Text("Method");
                            header.Cell().Element(HeaderCell).Text("Reference");
                            header.Cell().Element(HeaderCell).AlignRight().Text("Amount");
                        });
                        foreach (var payment in sheet.Payments)
                        {
                            table.Cell().Element(Cell).Text(payment.PaidUtc.ToLocalTime().ToString("MMM d, yyyy", Us));
                            table.Cell().Element(Cell).Text(payment.Method);
                            table.Cell().Element(Cell).Text(string.IsNullOrWhiteSpace(payment.Reference) ? "—" : payment.Reference);
                            table.Cell().Element(Cell).AlignRight().Text(Money(payment.Amount));
                        }
                    });
                }
            });
        })).GeneratePdf();
    }

    /// <summary>One family bill (every outstanding invoice on one sheet) per family, each starting on a new page.</summary>
    public static byte[] Families(TroopInfo troop, IReadOnlyList<FamilySheet> families, DateTime printed)
    {
        return Document.Create(document =>
        {
            foreach (var family in families)
            {
                document.Page(page =>
                {
                    Setup(page, "One payment can cover all of the invoices above.");
                    page.Content().Column(column =>
                    {
                        column.Item().Element(c => Letterhead(c, troop, "Family Bill", null));

                        column.Item().PaddingVertical(16).Row(row =>
                        {
                            row.RelativeItem().Column(billTo =>
                            {
                                billTo.Item().Element(Label).Text("Bill to");
                                billTo.Item().Text(family.BillTo);
                                billTo.Item().PaddingTop(10).Element(Label).Text("For");
                                billTo.Item().Text(string.Join(", ", family.Invoices.Select(i => i.BilledTo).Distinct()));
                            });
                            row.ConstantItem(200).Element(c => Facts(c,
                                ("Statement date", printed.ToString("MMM d, yyyy", Us)),
                                ("Invoices", family.Invoices.Count.ToString(Us)),
                                ("Earliest due", Date(family.Invoices.Min(i => i.DueDate)))));
                        });

                        column.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(3);
                                columns.RelativeColumn(4);
                                columns.ConstantColumn(80);
                                columns.ConstantColumn(80);
                                columns.ConstantColumn(85);
                            });
                            table.Header(header =>
                            {
                                header.Cell().Element(HeaderCell).Text("For");
                                header.Cell().Element(HeaderCell).Text("Description");
                                header.Cell().Element(HeaderCell).Text("Invoice date");
                                header.Cell().Element(HeaderCell).Text("Due date");
                                header.Cell().Element(HeaderCell).AlignRight().Text("Amount due");
                            });
                            foreach (var invoice in family.Invoices)
                            {
                                table.Cell().Element(Cell).Text(invoice.BilledTo);
                                table.Cell().Element(Cell).Text(invoice.Description);
                                table.Cell().Element(Cell).Text(invoice.IssuedUtc.ToLocalTime().ToString("MMM d, yyyy", Us));
                                table.Cell().Element(Cell).Text(Date(invoice.DueDate));
                                table.Cell().Element(Cell).AlignRight().Text(Money(invoice.AmountDueFromFamily));
                            }

                            table.Cell().ColumnSpan(4).Element(TotalCell).Text("Total due");
                            table.Cell().Element(TotalCell).AlignRight().Text(Money(family.TotalDue));
                        });

                        // Money already held for the family's scouts, which can pay their own invoices.
                        var usable = family.ScoutFunds.Where(f => family.Usable(f) > 0).ToList();
                        if (usable.Count > 0)
                        {
                            Section(column, "Scout account money you can use", section => section.Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.RelativeColumn(3);
                                    columns.ConstantColumn(110);
                                    columns.ConstantColumn(130);
                                });
                                table.Header(header =>
                                {
                                    header.Cell().Element(HeaderCell).Text("Scout");
                                    header.Cell().Element(HeaderCell).AlignRight().Text("In scout account");
                                    header.Cell().Element(HeaderCell).AlignRight().Text("Can go toward these");
                                });
                                foreach (var funds in usable)
                                {
                                    table.Cell().Element(Cell).Text(funds.Name);
                                    table.Cell().Element(Cell).AlignRight().Text(Money(funds.Balance));
                                    table.Cell().Element(Cell).AlignRight().Text(Money(family.Usable(funds)));
                                }

                                table.Cell().ColumnSpan(2).Element(TotalCell).Text("Left to pay if it's all used");
                                table.Cell().Element(TotalCell).AlignRight().Text(Money(family.DueAfterScoutFunds));
                            }));
                            column.Item().PaddingTop(6).Text(
                                "A scout's account can only pay that scout's own invoices. Let the treasurer know how much to use.")
                                .FontSize(9).FontColor(Muted);
                        }
                    });
                });
            }
        }).GeneratePdf();
    }

    /// <summary>One status sheet per family, each starting on a new page: scout fund activity,
    /// invoices and payments for the period. Long tables carry on onto further pages.</summary>
    public static byte[] FamilyStatus(TroopInfo troop, IReadOnlyList<FamilyStatusSheet> sheets, DateTime printed)
    {
        static string Day(DateOnly date) => date.ToString("MMM d, yyyy", Us);

        return Document.Create(document =>
        {
            foreach (var sheet in sheets)
            {
                document.Page(page =>
                {
                    Setup(page, $"Questions? {troop.ContactEmail} · Printed {printed.ToString("MMM d, yyyy", Us)}");
                    page.Content().Column(column =>
                    {
                        column.Item().Element(c => Letterhead(c, troop, "Family Status", null));

                        column.Item().PaddingVertical(16).Row(row =>
                        {
                            row.RelativeItem().Column(family =>
                            {
                                family.Item().Element(Label).Text("Prepared for");
                                family.Item().Text(sheet.Family.Name);
                                family.Item().PaddingTop(10).Element(Label).Text("Scouts");
                                family.Item().Text(sheet.Scouts.Count == 0
                                    ? "None — adult charges only"
                                    : string.Join(", ", sheet.Scouts.Select(s => s.Scout.IsActive ? s.Scout.FullName : $"{s.Scout.FullName} (inactive)")));
                            });
                            row.ConstantItem(200).Element(c => Facts(c,
                                ("From", Day(sheet.From)),
                                ("To", Day(sheet.To)),
                                ("Amount due now", Money(sheet.AmountDue))));
                        });

                        if (sheet.Scouts.Count > 0)
                        {
                            column.Item().Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.RelativeColumn();
                                    for (var i = 0; i < 5; i++)
                                    {
                                        columns.ConstantColumn(72);
                                    }
                                });
                                table.Header(header =>
                                {
                                    header.Cell().Element(HeaderCell).Text("Scout fund");
                                    foreach (var name in new[] { "Opening", "Money in", "Money out", "Closing", "Owed" })
                                    {
                                        header.Cell().Element(HeaderCell).AlignRight().Text(name);
                                    }
                                });
                                foreach (var fund in sheet.Scouts)
                                {
                                    table.Cell().Element(Cell).Text(fund.Scout.FullName);
                                    table.Cell().Element(Cell).AlignRight().Text(Money(fund.OpeningBalance));
                                    table.Cell().Element(Cell).AlignRight().Text(Money(fund.MoneyIn));
                                    table.Cell().Element(Cell).AlignRight().Text(Money(fund.MoneyOut));
                                    table.Cell().Element(Cell).AlignRight().Text(Money(fund.ClosingBalance));
                                    table.Cell().Element(Cell).AlignRight().Text(Money(fund.AmountOwed));
                                }
                                if (sheet.AdultAmountOwed > 0)
                                {
                                    table.Cell().Element(Cell).Text("Adult charges");
                                    table.Cell().ColumnSpan(4).Element(Cell);
                                    table.Cell().Element(Cell).AlignRight().Text(Money(sheet.AdultAmountOwed));
                                }

                                table.Cell().ColumnSpan(4).Element(TotalCell).Text("Total");
                                table.Cell().Element(TotalCell).AlignRight().Text(Money(sheet.Scouts.Sum(s => s.ClosingBalance)));
                                table.Cell().Element(TotalCell).AlignRight().Text(Money(sheet.AmountDue));
                            });
                        }

                        foreach (var fund in sheet.Scouts)
                        {
                            Section(column, $"Scout fund: {fund.Scout.FullName}", section => section.Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.ConstantColumn(85);
                                    columns.RelativeColumn();
                                    columns.ConstantColumn(80);
                                    columns.ConstantColumn(80);
                                });
                                table.Header(header =>
                                {
                                    header.Cell().Element(HeaderCell).Text("Date");
                                    header.Cell().Element(HeaderCell).Text("Description");
                                    header.Cell().Element(HeaderCell).AlignRight().Text("Amount");
                                    header.Cell().Element(HeaderCell).AlignRight().Text("Balance");
                                });

                                table.Cell().Element(Cell).Text(Day(sheet.From));
                                table.Cell().ColumnSpan(2).Element(Cell).Text("Opening balance").Italic();
                                table.Cell().Element(Cell).AlignRight().Text(Money(fund.OpeningBalance));

                                var balance = fund.OpeningBalance;
                                foreach (var entry in fund.Entries)
                                {
                                    balance += entry.SignedAmount;
                                    table.Cell().Element(Cell).Text(entry.CreatedUtc.ToLocalTime().ToString("MMM d, yyyy", Us));
                                    table.Cell().Element(Cell).Text(entry.Description);
                                    table.Cell().Element(Cell).AlignRight().Text(Money(entry.SignedAmount)).FontColor(entry.SignedAmount < 0 ? Red : "#1F2A37");
                                    table.Cell().Element(Cell).AlignRight().Text(Money(balance));
                                }
                                if (fund.Entries.Count == 0)
                                {
                                    table.Cell().ColumnSpan(4).Element(Cell).Text("No activity in this period.").FontColor(Muted);
                                }

                                table.Cell().ColumnSpan(3).Element(TotalCell).Text($"Closing balance, {Day(sheet.To)}");
                                table.Cell().Element(TotalCell).AlignRight().Text(Money(fund.ClosingBalance));
                            }));
                        }

                        if (sheet.Invoices.Count == 0)
                        {
                            Section(column, "Invoices", section => section.Text("No invoices in this period.").FontColor(Muted));
                        }
                        else
                        {
                            Section(column, "Invoices", section => section.Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.RelativeColumn(3);
                                    columns.RelativeColumn(4);
                                    columns.ConstantColumn(78);
                                    columns.ConstantColumn(70);
                                    columns.ConstantColumn(68);
                                    columns.ConstantColumn(72);
                                });
                                table.Header(header =>
                                {
                                    header.Cell().Element(HeaderCell).Text("For");
                                    header.Cell().Element(HeaderCell).Text("Description");
                                    header.Cell().Element(HeaderCell).Text("Invoice date");
                                    header.Cell().Element(HeaderCell).Text("Status");
                                    header.Cell().Element(HeaderCell).AlignRight().Text("Total");
                                    header.Cell().Element(HeaderCell).AlignRight().Text("Amount due");
                                });
                                foreach (var invoice in sheet.Invoices)
                                {
                                    var outstanding = invoice.StatusValue == InvoiceStatus.Outstanding;
                                    table.Cell().Element(Cell).Text(invoice.BilledTo);
                                    table.Cell().Element(Cell).Text(invoice.Description);
                                    table.Cell().Element(Cell).Text(invoice.IssuedUtc.ToLocalTime().ToString("MMM d, yyyy", Us));
                                    table.Cell().Element(Cell).Text(invoice.StatusValue);
                                    table.Cell().Element(Cell).AlignRight().Text(Money(invoice.TotalAmount));
                                    table.Cell().Element(Cell).AlignRight().Text(outstanding ? Money(invoice.AmountDueFromFamily) : "—");
                                }

                                table.Cell().ColumnSpan(5).Element(TotalCell).Text("Amount due now");
                                table.Cell().Element(TotalCell).AlignRight().Text(Money(sheet.AmountDue));
                            }));
                        }

                        if (sheet.Payments.Count > 0)
                        {
                            Section(column, "Payments from your family", section => section.Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.ConstantColumn(85);
                                    columns.RelativeColumn();
                                    columns.ConstantColumn(60);
                                    columns.ConstantColumn(95);
                                    columns.ConstantColumn(72);
                                });
                                table.Header(header =>
                                {
                                    header.Cell().Element(HeaderCell).Text("Date");
                                    header.Cell().Element(HeaderCell).Text("For");
                                    header.Cell().Element(HeaderCell).Text("Method");
                                    header.Cell().Element(HeaderCell).Text("Reference");
                                    header.Cell().Element(HeaderCell).AlignRight().Text("Amount");
                                });
                                foreach (var (payment, paidFor) in sheet.Payments)
                                {
                                    table.Cell().Element(Cell).Text(payment.PaidUtc.ToLocalTime().ToString("MMM d, yyyy", Us));
                                    table.Cell().Element(Cell).Text(paidFor);
                                    table.Cell().Element(Cell).Text(payment.Method);
                                    table.Cell().Element(Cell).Text(string.IsNullOrWhiteSpace(payment.Reference) ? "—" : payment.Reference);
                                    table.Cell().Element(Cell).AlignRight().Text(Money(payment.Amount));
                                }

                                table.Cell().ColumnSpan(4).Element(TotalCell).Text("Total paid");
                                table.Cell().Element(TotalCell).AlignRight().Text(Money(sheet.Payments.Sum(p => p.Payment.Amount)));
                            }));
                        }
                    });
                });
            }
        }).GeneratePdf();
    }
}
