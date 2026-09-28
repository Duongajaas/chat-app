using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace ChatApp.Services;

public class FileUploadService : IFileUploadService
{
    private readonly IConfiguration _config;

    public FileUploadService(IConfiguration config)
    {
        _config = config;
    }

    public async Task<ImageUploadResult> UploadImageAsync(IFormFile file)
    {
        // Hàm Trim() sẽ gọt sạch mọi dấu cách/enter tàng hình sinh ra lúc dán file .env
        var cloudName = _config["Cloudinary:CloudName"]?.Trim();
        var apiKey = _config["Cloudinary:ApiKey"]?.Trim();
        var apiSecret = _config["Cloudinary:ApiSecret"]?.Trim();

        if (string.IsNullOrEmpty(cloudName) || string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiSecret))
        {
            throw new Exception("Lỗi thiếu Key Cloudinary trong cấu hình!");
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