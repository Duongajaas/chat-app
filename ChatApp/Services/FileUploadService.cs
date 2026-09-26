using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace ChatApp.Services;

public class FileUploadService : IFileUploadService
{
    private readonly IConfiguration _config;

    // Chỉ nhận cấu hình, không khởi tạo Cloudinary ở đây để tránh lỗi vòng ngoài
    public FileUploadService(IConfiguration config)
    {
        _config = config;
    }

    public async Task<ImageUploadResult> UploadImageAsync(IFormFile file)
    {
        var cloudName = _config["Cloudinary:CloudName"];
        var apiKey = _config["Cloudinary:ApiKey"];
        var apiSecret = _config["Cloudinary:ApiSecret"];

        // Bắt lỗi ngay nếu cấu hình bị trống
        if (string.IsNullOrEmpty(cloudName) || string.IsNullOrEmpty(apiKey))
        {
            throw new Exception($"Lỗi cấu hình: CloudName='{cloudName}', ApiKey='{apiKey}'. Vui lòng kiểm tra lại file appsettings.json hoặc .env");
        }

        var acc = new Account(cloudName, apiKey, apiSecret);
        var cloudinary = new Cloudinary(acc);

        var uploadResult = new ImageUploadResult();

        if (file.Length > 0)
        {
            using var stream = file.OpenReadStream();
            var uploadParams = new ImageUploadParams
            {
                File = new FileDescription(file.FileName, stream),
                Transformation = new Transformation().Quality("auto").FetchFormat("auto")
            };
            
            uploadResult = await cloudinary.UploadAsync(uploadParams);
        }

        return uploadResult;
    }
}