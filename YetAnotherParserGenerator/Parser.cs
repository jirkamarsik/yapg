using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace YetAnotherParserGenerator
{
    /// <summary>
    /// Represents a LR(1) parser.
    /// </summary>
    public class Parser
    {
    	/// <summary>
    	/// Represents a partial result of the parsing as stored on the parser's stack.
    	/// </summary>
    	private struct PartialResult
		{
			public PartialResult(object _value, string _symbolName, int _lineNumber, int _columnNumber)
			{
				this.Value = _value;
				this.SymbolName = _symbolName;
				this.LineNumber = _lineNumber;
				this.ColumnNumber = _columnNumber;
			}
			
			/// <summary>
			/// The value returned by the user's code for nonterminals,
			/// the text spanning the token's width for terminals.
			/// </summary>
			public object Value;
			/// <summary>
			/// The grammar's name for the symbol.
			/// </summary>
			public string SymbolName;
			/// <summary>
			/// The number of the line on which the symbol begins.
			/// </summary>
			public int LineNumber;
			/// <summary>
			/// The number of the column at which the symbol begins.
			/// </summary>
			public int ColumnNumber;
		}
		
        private ParserAction[,] parseTable;
        private int[,] gotoTable;
        private string[] symbolNames;
        private ProductionOutline[] productions;
        private int numTerminals;
        private Func<object[], int[], int[], object, object>[] actions;

        /// <summary>
        /// Creates a new Parser instance using data stored in a ParserData object.
        /// </summary>
        /// <param name="parserData">Data on the parser automaton and grammar necessary for parsing</param>
        public Parser(ParserData parserData)
        {
            this.parseTable = parserData.ParseTable;
            this.gotoTable = parserData.GotoTable;
            this.symbolNames = parserData.SymbolNames;
            this.productions = parserData.Productions;
            this.numTerminals = parserData.ParseTable.GetLength(1);
            
            Assembly actionAssembly = Assembly.Load(parserData.ActionAssemblyBytes);
			Type actionCollection = actionAssembly.GetType("YetAnotherParserGenerator.UserGenerated.ActionCollection");
			MethodInfo retrieveActions = actionCollection.GetMethod("RetrieveActions");
			this.actions = (Func<object[], int[], int[], object, object>[]) retrieveActions.Invoke(null, new object [] {});
        }

        /// <summary>
        /// Parses input tokens supplied by the <i>lexer</i> and returns the result.
        /// </summary>
        /// <param name="lexer">The ILexer which will supply the Parser with tokens.</param>
        /// <returns>An object containing the value returned for the root nonterminal.</returns>
        /// <exception cref="ParsingException">when the lexer's output doesn't conform to the grammar's rules or
        /// when the lexer throws a ParsingException.</exception>
        public object Parse(ILexer lexer, object userObject)
        {
        	Stack<PartialResult> resultStack = new Stack<PartialResult>();
            Stack<int> stateStack = new Stack<int>();

            //počáteční stav
            stateStack.Push(0);

            bool done = false;
            Token nextToken = lexer.GetNextToken();
            int state = stateStack.Peek();
            while (!done && (lexer.HasTokens || ((state >= 0) && (parseTable[state, nextToken.SymbolCode].ActionType != ParserActionType.Fail))))
            {
                ParserAction nextAction = parseTable[state, nextToken.SymbolCode];

                switch (nextAction.ActionType)
                {
                    case ParserActionType.Shift:
                        resultStack.Push(new PartialResult(nextToken.Value, symbolNames[nextToken.SymbolCode],
                                                           nextToken.LineNumber, nextToken.ColumnNumber));
                        stateStack.Push(nextAction.Argument);
                        state = stateStack.Peek();
                        nextToken = lexer.GetNextToken();
                        break;
                    case ParserActionType.Reduce:
                        //podle informací o patřičném přepisovacím pravidle odebereme ze zásobníků
                        //příslušný počet prvků
                        ProductionOutline production = productions[nextAction.Argument];
                        Stack<PartialResult> constituents = new Stack<PartialResult>();
                        for (int i = 0; i < production.NumRHSSymbols; i++)
                        {
                            constituents.Push(resultStack.Pop());
                            stateStack.Pop();
                        }
                        state = stateStack.Peek();

                        // We take the values of the constituents and compute the value of the composite
						// element using the relevant action. This new result replaces the old ones on the
						// result stack; the accompanying state for the state stack is found in the goto
						// table of the parser. The line and column number for the new result are taken from
						// the first constituent. If the nonterminal has no constituents, we take the position
						// of the upcoming token.
                        int numConstituents = constituents.Count;
                        object[] constituentValues = new object[numConstituents];
                        int[] constituentLines = new int[numConstituents];
						int[] constituentColumns = new int[numConstituents];
						for (int i = 0; i < numConstituents; i++) {
							PartialResult constituent = constituents.Pop();
							constituentValues[i] = constituent.Value;
							constituentLines[i] = constituent.LineNumber;
							constituentColumns[i] = constituent.ColumnNumber;
						}
						object result = actions[nextAction.Argument](constituentValues, constituentLines, constituentColumns, userObject);
						if (numConstituents > 0)
							resultStack.Push(new PartialResult(result, symbolNames[production.LHSSymbol],
						                               		   constituentLines[0], constituentColumns[0]));
						else
							resultStack.Push(new PartialResult(result, symbolNames[production.LHSSymbol],
						                               		   nextToken.LineNumber, nextToken.ColumnNumber));
						
                        stateStack.Push(gotoTable[state, production.LHSSymbol - numTerminals]);
                        state = stateStack.Peek();

                        //přepisovací pravidlo 0 je vždy $start ::= <start-symbol> $end a provedení
                        //redukce podle tohoto pravidla znamená, že jsme načetli i $end a podařilo
                        //se nám vstup rozparsovat, tudíž končíme;
                        //pokud by se nám snažil lexer ještě nějaké tokeny vrazit, tak to ohlásíme
                        if (nextAction.Argument == 0)
                            done = true;
                        break;
                    case ParserActionType.Fail:
                        StringBuilder expectedTerminals = new StringBuilder();
                        for (int terminal = 0; terminal < numTerminals; terminal++)
                            if (parseTable[state, terminal].ActionType != ParserActionType.Fail)
                            {
                                expectedTerminals.Append(", ");
                                expectedTerminals.Append(symbolNames[terminal]);
                            }

                        throw new ParsingException(string.Format("Unexpected terminal {0}({1}) encountered by the parser, expected one of the following terminals: {2}.",
                                  symbolNames[nextToken.SymbolCode], nextToken.Value, expectedTerminals.ToString(2, expectedTerminals.Length - 2)),
                                  nextToken.LineNumber, nextToken.ColumnNumber);
                }
            }

            if (lexer.HasTokens)
            {
                //lexer nám chce něco říct, ale my už jsme hotovi; tohle by se s naším lexerem nemělo nikdy
                //stát, jelikož po tom, co vrátí $end, už další tokeny nenabízí
                nextToken = lexer.GetNextToken();
                throw new ParsingException(string.Format("There are additional symbols in the input string starting with terminal {0}({1}).",
                          symbolNames[nextToken.SymbolCode], nextToken.Value), nextToken.LineNumber, nextToken.ColumnNumber);
            }
            else if ((resultStack.Count == 1) && (resultStack.Peek().SymbolName == "$start"))
                //vše je, jak má být
                return resultStack.Pop().Value;
            else if (resultStack.Count == 0)
            {
                throw new ParsingException("There were no symbols in the input.",
                    nextToken.LineNumber, nextToken.ColumnNumber);
            }
            else
            {
                //tohle znamená, že parser nebyl ještě se vstupem spokojen a očekává další symboly
                StringBuilder symbolsOnStack = new StringBuilder();
                foreach (PartialResult stackItem in resultStack.Reverse())
                {
                    symbolsOnStack.Append(", ");
                    symbolsOnStack.Append(stackItem.SymbolName);
                }

                throw new ParsingException("The entire input was reduced to more than one symbol: "
                                            + symbolsOnStack.ToString(2, symbolsOnStack.Length - 2) +
                                            ". Input text was probably incomplete.", nextToken.LineNumber, nextToken.ColumnNumber);
            }
        }
    }
}
