﻿using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ICSharpCode.Decompiler.Ast;
using ICSharpCode.Decompiler.Ast.Transforms;
using MbUnit.Framework;
using Microsoft.CSharp;
using Mono.Cecil;

namespace ICSharpCode.Decompiler.Tests
{
	public abstract class DecompilerTestBase
	{
		protected static IEnumerable<Test> GenerateSectionTests(string samplesFileName)
		{
			string code = File.ReadAllText(Path.Combine(@"..\..\Tests", samplesFileName));
			foreach (var sectionName in CodeSampleFileParser.ListSections(code))
			{
				if (sectionName.EndsWith("(ignored)", StringComparison.OrdinalIgnoreCase))
					continue;

				var testedSectionName = sectionName;
				yield return new TestCase(testedSectionName, () =>
				{
					var testCode = CodeSampleFileParser.GetSection(testedSectionName, code);
					System.Diagnostics.Debug.WriteLine(testCode);
					var decompiledTestCode = RoundtripCode(testCode);
					Assert.AreEqual(testCode, decompiledTestCode);
				});
			}
		}

		protected static void ValidateFileRoundtrip(string samplesFileName)
		{
			var lines = File.ReadAllLines(Path.Combine(@"..\..\Tests", samplesFileName));
			var testCode = RemoveIgnorableLines(lines);
			var decompiledTestCode = RoundtripCode(testCode);
			Assert.AreEqual(testCode, decompiledTestCode);
		}

		static string RemoveIgnorableLines(IEnumerable<string> lines)
		{
			return CodeSampleFileParser.ConcatLines(lines.Where(l => !CodeSampleFileParser.IsCommentOrBlank(l)));
		}

		/// <summary>
		/// Compiles and decompiles a source code.
		/// </summary>
		/// <param name="code">The source code to copile.</param>
		/// <returns>The decompilation result of compiled source code.</returns>
		static string RoundtripCode(string code)
		{
			AssemblyDefinition assembly = Compile(code);
			AstBuilder decompiler = new AstBuilder(new DecompilerContext());
			decompiler.AddAssembly(assembly);

			var pipeline =
				decompiler.CreateStandardCodeTransformationPipeline()
				.Concat(new IAstTransform[] { new Helpers.RemoveCompilerAttribute(), new Helpers.RemoveRedundantBaseConstructorInitializers() });

			StringWriter output = new StringWriter();
			decompiler.TransformAndGenerateCode(new PlainTextOutput(output), pipeline);
			return output.ToString();
		}

		static AssemblyDefinition Compile(string code)
		{
			CSharpCodeProvider provider = new CSharpCodeProvider(new Dictionary<string, string> { { "CompilerVersion", "v4.0" } });
			CompilerParameters options = new CompilerParameters();
			options.ReferencedAssemblies.Add("System.Core.dll");
			CompilerResults results = provider.CompileAssemblyFromSource(options, code);
			try
			{
				if (results.Errors.Count > 0)
				{
					StringBuilder b = new StringBuilder("Compiler error:");
					foreach (var error in results.Errors)
					{
						b.AppendLine(error.ToString());
					}
					throw new Exception(b.ToString());
				}
				return AssemblyDefinition.ReadAssembly(results.PathToAssembly);
			}
			finally
			{
				File.Delete(results.PathToAssembly);
				results.TempFiles.Delete();
			}
		}
	}
}
