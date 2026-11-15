using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Warehouse3PL.WebApi.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public abstract class BaseApiController : ControllerBase
{
    private ISender? _sender;
    protected ISender Mediator => _sender ??= HttpContext.RequestServices.GetRequiredService<ISender>();
}

