using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Application.Features.Storage.Queries.ResolveStorageDownload;

namespace Warehouse3PL.WebApi.Controllers;

[Authorize]
[Tags("Storage & Evidence Download")]
[Route("api/v1/storage")]
public class StorageController : BaseApiController
{
    /// <summary>
    /// Downloads a finalized private evidence artifact after validating authentication,
    /// object-key namespace/traversal rules, HMAC-SHA256 signature/expiry, and tenant/facility/driver ownership.
    /// </summary>
    [HttpGet("download")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(
        [FromQuery(Name = "key")] string? key,
        [FromQuery(Name = "token")] string? token,
        CancellationToken ct)
    {
        var result = await Mediator.Send(new ResolveStorageDownloadQuery(key, token), ct);

        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
        Response.Headers["Pragma"] = "no-cache";
        Response.Headers["Content-Security-Policy"] = "default-src 'none'; sandbox";

        return File(result.ContentStream, result.ContentType, result.SafeFileName);
    }
}
