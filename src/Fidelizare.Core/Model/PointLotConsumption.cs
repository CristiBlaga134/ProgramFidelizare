namespace Fidelizare.Core.Model;

/// <summary>
/// Legătura dintre o mișcare de consum și lotul din care s-au luat punctele.
///
/// Fără această evidență, anularea unei recompense ar fi aproximativă: punctele
/// s-ar restitui cu o valabilitate nouă, adică clientul ar câștiga timp pe care
/// nu îl avea. Aici se știe exact din ce loturi s-a consumat, deci restituirea
/// se face înapoi în loturile originale, cu datele lor de expirare.
/// </summary>
public sealed class PointLotConsumption
{
    public int Id { get; set; }

    public int PointLotId { get; set; }

    /// <summary>Mișcarea din registru care a consumat punctele.</summary>
    public int PointTransactionId { get; set; }

    public int Points { get; set; }

    public DateTime OccurredAt { get; set; }
}
