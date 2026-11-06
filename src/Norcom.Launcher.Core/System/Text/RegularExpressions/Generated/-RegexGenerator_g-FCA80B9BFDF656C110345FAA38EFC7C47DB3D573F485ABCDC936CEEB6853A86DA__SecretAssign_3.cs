using System.CodeDom.Compiler;
using System.Runtime.CompilerServices;

namespace System.Text.RegularExpressions.Generated;

[GeneratedCode("System.Text.RegularExpressions.Generator", "8.0.15.2219")]
internal sealed class _003CRegexGenerator_g_003EFCA80B9BFDF656C110345FAA38EFC7C47DB3D573F485ABCDC936CEEB6853A86DA__SecretAssign_3 : Regex
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
				if (num <= inputSpan.Length - 7)
				{
					ReadOnlySpan<char> readOnlySpan = inputSpan.Slice(num);
					int num2;
					for (num2 = 0; num2 < readOnlySpan.Length - 6; num2++)
					{
						int num3 = readOnlySpan.Slice(num2 + 4).IndexOfAny(_003CRegexGenerator_g_003EFCA80B9BFDF656C110345FAA38EFC7C47DB3D573F485ABCDC936CEEB6853A86DA__Utilities.s_ascii_C0880000C08800);
						if (num3 < 0)
						{
							break;
						}
						num2 += num3;
						ulong num4;
						if ((long)((ulong)(-8069887579878457344L << (int)(num4 = (uint)(readOnlySpan[num2 + 3] - 69))) & (num4 - 64)) < 0L && (long)((ulong)(-9223070768521211392L << (int)(num4 = (uint)(readOnlySpan[num2] - 65))) & (num4 - 64)) < 0L)
						{
							runtextpos = num + num2;
							return true;
						}
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
				num4 = num;
				num3 = Crawlpos();
				char c;
				if ((uint)span.Length < 5u || !span.StartsWith("to", StringComparison.OrdinalIgnoreCase) || (((c = span[2]) | 0x20) != 107 && c != 'K') || !span.Slice(3).StartsWith("en", StringComparison.OrdinalIgnoreCase))
				{
					goto IL_00a5;
				}
				num2 = 0;
				num += 5;
				span = inputSpan.Slice(num);
				goto IL_0262;
				IL_0137:
				num = num4;
				span = inputSpan.Slice(num);
				UncaptureUntil(num3);
				if ((uint)span.Length < 7u || !span.StartsWith("webhoo", StringComparison.OrdinalIgnoreCase) || (((c = span[6]) | 0x20) != 107 && c != 'K'))
				{
					goto IL_01a2;
				}
				num2 = 3;
				num += 7;
				span = inputSpan.Slice(num);
				goto IL_0262;
				IL_01a2:
				num = num4;
				span = inputSpan.Slice(num);
				UncaptureUntil(num3);
				if ((uint)span.Length < 11u || !span.StartsWith("accessto", StringComparison.OrdinalIgnoreCase) || (((c = span[8]) | 0x20) != 107 && c != 'K') || !span.Slice(9).StartsWith("en", StringComparison.OrdinalIgnoreCase))
				{
					UncaptureUntil(0);
					return false;
				}
				num2 = 4;
				num += 11;
				span = inputSpan.Slice(num);
				goto IL_0262;
				IL_00ed:
				num = num4;
				span = inputSpan.Slice(num);
				UncaptureUntil(num3);
				if ((uint)span.Length < 13u || !span.StartsWith("authorization", StringComparison.OrdinalIgnoreCase))
				{
					goto IL_0137;
				}
				num2 = 2;
				num += 13;
				span = inputSpan.Slice(num);
				goto IL_0262;
				IL_00a5:
				num = num4;
				span = inputSpan.Slice(num);
				UncaptureUntil(num3);
				if ((uint)span.Length < 8u || !span.StartsWith("password", StringComparison.OrdinalIgnoreCase))
				{
					goto IL_00ed;
				}
				num2 = 1;
				num += 8;
				span = inputSpan.Slice(num);
				goto IL_0262;
				IL_0262:
				while (true)
				{
					Capture(1, num5, num);
					if (!span.IsEmpty && !((c = span[0]) != ':' && c != '='))
					{
						num++;
						span = inputSpan.Slice(num);
						num7 = num;
						int i;
						for (i = 0; (uint)i < (uint)span.Length && char.IsWhiteSpace(span[i]); i++)
						{
						}
						span = span.Slice(i);
						num += i;
						num8 = num;
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
					switch (num2)
					{
					case 0:
						break;
					case 1:
						goto IL_00ed;
					case 2:
						goto IL_0137;
					case 3:
						goto IL_01a2;
					case 4:
						UncaptureUntil(0);
						return false;
					default:
						continue;
					}
					break;
				}
				goto IL_00a5;
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

	internal static readonly _003CRegexGenerator_g_003EFCA80B9BFDF656C110345FAA38EFC7C47DB3D573F485ABCDC936CEEB6853A86DA__SecretAssign_3 Instance = new _003CRegexGenerator_g_003EFCA80B9BFDF656C110345FAA38EFC7C47DB3D573F485ABCDC936CEEB6853A86DA__SecretAssign_3();

	private _003CRegexGenerator_g_003EFCA80B9BFDF656C110345FAA38EFC7C47DB3D573F485ABCDC936CEEB6853A86DA__SecretAssign_3()
	{
		pattern = "(?i)(token|password|authorization|webhook|accessToken)[=:]\\s*\\S+";
		roptions = RegexOptions.CultureInvariant;
		Regex.ValidateMatchTimeout(_003CRegexGenerator_g_003EFCA80B9BFDF656C110345FAA38EFC7C47DB3D573F485ABCDC936CEEB6853A86DA__Utilities.s_defaultTimeout);
		internalMatchTimeout = _003CRegexGenerator_g_003EFCA80B9BFDF656C110345FAA38EFC7C47DB3D573F485ABCDC936CEEB6853A86DA__Utilities.s_defaultTimeout;
		factory = new RunnerFactory();
		capsize = 2;
	}
}
