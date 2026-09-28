using Microsoft.AspNetCore.Http;
using CloudinaryDotNet.Actions;

namespace ChatApp.Services;

public interface IFileUploadService
{
    // Hàm này sẽ nhận file gửi lên và trả về kết quả (chứa URL)
    Task<ImageUploadResult> UploadImageAsync(IFormFile file);
}