using ChatApp.DTOs;
using ChatApp.Media;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ChatApp.Controllers;

[ApiController, Authorize, Route("api/expressions")]
public sealed class ExpressionsController(ExpressionCatalog catalog) : ControllerBase
{
    [HttpGet("sticker-packs"), EnableRateLimiting("media-read")]
    public ActionResult<StickerPack[]> GetStickerPacks() => Ok(catalog.Packs);
}
