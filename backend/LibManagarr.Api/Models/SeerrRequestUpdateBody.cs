namespace LibManagarr.Api.Models;

// API body for "PUT /api/requests/{id}"; every field is optional. Seerr's required mediaType is
// resolved server-side from the request detail, so the frontend never sends Seerr field names.
// Seasons are the tv season numbers the request should cover; the server id is not editable
// and stays whatever the request detail carries.
public sealed class SeerrRequestUpdateBody
{
    public int? ProfileId { get; set; }

    public string? RootFolder { get; set; }

    public List<int>? Seasons { get; set; }

    public bool? Is4kRequest { get; set; }
}
