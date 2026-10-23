namespace LibManagarr.Api.Models;

// Paged response for "GET /api/requests", following the paged ARR response style.
public sealed class SeerrRequestsResponse
{
    public IReadOnlyList<SeerrRequest> Requests { get; set; } = [];

    public int Total { get; set; }

    public int Page { get; set; }

    public int Limit { get; set; }
}
