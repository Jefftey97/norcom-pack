namespace Norcom.Launcher.Core.Services;

public interface IDataProtector
{
	byte[] Protect(byte[] plaintext);

	byte[] Unprotect(byte[] ciphertext);
}
