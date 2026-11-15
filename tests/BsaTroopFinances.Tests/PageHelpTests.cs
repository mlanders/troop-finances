using Bunit;
using BsaTroopFinances.Components;

namespace BsaTroopFinances.Tests;

public class PageHelpTests : BunitContext
{
    [Fact]
    public void The_help_is_hidden_until_the_info_button_is_pressed_and_hides_again()
    {
        var page = Render<PageHelp>(p => p
            .Add(x => x.Heading, "People & Access")
            .AddChildContent("<p id=\"helpText\">How access works</p>"));

        Assert.Equal("People & Access", page.Find("h1").TextContent);
        Assert.Empty(page.FindAll("#helpText"));
        Assert.Equal("false", page.Find("#pageHelpToggle").GetAttribute("aria-expanded"));

        page.Find("#pageHelpToggle").Click();
        Assert.Single(page.FindAll("#helpText"));
        Assert.Equal("true", page.Find("#pageHelpToggle").GetAttribute("aria-expanded"));

        page.Find(".page-help-close").Click();
        Assert.Empty(page.FindAll("#helpText"));
    }
}
