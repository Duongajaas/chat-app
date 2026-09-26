using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;

namespace ChatApp.Controllers;

[Route("api/[controller]")]
[ApiController]
public class FilesController : ControllerBase
{
    private readonly IConfiguration _config;

    // Tiêm thẳng cấu hình hệ thống (luôn luôn hoạt động), bỏ qua Service trung gian
    public FilesController(IConfiguration config)
    {
        _config = config;
    }

    [HttpPost("upload")]
    public async Task<IActionResult> Upload(IFormFile file)
    {
        try
        {
            if (file == null || file.Length == 0)
                return BadRequest("Không có file nào được gửi lên.");

            // Đọc Key trực tiếp
            var cloudName = _config["Cloudinary:CloudName"];
            var apiKey = _config["Cloudinary:ApiKey"];
            var apiSecret = _config["Cloudinary:ApiSecret"];

            if (string.IsNullOrEmpty(cloudName) || string.IsNullOrEmpty(apiKey))
                return BadRequest("Không tìm thấy Key Cloudinary trong file appsettings.json!");

            // Kết nối và Upload
            var acc = new Account(cloudName, apiKey, apiSecret);
            var cloudinary = new Cloudinary(acc);

            using var stream = file.OpenReadStream();
            var uploadParams = new ImageUploadParams
            {
                File = new FileDescription(file.FileName, stream),
                Transformation = new Transformation().Quality("auto").FetchFormat("auto")
            };
            
            var result = await cloudinary.UploadAsync(uploadParams);
            
            if (result.Error != null)
                return BadRequest(result.Error.Message);

            // Trả về Link ảnh
            return Ok(new { Url = result.SecureUrl.ToString() });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}