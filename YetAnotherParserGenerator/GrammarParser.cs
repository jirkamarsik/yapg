using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace YetAnotherParserGenerator
{
    /// <summary>
    /// A static class responsible for translating human-readable
    /// grammar specifications into internal representations.
    /// </summary>
    public class GrammarParser
    {
        // Symbol codes for the various tokens used by the GrammarLexer
		// and the GrammarParser.
		internal static readonly int CODE_SKIP = -1, CODE_END = 0, CODE_HEADER = 1,
			CODE_HEADERCODE = 2, CODE_LEXER = 3, CODE_NULL = 4, CODE_IDENTIFIER = 5,
			CODE_REGEXOPTS = 6, CODE_EQUALS = 7, CODE_REGEX = 8, CODE_PARSER = 9,
			CODE_START = 10, CODE_TYPE = 11, CODE_DOT = 12, CODE_LANGLE = 13,
			CODE_RANGLE = 14, CODE_DERIVES = 15, CODE_CODE = 16, CODE_OR = 17;
		
		public static Grammar ParseGrammarProgram(TextReader input, out IList<string> warningMessages)
		{
            GrammarLexer lexer = new GrammarLexer();
			lexer.SourceString = input.ReadToEnd();
			Token token = lexer.GetNextToken();

            //sem si budeme ukládat chyby a warningy; pokud se vyskytne nějaká chyba, čteme dál a 
            //až na konci vyhodíme výjimku se všemi chybami a warningami; pokud vše proběhne bez
            //závažnějších chyb, tak seznam warningů pošlem zpátky volajícímu
            List<string> errorMessages = new List<string>();
            warningMessages = new List<string>();

			//the code to be inserted at the start of the generated code
			string headerCode = null;
			
            //seznam jmen všech symbolů; slouží potom jako převodní tabulka z kódu symbolu na jeho jméno
            List<string> symbolNames = new List<string>();
            //"inverzní tabulka" k symbolNames, která nám pro jméno symbolu řekne jeho kód
            Dictionary<string, int> symbolCodes = new Dictionary<string, int>();

            //seznam regulárních výrazů definujících terminální symboly; netvoříme z nich rovnou výsledný
            //lexerův regex, ale ukládáme si je zvlášť, abychom v případě chyby při kompilaci celkového regexu
            //mohli jednodušše otestovat, které výrazy jsou na vině
            List<string> regexes = new List<string>();
            //pro každý výraz v regexes si pamatujeme pozici, kde jsme ho našli, abychom mohli vydat podrobnější
            //zprávu
            List<int> regexLines = new List<int>();
            List<int> regexColumns = new List<int>();
            //pro každý výraz v regexes si také pamatujeme kód symbolu, který je popisován oním výrazem,
            //v případě, že výraz má matchovat řetězce, které chceme ignorovat, je v tomto poli hodnota -1;
            //jedná se o runtime data, která pak přímo používá náš lexer
            List<int> groupSymbolCodes = new List<int>();
            //výsledný regulární výraz, pomocí kterého lexer scanuje tokeny; druhá část runtime dat pro náš lexer
            Regex regex = null;

            //jméno pseudoterminálu, jehož tokeny se nemají posílat parseru, ale zahazovat
            string nullTerminal = "";
            //globální optiony .NETímu regex stroji (case insensitive, multiline...)
            string regexOpts = null;
            
			if (token.SymbolCode == CODE_HEADER) {
				token = lexer.GetNextToken();
				headerCode = token.Value;
				token = lexer.GetNextToken();
			}
			
			// skipping LEXER
			token = lexer.GetNextToken();
			
			if (token.SymbolCode == CODE_NULL) {
				token = lexer.GetNextToken();
				nullTerminal = token.Value;
				token = lexer.GetNextToken();
			}
			
			if (token.SymbolCode == CODE_REGEXOPTS) {
				regexOpts = token.Value;
				token = lexer.GetNextToken();
			}

            symbolNames.Add("$end");
            symbolCodes["$end"] = 0;

            while (token.SymbolCode == CODE_IDENTIFIER)
            {
                string symbol = token.Value;
                token = lexer.GetNextToken();
                // skipping EQUALS
				token = lexer.GetNextToken();
				string capturingRegex = token.Value;
				token = lexer.GetNextToken();

                if (symbol == nullTerminal)
                {
                    groupSymbolCodes.Add(-1);
                }
                else
                {
                    if (!symbolCodes.ContainsKey(symbol))
                    {
                        symbolNames.Add(symbol);
                        symbolCodes[symbol] = symbolNames.Count - 1;
                    }
                    groupSymbolCodes.Add(symbolCodes[symbol]);
                }

                regexes.Add(capturingRegex);
                regexLines.Add(token.LineNumber);
                regexColumns.Add(token.ColumnNumber);
            }

            StringBuilder pattern = new StringBuilder();

            if (regexOpts != null)
                pattern.Append(regexOpts);

            for (int i = 0; i < regexes.Count; i++)
            {
                //všechny uživatelovi regulární výrazy oddělíme ořítky a zapíšeme je v pořadí, v jakém nám
                //je zadal (v .NETím Regex enginu mají výrazy v ořítku víc nalevo přednost) a každý výraz
                //strčíme do capture groupy pojmenované __i, kde i je pořadové číslo výrazu, počítáno od 0
                if (i != 0)
                    pattern.Append('|');
                pattern.AppendFormat("(?<{0}>{1})", "__" + i.ToString(), regexes[i]);
            }

            try
            {
                regex = new Regex(pattern.ToString(), RegexOptions.Compiled);
            }
            catch (ArgumentException)
            {
                try
                {
                    new Regex(regexOpts);
                }
                catch (ArgumentException)
                {
                	// FIXME: We no longer have the line and columnd data on regexOpts.
                    errorMessages.Add(string.Format("{0},{1}: The RegEx options are invalid.", -1, -1));
                }

                for (int i = 0; i < regexes.Count; i++)
                {
                    try
                    {
                        new Regex(regexes[i]);
                    }
                    catch (ArgumentException)
                    {
                        errorMessages.Add(string.Format("{0},{1}: This regular expression is invalid.",
                            regexLines[i], regexColumns[i]));
                    }
                }
            }

            int numTerminals = symbolNames.Count;
            
            // skipping PARSER
			token = lexer.GetNextToken();

            //neterminály, které se objevily na levé straně nějakého pravidla
            HashSet<int> reducibleNonterminals = new HashSet<int>();
            //neterminály, které se objevily na pravé straně nějakého pravidla
            HashSet<int> usedNonterminals = new HashSet<int>();

            bool[] terminalUsed = new bool[numTerminals];
            //
            terminalUsed[0] = true;

            List<ProductionWithAction> productions = new List<ProductionWithAction>();

            symbolNames.Add("$start");
            symbolCodes["$start"] = numTerminals;
            //semhle dáme <$start> výjimečně, protože ani nechceme,
            //aby ho někdo dával na pravou stranu nějakého pravidla
            usedNonterminals.Add(symbolCodes["$start"]);

			// skipping START
			token = lexer.GetNextToken();
			// skipping LANGLE
			token = lexer.GetNextToken();
			
            string startSymbol = token.Value;

            if (symbolCodes.ContainsKey(startSymbol) && symbolCodes[startSymbol] < numTerminals)
                errorMessages.Add(string.Format("{0},{1}: The nonterminal <{2}> shares it's name with a terminal symbol.",
                    token.LineNumber, token.ColumnNumber, startSymbol));

			token = lexer.GetNextToken();
			
			// skipping RANGLE
			token = lexer.GetNextToken();
			
            symbolNames.Add(startSymbol);
            symbolCodes[startSymbol] = symbolNames.Count - 1;


            //naše 0. pravidlo, které výstižně popisuje způsob, jakým si gramatiku upravujeme
            productions.Add(new ProductionWithAction(new Production(
                0, symbolCodes["$start"], new int[] { symbolCodes[startSymbol], symbolCodes["$end"] }), "{ return _1; }"));

            reducibleNonterminals.Add(symbolCodes["$start"]);
            usedNonterminals.Add(symbolCodes[startSymbol]);
            terminalUsed[symbolCodes["$end"]] = true;
            
			var typeMappings = new Dictionary<string, string>();

            //zpracování pravidel
            while (token.SymbolCode != CODE_END)
            {
            	if (token.SymbolCode == CODE_TYPE) {
            	
            		token = lexer.GetNextToken();
					
					// skipping LANGLE
					token = lexer.GetNextToken();
					string nonterminal = token.Value;
					
                    if (symbolCodes.ContainsKey(nonterminal) && symbolCodes[nonterminal] < numTerminals)
                        errorMessages.Add(string.Format("{0},{1}: The nonterminal <{2}> shares it's name with a terminal symbol.",
                            token.LineNumber, token.ColumnNumber, nonterminal));

                    if (!symbolCodes.ContainsKey(nonterminal))
                    {
                        symbolNames.Add(nonterminal);
                        symbolCodes[nonterminal] = symbolNames.Count - 1;
                    }
                    
                    token = lexer.GetNextToken();
                    // skipping RANGLE
					token = lexer.GetNextToken();
					
					StringBuilder typeBuilder = new StringBuilder(token.Value);
					token = lexer.GetNextToken();
					while (token.SymbolCode == CODE_DOT) {
						typeBuilder.Append(".");
						token = lexer.GetNextToken();
						typeBuilder.Append(token.Value);
						token = lexer.GetNextToken();
					}
					
					typeMappings.Add(nonterminal, typeBuilder.ToString());
					
				} else {
	
	                //extrahujeme symbol na levé straně a zpracujeme ho
	                // skipping LANGLE
	                token = lexer.GetNextToken();
	                
	                string lhsSymbol = token.Value;
	                
	                if (symbolCodes.ContainsKey(lhsSymbol) && symbolCodes[lhsSymbol] < numTerminals)
	                    errorMessages.Add(string.Format("{0},{1}: The nonterminal <{2}> shares it's name with a terminal symbol.",
	                        token.LineNumber, token.ColumnNumber, lhsSymbol));
	
	                if (!symbolCodes.ContainsKey(lhsSymbol))
	                {
	                    symbolNames.Add(lhsSymbol);
	                    symbolCodes[lhsSymbol] = symbolNames.Count - 1;
	                }
	
	                if (!reducibleNonterminals.Contains(symbolCodes[lhsSymbol]))
	                    reducibleNonterminals.Add(symbolCodes[lhsSymbol]);
	                
	                token = lexer.GetNextToken();
	                
					//skipping RANGLE
					token = lexer.GetNextToken();
	
	
	                int lhsSymbolCode = symbolCodes[lhsSymbol];
	                
	                //Zpracujeme výraz na pravé straně, který může sestávat z několika seznamů symbolů oddělenými
	                //ořítky. Každý z těchto seznamů pak tvoří jedno pravidlo bez ořítek.
	                while ((token.SymbolCode == CODE_DERIVES) || (token.SymbolCode == CODE_OR))
	                {
	                	token = lexer.GetNextToken();
	                    List<int> rhsSymbols = new List<int>();
	
	                    while (token.SymbolCode != CODE_CODE)
	                    {
	                        int rhsSymbolCode = -1;
	                        if (token.SymbolCode == CODE_LANGLE)
	                        {
	                        	//skipping LANGLE
								token = lexer.GetNextToken();
								
	                            string rhsSymbol = token.Value;
	
	                            if (symbolCodes.ContainsKey(rhsSymbol) && symbolCodes[rhsSymbol] < numTerminals)
	                                errorMessages.Add(string.Format("{0},{1}: The nonterminal <{2}> shares it's name with a terminal symbol.",
	                                    token.LineNumber, token.ColumnNumber, rhsSymbol));
	
	                            if (!symbolCodes.ContainsKey(rhsSymbol))
	                            {
	                                symbolNames.Add(rhsSymbol);
	                                symbolCodes[rhsSymbol] = symbolNames.Count - 1;
	                            }
	
	                            if (!usedNonterminals.Contains(symbolCodes[rhsSymbol]))
	                                usedNonterminals.Add(symbolCodes[rhsSymbol]);
	                            
								token = lexer.GetNextToken();
								
								//skipping RANGLE
								token = lexer.GetNextToken();
	
	                            rhsSymbolCode = symbolCodes[rhsSymbol];
	                        }
	                        else
	                        {
	                            string rhsSymbol = token.Value;
	
	                            if (!symbolCodes.ContainsKey(rhsSymbol))
	                                errorMessages.Add(string.Format("{0},{1}: The terminal '{2}' is used but not defined.",
	                                    token.LineNumber, token.ColumnNumber, rhsSymbol));
	                            else
	                            {
	                                rhsSymbolCode = symbolCodes[rhsSymbol];
	                                terminalUsed[rhsSymbolCode] = true;
	                            }
	                            
								token = lexer.GetNextToken();
	                        }
	
	                        rhsSymbols.Add(rhsSymbolCode);
	                    }
	                    
						string code = token.Value;
						token = lexer.GetNextToken();
	
	                    productions.Add(new ProductionWithAction(
							new Production(productions.Count, lhsSymbolCode, rhsSymbols), code));
	                }
				}
            }

            //ToArray voláme proto, aby se líná metoda Intersect vyhodnotila a nedošlo by pak při vykonávání
            //dalšího příkazu k chybě
            int[] theGoodOnes = usedNonterminals.Intersect(reducibleNonterminals).ToArray();
            usedNonterminals.ExceptWith(theGoodOnes);
            reducibleNonterminals.ExceptWith(theGoodOnes);

            foreach (int nonterminal in usedNonterminals)
                warningMessages.Add(string.Format("Warning: The nonterminal <{0}> isn't reducible.",
                                                symbolNames[nonterminal]));
            foreach (int nonterminal in reducibleNonterminals)
                warningMessages.Add(string.Format("Warning: The nonterminal <{0}> is defined but never used.", symbolNames[nonterminal]));

            for (int terminal = 0; terminal < numTerminals; terminal++)
                if (!terminalUsed[terminal])
                    warningMessages.Add(string.Format("Warning: The terminal '{0}' is defined but never used.", symbolNames[terminal]));


            if (errorMessages.Count > 0)
                throw new InvalidSpecificationException(errorMessages.Concat(warningMessages));


            //už máme vše načte a zkontrolováno, teď už jen setřídíme pravidla podle levé strany,
            //přečíslujeme je a pro každý neterminál dopočítáme indexy, na kterých začínají pravidla
            //s daným neterminálem
            Production[] productionsArray = new Production[productions.Count];
            productionsArray[0] = productions[0].Production;
            string[] actions = new string[productions.Count];
			actions[0] = productions[0].Action;
            IEnumerable<ProductionWithAction> sortedProductions =
				productions.GetRange(1, productions.Count - 1).OrderBy((prod => prod.Production.LHSSymbol));

            int k = 1;
            foreach (ProductionWithAction productionWithAction in sortedProductions)
            {
                productionsArray[k] = productionWithAction.Production;
                productionsArray[k].ProductionCode = k;
                actions[k] = productionWithAction.Action;
                k++;
            }

            int numNonterminals = symbolCodes.Count - numTerminals;

            int[] nonterminalProductionOffset = new int[numNonterminals + 1];

            int offset = 0;
            for (int nonterminal = 0; nonterminal < numNonterminals; nonterminal++)
            {
                nonterminalProductionOffset[nonterminal] = offset;
                while ((offset < productionsArray.Length) &&
                    (productionsArray[offset].LHSSymbol == numTerminals + nonterminal))
                    offset++;
            }
            nonterminalProductionOffset[nonterminalProductionOffset.Length - 1] = offset;
            
			string[] nonterminalTypes = new string[numNonterminals];
			foreach (var typeMapping in typeMappings) {
				nonterminalTypes[symbolCodes[typeMapping.Key] - numTerminals] = typeMapping.Value;
			}

            //a teď už to jen zabalíme a pošleme
            GrammarDefinition grammarDefinition = new GrammarDefinition(symbolNames.ToArray(), productionsArray, nonterminalProductionOffset, numTerminals);
            LexerData lexerData = new LexerData(regex, groupSymbolCodes);
            GrammarCode grammarCode = new GrammarCode(headerCode, actions, nonterminalTypes);
            Grammar grammar = new Grammar(grammarDefinition, lexerData, grammarCode);

            return grammar;
		}
			
        /// <summary>
        /// Reads the grammar specification from <i>input</i> and returns the Grammar object with all data
        /// which can be read directly from the grammar specification.
        /// </summary>
        /// <param name="input">The TextReader from which the grammar specification will be read.</param>
        /// <param name="warningMessages">A list containing warning messages generated by the grammar parser.</param>
        /// <returns>The Grammar which was read from the <i>input</i> containing all the data which can be
        /// read directly from the grammar specification.</returns>
        /// <exception cref="InvalidSpecificationException">when the grammar's specification contains any
        /// discrepancies.</exception>
        public static Grammar ParseGrammar(TextReader input, out IList<string> warningMessages)
        {
            ParseTree inputTree = parseGrammarSpecification(input.ReadToEnd());

            //sem si budeme ukládat chyby a warningy; pokud se vyskytne nějaká chyba, čteme dál a 
            //až na konci vyhodíme výjimku se všemi chybami a warningami; pokud vše proběhne bez
            //závažnějších chyb, tak seznam warningů pošlem zpátky volajícímu
            List<string> errorMessages = new List<string>();
            warningMessages = new List<string>();

            //seznam jmen všech symbolů; slouží potom jako převodní tabulka z kódu symbolu na jeho jméno
            List<string> symbolNames = new List<string>();
            //"inverzní tabulka" k symbolNames, která nám pro jméno symbolu řekne jeho kód
            Dictionary<string, int> symbolCodes = new Dictionary<string, int>();

            //seznam regulárních výrazů definujících terminální symboly; netvoříme z nich rovnou výsledný
            //lexerův regex, ale ukládáme si je zvlášť, abychom v případě chyby při kompilaci celkového regexu
            //mohli jednodušše otestovat, které výrazy jsou na vině
            List<string> regexes = new List<string>();
            //pro každý výraz v regexes si pamatujeme pozici, kde jsme ho našli, abychom mohli vydat podrobnější
            //zprávu
            List<int> regexLines = new List<int>();
            List<int> regexColumns = new List<int>();
            //pro každý výraz v regexes si také pamatujeme kód symbolu, který je popisován oním výrazem,
            //v případě, že výraz má matchovat řetězce, které chceme ignorovat, je v tomto poli hodnota -1;
            //jedná se o runtime data, která pak přímo používá náš lexer
            List<int> groupSymbolCodes = new List<int>();
            //výsledný regulární výraz, pomocí kterého lexer scanuje tokeny; druhá část runtime dat pro náš lexer
            Regex regex = null;

            //jméno pseudoterminálu, jehož tokeny se nemají posílat parseru, ale zahazovat
            string nullTerminal = "";
            //globální optiony .NETímu regex stroji (case insensitive, multiline...)
            string regexOpts = null;

            if (inputTree.Daughters[0].Daughters.Length > 0)
                nullTerminal = inputTree.Daughters[0].Daughters[1].Value;

            if (inputTree.Daughters[1].Daughters.Length > 0)
                regexOpts = inputTree.Daughters[1].Daughters[0].Value;

            symbolNames.Add("$end");
            symbolCodes["$end"] = 0;

            ParseTree tokenDefs = inputTree.Daughters[2];
            while (tokenDefs != null)
            {
                string tokenDef = tokenDefs.Daughters[0].Value;
                int equalsIndex = tokenDef.IndexOf('=');
                string symbol = tokenDef.Substring(0, equalsIndex);

                if (symbol == nullTerminal)
                {
                    groupSymbolCodes.Add(-1);
                }
                else
                {
                    if (!symbolCodes.ContainsKey(symbol))
                    {
                        symbolNames.Add(symbol);
                        symbolCodes[symbol] = symbolNames.Count - 1;
                    }
                    groupSymbolCodes.Add(symbolCodes[symbol]);
                }

                regexes.Add(tokenDef.Substring(equalsIndex + 1));
                regexLines.Add(tokenDefs.Daughters[0].LineNumber);
                regexColumns.Add(tokenDefs.Daughters[0].ColumnNumber + equalsIndex + 1);

                if (tokenDefs.Daughters.Length > 1)
                    tokenDefs = tokenDefs.Daughters[1];
                else
                    tokenDefs = null;
            }

            StringBuilder pattern = new StringBuilder();

            if (regexOpts != null)
                pattern.Append(regexOpts);

            for (int i = 0; i < regexes.Count; i++)
            {
                //všechny uživatelovi regulární výrazy oddělíme ořítky a zapíšeme je v pořadí, v jakém nám
                //je zadal (v .NETím Regex enginu mají výrazy v ořítku víc nalevo přednost) a každý výraz
                //strčíme do capture groupy pojmenované __i, kde i je pořadové číslo výrazu, počítáno od 0
                if (i != 0)
                    pattern.Append('|');
                pattern.AppendFormat("(?<{0}>{1})", "__" + i.ToString(), regexes[i]);
            }

            try
            {
                regex = new Regex(pattern.ToString(), RegexOptions.Compiled);
            }
            catch (ArgumentException)
            {
                try
                {
                    new Regex(regexOpts);
                }
                catch (ArgumentException)
                {
                    errorMessages.Add(string.Format("{0},{1}: The RegEx options are invalid.",
                        inputTree.Daughters[1].LineNumber, inputTree.Daughters[1].ColumnNumber));
                }

                for (int i = 0; i < regexes.Count; i++)
                {
                    try
                    {
                        new Regex(regexes[i]);
                    }
                    catch (ArgumentException)
                    {
                        errorMessages.Add(string.Format("{0},{1}: This regular expression is invalid.",
                            regexLines[i], regexColumns[i]));
                    }
                }
            }

            int numTerminals = symbolNames.Count;

            //neterminály, které se objevily na levé straně nějakého pravidla
            HashSet<int> reducibleNonterminals = new HashSet<int>();
            //neterminály, které se objevily na pravé straně nějakého pravidla
            HashSet<int> usedNonterminals = new HashSet<int>();

            bool[] terminalUsed = new bool[numTerminals];
            //
            terminalUsed[0] = true;

            List<Production> productions = new List<Production>();

            symbolNames.Add("$start");
            symbolCodes["$start"] = numTerminals;
            //semhle dáme <$start> výjimečně, protože ani nechceme,
            //aby ho někdo dával na pravou stranu nějakého pravidla
            usedNonterminals.Add(symbolCodes["$start"]);

            string startSymbol = inputTree.Daughters[3].Daughters[1].Value;

            if (symbolCodes.ContainsKey(startSymbol) && symbolCodes[startSymbol] < numTerminals)
                errorMessages.Add(string.Format("{0},{1}: The nonterminal <{2}> shares it's name with a terminal symbol.",
                    inputTree.Daughters[3].Daughters[1].LineNumber, inputTree.Daughters[3].Daughters[1].ColumnNumber, startSymbol));

            symbolNames.Add(startSymbol);
            symbolCodes[startSymbol] = symbolNames.Count - 1;


            //naše 0. pravidlo, které výstižně popisuje způsob, jakým si gramatiku upravujeme
            productions.Add(new Production(
                0, symbolCodes["$start"], new int[] { symbolCodes[startSymbol], symbolCodes["$end"] }));

            reducibleNonterminals.Add(symbolCodes["$start"]);
            usedNonterminals.Add(symbolCodes[startSymbol]);
            terminalUsed[symbolCodes["$end"]] = true;

            //zpracování pravidel
            ParseTree rules = inputTree.Daughters[4];
            while (rules != null)
            {
                ParseTree rule = rules.Daughters[0];

                //extrahujeme symbol na levé straně a zpracujeme ho
                string lhsSymbol = rule.Daughters[0].Daughters[1].Value;

                if (symbolCodes.ContainsKey(lhsSymbol) && symbolCodes[lhsSymbol] < numTerminals)
                    errorMessages.Add(string.Format("{0},{1}: The nonterminal <{2}> shares it's name with a terminal symbol.",
                        rule.Daughters[0].LineNumber, rule.Daughters[0].ColumnNumber, lhsSymbol));

                if (!symbolCodes.ContainsKey(lhsSymbol))
                {
                    symbolNames.Add(lhsSymbol);
                    symbolCodes[lhsSymbol] = symbolNames.Count - 1;
                }

                if (!reducibleNonterminals.Contains(symbolCodes[lhsSymbol]))
                    reducibleNonterminals.Add(symbolCodes[lhsSymbol]);

                int lhsSymbolCode = symbolCodes[lhsSymbol];

                //Zpracujeme výraz na pravé straně, který může sestávat z několika seznamů symbolů oddělenými
                //ořítky. Každý z těchto seznamů pak tvoří jedno pravidlo bez ořítek.
                ParseTree expression = rule.Daughters[2];
                while (expression != null)
                {
                    ParseTree symbolList = expression.Daughters[0];

                    List<int> rhsSymbols = new List<int>();

                    while (symbolList.Daughters.Length > 0)
                    {
                        ParseTree symbol = symbolList.Daughters[0];

                        int rhsSymbolCode = -1;
                        if (symbol.Daughters[0].SymbolName == "nonterminal")
                        {
                            string rhsSymbol = symbol.Daughters[0].Daughters[1].Value;

                            if (symbolCodes.ContainsKey(rhsSymbol) && symbolCodes[rhsSymbol] < numTerminals)
                                errorMessages.Add(string.Format("{0},{1}: The nonterminal <{2}> shares it's name with a terminal symbol.",
                                    symbol.LineNumber, symbol.ColumnNumber, rhsSymbol));

                            if (!symbolCodes.ContainsKey(rhsSymbol))
                            {
                                symbolNames.Add(rhsSymbol);
                                symbolCodes[rhsSymbol] = symbolNames.Count - 1;
                            }

                            if (!usedNonterminals.Contains(symbolCodes[rhsSymbol]))
                                usedNonterminals.Add(symbolCodes[rhsSymbol]);

                            rhsSymbolCode = symbolCodes[rhsSymbol];
                        }
                        else
                        {
                            string rhsSymbol = symbol.Daughters[0].Daughters[0].Value;

                            if (!symbolCodes.ContainsKey(rhsSymbol))
                                errorMessages.Add(string.Format("{0},{1}: The terminal '{2}' is used but not defined.",
                                    symbol.LineNumber, symbol.ColumnNumber, rhsSymbol));
                            else
                            {
                                rhsSymbolCode = symbolCodes[rhsSymbol];
                                terminalUsed[rhsSymbolCode] = true;
                            }
                        }

                        rhsSymbols.Add(rhsSymbolCode);

                        if (symbolList.Daughters.Length > 1)
                            symbolList = symbolList.Daughters[1];
                        else
                            symbolList = null;
                    }

                    productions.Add(new Production(productions.Count, lhsSymbolCode, rhsSymbols));

                    if (expression.Daughters.Length > 1)
                        expression = expression.Daughters[2];
                    else
                        expression = null;
                }

                if (rules.Daughters.Length > 1)
                    rules = rules.Daughters[1];
                else
                    rules = null;
            }

            //ToArray voláme proto, aby se líná metoda Intersect vyhodnotila a nedošlo by pak při vykonávání
            //dalšího příkazu k chybě
            int[] theGoodOnes = usedNonterminals.Intersect(reducibleNonterminals).ToArray();
            usedNonterminals.ExceptWith(theGoodOnes);
            reducibleNonterminals.ExceptWith(theGoodOnes);

            foreach (int nonterminal in usedNonterminals)
                warningMessages.Add(string.Format("Warning: The nonterminal <{0}> isn't reducible.",
                                                symbolNames[nonterminal]));
            foreach (int nonterminal in reducibleNonterminals)
                warningMessages.Add(string.Format("Warning: The nonterminal <{0}> is defined but never used.", symbolNames[nonterminal]));

            for (int terminal = 0; terminal < numTerminals; terminal++)
                if (!terminalUsed[terminal])
                    warningMessages.Add(string.Format("Warning: The terminal '{0}' is defined but never used.", symbolNames[terminal]));


            if (errorMessages.Count > 0)
                throw new InvalidSpecificationException(errorMessages.Concat(warningMessages));


            //už máme vše načte a zkontrolováno, teď už jen setřídíme pravidla podle levé strany,
            //přečíslujeme je a pro každý neterminál dopočítáme indexy, na kterých začínají pravidla
            //s daným neterminálem
            Production[] productionsArray = new Production[productions.Count];
            productionsArray[0] = productions[0];
            IEnumerable<Production> sortedProductions = productions.GetRange(1, productions.Count - 1).OrderBy((prod => prod.LHSSymbol));

            int k = 1;
            foreach (Production production in sortedProductions)
            {
                productionsArray[k] = production;
                productionsArray[k].ProductionCode = k;
                k++;
            }

            int numNonterminals = symbolCodes.Count - numTerminals;

            int[] nonterminalProductionOffset = new int[numNonterminals + 1];

            int offset = 0;
            for (int nonterminal = 0; nonterminal < numNonterminals; nonterminal++)
            {
                nonterminalProductionOffset[nonterminal] = offset;
                while ((offset < productionsArray.Length) &&
                    (productionsArray[offset].LHSSymbol == numTerminals + nonterminal))
                    offset++;
            }
            nonterminalProductionOffset[nonterminalProductionOffset.Length - 1] = offset;

            //a teď už to jen zabalíme a pošleme
            GrammarDefinition grammarDefinition = new GrammarDefinition(symbolNames.ToArray(), productionsArray, nonterminalProductionOffset, numTerminals);
            LexerData lexerData = new LexerData(regex, groupSymbolCodes);
            Grammar grammar = new Grammar(grammarDefinition, lexerData, null);

            return grammar;
        }

        private static ParseTree parseGrammarSpecification(string grammarSpecification)
        {
            LexerData lexerData;
            ParserData parserData;

            Grammar.ReadRuntimeDataFromStream(
                new MemoryStream(YetAnotherParserGenerator.Properties.Resources.SpecificationGrammar),
                out lexerData, out parserData);

            Lexer lexer = new Lexer(lexerData);
            lexer.SourceString = grammarSpecification;
            Parser parser = new Parser(parserData);

            return parser.Parse(lexer);
        }
    }
}