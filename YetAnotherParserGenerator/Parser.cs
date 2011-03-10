using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace YetAnotherParserGenerator
{
    /// <summary>
    /// Represents a LR(1) parser.
    /// </summary>
    public class Parser
    {
        private ParserAction[,] parseTable;
        private int[,] gotoTable;
        private string[] symbolNames;
        private ProductionOutline[] productions;
        private int numTerminals;

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
        }

        /// <summary>
        /// Creates a new Parser instance from individual pieces of necessary data.
        /// </summary>
        /// <param name="parseTable">The parse table dictating an action for a pair of state/lookahead terminal symbol.</param>
        /// <param name="gotoTable">A table directing the parser to a new state after reducing to a nonterminal sybmol
        /// (indexed by state/nonterminal symbol)</param>
        /// <param name="symbolNames">An array containing the names of the grammars symbols, indexed by symbol codes.</param>
        /// <param name="productions">An array of the grammar's stripped down productions, indexed by productions' ordinal codes.</param>
        public Parser(ParserAction[,] parseTable, int[,] gotoTable, string[] symbolNames, ProductionOutline[] productions)
        {
            this.parseTable = parseTable;
            this.gotoTable = gotoTable;
            this.symbolNames = symbolNames;
            this.productions = productions;
            this.numTerminals = parseTable.GetLength(1);
        }

        /// <summary>
        /// Parses input tokens supplied by the <i>lexer</i> and returns the resulting ParseTree.
        /// </summary>
        /// <param name="lexer">The ILexer which will supply the Parser with tokens.</param>
        /// <returns>A ParseTree describing the structure of the lexer's output.</returns>
        /// <exception cref="ParsingException">when the lexer's output doesn't conform to the grammar's rules or
        /// when the lexer throws a ParsingException.</exception>
        public ParseTree Parse(ILexer lexer)
        {
            Stack<ParseTree> treeStack = new Stack<ParseTree>();
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
                        treeStack.Push(new ParseTree(symbolNames[nextToken.SymbolCode], nextToken.Value,
                                                     nextToken.LineNumber, nextToken.ColumnNumber));
                        stateStack.Push(nextAction.Argument);
                        state = stateStack.Peek();
                        nextToken = lexer.GetNextToken();
                        break;
                    case ParserActionType.Reduce:
                        //podle informací o patřičném přepisovacím pravidle odebereme ze zásobníků
                        //příslušný počet prvků
                        ProductionOutline production = productions[nextAction.Argument];
                        Stack<ParseTree> daughters = new Stack<ParseTree>();
                        for (int i = 0; i < production.NumRHSSymbols; i++)
                        {
                            daughters.Push(treeStack.Pop());
                            stateStack.Pop();
                        }
                        state = stateStack.Peek();

                        //stromky z lesního zásobníku poskládáme pod nový strom a ten uložíme na zásobník;
                        //stav, který ho na zásobník doprovodí, dohledáme v goto tabulce;
                        //pokud jsme redukovali na neprázdný neterminál, označíme začátek neterminálu
                        //jako začátek prvního terminálu, což bychom normálně mohli dělat v konstruktoru
                        //ParseTree, ale je tu zvláštní případ nulovatelných neterminálů a kolekce
                        //daughters tak někdy může být prázdná, potom musíme číslo řádku a sloupku dodat my
                        if (daughters.Count > 0)
                            treeStack.Push(new ParseTree(symbolNames[production.LHSSymbol], daughters,
                                                         daughters.Peek().LineNumber, daughters.Peek().ColumnNumber));
                        else
                            treeStack.Push(new ParseTree(symbolNames[production.LHSSymbol], daughters,
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
            else if ((treeStack.Count == 1) && (treeStack.Peek().SymbolName == "$start"))
                //vše je, jak má být
                return treeStack.Pop().Daughters[0];
            else if (treeStack.Count == 0)
            {
                throw new ParsingException("There were no symbols in the input.",
                    nextToken.LineNumber, nextToken.ColumnNumber);
            }
            else
            {
                //tohle znamená, že parser nebyl ještě se vstupem spokojen a očekává další symboly
                StringBuilder symbolsOnStack = new StringBuilder();
                foreach (ParseTree stackItem in treeStack.Reverse())
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
