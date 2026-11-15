using BsaTroopFinances.Components;
using BsaTroopFinances.Data;
using BsaTroopFinances.Data.Repositories;
using BsaTroopFinances.Services;
using BsaTroopFinances.Storage;
using System.Globalization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;
using Microsoft.EntityFrameworkCore;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("en-US");
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("en-US");

// Command-line restore mode (see Services/BackupRestoreCommand). Its arguments are kept out of
// the web host's command-line configuration, which can't parse them.
if (args.Length > 0 && args[0] == BackupRestoreCommand.Name)
{
    var restoreConfiguration = WebApplication.CreateBuilder().Configuration;
    TroopInfo.UseTimeZone(restoreConfiguration);
    Environment.ExitCode = await BackupRestoreCommand.RunAsync(args[1..], restoreConfiguration);
    return;
}

if (args.Length > 0 && args[0] is TestDataCommand.Name or RosterImportCommand.Name)
{
    var commandBuilder = WebApplication.CreateBuilder();
    TroopInfo.UseTimeZone(commandBuilder.Configuration);
    Environment.ExitCode = args[0] == TestDataCommand.Name
        ? await TestDataCommand.RunAsync(args[1..], commandBuilder.Configuration, commandBuilder.Environment)
        : await RosterImportCommand.RunAsync(args[1..], commandBuilder.Configuration, commandBuilder.Environment);
    return;
}

var builder = WebApplication.CreateBuilder(args);

// ---- The troop this copy belongs to (the "Troop" settings; see Services/TroopInfo). A real site
// won't start without them, rather than run half-labelled or without a Global admin. ----
if (builder.Environment.IsProduction() && TroopInfo.Problems(builder.Configuration) is { Count: > 0 } troopProblems)
{
    throw new InvalidOperationException(
        "This site isn't set up for a troop yet: " + string.Join("; ", troopProblems) +
        ". See infra/HOSTING.md, \"Your troop's settings\".");
}

TroopInfo.UseTimeZone(builder.Configuration);
builder.Services.AddSingleton(TroopInfo.From(builder.Configuration));

// ---- Razor Components (Blazor Server) ----
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    // List pages (Invoices, Reimbursements) hand their prerendered first page to the live
    // connection, and the browser sends that state back when the connection starts. A full page of
    // 25 invoices is ~30 KB before encryption, over SignalR's 32 KB default -- the server then
    // drops the connection silently (logged only at Debug) and every filter on the page goes dead.
    .AddHubOptions(options => options.MaximumReceiveMessageSize = 512 * 1024);

// ---- Auth: three schemes, forwarded by path. /admin/* uses the "AdminCookie", issued after a
// Microsoft (any work, school or personal account) or Google sign-in -- same approach as the
// meal-planner app. What that person may do comes from the AdminUsers table (see
// AdminRoleClaimsTransformation), not from the sign-in itself. /portal/* uses a separate
// lightweight cookie issued after an emailed one-time-code login (regular parents/guardians --
// no Microsoft or Google account required). AddPolicyScheme is what makes Blazor Server's
// cascading AuthenticationState resolve the right ambient identity per path -- without it, only
// one of the two cookie handlers would ever be consulted for the "current user". ----

// Local-only fake admin sign-in (Development + DevAuth:Enabled; throws anywhere else). See Services/DevAuth.
var devAuthEnabled = DevAuth.IsEnabled(builder.Configuration, builder.Environment);

// Each provider is offered only once its client id is configured (see infra/DEPLOY.md).
var microsoftSignIn = !devAuthEnabled && !string.IsNullOrWhiteSpace(builder.Configuration["AzureAd:ClientId"]);
// Google needs its secret too: registered with an id but no secret, the Google handler fails
// validation on every request, taking the whole site down rather than just its button.
var googleSignIn = !devAuthEnabled
    && !string.IsNullOrWhiteSpace(builder.Configuration["Authentication:Google:ClientId"])
    && !string.IsNullOrWhiteSpace(builder.Configuration["Authentication:Google:ClientSecret"]);

