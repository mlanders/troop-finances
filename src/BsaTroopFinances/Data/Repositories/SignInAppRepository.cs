using BsaTroopFinances.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BsaTroopFinances.Data.Repositories;

public class SignInAppRepository(FinanceDb db) : ISignInAppRepository
{
    public async Task<IReadOnlyList<SignInPersonEntity>> GetPeopleAsync() =>
        await db.ReadAsync(async c => await c.SignInPeople.AsNoTracking().OrderBy(p => p.FullName).ToListAsync());

    public async Task<SignInPersonEntity?> GetPersonAsync(string personId) =>
        await db.ReadAsync(c => c.SignInPeople.AsNoTracking()
            .FirstOrDefaultAsync(p => p.PartitionKey == SignInPersonEntity.PartitionKeyValue && p.RowKey == personId));

    public Task SavePersonAsync(SignInPersonEntity person) => db.WriteAsync(async c =>
    {
        person.PartitionKey = SignInPersonEntity.PartitionKeyValue;
        if (await c.SignInPeople.AnyAsync(p => p.PartitionKey == person.PartitionKey && p.RowKey == person.RowKey))
        {
            await c.UpdateDetachedAsync(person);
        }
        else
        {
            c.SignInPeople.Add(person);
        }
    });

    public async Task<IReadOnlyList<TripImportEntity>> GetTripsAsync() =>
        await db.ReadAsync(async c => await c.TripImports.AsNoTracking().OrderBy(t => t.StartDate).ToListAsync());

    public async Task<TripImportEntity?> GetTripAsync(string signUpId) =>
        await db.ReadAsync(c => c.TripImports.AsNoTracking()
            .FirstOrDefaultAsync(t => t.PartitionKey == TripImportEntity.PartitionKeyValue && t.RowKey == signUpId));

    public Task SaveTripAsync(TripImportEntity trip) => db.WriteAsync(async c =>
    {
        trip.PartitionKey = TripImportEntity.PartitionKeyValue;
        if (await c.TripImports.AnyAsync(t => t.PartitionKey == trip.PartitionKey && t.RowKey == trip.RowKey))
        {
            await c.UpdateDetachedAsync(trip);
        }
        else
        {
            c.TripImports.Add(trip);
        }
    });

    public async Task<IReadOnlyList<TripImportLineEntity>> GetLinesAsync(string signUpId) =>
        await db.ReadAsync(async c => await c.TripImportLines.AsNoTracking()
            .Where(l => l.PartitionKey == signUpId).OrderBy(l => l.Name).ToListAsync());

    public async Task<IReadOnlyList<TripImportLineEntity>> GetAllLinesAsync() =>
        await db.ReadAsync(async c => await c.TripImportLines.AsNoTracking().ToListAsync());

    public async Task<TripImportLineEntity?> GetLineAsync(string signUpId, string entryId) =>
        await db.ReadAsync(c => c.TripImportLines.AsNoTracking()
            .FirstOrDefaultAsync(l => l.PartitionKey == signUpId && l.RowKey == entryId));

    public Task SaveLineAsync(TripImportLineEntity line) => db.WriteAsync(async c =>
    {
        if (await c.TripImportLines.AnyAsync(l => l.PartitionKey == line.PartitionKey && l.RowKey == line.RowKey))
        {
            await c.UpdateDetachedAsync(line);
        }
        else
        {
            c.TripImportLines.Add(line);
        }
    });

    public async Task<TripPaymentEntity?> GetPaymentAsync(string signUpId, string paymentId) =>
        await db.ReadAsync(c => c.TripPayments.AsNoTracking()
            .FirstOrDefaultAsync(p => p.PartitionKey == signUpId && p.RowKey == paymentId));

    public Task AddPaymentAsync(TripPaymentEntity payment) => db.WriteAsync(c =>
    {
        c.TripPayments.Add(payment);
        return Task.CompletedTask;
    });
}
