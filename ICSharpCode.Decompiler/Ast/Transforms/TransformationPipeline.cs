// Copyright (c) 2011 AlphaSierraPapa for the SharpDevelop Team
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy of this
// software and associated documentation files (the "Software"), to deal in the Software
// without restriction, including without limitation the rights to use, copy, modify, merge,
// publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
// to whom the Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all copies or
// substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
// PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
// FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ICSharpCode.NRefactory.CSharp;

namespace ICSharpCode.Decompiler.Ast.Transforms
{
	public interface IAstTransform
	{
		void Run(AstNode compilationUnit);
	}

	public class VisitorTransform<T, S> : IAstTransform
	{
		private IAstVisitor<T, S> visitor;
		private T data;

		public VisitorTransform(IAstVisitor<T, S> visitor, T data) {
			if (visitor == null)
				throw new ArgumentException("visitor");
			this.visitor = visitor;
			this.data = data;
		}

		public void Run(AstNode node)
		{
			if (node == null)
				throw new ArgumentNullException("node");
			node.AcceptVisitor(visitor, data);
		}
	}

	public static class VisitorTransform
	{
		public static VisitorTransform<T, S> Create<T, S>(IAstVisitor<T, S> visitor, T data)
		{
			return new VisitorTransform<T, S>(visitor, data);
		}
	}
	
	public static class TransformationPipeline
	{
		public static IAstTransform[] CreatePipeline(DecompilerContext context)
		{
			return new IAstTransform[] {
				new PushNegation(),
				new DelegateConstruction(context),
				new PatternStatementTransform(context),
				new ReplaceMethodCallsWithOperators(),
				new IntroduceUnsafeModifier(),
				new AddCheckedBlocks(),
				new DeclareVariables(context), // should run after most transforms that modify statements
				new ConvertConstructorCallIntoInitializer(), // must run after DeclareVariables
				new IntroduceUsingDeclarations(context),
				new IntroduceExtensionMethods(context), // must run after IntroduceUsingDeclarations
				new IntroduceQueryExpressions(context), // must run after IntroduceExtensionMethods
				new CombineQueryExpressions(context),
			};
		}

		public static void RunTransformationsUntil(AstNode node, Predicate<IAstTransform> abortCondition, DecompilerContext context) {
			if (node == null)
				return;

			RunTransformations(node, CreateTransformationPipeline(context).TakeWhile(tr => abortCondition == null || !abortCondition(tr)), context);
		}

		public static void RunTransformations(AstNode node, IEnumerable<IAstTransform> transformations, DecompilerContext context)
		{
			if (node == null)
				return;

			foreach (var transform in transformations) {
				context.CancellationToken.ThrowIfCancellationRequested();
				transform.Run(node);
			}
		}

		public static IEnumerable<IAstTransform> CreateTransformationPipeline(DecompilerContext context) {
			foreach (var transform in CreatePipeline(context)) {
				yield return transform;
			}
		}
	}
}