var authenticationBuilder = builder.Services.AddAuthentication(options => options.DefaultScheme = "PathScheme")
    .AddPolicyScheme("PathScheme", "Path-based scheme selector", options =>
    {
        options.ForwardDefaultSelector = context =>
            context.Request.Path.StartsWithSegments("/portal") ? "ParentAuth" : "AdminCookie";
    })
    .AddCookie("ParentAuth", options =>
    {
        options.Cookie.Name = "troopfin_parent";
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax; // guardians land here from a top-level GET off an emailed link
        options.LoginPath = "/portal/login";
        // Parents mostly open the site from an icon on their phone's home screen, so the sign-in
        // is a persistent cookie (PortalLogin sets IsPersistent) good for 30 days, renewed as they
        // use it. With a shorter session they would need a new emailed code nearly every time
        // they opened the app.
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
    });

if (microsoftSignIn)
{
    // AzureAd:TenantId "common" takes any Microsoft account. Microsoft.Identity.Web registers the
    // admin cookie itself here, and validates AzureAd:ClientId on every request once registered --
    // which is why it's only added when a client id is configured.
    authenticationBuilder.AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"), cookieScheme: "AdminCookie");
    builder.Services.PostConfigure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
        SignInFailures.SendToRetryPage(options.Events));
}
else
{
    authenticationBuilder.AddCookie("AdminCookie");
}

if (googleSignIn)
{
    authenticationBuilder.AddGoogle(options =>
    {
        options.ClientId = builder.Configuration["Authentication:Google:ClientId"]!;
        options.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"]!;
        options.SignInScheme = "AdminCookie";
        // Whether Google vouches for the address (see AdminIdentity.VerifiedEmail): "verified_email"
        // from the v2 userinfo endpoint, "email_verified" from v3.
        options.ClaimActions.MapJsonKey(AdminIdentity.EmailVerifiedClaimType, "verified_email");
        options.ClaimActions.MapJsonKey(AdminIdentity.EmailVerifiedClaimType, "email_verified");
        SignInFailures.SendToRetryPage(options.Events);
    });
}

// The "AdminOnly" policy below pins AddAuthenticationSchemes("AdminCookie") (needed so a 401 on
// /admin/* challenges specifically the admin cookie, not "ParentAuth"), so an unauthenticated
// challenge is handled by the cookie scheme itself: a redirect to LoginPath, the sign-in page
// with its Microsoft and Google buttons.
builder.Services.Configure<CookieAuthenticationOptions>("AdminCookie", options =>
{
    options.LoginPath = "/admin/signin";
    options.ReturnUrlParameter = "returnUrl";
    if (devAuthEnabled)
    {
        DevAuth.UseDevSignIn(options);
    }
    // Signed in but not allowed (no role yet, or a Viewer on an Editor-only page).
    options.AccessDeniedPath = "/admin/no-access";

    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    // Strict caused a sign-in redirect loop (AADSTS50196) in production: the admin cookie set
    // during Entra ID's form_post callback wasn't reliably sent on the very next redirect in that
    // same chain under Strict, so the app challenged again, Entra signed back in instantly (still
    // had a session), and it looped. Lax is the standard recommendation for an app cookie that has
    // to survive an external identity provider's redirect chain -- still blocks cross-site
    // subresource/background use of the cookie, just not top-level redirect navigations like this.
    options.Cookie.SameSite = SameSiteMode.Lax;
    // 14 days (Microsoft.Identity.Web's default) is too long for a finance-admin session;
    // 8 hours covers a working day without forcing re-auth mid-task. The role isn't in the
    // cookie -- it's looked up per request -- so a removed admin loses access within a minute
    // or two regardless (see AdminUserService and AdminRevalidatingAuthStateProvider).
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
});

