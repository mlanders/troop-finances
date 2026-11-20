using BsaTroopFinances.Data.Entities;

namespace BsaTroopFinances.Data.Repositories;

/// <summary>What the meeting sign-in app has sent: its roster, its trips and its payments.</summary>
public interface ISignInAppRepository
{
    Task<IReadOnlyList<SignInPersonEntity>> GetPeopleAsync();

    Task<SignInPersonEntity?> GetPersonAsync(string personId);

    /// <summary>Adds the person, or updates them, by the sign-in app's PersonId.</summary>
    Task SavePersonAsync(SignInPersonEntity person);

    /// <summary>Every trip, soonest first.</summary>
    Task<IReadOnlyList<TripImportEntity>> GetTripsAsync();

    Task<TripImportEntity?> GetTripAsync(string signUpId);

    Task SaveTripAsync(TripImportEntity trip);

    Task<IReadOnlyList<TripImportLineEntity>> GetLinesAsync(string signUpId);

    /// <summary>Every line, on every trip: for counting what's waiting on the treasurer.</summary>
    Task<IReadOnlyList<TripImportLineEntity>> GetAllLinesAsync();

    Task<TripImportLineEntity?> GetLineAsync(string signUpId, string entryId);

    /// <summary>Adds the line, or updates it, by sign-up and entry id.</summary>
    Task SaveLineAsync(TripImportLineEntity line);

    Task<TripPaymentEntity?> GetPaymentAsync(string signUpId, string paymentId);

    Task AddPaymentAsync(TripPaymentEntity payment);
}
