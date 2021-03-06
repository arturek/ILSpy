﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MbUnit.Framework;

namespace ICSharpCode.Decompiler.Tests.Types
{
	[TestFixture]
	public class EnumTests : DecompilerTestBase
	{
		[StaticTestFactory]
		public static IEnumerable<Test> EnumSamples()
		{
			return GenerateSectionTests(@"Types\S_EnumSamples.cs");
		}
	}
}