builder.Services.AddAuthorization(options =>
{
    // Viewing /admin requires the Read-only (Viewer) or Edit (Editor) role; making changes
    // requires Edit (AdminWriter). See ClaimsPrincipalExtensions.
    options.AddPolicy("AdminOnly", p => p.RequireAuthenticatedUser().AddAuthenticationSchemes("AdminCookie")
        .RequireAssertion(context => context.User.HasTroopFinanceAccess()));
    options.AddPolicy("ParentOnly", p => p.RequireAuthenticatedUser().AddAuthenticationSchemes("ParentAuth"));
    options.AddPolicy("AdminWriter", p => p.RequireAuthenticatedUser().AddAuthenticationSchemes("AdminCookie")
        .RequireAssertion(context => context.User.IsTroopFinanceWriter()));
});
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, AdminRevalidatingAuthStateProvider>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<IClaimsTransformation, AdminRoleClaimsTransformation>();

// ---- Finance data: Azure SQL (SQL Server LocalDB locally, SQL Server in a container elsewhere),
// or PostgreSQL with Database:Provider "Postgres", through EF Core. Each operation gets a
// short-lived context from the factory; FinanceDb adds shared transactions (see Data/FinanceDb).
// The sign-in cookies' Data Protection keys are kept here too, so they survive restarts on any
// host (see Data/FinanceDatabase). ----
var financeDb = FinanceDatabase.Connection(builder.Configuration)
    ?? throw new InvalidOperationException(
        "ConnectionStrings:FinanceDb is not configured. Locally it points at SQL Server LocalDB (see " +
        "appsettings.Development.json); in Azure it's the Azure SQL connection string (see infra/DEPLOY.md).");
builder.Services.AddFinanceDatabase(financeDb);

// ---- File storage (receipts and their thumbnails): Azure Blob Storage by default, or an S3
// bucket or a folder on disk (Storage:Provider; see Storage/BlobStores and infra/HOSTING.md). ----
builder.Services.AddSingleton(_ => BlobStores.CreateMain(builder.Configuration));
builder.Services.AddHostedService<StorageInitializer>();

// ---- Nightly backups to separate storage (see Services/BackupService). Left idle, with a
// startup warning, when no backup store is configured. Started by the in-app timer, or from
// outside through POST /internal/backup (Backup:Schedule "External"; see Services/BackupTrigger). ----
builder.Services.AddSingleton<IBackupService>(sp => new BackupService(
    sp.GetRequiredService<IDbContextFactory<FinanceDbContext>>(),
    sp.GetRequiredService<IBlobStore>(),
    BlobStores.CreateBackup(builder.Configuration),
    sp.GetRequiredService<TimeProvider>(),
    sp.GetRequiredService<ILogger<BackupService>>()));
builder.Services.AddHostedService<NightlyBackupWorker>();

// For hosting platforms' "is it up?" checks: answers without touching the database or storage.
builder.Services.AddHealthChecks();

// Registered after StorageInitializer so the tables exist before it runs.
if (devAuthEnabled && builder.Configuration.GetValue<bool>("DevAuth:SeedSampleData"))
{
    builder.Services.AddHostedService<DevSampleDataSeeder>();
}

// ---- Data access ----
builder.Services.AddScoped<IScoutRepository, ScoutRepository>();
builder.Services.AddScoped<IGuardianRepository, GuardianRepository>();
builder.Services.AddScoped<IGuardianScoutLinkRepository, GuardianScoutLinkRepository>();
builder.Services.AddScoped<IFundraiserEventRepository, FundraiserEventRepository>();
builder.Services.AddScoped<IDonationRepository, DonationRepository>();
builder.Services.AddScoped<ITroopBankRepository, TroopBankRepository>();
builder.Services.AddScoped<IScoutLedgerRepository, ScoutLedgerRepository>();
builder.Services.AddScoped<IReimbursementRequestRepository, ReimbursementRequestRepository>();
builder.Services.AddScoped<IInvoiceRepository, InvoiceRepository>();
builder.Services.AddScoped<IInvoicePaymentRepository, InvoicePaymentRepository>();
builder.Services.AddScoped<IFinanceAuditRepository, FinanceAuditRepository>();
builder.Services.AddScoped<ILoginCodeRepository, LoginCodeRepository>();
builder.Services.AddScoped<IGuardianAuthenticatorRepository, GuardianAuthenticatorRepository>();
builder.Services.AddScoped<IListViewRepository, ListViewRepository>();
builder.Services.AddScoped<IEmailLogRepository, EmailLogRepository>();
builder.Services.AddScoped<IEmailSettingsRepository, EmailSettingsRepository>();
builder.Services.AddScoped<IAdminUserRepository, AdminUserRepository>();

