using System.CodeDom.Compiler;
using System.Runtime.CompilerServices;

namespace System.Text.RegularExpressions.Generated;

[GeneratedCode("System.Text.RegularExpressions.Generator", "8.0.15.2219")]
internal sealed class _003CRegexGenerator_g_003EFCA80B9BFDF656C110345FAA38EFC7C47DB3D573F485ABCDC936CEEB6853A86DA__AccessFlag_4 : Regex
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
				if (num <= inputSpan.Length - 15)
				{
					int num2 = inputSpan.Slice(num).IndexOf("--");
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
				int num5 = 0;
				int num6 = 0;
				int num7 = 0;
				int num8 = 0;
				ReadOnlySpan<char> span = inputSpan.Slice(num);
				num5 = num;
				if ((uint)span.Length < 3u || !span.StartsWith("--a", StringComparison.OrdinalIgnoreCase))
				{
					UncaptureUntil(0);
					return false;
				}
				num4 = num;
				num3 = Crawlpos();
				char c;
				if ((uint)span.Length < 13u || !span.Slice(3).StartsWith("ccessto", StringComparison.OrdinalIgnoreCase) || (((c = span[10]) | 0x20) != 107 && c != 'K') || !span.Slice(11).StartsWith("en", StringComparison.OrdinalIgnoreCase))
				{
					goto IL_00d6;
				}
				num2 = 0;
				num += 13;
				span = inputSpan.Slice(num);
				goto IL_018b;
				IL_018b:
				while (true)
				{
					Capture(1, num5, num);
					num7 = num;
					int i;
					for (i = 0; (uint)i < (uint)span.Length && char.IsWhiteSpace(span[i]); i++)
					{
					}
					if (i != 0)
					{
						span = span.Slice(i);
						num += i;
						num8 = num;
						num7++;
						while (true)
						{
							num6 = Crawlpos();
							int j;
							for (j = 0; (uint)j < (uint)span.Length && !char.IsWhiteSpace(span[j]); j++)
							{
							}
							if (j == 0)
							{
								UncaptureUntil(num6);
								if (_003CRegexGenerator_g_003EFCA80B9BFDF656C110345FAA38EFC7C47DB3D573F485ABCDC936CEEB6853A86DA__Utilities.s_hasTimeout)
								{
									CheckTimeout();
								}
								if (num7 >= num8)
								{
									break;
								}
								num = --num8;
								span = inputSpan.Slice(num);
								continue;
							}
							span = span.Slice(j);
							Capture(0, start, runtextpos = num + j);
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
						UncaptureUntil(0);
						return false;
					}
				}
				goto IL_00d6;
				IL_00d6:
				num = num4;
				span = inputSpan.Slice(num);
				UncaptureUntil(num3);
				if ((uint)span.Length < 19u || !span.Slice(3).StartsWith("uth_access_to", StringComparison.OrdinalIgnoreCase) || (((c = span[16]) | 0x20) != 107 && c != 'K') || !span.Slice(17).StartsWith("en", StringComparison.OrdinalIgnoreCase))
				{
					UncaptureUntil(0);
					return false;
				}
				num2 = 1;
				num += 19;
				span = inputSpan.Slice(num);
				goto IL_018b;
				[MethodImpl(MethodImplOptions.AggressiveInlining)]
				void UncaptureUntil(int capturePosition)
				{
					while (Crawlpos() > capturePosition)
					{
						Uncapture();
					}
				}
			}
		}

		protected override RegexRunner CreateInstance()
		{
			return new Runner();
		}
	}

	internal static readonly _003CRegexGenerator_g_003EFCA80B9BFDF656C110345FAA38EFC7C47DB3D573F485ABCDC936CEEB6853A86DA__AccessFlag_4 Instance = new _003CRegexGenerator_g_003EFCA80B9BFDF656C110345FAA38EFC7C47DB3D573F485ABCDC936CEEB6853A86DA__AccessFlag_4();

	private _003CRegexGenerator_g_003EFCA80B9BFDF656C110345FAA38EFC7C47DB3D573F485ABCDC936CEEB6853A86DA__AccessFlag_4()
	{
		pattern = "(?i)(--accessToken|--auth_access_token)\\s+\\S+";
		roptions = RegexOptions.CultureInvariant;
		Regex.ValidateMatchTimeout(_003CRegexGenerator_g_003EFCA80B9BFDF656C110345FAA38EFC7C47DB3D573F485ABCDC936CEEB6853A86DA__Utilities.s_defaultTimeout);
		internalMatchTimeout = _003CRegexGenerator_g_003EFCA80B9BFDF656C110345FAA38EFC7C47DB3D573F485ABCDC936CEEB6853A86DA__Utilities.s_defaultTimeout;
		factory = new RunnerFactory();
		capsize = 2;
	}
}
