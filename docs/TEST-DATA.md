# Test data

## A year of activity

To fill the lists (paging, date filters, reports) on a local copy, add a large batch of
back-dated activity for the scouts and guardians already in the local database. Azurite must be
running:

```bash
dotnet run --project src/BsaTroopFinances -- seed-test-data [--invoices 250] [--reimbursements 120] [--fundraisers 2] [--seed <n>] [--yes]
```

It spreads invoices, reimbursements and fundraisers over the last 12 months in every status,
through the app's real services, so balances, ledgers and history stay consistent. The same
`--seed` gives the same data.

- Each run adds more; nothing existing is changed or removed.
- Everything it creates is recorded as done by "Test Data", and no emails are sent.
- It refuses to run outside Development, or against a database or storage account that isn't on
  this machine, unless both are named with `--target` and `--receipts-target` (then the database
  name has to be typed to confirm).

Code: `Services/TestDataCommand.cs`, `Services/TestDataLoader.cs`, `Services/TestDataGenerator.cs`.

## In the site

The same load is available at **Settings > Test data** (people with Edit access) while the
`TestData:Enabled` setting is on. It is on locally. On a hosted site, add the setting
`TestData__Enabled=true` only while the site holds nothing but test data, and **remove it before
going live**: there is no undo short of restoring a backup.

## Using your troop's own roster

To generate the test data for your real scouts instead of the sample ones, first copy just the
roster (scouts, guardians and their links; zero balances, no money records or receipts) from the
newest nightly backup of your live site. It only reads from the backup storage, and skips anyone
already in the local database:

```powershell
$env:ConnectionStrings__BackupStorage = "<backup account connection string>"
dotnet run --project src/BsaTroopFinances -- import-roster [<yyyy-MM-dd>] [--yes]
```

This puts real families' names and email addresses on your computer, so treat that local
database as you would the live one, and delete it when you're done
(`Services/RosterImportCommand.cs`).