// ---- App services ----
builder.Services.AddScoped<IScoutAccountService, ScoutAccountService>();
builder.Services.AddScoped<IDonationService, DonationService>();
builder.Services.AddScoped<ITroopAccountService, TroopAccountService>();
builder.Services.AddScoped<IReimbursementService, ReimbursementService>();
builder.Services.AddScoped<IEmailCodeAuthService, EmailCodeAuthService>();
builder.Services.AddSingleton<AuthenticatorSecretProtector>();
builder.Services.AddScoped<IGuardianAuthenticatorService, GuardianAuthenticatorService>();
builder.Services.AddScoped<IReceiptStorageService, ReceiptStorageService>();
builder.Services.AddScoped<IReceiptThumbnailService, ReceiptThumbnailService>();
builder.Services.AddScoped<IInvoiceDocumentService, InvoiceDocumentService>();
builder.Services.AddScoped<IFamilyStatusService, FamilyStatusService>();
builder.Services.AddScoped<IReportingService, ReportingService>();
builder.Services.AddScoped<ITroopReportService, TroopReportService>();
builder.Services.AddScoped<INotificationService, EmailNotificationService>();
builder.Services.AddScoped<IAdminUserService, AdminUserService>();
builder.Services.AddSingleton(TimeProvider.System);
// The admin "Test data" page; does nothing unless TestData:Enabled is set (see Services/TestDataLoader).
builder.Services.AddSingleton<ITestDataLoader, TestDataLoader>();

// Email:Provider picks how email goes out; left empty it's Azure Communication Services in
// Production and the console (no ACS resource needed) in Development. See Services/EmailProviders.
EmailProviders.Add(builder.Services, builder.Configuration, builder.Environment);

var app = builder.Build();
var troopInfo = app.Services.GetRequiredService<TroopInfo>();

app.Logger.LogInformation("Dates are shown in time zone {TimeZone} (UTC offset now {Offset})",
    TimeZoneInfo.Local.Id, TimeZoneInfo.Local.GetUtcOffset(DateTimeOffset.UtcNow));

// ---- HTTP pipeline ----
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
// Behind a proxy that ends HTTPS (every host but a bare server), the app sees plain HTTP unless it
// reads the proxy's X-Forwarded-Proto header -- then sign-in would build http:// return addresses
// and this would redirect forever. ASP.NET reads those headers when ASPNETCORE_FORWARDEDHEADERS_ENABLED
// is true: Azure App Service sets it itself; the Dockerfile sets it for every other host.
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapHealthChecks("/healthz");

// The installed app's name comes from the troop's settings, so the manifest is made here rather
// than kept as a file in wwwroot.
app.MapGet("/manifest.webmanifest", () => Results.Text(TroopManifest.Json(troopInfo), "application/manifest+json"))
    .AllowAnonymous();
// ...and so are its home-screen icons, which carry the troop's number (see Services/TroopIcons).
var troopIcons = TroopIcons.FromWebRoot(app.Environment, troopInfo);
app.MapGet("/icons/{file}", (string file, HttpContext http) =>
{
    if (troopIcons.Png(file) is not { } png)
    {
        return Results.NotFound();
    }

    http.Response.Headers.CacheControl = "public, max-age=86400";
    return Results.File(png, "image/png");
}).AllowAnonymous();

