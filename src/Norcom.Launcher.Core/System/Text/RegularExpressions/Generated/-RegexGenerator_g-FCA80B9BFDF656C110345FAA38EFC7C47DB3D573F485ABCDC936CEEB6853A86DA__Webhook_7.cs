using System.CodeDom.Compiler;

namespace System.Text.RegularExpressions.Generated;

[GeneratedCode("System.Text.RegularExpressions.Generator", "8.0.15.2219")]
internal sealed class _003CRegexGenerator_g_003EFCA80B9BFDF656C110345FAA38EFC7C47DB3D573F485ABCDC936CEEB6853A86DA__Webhook_7 : Regex
{
	private sealed class RunnerFactory : RegexRunnerFactory
	{
		private sealed class Runner : RegexRunner
		{
			protected override void Scan(ReadOnlySpan<char> inputSpan)
			{
				while (TryFindNextPossibleStartingPosition(inputSpan) && !TryMatchAtCurrentPosition(inputSpan) && runtextpos != inputSpan.Length)
				{
					runtextpos++;
					if (_003CRegexGenerator_g_003EFCA80B9BFDF656C110345FAA38EFC7C47DB3D573F485ABCDC936CEEB6853A86DA__Utilities.s_hasTimeout)
					{
						CheckTimeout();
					}
				}
			}

			private bool TryFindNextPossibleStartingPosition(ReadOnlySpan<char> inputSpan)
			{
				int num = runtextpos;
				if (num <= inputSpan.Length - 34)
				{
					int num2 = inputSpan.Slice(num).IndexOf("https://", StringComparison.OrdinalIgnoreCase);
					if (num2 >= 0)
					{
						runtextpos = num + num2;
						return true;
					}
				}
				runtextpos = inputSpan.Length;
				return false;
			}

			private bool TryMatchAtCurrentPosition(ReadOnlySpan<char> inputSpan)
			{
				int num = runtextpos;
				int start = num;
				int num2 = 0;
				int num3 = 0;
				int num4 = 0;
				int pos = 0;
				ReadOnlySpan<char> span = inputSpan.Slice(num);
				if ((uint)span.Length < 8u || !span.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
				{
					return false;
				}
				num3 = num;
				if ((uint)span.Length >= 15u && span.Slice(8).StartsWith("discord", StringComparison.OrdinalIgnoreCase))
				{
					num += 15;
					span = inputSpan.Slice(num);
					num4 = 0;
					while (true)
					{
						_003CRegexGenerator_g_003EFCA80B9BFDF656C110345FAA38EFC7C47DB3D573F485ABCDC936CEEB6853A86DA__Utilities.StackPush(ref runstack, ref pos, num);
						num4++;
						if ((uint)span.Length < 3u || !span.StartsWith("app", StringComparison.OrdinalIgnoreCase))
						{
							break;
						}
						num += 3;
						span = inputSpan.Slice(num);
						if (num4 == 0)
						{
							continue;
						}
						goto IL_00e7;
					}
					goto IL_00c4;
				}
				goto IL_0117;
				IL_0117:
				num = num3;
				span = inputSpan.Slice(num);
				if ((uint)span.Length < 26u || !span.Slice(8).StartsWith("canary.discord.com", StringComparison.OrdinalIgnoreCase))
				{
					return false;
				}
				num2 = 1;
				num += 26;
				span = inputSpan.Slice(num);
				goto IL_0177;
				IL_00c4:
				if (--num4 >= 0)
				{
					num = runstack[--pos];
					span = inputSpan.Slice(num);
					goto IL_00e7;
				}
				goto IL_0117;
				IL_00e7:
				if ((uint)span.Length < 4u || !span.StartsWith(".com", StringComparison.OrdinalIgnoreCase))
				{
					goto IL_00c4;
				}
				num2 = 0;
				num += 4;
				span = inputSpan.Slice(num);
				goto IL_0177;
				IL_0177:
				while (true)
				{
					char c;
					if ((uint)span.Length >= 14u && span.StartsWith("/api/webhoo", StringComparison.OrdinalIgnoreCase) && !(((c = span[11]) | 0x20) != 107 && c != 'K') && span.Slice(12).StartsWith("s/", StringComparison.OrdinalIgnoreCase))
					{
						num += 14;
						span = inputSpan.Slice(num);
						int i;
						for (i = 0; (uint)i < (uint)span.Length && !char.IsWhiteSpace(span[i]); i++)
						{
						}
						if (i != 0)
						{
							span = span.Slice(i);
							Capture(0, start, runtextpos = num + i);
							return true;
						}
					}
					if (_003CRegexGenerator_g_003EFCA80B9BFDF656C110345FAA38EFC7C47DB3D573F485ABCDC936CEEB6853A86DA__Utilities.s_hasTimeout)
					{
						CheckTimeout();
					}
					if (num2 == 0)
					{
						break;
					}
					if (num2 == 1)
					{
						return false;
					}
				}
				goto IL_00c4;
			}
		}

		protected override RegexRunner CreateInstance()
		{
			return new Runner();
		}
	}

	internal static readonly _003CRegexGenerator_g_003EFCA80B9BFDF656C110345FAA38EFC7C47DB3D573F485ABCDC936CEEB6853A86DA__Webhook_7 Instance = new _003CRegexGenerator_g_003EFCA80B9BFDF656C110345FAA38EFC7C47DB3D573F485ABCDC936CEEB6853A86DA__Webhook_7();

	private _003CRegexGenerator_g_003EFCA80B9BFDF656C110345FAA38EFC7C47DB3D573F485ABCDC936CEEB6853A86DA__Webhook_7()
	{
		pattern = "(?i)https://(?:discord(?:app)?\\.com|canary\\.discord\\.com)/api/webhooks/\\S+";
		roptions = RegexOptions.CultureInvariant;
		Regex.ValidateMatchTimeout(_003CRegexGenerator_g_003EFCA80B9BFDF656C110345FAA38EFC7C47DB3D573F485ABCDC936CEEB6853A86DA__Utilities.s_defaultTimeout);
		internalMatchTimeout = _003CRegexGenerator_g_003EFCA80B9BFDF656C110345FAA38EFC7C47DB3D573F485ABCDC936CEEB6853A86DA__Utilities.s_defaultTimeout;
		factory = new RunnerFactory();
		capsize = 1;
	}
}
