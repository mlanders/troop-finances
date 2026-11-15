namespace BsaTroopFinances.Services;

/// <summary>
/// Runs the nightly backup from inside the web app -- the default (Backup:Schedule "InProcess"),
/// which suits a host that keeps the app running all night, like the App Service with Always On.
/// Every 15 minutes it checks whether it's past 2:00 AM Pacific and today's snapshot is still
/// incomplete, and if so runs it. Checking rather than firing once at 2:00 means a deploy or
/// restart over that time just delays the backup to the next check, and a failed run is retried
/// on the next check because its snapshot never got a manifest.
///
/// On a host that stops the app when nobody is using it (scale to zero), set Backup:Schedule to
/// "External" and have a scheduler call POST /internal/backup instead (see
/// <see cref="BackupTrigger"/> and infra/HOSTING.md); this worker then does nothing.
/// </summary>
public class NightlyBackupWorker(IBackupService backupService, IConfiguration configuration, TimeProvider timeProvider, ILogger<NightlyBackupWorker> logger)
    : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(15);
    private const int BackupHour = 2;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!backupService.IsConfigured)
        {
            logger.LogWarning("Nightly backups are disabled: no backup storage is configured (ConnectionStrings:BackupStorage or BackupStorage:Provider).");
            return;
        }

        if (BackupTrigger.IsExternalSchedule(configuration))
        {
            if (BackupTrigger.Key(configuration) is null)
            {
                logger.LogWarning("Backup:Schedule is External but Backup:TriggerKey isn't set (at least {Length} characters), so nothing can start the nightly backup.",
                    BackupTrigger.MinimumKeyLength);
            }
            else
            {
                logger.LogInformation("Nightly backups are started from outside the app (Backup:Schedule is External): POST {Path}.", BackupTrigger.Path);
            }

            return;
        }

        using var timer = new PeriodicTimer(CheckInterval, timeProvider);
        do
        {
            try
            {
                if (timeProvider.GetLocalNow().Hour >= BackupHour
                    && !await backupService.IsSnapshotCompleteAsync(backupService.TodaySnapshotDate, stoppingToken))
                {
                    var result = await backupService.RunBackupAsync(stoppingToken);
                    if (!result.Succeeded)
                    {
                        logger.LogWarning("Nightly backup did not complete: {Message}", result.Message);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Nightly backup check failed; will retry at the next check");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