// ---- Nightly backup started from outside the app (see Services/BackupTrigger). Only exists
// once Backup:TriggerKey is set. Not cancelled when the caller hangs up: a half-made snapshot
// is just retried next time, but there's no reason to throw away a nearly finished one. ----
if (BackupTrigger.Key(app.Configuration) is { } backupTriggerKey)
{
    app.MapPost(BackupTrigger.Path, async (HttpContext http, IBackupService backups, IHostApplicationLifetime lifetime) =>
    {
        if (!BackupTrigger.IsAuthorized(http.Request.Headers.Authorization, backupTriggerKey))
        {
            return Results.Unauthorized();
        }

        if (!backups.IsConfigured)
        {
            return Results.Problem(BackupService.NotConfiguredMessage, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var result = await backups.RunBackupAsync(lifetime.ApplicationStopping);
        return result.Succeeded
            ? Results.Ok(new { result.Message })
            : Results.Problem(result.Message, statusCode: StatusCodes.Status500InternalServerError);
    }).DisableAntiforgery();
}
else if (!string.IsNullOrWhiteSpace(app.Configuration["Backup:TriggerKey"]))
{
    app.Logger.LogWarning("Backup:TriggerKey is shorter than {Length} characters, so {Path} is turned off.",
        BackupTrigger.MinimumKeyLength, BackupTrigger.Path);
}

// ---- Receipt downloads: no public access, no SAS tokens -- every download is mediated here,
// checking auth (and, for parents, ownership) before streaming from blob storage. ----
app.MapGet("/admin/reimbursements/{id}/receipt", async (
    string id, string? guardianId, IReimbursementRequestRepository requests, IReceiptStorageService receipts) =>
{
    // guardianId is an optional hint (the caller's PartitionKey, when already known) that turns
    // this into a direct point read instead of a full-table scan; see ReimbursementDetail.razor.
    var request = string.IsNullOrEmpty(guardianId) ? null : await requests.GetByIdAsync(guardianId, id);
    request ??= await requests.GetByRequestIdAsync(id);
    if (request is null)
    {
        return Results.NotFound();
    }

    var download = await receipts.DownloadAsync(request.ReceiptBlobPath, request.ReceiptContentType, request.ReceiptOriginalFileName);
    return Results.File(download.Content, download.ContentType);
}).RequireAuthorization("AdminOnly");

app.MapGet("/admin/bank/accounts/{accountId}/entries/{rowKey}/receipt", async (
    string accountId, string rowKey, ITroopBankRepository bank, IReceiptStorageService receipts) =>
{
    var entry = await bank.GetTransactionAsync(accountId, rowKey);
    if (entry is null || !entry.HasReceipt)
    {
        return Results.NotFound();
    }

    var download = await receipts.DownloadAsync(entry.ReceiptBlobPath, entry.ReceiptContentType, entry.ReceiptFileName);
    return Results.File(download.Content, download.ContentType);
}).RequireAuthorization("AdminOnly");

app.MapGet("/portal/reimbursements/{id}/receipt", async (
    string id, HttpContext http, IReimbursementRequestRepository requests, IReceiptStorageService receipts) =>
{
    var guardianId = http.User.FindFirst("GuardianId")?.Value;
    var request = guardianId is null ? null : await requests.GetByIdAsync(guardianId, id);
    if (request is null)
    {
        return Results.NotFound();
    }

    var download = await receipts.DownloadAsync(request.ReceiptBlobPath, request.ReceiptContentType, request.ReceiptOriginalFileName);
    return Results.File(download.Content, download.ContentType);
}).RequireAuthorization("ParentOnly");

// ---- Receipt thumbnails: the small previews beside each receipt, drawn on the server and behind
// the same checks as the receipt itself. Always answers with an image, so a receipt that can't be
// drawn shows a plain placeholder rather than a broken picture. ----
app.MapGet("/admin/reimbursements/{id}/thumbnail", async (
    string id, string? guardianId, HttpContext http, IReimbursementRequestRepository requests, IReceiptThumbnailService thumbnails) =>
{
    var request = string.IsNullOrEmpty(guardianId) ? null : await requests.GetByIdAsync(guardianId, id);
    request ??= await requests.GetByRequestIdAsync(id);
    return request is null ? Results.NotFound() : await ThumbnailResultAsync(http, request, thumbnails);
}).RequireAuthorization("AdminOnly");

app.MapGet("/portal/reimbursements/{id}/thumbnail", async (
    string id, HttpContext http, IReimbursementRequestRepository requests, IReceiptThumbnailService thumbnails) =>
{
    var guardianId = http.User.FindFirst("GuardianId")?.Value;
    var request = guardianId is null ? null : await requests.GetByIdAsync(guardianId, id);
    return request is null ? Results.NotFound() : await ThumbnailResultAsync(http, request, thumbnails);
}).RequireAuthorization("ParentOnly");

// ---- Printable invoices: PDF files, opened in a new tab from the "Print" links. The treasurer
// can print any invoice; a parent only their own family's. ----
app.MapGet("/admin/invoices/{id}/print", async (string id, string? scoutId, HttpContext http, IInvoiceDocumentService documents) =>
{
    var sheet = await documents.GetInvoiceAsync(id, scoutId);
    return sheet is null
        ? Results.NotFound("Invoice not found.")
        : Pdf(http, InvoicePdf.Invoice(troopInfo, sheet, DateTime.Now), $"Invoice - {sheet.Invoice.BilledTo}");
}).RequireAuthorization("AdminOnly");

// ?guardianId= picks the family; ?invoices=id1,id2 limits it to the invoices ticked on Record
// family payment (otherwise every outstanding invoice for the family).
app.MapGet("/admin/invoices/family-payment/print", async (
    string? guardianId, string? invoices, HttpContext http, IGuardianRepository guardians, IInvoiceDocumentService documents) =>
{
    var guardian = string.IsNullOrEmpty(guardianId) ? null : await guardians.GetByIdAsync(guardianId);
    if (guardian is null)
    {
        return Results.NotFound("Guardian not found.");
    }

    var only = string.IsNullOrWhiteSpace(invoices)
        ? null
        : invoices.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();
    var family = await documents.GetFamilyAsync(guardian, only);
    return family.Invoices.Count == 0
        ? Results.NotFound($"{guardian.FullName} has no outstanding invoices to print.")
        : Pdf(http, InvoicePdf.Families(troopInfo, [family], DateTime.Now), $"Family bill - {family.BillTo}");
}).RequireAuthorization("AdminOnly");

app.MapGet("/admin/bank/deposits/{id}/print", async (string id, HttpContext http, ITroopBankRepository bank) =>
{
    var deposit = await bank.GetDepositAsync(id);
    if (deposit is null)
    {
        return Results.NotFound("Deposit not found.");
    }

    var account = await bank.GetAccountAsync(deposit.AccountId);
    var lines = await bank.GetDepositLinesAsync(id);
    return Pdf(http, BankPdf.DepositRecord(troopInfo, deposit, lines, account?.Name ?? "", DateTime.Now), $"Deposit {deposit.Date.UtcDateTime:yyyy-MM-dd}");
}).RequireAuthorization("AdminOnly");

app.MapGet("/admin/donations/{id}/thank-you-letter", async (string id, HttpContext http, IDonationRepository donations, TroopInfo troop) =>
{
    var donation = await donations.GetByIdAsync(id);
    if (donation is null)
    {
        return Results.NotFound("Donation not found.");
    }

    var letter = DonationLetter.Build(donation, troop, http.User.Identity?.Name ?? "", DateOnly.FromDateTime(DateTime.Now));
    return Docx(http, letter, $"Thank-you letter - {donation.DonorName} - {DateTime.Now:yyyy-MM-dd}");
}).RequireAuthorization("AdminOnly");

app.MapGet("/admin/invoices/print-outstanding", async (HttpContext http, IInvoiceDocumentService documents) =>
{
    var families = await documents.GetAllOutstandingAsync();
    return families.Count == 0
        ? Results.NotFound("There are no outstanding invoices to print.")
        : Pdf(http, InvoicePdf.Families(troopInfo, families, DateTime.Now), $"Outstanding invoices {DateTime.Now:yyyy-MM-dd}");
}).RequireAuthorization("AdminOnly");

app.MapGet("/portal/invoices/{id}/print", async (string id, HttpContext http, IGuardianRepository guardians, IInvoiceDocumentService documents) =>
{
    var guardian = await BsaTroopFinances.Components.Pages.Portal.PortalGuardian.ResolveAsync(http.User, guardians);
    var sheet = guardian is null ? null : await documents.GetInvoiceForGuardianAsync(guardian, id);
    return sheet is null
        ? Results.NotFound("Invoice not found.")
        : Pdf(http, InvoicePdf.Invoice(troopInfo, sheet, DateTime.Now), $"Invoice - {sheet.Invoice.BilledTo}");
}).RequireAuthorization("ParentOnly");

// Always the signed-in guardian's own family: there is no id in the address to change.
app.MapGet("/portal/invoices/family/print", async (HttpContext http, IGuardianRepository guardians, IInvoiceDocumentService documents) =>
{
    var guardian = await BsaTroopFinances.Components.Pages.Portal.PortalGuardian.ResolveAsync(http.User, guardians);
    if (guardian is null)
    {
        return Results.NotFound("Your account isn't set up yet. Contact the troop treasurer.");
    }

    var family = await documents.GetFamilyAsync(guardian);
    return family.Invoices.Count == 0
        ? Results.NotFound("You have no outstanding invoices to print.")
        : Pdf(http, InvoicePdf.Families(troopInfo, [family], DateTime.Now), "Family bill");
}).RequireAuthorization("ParentOnly");

// ---- Families, picked on the Families page: one guardian id per family (?g=id&g=id), each
// standing for that guardian's whole household. ----
app.MapGet("/admin/families/invoices", async ([FromQuery] string[]? g, HttpContext http, IFamilyStatusService families, IInvoiceDocumentService documents) =>
{
    var sheets = new List<FamilySheet>();
    foreach (var household in await families.ResolveAsync(g ?? []))
    {
        if (await documents.GetHouseholdAsync(household) is { Invoices.Count: > 0 } sheet)
        {
            sheets.Add(sheet);
        }
    }

    return sheets.Count == 0
        ? Results.NotFound("None of the selected families have outstanding invoices.")
        : Pdf(http, InvoicePdf.Families(troopInfo, sheets, DateTime.Now), sheets.Count == 1 ? $"Family bill - {sheets[0].BillTo}" : $"Family bills {DateTime.Now:yyyy-MM-dd}");
}).RequireAuthorization("AdminOnly");

app.MapGet("/admin/families/status-sheets", async (
    [FromQuery] string[]? g, DateOnly? from, DateOnly? to, HttpContext http, IFamilyStatusService families) =>
{
    var (start, end) = FamilyStatusService.Period(from, to, DateOnly.FromDateTime(DateTime.Now));
    var sheets = new List<FamilyStatusSheet>();
    foreach (var household in await families.ResolveAsync(g ?? []))
    {
        sheets.Add(await families.GetSheetAsync(household, start, end));
    }

    return sheets.Count == 0
        ? Results.NotFound("No families were selected.")
        : Pdf(http, InvoicePdf.FamilyStatus(troopInfo, sheets, DateTime.Now), sheets.Count == 1 ? $"Family status - {sheets[0].Family.Name}" : $"Family status {DateTime.Now:yyyy-MM-dd}");
}).RequireAuthorization("AdminOnly");

app.MapGet("/admin/reports/troop/print", async (DateOnly? from, DateOnly? to, HttpContext http, ITroopReportService reports) =>
    Pdf(http, TroopReportPdf.Create(troopInfo, await reports.GetAsync(from, to), DateTime.Now), $"Troop finances {DateTime.Now:yyyy-MM-dd}"))
    .RequireAuthorization("AdminOnly");

// A parent's own sheet: just them and their own scouts, never a scout linked only to someone else.
app.MapGet("/portal/status-sheet", async (DateOnly? from, DateOnly? to, HttpContext http, IGuardianRepository guardians, IFamilyStatusService families) =>
{
    var guardian = await BsaTroopFinances.Components.Pages.Portal.PortalGuardian.ResolveAsync(http.User, guardians);
    if (guardian is null)
    {
        return Results.NotFound("Your account isn't set up yet. Contact the troop treasurer.");
    }

    var (start, end) = FamilyStatusService.Period(from, to, DateOnly.FromDateTime(DateTime.Now));
    var sheet = await families.GetSheetAsync(await families.GetOwnHouseholdAsync(guardian), start, end);
    return Pdf(http, InvoicePdf.FamilyStatus(troopInfo, [sheet], DateTime.Now), "Family status");
}).RequireAuthorization("ParentOnly");

app.MapPost("/portal/receipts/{id}/delete", async (
    string id, HttpContext http, IReimbursementService reimbursements) =>
{
    var guardianId = http.User.FindFirst("GuardianId")?.Value;
    if (guardianId is null)
    {
        return Results.NotFound();
    }

    try
    {
        await reimbursements.DeletePendingAsync(guardianId, id);
        return Results.LocalRedirect("/portal/receipts");
    }
    catch (InvalidOperationException)
    {
        return Results.NotFound();
    }
}).RequireAuthorization("ParentOnly");

app.MapPost("/portal/logout", async (HttpContext http) =>
{
    await http.SignOutAsync("ParentAuth");
    return Results.LocalRedirect("/");
});

// ---- Admin sign-in: the buttons on /admin/signin land here, and this hands off to Microsoft or
// Google. Both come back signed into "AdminCookie" and are sent on to returnUrl. ----
app.MapGet("/admin/signin/{provider}", (string provider, string? returnUrl) =>
{
    var scheme = provider.ToLowerInvariant() switch
    {
        "microsoft" when microsoftSignIn => OpenIdConnectDefaults.AuthenticationScheme,
        "google" when googleSignIn => GoogleDefaults.AuthenticationScheme,
        _ => null,
    };
    return scheme is null
        ? Results.LocalRedirect("/admin/signin")
        : Results.Challenge(new AuthenticationProperties { RedirectUri = AdminIdentity.SafeReturnUrl(returnUrl) }, [scheme]);
});

// Clears this app's session only. Signing out of Microsoft too would mean nothing to a Google
// sign-in, and would sign the person out of everything else they use that account for.
app.MapPost("/admin/signout", async (HttpContext http) =>
{
    await http.SignOutAsync("AdminCookie");
    return Results.LocalRedirect("/");
});

if (devAuthEnabled)
{
    DevAuth.MapEndpoints(app);
    app.Logger.LogWarning("DevAuth is ON: admin sign-in is faked at {Path}. Never enable this outside local development.", DevAuth.SignInPath);
}

app.Run();

// Shown in the browser's PDF viewer (inline) rather than downloaded; the name is what "Save" offers.
static IResult Pdf(HttpContext http, byte[] pdf, string name)
{
    var fileName = string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c is ' ' or '-' ? c : '_')) + ".pdf";
    http.Response.Headers.ContentDisposition = new System.Net.Mime.ContentDisposition { Inline = true, FileName = fileName }.ToString();
    http.Response.Headers.CacheControl = "no-store";
    return Results.Bytes(pdf, "application/pdf");
}

