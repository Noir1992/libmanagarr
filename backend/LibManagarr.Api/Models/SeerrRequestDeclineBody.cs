namespace LibManagarr.Api.Models;

// API body for "POST /api/requests/{id}/decline"; the reason is optional upstream as well.
public sealed class SeerrRequestDeclineBody
{
    public string? Reason { get; set; }
}
