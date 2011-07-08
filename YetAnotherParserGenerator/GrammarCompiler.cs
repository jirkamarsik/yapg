using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Microsoft.CSharp;

namespace YetAnotherParserGenerator
{
	public class GrammarCompiler
	{
		public void CompileGrammarCode(Grammar grammar, string compilerOptions)
		{
			// First we wrap the user's actions into methods which are supply the user's
			// code with correctly typed arguments and information about line and column
			// locations of symbols. This wrapper code also exposes a handy accessor function
			// which gives us delegates to all the actions in one handy array.
			StringBuilder codeBuilder = new StringBuilder();
			
			// First goes the user's header code with the possible "using" statements.
			codeBuilder.Append(grammar.GrammarCode.HeaderCode);
			
			codeBuilder.Append(@"
				namespace YetAnotherParserGenerator.UserGenerated
				{
				class ActionCollection
				{
				public static System.Func<object[], int[], int[], object, object>[] RetrieveActions()
				{
					return new System.Func<object[], int[], int[], object, object>[] { ");
			
			for (int i = 0; i < grammar.GrammarCode.ProductionActions.Length; i++) {
				if (i > 0)
					codeBuilder.Append(", ");
				codeBuilder.Append(string.Format("Action{0}", i));
			}
			
			codeBuilder.AppendLine("}; }");
			
			for (int i = 0; i < grammar.GrammarCode.ProductionActions.Length; i++) {
			
				codeBuilder.AppendLine("public static object Action" + i.ToString() + "(object[] __args, int[] __lines, int[] __columns, object __state) {");
				
				for (int j = 0; j < grammar.Productions[i].RHSSymbols.Count; j++) {
				
					// A terminal's value is always the string it spans in the input.
					if (grammar.Productions[i].RHSSymbols[j] < grammar.NumTerminals) {
						codeBuilder.AppendLine(string.Format("string _{0} = (string) __args[{1}];", j + 1, j));
					} else {
						// The nonterminal value is either interpreted as the user-specified type
						// or as a generic object if no type was further specified by the user.
						string nonterminalType = grammar.GrammarCode.NonterminalTypes
									[grammar.Productions[i].RHSSymbols[j] - grammar.NumTerminals];
						if (nonterminalType != null) {
							codeBuilder.AppendLine(string.Format("{0} _{1} = ({0}) __args[{2}];", nonterminalType, j + 1, j));
						} else {
							codeBuilder.AppendLine(string.Format("object _{0} = __args[{1}];", j + 1, j));
						}
					}
					
					codeBuilder.AppendLine(string.Format("int _line{0} = __lines[{1}];", j + 1, j));
					codeBuilder.AppendLine(string.Format("int _column{0} = __columns[{1}];", j + 1, j));
				}
				
				if (grammar.GrammarCode.UserObjectType != null)
					codeBuilder.AppendLine(string.Format("{0} _state = ({0}) __state;", grammar.GrammarCode.UserObjectType));
				else
					codeBuilder.AppendLine("object _state = __state;");
				
				codeBuilder.Append(grammar.GrammarCode.ProductionActions[i]);
				
				codeBuilder.AppendLine("}");
			}
			
			// We close our wrapper class and namespace.
			codeBuilder.Append("} }");
            
			
			CSharpCodeProvider compiler = new CSharpCodeProvider();
			
			CompilerParameters cp = new CompilerParameters();
			cp.GenerateExecutable = false;
			cp.CompilerOptions = compilerOptions;
			CompilerResults cr = compiler.CompileAssemblyFromSource(cp, codeBuilder.ToString());
			
			List<string> errors = new List<string>();
			foreach (CompilerError error in cr.Errors)
				if (!error.IsWarning)
					errors.Add(error.ToString());
			
			if (errors.Count > 0) {
				throw new InvalidSpecificationException(errors);
			}
			
			grammar.ParserData.ActionAssemblyBytes = File.ReadAllBytes(cr.PathToAssembly);
			File.Delete(cr.PathToAssembly);
		}
	}
}
