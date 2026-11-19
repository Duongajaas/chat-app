namespace ChatApp.Services;

public interface IMessageEncryptionService
{
    string Encrypt(string? plaintext);
    string Decrypt(string? ciphertext);
}
