using ChatApp.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ChatApp.Controllers;

[Route("api/[controller]")]
[ApiController]
public class FilesController : ControllerBase
{
    private readonly IFileUploadService _fileUploadService;

    // Inject FileUploadService (nơi đã có sẵn hàm .Trim() dọn rác cực sạch)
    public FilesController(IFileUploadService fileUploadService)
    {
        _fileUploadService = fileUploadService;
    }

    [HttpPost("upload")]
    public async Task<IActionResult> Upload(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "Vui lòng chọn một file hợp lệ." });

        // Gọi sang Service để xử lý upload
        var result = await _fileUploadService.UploadImageAsync(file);

        // Bắt lỗi từ Cloudinary nếu có (Invalid Signature, v.v.)
        if (result.Error != null)
        {
            return BadRequest(result.Error.Message);
        }

        return Ok(new { url = result.SecureUrl?.ToString() });
    }
}