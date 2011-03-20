using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MbUnit.Framework;

namespace ICSharpCode.Decompiler.Tests.Types
{
	[TestFixture]
	public class TypeTests : DecompilerTestBase
	{
		[StaticTestFactory]
		public static IEnumerable<Test> TypeMemberDeclarationsSamples()
		{
			return GenerateSectionTests(@"Types\S_TypeMemberDeclarations.cs");
		}
	}
}
