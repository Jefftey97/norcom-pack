using System.CodeDom.Compiler;

namespace System.Text.RegularExpressions.Generated;

[GeneratedCode("System.Text.RegularExpressions.Generator", "8.0.15.2219")]
internal sealed class _003CRegexGenerator_g_003EFCA80B9BFDF656C110345FAA38EFC7C47DB3D573F485ABCDC936CEEB6853A86DA__UserPath_8 : Regex
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
				if (num <= inputSpan.Length - 10)
				{
					int num2 = inputSpan.Slice(num + 1).IndexOf(":\\");
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
				int num5 = 0;
				ReadOnlySpan<char> readOnlySpan = inputSpan.Slice(num);
				char c;
				if ((uint)readOnlySpan.Length < 9u || (((c = readOnlySpan[0]) < '\u0080') ? (!char.IsAsciiLetter(c)) : (!RegexRunner.CharInClass(c, "\0\u0006\0A[a{KÅ"))) || !readOnlySpan.Slice(1).StartsWith(":\\users\\", StringComparison.OrdinalIgnoreCase))
				{
					return false;
				}
				num += 9;
				readOnlySpan = inputSpan.Slice(num);
				num2 = num;
				int i;
				for (i = 0; (uint)i < (uint)readOnlySpan.Length; i++)
				{
					if ((((c = readOnlySpan[i]) < '\u0080') ? ("쇿\uffff\ufffe\uffff\uffff\uefff\uffff\uffff"[(int)c >> 4] & (1 << (c & 0xF))) : (RegexRunner.CharInClass(c, "\u0001\u0002\u0001\\]d") ? 1 : 0)) == 0)
					{
						break;
					}
				}
				if (i == 0)
				{
					return false;
				}
				readOnlySpan = readOnlySpan.Slice(i);
				num += i;
				num3 = num;
				num2++;
				while (true)
				{
					num5 = pos;
					num4 = 0;
					while (true)
					{
						_003CRegexGenerator_g_003EFCA80B9BFDF656C110345FAA38EFC7C47DB3D573F485ABCDC936CEEB6853A86DA__Utilities.StackPush(ref runstack, ref pos, num);
						num4++;
						if (readOnlySpan.IsEmpty || readOnlySpan[0] != '\\')
						{
							break;
						}
						num++;
						readOnlySpan = inputSpan.Slice(num);
						int j;
						for (j = 0; (uint)j < (uint)readOnlySpan.Length; j++)
						{
							if ((((c = readOnlySpan[j]) < '\u0080') ? ("쇿\uffff\ufffe\uffff\uffff\uefff\uffff\uffff"[(int)c >> 4] & (1 << (c & 0xF))) : (RegexRunner.CharInClass(c, "\u0001\u0002\u0001\\]d") ? 1 : 0)) == 0)
							{
								break;
							}
						}
						if (j == 0)
						{
							break;
						}
						readOnlySpan = readOnlySpan.Slice(j);
						num += j;
					}
					if (--num4 >= 0)
					{
						break;
					}
					if (_003CRegexGenerator_g_003EFCA80B9BFDF656C110345FAA38EFC7C47DB3D573F485ABCDC936CEEB6853A86DA__Utilities.s_hasTimeout)
					{
						CheckTimeout();
					}
					if (num2 >= num3)
					{
						return false;
					}
					num = --num3;
					readOnlySpan = inputSpan.Slice(num);
				}
				num = runstack[--pos];
				readOnlySpan = inputSpan.Slice(num);
				pos = num5;
				runtextpos = num;
				Capture(0, start, num);
				return true;
			}
		}

		protected override RegexRunner CreateInstance()
		{
			return new Runner();
		}
	}

	internal static readonly _003CRegexGenerator_g_003EFCA80B9BFDF656C110345FAA38EFC7C47DB3D573F485ABCDC936CEEB6853A86DA__UserPath_8 Instance = new _003CRegexGenerator_g_003EFCA80B9BFDF656C110345FAA38EFC7C47DB3D573F485ABCDC936CEEB6853A86DA__UserPath_8();

	private _003CRegexGenerator_g_003EFCA80B9BFDF656C110345FAA38EFC7C47DB3D573F485ABCDC936CEEB6853A86DA__UserPath_8()
	{
		pattern = "(?i)[A-Za-z]:\\\\Users\\\\[^\\\\\\s]+(?:\\\\[^\\\\\\s]+)*";
		roptions = RegexOptions.CultureInvariant;
		Regex.ValidateMatchTimeout(_003CRegexGenerator_g_003EFCA80B9BFDF656C110345FAA38EFC7C47DB3D573F485ABCDC936CEEB6853A86DA__Utilities.s_defaultTimeout);
		internalMatchTimeout = _003CRegexGenerator_g_003EFCA80B9BFDF656C110345FAA38EFC7C47DB3D573F485ABCDC936CEEB6853A86DA__Utilities.s_defaultTimeout;
		factory = new RunnerFactory();
		capsize = 1;
	}
}
