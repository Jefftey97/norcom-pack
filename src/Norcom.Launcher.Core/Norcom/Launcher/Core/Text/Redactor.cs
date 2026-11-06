using System;
using System.CodeDom.Compiler;
using System.Text.RegularExpressions;
using System.Text.RegularExpressions.Generated;

namespace Norcom.Launcher.Core.Text;

public static class Redactor
{
	public static string Clean(string? text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return string.Empty;
		}
		string input = text;
		input = Bearer().Replace(input, "Bearer <redacted>");
		input = SecretAssign().Replace(input, "$1=<redacted>");
		input = AccessFlag().Replace(input, "$1 <redacted>");
		input = Xbl().Replace(input, "XBL3.0 x=<redacted>");
		input = Jwt().Replace(input, "<jwt>");
		input = Webhook().Replace(input, "<webhook>");
		if (input.Length > 4000)
		{
			input = input.Substring(0, 4000) + "…";
		}
		return input;
	}

	public static string ForWire(string? text, int max)
	{
		string input = Clean(text);
		input = UserPath().Replace(input, "X:\\Users\\<user>");
		input = HomePath().Replace(input, "/home/<user>");
		input = MacPath().Replace(input, "/Users/<user>");
		input = input.Replace("launcher_accounts.json", "<account-file>", StringComparison.OrdinalIgnoreCase);
		if (input.Length > max)
		{
			input = input.Substring(0, max);
		}
		return input;
	}

	[GeneratedCode("System.Text.RegularExpressions.Generator", "8.0.15.2219")]
	private static Regex Bearer()
	{
		return _003CRegexGenerator_g_003EFCA80B9BFDF656C110345FAA38EFC7C47DB3D573F485ABCDC936CEEB6853A86DA__Bearer_2.Instance;
	}

	[GeneratedCode("System.Text.RegularExpressions.Generator", "8.0.15.2219")]
	private static Regex SecretAssign()
	{
		return _003CRegexGenerator_g_003EFCA80B9BFDF656C110345FAA38EFC7C47DB3D573F485ABCDC936CEEB6853A86DA__SecretAssign_3.Instance;
	}

	[GeneratedCode("System.Text.RegularExpressions.Generator", "8.0.15.2219")]
	private static Regex AccessFlag()
	{
		return _003CRegexGenerator_g_003EFCA80B9BFDF656C110345FAA38EFC7C47DB3D573F485ABCDC936CEEB6853A86DA__AccessFlag_4.Instance;
	}

	[GeneratedCode("System.Text.RegularExpressions.Generator", "8.0.15.2219")]
	private static Regex Xbl()
	{
		return _003CRegexGenerator_g_003EFCA80B9BFDF656C110345FAA38EFC7C47DB3D573F485ABCDC936CEEB6853A86DA__Xbl_5.Instance;
	}

	[GeneratedCode("System.Text.RegularExpressions.Generator", "8.0.15.2219")]
	private static Regex Jwt()
	{
		return _003CRegexGenerator_g_003EFCA80B9BFDF656C110345FAA38EFC7C47DB3D573F485ABCDC936CEEB6853A86DA__Jwt_6.Instance;
	}

	[GeneratedCode("System.Text.RegularExpressions.Generator", "8.0.15.2219")]
	private static Regex Webhook()
	{
		return _003CRegexGenerator_g_003EFCA80B9BFDF656C110345FAA38EFC7C47DB3D573F485ABCDC936CEEB6853A86DA__Webhook_7.Instance;
	}

	[GeneratedCode("System.Text.RegularExpressions.Generator", "8.0.15.2219")]
	private static Regex UserPath()
	{
		return _003CRegexGenerator_g_003EFCA80B9BFDF656C110345FAA38EFC7C47DB3D573F485ABCDC936CEEB6853A86DA__UserPath_8.Instance;
	}

	[GeneratedCode("System.Text.RegularExpressions.Generator", "8.0.15.2219")]
	private static Regex HomePath()
	{
		return _003CRegexGenerator_g_003EFCA80B9BFDF656C110345FAA38EFC7C47DB3D573F485ABCDC936CEEB6853A86DA__HomePath_9.Instance;
	}

	[GeneratedCode("System.Text.RegularExpressions.Generator", "8.0.15.2219")]
	private static Regex MacPath()
	{
		return _003CRegexGenerator_g_003EFCA80B9BFDF656C110345FAA38EFC7C47DB3D573F485ABCDC936CEEB6853A86DA__MacPath_10.Instance;
	}
}
