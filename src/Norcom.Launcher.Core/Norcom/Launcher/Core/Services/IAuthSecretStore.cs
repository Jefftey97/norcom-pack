namespace Norcom.Launcher.Core.Services;

public interface IAuthSecretStore
{
	string Directory { get; }

	void Write(string name, byte[] plaintext);

	byte[]? Read(string name);

	void Delete(string name);
}