static IResult Docx(HttpContext http, byte[] document, string name)
{
    var fileName = string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c is ' ' or '-' ? c : '_')) + ".docx";
    http.Response.Headers.ContentDisposition = new System.Net.Mime.ContentDisposition { FileName = fileName }.ToString();
    http.Response.Headers.CacheControl = "no-store";
    return Results.Bytes(document, "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
}

static async Task<IResult> ThumbnailResultAsync(HttpContext http, BsaTroopFinances.Data.Entities.ReimbursementRequestEntity request, IReceiptThumbnailService thumbnails)
{
    var jpeg = await thumbnails.GetOrCreateAsync(request, http.RequestAborted);
    if (jpeg is not null)
    {
        // A receipt's file never changes, so neither does its thumbnail; "private" keeps it out of shared caches.
        http.Response.Headers.CacheControl = "private, max-age=604800";
        return Results.Bytes(jpeg, "image/jpeg");
    }

    var label = request.ReceiptContentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase) ? "PDF" : "Receipt";
    http.Response.Headers.CacheControl = "private, max-age=300";
    return Results.Content(
        $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 213 213"><rect width="213" height="213" fill="#fff"/><text x="106.5" y="113" text-anchor="middle" font-family="sans-serif" font-size="20" font-weight="700" fill="#5b6472">{label}</text></svg>""",
        "image/svg+xml");
}
