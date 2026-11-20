using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Data.Repositories;
using BsaTroopFinances.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace BsaTroopFinances.Tests;

/// <summary>
/// The sign-in app's API over real HTTP (an in-memory test server, mapped by the same
/// SignInAppApi.Map as Program.cs): it doesn't exist without a long enough SignInApp:ApiKey,
/// refuses anyone without that key, and answers with the status codes the sign-in app relies on.
/// </summary>
public sealed class SignInAppApiTests : IAsyncDisposable
{
    private const string Key = "0123456789abcdef0123456789abcdef";

    private readonly InMemorySignInAppRepository _repository = new();
    private readonly IInvoiceRepository _invoices = Substitute.For<IInvoiceRepository>();
    private readonly IScoutAccountService _accounts = Substitute.For<IScoutAccountService>();
    private readonly IReimbursementRequestRepository _requests = Substitute.For<IReimbursementRequestRepository>();
    private WebApplication? _app;

    public SignInAppApiTests() =>
        _requests.GetBySourcePrefixAsync(Arg.Any<string>()).Returns([]);

    private async Task<HttpClient> StartAsync(string? configuredKey, string? sentKey = Key)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection([new("SignInApp:ApiKey", configuredKey)]);

        var scouts = Substitute.For<IScoutRepository>();
        scouts.GetAllAsync().Returns([]);
        var guardians = Substitute.For<IGuardianRepository>();
        guardians.GetAllAsync().Returns([]);
        var audit = Substitute.For<IFinanceAuditRepository>();
        builder.Services.AddSingleton(new SignInAppService(_repository, scouts, guardians, _invoices, _accounts, audit,
            new PassThroughUnitOfWork(), TimeProvider.System));
        builder.Services.AddSingleton(new SignInFuelService(_repository, _requests, guardians, audit, new PassThroughUnitOfWork(), TimeProvider.System));

        _app = builder.Build();
        SignInAppApi.Map(_app);
        await _app.StartAsync();

