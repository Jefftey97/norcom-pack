using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Norcom.Launcher.Core.Services;

public static class KnownVisualAssets
{
	public static readonly VisualAsset[] All = new VisualAsset[6]
	{
		new VisualAsset("shaderpacks/ComplementaryUnbound_r5.9.3.zip", "https://cdn.modrinth.com/data/R6NEzAwj/versions/B1kyfoUZ/ComplementaryUnbound_r5.9.3.zip", "a085d1d8cda67cd17a2b414eeba48bbd809da069f269bab46246235ec8fe714a275013c70d0504527b0987d810deddddba4564332414ab3b3bd7435932ff808c"),
		new VisualAsset("resourcepacks/Patrix_26.3_32x_basic.zip", "https://cdn.modrinth.com/data/olO1TaXd/versions/V9k0Ak3w/Patrix_26.3_32x_basic.zip", "ad03f836285a0a894b0ed5d64ee3c910a67dd63c743e44f6f4330d32223dbff27795f7773d6ea6fb283591b489770822f796ce9184ac3f69663dc5a0cf4d0352"),
		new VisualAsset("resourcepacks/Patrix_26.3_32x_addon.zip", "https://cdn.modrinth.com/data/olO1TaXd/versions/V9k0Ak3w/Patrix_26.3_32x_addon.zip", "b739041ad699ea3e532724ffabd128567f5f0686afbd259ba8eb7f88d9f194781cadeebfa9b18d46f6de9a453bf674d78b9d806a950eddcf11ec4fccb320333f"),
		new VisualAsset("resourcepacks/Patrix_26.3_models.zip", "https://cdn.modrinth.com/data/olO1TaXd/versions/V9k0Ak3w/Patrix_26.3_models.zip", "39d4e6fd92d1daa9fb515ed59f57dc3d09ba5b2067c9f4bc0639447dc74079c9aff46fa1138495074ee3b1eb6ef2b6bbd85d5c2dd576cf69656f9b62163067dd"),
		new VisualAsset("resourcepacks/Alacrity.zip", "https://cdn.modrinth.com/data/PUUpX2qq/versions/Kc2ktOzs/Alacrity.zip", "0b0faa45553153000b4cbb187672f49d06e19b4212f3c490964cc91e6e97767ba00ab42a30c4b77ab52994883f9a2a1251f98fd383d59cfa07ce5793d1452822"),
		new VisualAsset("resourcepacks/norcom_marble.zip", "https://raw.githubusercontent.com/Jefftey97/norcom-pack/main/resourcepacks/norcom_marble.zip", "95c00fd5d78d426d534e0579511ae261421a8e8ef1fad945a54517d7b9ac419b7d53e5f4c61cd49fa8b8622d1648b1b0babb2a470e76cac2e53f8929915cc128")
	};

	public static async Task EnsureAsync(HttpClient http, string gameDir, TransferMeter meter, CancellationToken ct)
	{
		int i = 0;
		VisualAsset[] all = All;
		foreach (VisualAsset asset in all)
		{
			i++;
			string dest = Path.Combine(gameDir, asset.RelativePath.Replace('/', Path.DirectorySeparatorChar));
			meter.SetPhase("Checking " + Path.GetFileName(asset.RelativePath), 0.2 + 0.7 * ((double)i / (double)All.Length), 0.05);
			bool flag = false;
			if (File.Exists(dest))
			{
				flag = Hashing.EqualsHex(await Hashing.FileHexAsync(dest, "sha512", ct).ConfigureAwait(continueOnCapturedContext: false), asset.Sha512);
			}
			if (!flag)
			{
				meter.SetPhase("Downloading " + Path.GetFileName(asset.RelativePath), meter.Snapshot().Fraction, 0.1);
				await HttpDownload.DownloadAsync(http, asset.Url, dest, meter, ct).ConfigureAwait(continueOnCapturedContext: false);
				if (!Hashing.EqualsHex(await Hashing.FileHexAsync(dest, "sha512", ct).ConfigureAwait(continueOnCapturedContext: false), asset.Sha512))
				{
					throw new InvalidDataException("Visual file hash mismatch: " + asset.RelativePath);
				}
			}
		}
	}
}
