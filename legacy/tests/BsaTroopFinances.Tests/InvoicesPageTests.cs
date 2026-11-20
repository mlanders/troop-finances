using Bunit;
using Bunit.TestDoubles;
using BsaTroopFinances.Components.Pages.Admin;
using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Data.Repositories;
using BsaTroopFinances.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace BsaTroopFinances.Tests;

public class InvoicesPageTests : AdminPageTestBase
{
    private readonly IInvoiceRepository _invoices;
    private readonly BunitPersistentComponentState _state;
    private readonly IListViewRepository _views;
    private readonly IFinanceAuditRepository _audit;

    public InvoicesPageTests()
    {
        _state = AddBunitPersistentComponentState();
        _views = Fake<IListViewRepository>();
        _views.GetForListAsync(Arg.Any<string>()).Returns(new List<ListViewEntity>());
        _audit = Fake<IFinanceAuditRepository>();
        _invoices = Fake<IInvoiceRepository>();
        _invoices.GetPageAsync(Arg.Any<string?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(new PagedResult<InvoiceEntity>([Invoice()], 1));
    }

    [Fact]
    public void Money_columns_end_with_total_and_status_is_last()
    {
        SignInAs(AdminRole.Writer);

        var page = Render<Invoices>();

        var headers = page.FindAll("thead th").Select(th => th.TextContent).ToList();
        Assert.Equal(["Issued", "Due date", "Billed to", "Description", "Paid from scout account", "Still owed", "Total", "Status"], headers);

        var cells = page.FindAll("tbody tr td").Select(td => td.TextContent.Trim()).ToList();
        Assert.Equal("$100.00", cells[4]);
        Assert.Equal("$175.00", cells[5]);
        Assert.Equal("$275.00", cells[6]);
    }

    [Fact]
    public void Writer_sees_edit_pencil_linking_to_the_invoice_page()
    {
        SignInAs(AdminRole.Writer);

        var page = Render<Invoices>();

        var link = page.Find("td a.icon-link");
        Assert.Equal("/admin/invoices/inv1?scoutId=scout1", link.GetAttribute("href"));
        Assert.NotNull(link.QuerySelector(".fa-pen"));
        Assert.Contains("Issue a new invoice", page.Markup);
    }

    [Fact]
    public void Reader_sees_view_eye_and_no_issue_buttons()
    {
        SignInAs(AdminRole.Reader);

        var page = Render<Invoices>();

        var link = page.Find("td a.icon-link");
        Assert.NotNull(link.QuerySelector(".fa-eye"));
        Assert.Null(link.QuerySelector(".fa-pen"));
        Assert.DoesNotContain("Issue a new invoice", page.Markup);
    }

    [Fact]
    public void Each_invoice_is_a_single_row_with_no_action_buttons()
    {
        SignInAs(AdminRole.Writer);

        var page = Render<Invoices>();

        var rows = page.FindAll("tbody tr");
        Assert.Single(rows);
        Assert.Empty(page.FindAll("tbody button"));
        Assert.DoesNotContain("Record payment", page.Find("tbody").TextContent);
    }

    [Fact]
    public void First_page_from_prerender_is_reused_and_later_pages_are_fetched_on_demand()
    {
        SignInAs(AdminRole.Writer);
        _state.Persist("invoices-first-page", new PagedResult<InvoiceEntity>([Invoice(description: "Prerendered invoice")], 30));

        var page = Render<Invoices>();

        Assert.Contains("Prerendered invoice", page.Find("tbody").TextContent);
        _invoices.DidNotReceiveWithAnyArgs().GetPageAsync(default, default, default, default, default);

        page.FindAll(".row-actions button").Single(b => b.TextContent == "Next").Click();

        _invoices.Received(1).GetPageAsync(InvoiceStatus.Outstanding, null, null, 2, 25);
    }

    [Fact]
    public void Typing_a_name_asks_the_database_for_matching_invoices_from_page_one()
    {
        SignInAs(AdminRole.Writer);
        _invoices.GetPageAsync(InvoiceStatus.Outstanding, null, null, 1, 25, "riv")
            .Returns(new PagedResult<InvoiceEntity>([Invoice(description: "Rivera family campout")], 1));
        var page = Render<Invoices>();

        page.Find(".filter-toggle").Click();
        page.Find("#nameSearch").Input(" riv ");

        _invoices.Received(1).GetPageAsync(InvoiceStatus.Outstanding, null, null, 1, 25, "riv");
        Assert.Contains("Rivera family campout", page.Find("tbody").TextContent);

        // Clearing the box goes back to the unfiltered list.
        page.Find("#nameSearch").Input("");
        Assert.Contains("Summer camp deposit", page.Find("tbody").TextContent);
    }

    [Fact]
    public void An_invoice_billed_to_an_adult_shows_the_adult_and_links_by_its_owner()
    {
        SignInAs(AdminRole.Writer);
        _invoices.GetPageAsync(Arg.Any<string?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(new PagedResult<InvoiceEntity>([new InvoiceEntity
            {
                PartitionKey = "maria", RowKey = "inv9", InvoiceId = "inv9", GuardianId = "maria", GuardianName = "Maria Rivera",
                Description = "Summer camp, adult", TotalAmount = 320, AmountDueFromFamily = 320, StatusValue = InvoiceStatus.Outstanding,
            }], 1));

        var page = Render<Invoices>();

        var billedTo = page.FindAll("tbody tr td")[2].TextContent;
        Assert.Contains("Maria Rivera", billedTo);
        Assert.Contains("Adult", billedTo);
        Assert.Equal("/admin/invoices/inv9?scoutId=maria", page.Find("td a.icon-link").GetAttribute("href"));
    }

    [Fact]
    public void Everyone_gets_the_print_all_outstanding_link()
    {
        SignInAs(AdminRole.Reader);

        var link = Render<Invoices>().Find(".action-bar a[href='/admin/invoices/print-outstanding']");

        Assert.Equal("_blank", link.GetAttribute("target"));
    }

    [Fact]
    public void Prerender_loads_one_page_and_hands_it_on()
    {
        SignInAs(AdminRole.Writer);

        Render<Invoices>();
        _state.TriggerOnPersisting();

        _invoices.Received(1).GetPageAsync(InvoiceStatus.Outstanding, null, null, 1, 25);
        Assert.True(_state.TryTake<PagedResult<InvoiceEntity>>("invoices-first-page", out var handedOn));
        Assert.Equal("inv1", Assert.Single(handedOn!.Items).InvoiceId);
    }

    private static readonly ListFilters SummerCamp = new(InvoiceStatus.Paid, new DateOnly(2026, 6, 1), new DateOnly(2026, 8, 31), description: "summer camp");

    private static ListViewEntity SummerCampView(string createdBy = "Someone Else") => new()
    {
        PartitionKey = ListViewEntity.Invoices, RowKey = "v1", Name = "Summer camp 2026", Status = InvoiceStatus.Paid,
        FromDate = new DateOnly(2026, 6, 1), ToDate = new DateOnly(2026, 8, 31), DescriptionSearch = "summer camp", CreatedBy = createdBy,
    };

    private string CurrentUri => Services.GetRequiredService<NavigationManager>().Uri;

    [Fact]
    public void Dates_and_searches_wait_behind_the_filters_button_which_counts_the_ones_on()
    {
        SignInAs(AdminRole.Reader);
        var page = Render<Invoices>();

        Assert.Empty(page.FindAll("#descriptionSearch"));
        Assert.Empty(page.FindAll(".filter-count"));

        page.Find(".filter-toggle").Click();
        page.Find("#descriptionSearch").Input(" camp ");

        _invoices.Received(1).GetPageAsync(InvoiceStatus.Outstanding, null, null, 1, 25, null, "camp");
        Assert.Equal("1", page.Find(".filter-count").TextContent.Trim());
        Assert.Equal("Custom", page.Find("#viewPicker option").TextContent);
    }

    [Fact]
    public void Any_admin_can_save_the_current_filters_as_a_named_view()
    {
        SignInAs(AdminRole.Reader, "reader@example.com");
        var saved = new List<ListViewEntity>();
        _views.GetForListAsync(ListViewEntity.Invoices).Returns(_ => saved.ToList());
        _views.AddAsync(Arg.Do<ListViewEntity>(saved.Add)).Returns(Task.CompletedTask);
        var page = Render<Invoices>();

        page.Find(".filter-toggle").Click();
        page.FindAll(".choice-btn").Single(b => b.TextContent == "Paid").Click();
        page.FindAll(".filter-panel input[type=date]")[0].Change("2026-06-01");
        page.FindAll(".filter-panel input[type=date]")[1].Change("2026-08-31");
        page.Find("#descriptionSearch").Input("summer camp");
        page.FindAll("button").Single(b => b.TextContent.Contains("Save as view")).Click();
        page.Find("#viewName").Change("Summer camp 2026");
        page.Find(".filter-save-form").Submit();

        var view = Assert.Single(saved);
        Assert.Equal((ListViewEntity.Invoices, "Summer camp 2026", InvoiceStatus.Paid, "summer camp", "reader@example.com"),
            (view.PartitionKey, view.Name, view.Status, view.DescriptionSearch, view.CreatedBy));
        Assert.Equal((new DateOnly(2026, 6, 1), new DateOnly(2026, 8, 31)), (view.FromDate, view.ToDate));
        Assert.EndsWith($"/admin/invoices?view={view.ViewId}", CurrentUri);
        Assert.Contains("Summer camp 2026", page.Find("#viewPicker").TextContent);
        _audit.Received(1).AddAsync(FinanceAuditEntityType.ListView, ListViewEntity.Invoices, "View saved", "reader@example.com",
            Arg.Is<string?>(n => n == null), Arg.Is<string?>(d => d != null && d.Contains("Summer camp 2026") && d.Contains("summer camp")));
        // The person who saved it may delete it.
        Assert.Contains("Delete view", page.Find(".filter-bar").TextContent);
    }

    [Fact]
    public void Picking_a_view_applies_its_filters_and_changing_one_makes_it_custom()
    {
        SignInAs(AdminRole.Reader);
        _views.GetForListAsync(ListViewEntity.Invoices).Returns(new List<ListViewEntity> { SummerCampView() });
        var page = Render<Invoices>();

        page.Find("#viewPicker").Change("v1");

        _invoices.Received(1).GetPageAsync(InvoiceStatus.Paid, SummerCamp.FromTime, SummerCamp.ToTime, 1, 25, null, "summer camp");
        Assert.EndsWith("/admin/invoices?view=v1", CurrentUri);
        Assert.Equal("2", page.Find(".filter-count").TextContent.Trim());

        page.FindAll(".choice-btn").Single(b => b.TextContent == "All").Click();

        Assert.Equal("Custom", page.Find("#viewPicker option").TextContent);
        Assert.EndsWith("/admin/invoices", CurrentUri);
        _invoices.Received(1).GetPageAsync(null, SummerCamp.FromTime, SummerCamp.ToTime, 1, 25, null, "summer camp");
    }

    [Fact]
    public void A_link_to_a_view_opens_the_list_already_filtered()
    {
        SignInAs(AdminRole.Reader);
        _views.GetForListAsync(ListViewEntity.Invoices).Returns(new List<ListViewEntity> { SummerCampView() });
        _views.GetAsync(ListViewEntity.Invoices, "v1").Returns(SummerCampView());
        Services.GetRequiredService<NavigationManager>().NavigateTo("/admin/invoices?view=v1");

        Render<Invoices>();

        _invoices.Received(1).GetPageAsync(InvoiceStatus.Paid, SummerCamp.FromTime, SummerCamp.ToTime, 1, 25, null, "summer camp");
        _invoices.DidNotReceive().GetPageAsync(InvoiceStatus.Outstanding, Arg.Any<DateTimeOffset?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<string?>());
    }

    [Theory]
    [InlineData(AdminRole.Reader, "Someone Else", false)]
    [InlineData(AdminRole.Reader, "Test Admin", true)]
    [InlineData(AdminRole.Writer, "Someone Else", true)]
    public void A_view_can_be_deleted_by_whoever_saved_it_or_an_editor(AdminRole role, string createdBy, bool canDelete)
    {
        SignInAs(role);
        _views.GetForListAsync(ListViewEntity.Invoices).Returns(new List<ListViewEntity> { SummerCampView(createdBy) });
        var page = Render<Invoices>();
        page.Find("#viewPicker").Change("v1");

        var delete = page.FindAll(".filter-bar button").Where(b => b.TextContent.Contains("Delete view")).ToList();
        Assert.Equal(canDelete, delete.Count == 1);
        if (!canDelete)
        {
            return;
        }

        delete[0].Click();
        page.Find(".filter-view-confirm .btn-danger").Click();

        _views.Received(1).DeleteAsync(ListViewEntity.Invoices, "v1");
        Assert.EndsWith("/admin/invoices", CurrentUri);
    }
}