        var client = _app.GetTestClient();
        if (sentKey is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sentKey);
        }

        return client;
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    private static readonly SignInRosterPerson[] Roster = [new("p-sam", "Sam Patel", SignInPersonType.Scout, true)];

    private static TripUpload Trip(decimal amount = 40) =>
        new("Campout", "Dec 4 – 6", new DateOnly(2099, 12, 4), new DateOnly(2099, 12, 6), null, "Leader",
            [new("e1", "Sam Patel", "p-sam", SignInPersonType.Scout, amount, "")]);

    private static TripPaymentUpload Payment(string entryId = "e1", string method = InvoicePaymentMethod.Cash) =>
        new("pay1", entryId, 20, method, null, "Pat Lee");

    /// <summary>Puts trip t1 on file with Sam's invoice issued (or voided).</summary>
    private void InvoicedTrip(string status = InvoiceStatus.Outstanding)
    {
        _repository.Trips["t1"] = new TripImportEntity { PartitionKey = TripImportEntity.PartitionKeyValue, RowKey = "t1", Title = "Campout" };
        _repository.Lines[("t1", "e1")] = new TripImportLineEntity
        {
            PartitionKey = "t1", RowKey = "e1", Name = "Sam Patel", SignInPersonId = "p-sam", PersonType = SignInPersonType.Scout, Amount = 40,
            InvoiceOwnerId = "s-sam", InvoiceId = "inv1", InvoicedAmount = 40,
        };
        _invoices.GetByIdAsync("s-sam", "inv1").Returns(new InvoiceEntity
        {
            PartitionKey = "s-sam", RowKey = "inv1", InvoiceId = "inv1", ScoutId = "s-sam", TotalAmount = 40, AmountDueFromFamily = 40, StatusValue = status,
        });
    }

    // ---- Without the sign-in app

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0123456789abcdef0123456789abcde")]   // one character short
    public async Task Without_a_long_enough_key_the_api_isnt_there_at_all(string? configuredKey)
    {
        var client = await StartAsync(configuredKey, sentKey: configuredKey is { Length: > 0 } ? configuredKey : Key);

        var responses = new[]
        {
            await client.PutAsJsonAsync("/api/signin/roster", Roster),
            await client.PutAsJsonAsync("/api/signin/trips/t1", Trip()),
            await client.GetAsync("/api/signin/trips/t1/status"),
            await client.PostAsJsonAsync("/api/signin/trips/t1/payments", Payment()),
            await client.GetAsync("/api/signin/trips/t1/fuel"),
        };

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.NotFound, r.StatusCode));
        Assert.Empty(_repository.People);
        Assert.Empty(_repository.Trips);
    }

    // ---- With the sign-in app: the key

    [Theory]
    [InlineData(null)]
    [InlineData("0123456789abcdef0123456789abcdeX")]
    [InlineData("wrong")]
    public async Task Without_the_right_key_every_call_is_refused_and_nothing_is_saved(string? sentKey)
    {
        var client = await StartAsync(Key, sentKey);

        var responses = new[]
        {
            await client.PutAsJsonAsync("/api/signin/roster", Roster),
            await client.PutAsJsonAsync("/api/signin/trips/t1", Trip()),
            await client.GetAsync("/api/signin/trips/t1/status"),
            await client.PostAsJsonAsync("/api/signin/trips/t1/payments", Payment()),
            await client.GetAsync("/api/signin/trips/t1/fuel"),
        };

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode));
        Assert.Empty(_repository.People);
        Assert.Empty(_repository.Trips);
    }

    // ---- With the sign-in app: what each call answers

    [Fact]
    public async Task The_roster_and_a_trip_are_saved()
    {
        var client = await StartAsync(Key);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync("/api/signin/roster", Roster)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync("/api/signin/trips/t1", Trip())).StatusCode);

        Assert.Equal("Sam Patel", _repository.People["p-sam"].FullName);
        Assert.Equal(40m, _repository.Lines[("t1", "e1")].Amount);
    }

    [Fact]
    public async Task A_trip_that_doesnt_make_sense_is_422_and_isnt_saved()
    {
        var client = await StartAsync(Key);

        var response = await client.PutAsJsonAsync("/api/signin/trips/t1", Trip(amount: -5));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Empty(_repository.Trips);
    }

    [Fact]
    public async Task The_status_of_each_person_comes_back_as_json()
    {
        var client = await StartAsync(Key);
        InvoicedTrip();

        var status = await client.GetFromJsonAsync<List<TripLineStatusDto>>("/api/signin/trips/t1/status");

        Assert.Equal(new TripLineStatusDto("e1", true, TripLineStatus.Outstanding, 40, 40, 0), Assert.Single(status!));
        Assert.Empty((await client.GetFromJsonAsync<List<TripLineStatusDto>>("/api/signin/trips/never-sent/status"))!);
    }

    [Fact]
    public async Task A_payment_is_200_when_recorded_and_200_again_when_sent_twice()
    {
        var client = await StartAsync(Key);
        InvoicedTrip();

        var first = await client.PostAsJsonAsync("/api/signin/trips/t1/payments", Payment());
        var again = await client.PostAsJsonAsync("/api/signin/trips/t1/payments", Payment());

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (first.StatusCode, again.StatusCode));
        Assert.Contains("\"Recorded\"", await first.Content.ReadAsStringAsync());
        Assert.Contains("\"AlreadyRecorded\"", await again.Content.ReadAsStringAsync());
        await _accounts.Received(1).RecordInvoicePaymentAsync("s-sam", "inv1", 20, InvoicePaymentMethod.Cash, null, "Pat Lee");
    }

    [Fact]
    public async Task A_payment_for_someone_not_on_the_trip_is_404()
    {
        var client = await StartAsync(Key);
        InvoicedTrip();

        var response = await client.PostAsJsonAsync("/api/signin/trips/t1/payments", Payment(entryId: "e9"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("NotFound", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData(InvoiceStatus.Voided)]
    public async Task A_payment_before_the_invoice_is_issued_is_409(string? invoiceStatus)
    {
        var client = await StartAsync(Key);
        if (invoiceStatus is null)
        {
            await client.PutAsJsonAsync("/api/signin/trips/t1", Trip());
        }
        else
        {
            InvoicedTrip(invoiceStatus);
        }

        var response = await client.PostAsJsonAsync("/api/signin/trips/t1/payments", Payment());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("NotInvoiced", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_payment_that_cant_be_recorded_is_422_with_the_reason()
    {
        var client = await StartAsync(Key);
        InvoicedTrip();

        var response = await client.PostAsJsonAsync("/api/signin/trips/t1/payments", Payment(method: InvoicePaymentMethod.ScoutFunds));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("check, cash, Venmo or other", await response.Content.ReadAsStringAsync());
        Assert.Empty(_repository.Payments);
    }

    [Fact]
    public async Task Fuel_paybacks_can_be_read_and_a_bad_amount_is_422()
    {
        var client = await StartAsync(Key);

        var status = await client.GetAsync("/api/signin/trips/t1/fuel");
        var bad = await client.PutAsJsonAsync("/api/signin/trips/t1/fuel",
            new FuelUpload("Campout", "Dec 4 – 6", "Leader", [new("e1", "Pat Lee", "p-pat", -10, "Gas")]));

        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        Assert.Equal("[]", await status.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, bad.StatusCode);
    }
}
