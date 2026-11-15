using Bunit;
using BsaTroopFinances.Components.Pages.Admin;
using BsaTroopFinances.Services;
using NSubstitute;

namespace BsaTroopFinances.Tests;

public class TestDataSettingsPageTests : AdminPageTestBase
{
    private readonly ITestDataLoader _loader;

    public TestDataSettingsPageTests()
    {
        _loader = Fake<ITestDataLoader>();
        _loader.IsEnabled.Returns(true);
        _loader.LoadAsync(Arg.Any<TestDataOptions>()).Returns(new TestDataSummary { Invoices = 250, Reimbursements = 120, Fundraisers = 2 });
    }

    [Fact]
    public void Writer_loads_test_data_only_after_confirming()
    {
        SignInAs(AdminRole.Writer);
        var page = Render<TestDataSettings>();

        page.Find("button.btn-primary").Click();
        _loader.DidNotReceiveWithAnyArgs().LoadAsync(default!);

        page.Find("button.btn-danger").Click();

        _loader.Received(1).LoadAsync(Arg.Is<TestDataOptions>(o => o.Invoices == 250 && o.Reimbursements == 120 && o.Fundraisers == 2));
        Assert.Contains("Added 250 invoices", page.Find(".alert-success").TextContent);
    }

    [Fact]
    public void Turned_off_shows_no_way_to_load()
    {
        _loader.IsEnabled.Returns(false);
        SignInAs(AdminRole.Writer);

        var page = Render<TestDataSettings>();

        Assert.Empty(page.FindAll("button"));
        Assert.Contains("turned off", page.Markup);
    }

    [Fact]
    public void Reader_cannot_load()
    {
        SignInAs(AdminRole.Reader);

        var page = Render<TestDataSettings>();

        Assert.Empty(page.FindAll("button"));
    }

    [Fact]
    public void Settings_lists_test_data_only_while_it_is_turned_on()
    {
        SignInAs(AdminRole.Writer);
        Fake<IAdminUserService>();
        Assert.Contains("/admin/settings/test-data", Render<Settings>().Markup);

        _loader.IsEnabled.Returns(false);
        Assert.DoesNotContain("/admin/settings/test-data", Render<Settings>().Markup);
    }
}
