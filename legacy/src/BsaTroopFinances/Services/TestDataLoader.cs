using BsaTroopFinances.Data;
using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Data.Repositories;
using BsaTroopFinances.Storage;
using Microsoft.EntityFrameworkCore;

namespace BsaTroopFinances.Services;

public interface ITestDataLoader
{
    /// <summary>True only while the TestData:Enabled setting is on. It is meant to be on before the
    /// troop goes live and removed after, so real records can never be mixed with generated ones.</summary>
    bool IsEnabled { get; }

    /// <summary>Adds a year of generated activity for the scouts and guardians already in the
    /// database (see <see cref="TestDataGenerator"/>). Throws InvalidOperationException when turned
    /// off, when another load is running, or when there is nobody to generate it for.</summary>
    Task<TestDataSummary> LoadAsync(TestDataOptions options);
}

/// <summary>
/// Runs the test-data generator against the app's own database and receipt storage: behind the
/// admin "Test data" settings page and the seed-test-data command. The generator needs the real
/// services with a clock it can move and with emails switched off, so each load builds its own
/// small set of them rather than borrowing the ones the rest of the site is using.
/// </summary>
public class TestDataLoader(IDbContextFactory<FinanceDbContext> dbFactory, IBlobStore blobs, IConfiguration configuration) : ITestDataLoader
{
    private static readonly SemaphoreSlim RunLock = new(1, 1);

    public bool IsEnabled => configuration.GetValue<bool>("TestData:Enabled");

    public async Task<TestDataSummary> LoadAsync(TestDataOptions options)
    {
        if (!IsEnabled)
        {
            throw new InvalidOperationException("Loading test data is turned off (TestData:Enabled).");
        }

        return await GenerateAsync(dbFactory, blobs, options);
    }

    /// <summary>The load itself, without the on/off setting -- the command line has its own checks.</summary>
    public static async Task<TestDataSummary> GenerateAsync(IDbContextFactory<FinanceDbContext> dbFactory, IBlobStore blobs, TestDataOptions options)
    {
        if (!await RunLock.WaitAsync(0))
        {
            throw new InvalidOperationException("Test data is already being loaded.");
        }

        try
        {
            var clock = new SettableClock(DateTimeOffset.UtcNow);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(dbFactory);
            services.AddSingleton<FinanceDb>();
            services.AddSingleton<IUnitOfWork>(sp => sp.GetRequiredService<FinanceDb>());
            services.AddSingleton<IBlobStore>(blobs);
            services.AddSingleton(clock);
            services.AddSingleton<TimeProvider>(clock);
            services.AddSingleton<INotificationService, NoNotifications>();
            services.AddScoped<IScoutRepository, ScoutRepository>();
            services.AddScoped<IGuardianRepository, GuardianRepository>();
            services.AddScoped<IGuardianScoutLinkRepository, GuardianScoutLinkRepository>();
            services.AddScoped<IFundraiserEventRepository, FundraiserEventRepository>();
            services.AddScoped<IScoutLedgerRepository, ScoutLedgerRepository>();
            services.AddScoped<IReimbursementRequestRepository, ReimbursementRequestRepository>();
            services.AddScoped<IInvoiceRepository, InvoiceRepository>();
            services.AddScoped<IInvoicePaymentRepository, InvoicePaymentRepository>();
            services.AddScoped<IFinanceAuditRepository, FinanceAuditRepository>();
            services.AddScoped<IReceiptStorageService, ReceiptStorageService>();
            services.AddScoped<IScoutAccountService, ScoutAccountService>();
            services.AddScoped<IReimbursementService, ReimbursementService>();
            services.AddScoped<TestDataGenerator>();

            await using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();

            if (options.Reimbursements > 0)
            {
                await blobs.EnsureContainerAsync(TableNames.ReceiptsContainer);
            }

            return await scope.ServiceProvider.GetRequiredService<TestDataGenerator>().GenerateAsync(options);
        }
        finally
        {
            RunLock.Release();
        }
    }

    private sealed class NoNotifications : INotificationService
    {
        public Task SendLoginCodeAsync(string toEmail, string code) => Task.CompletedTask;

        public Task AuthenticatorChangedAsync(GuardianEntity guardian, bool added) => Task.CompletedTask;

        public Task ReceiptSubmittedAsync(ReimbursementRequestEntity request) => Task.CompletedTask;

        public Task ReceiptStatusChangedAsync(ReimbursementRequestEntity request) => Task.CompletedTask;

        public Task InvoiceIssuedAsync(InvoiceEntity invoice) => Task.CompletedTask;

        public Task EditorGrantedAsync(AdminUserEntity grantee, string grantedBy) => Task.CompletedTask;

        public Task TemporaryEditStartedAsync(AdminUserEntity person, DateTimeOffset until, string reason) => Task.CompletedTask;
    }
}
