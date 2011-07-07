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
        internal static readonly int CODE_SKIP = -1, CODE_END = 0, CODE_HEADER = 1, CODE_HEADERCODE = 2,
            CODE_LEXER = 3, CODE_NULL = 4, CODE_REGEXOPTS = 5, CODE_IDENTIFIER = 6, CODE_EQUALS = 7,
            CODE_REGEX = 8, CODE_PARSER = 9, CODE_START = 10, CODE_USEROBJECT = 11, CODE_TYPE = 12,
            CODE_DOT = 13, CODE_QUOTED = 14, CODE_LANGLE = 15, CODE_RANGLE = 16, CODE_DERIVES = 17,
            CODE_CODE = 18, CODE_OR = 19;
            
        public struct RegexOccurence
        {
            public string Regex;
            public int SymbolCode;
            public int LineNumber;
            public int ColumnNumber;
        }
        
        public class GrammarParserLocals
        {
            public GrammarParserLocals(string fileName, out IList<string> warningMessages) {
                this.FileName = fileName;
                this.WarningMessages = new List<string>();
                warningMessages = this.WarningMessages;
            }
            
            public IList<string> WarningMessages;
            public IList<string> ErrorMessages;
            
            public string FileName;
            
            public string HeaderCode;
            
            public List<string> SymbolNames;
            public int NumTerminals;
            
            public string NullTerminal;
            
            public string UserObjectType;
            
            public HashSet<int> ExpandableNonterminals;
            public HashSet<int> UsedNonterminals;
            public bool[] TerminalUsed;
        }
		
		public static Grammar ParseGrammar(string specificationPath, out IList<string> warningMessages)
		{
#if BOOTSTRAP      
            GrammarLexer lexer = new GrammarLexer();
            lexer.SourceString = File.ReadAllText(specificationPath);
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
                	// FIXME: We no longer have the line and column data on regexOpts.
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

			
			string userObjectType = null;
			
			if (token.SymbolCode == CODE_USEROBJECT) {
			
				token = lexer.GetNextToken();
				
                if (token.SymbolCode == CODE_QUOTED) {
                    userObjectType = token.Value.Substring(1, token.Value.Length - 2);
                    token = lexer.GetNextToken();
                } else {
    				StringBuilder typeBuilder = new StringBuilder(token.Value);
    				token = lexer.GetNextToken();
    				while (token.SymbolCode == CODE_DOT) {
    					typeBuilder.Append(".");
    					token = lexer.GetNextToken();
    					typeBuilder.Append(token.Value);
    					token = lexer.GetNextToken();
    				}
    				
    				userObjectType = typeBuilder.ToString();
                }            
			}

            //naše 0. pravidlo, které výstižně popisuje způsob, jakým si gramatiku upravujeme
            productions.Add(new ProductionWithAction(new Production(
                symbolCodes["$start"], new int[] { symbolCodes[startSymbol], symbolCodes["$end"] }), "{ return _1; }"));

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
                    
                    if (token.SymbolCode == CODE_QUOTED) {
                        // QUOTED
                        typeMappings.Add(nonterminal, token.Value.Substring(1, token.Value.Length - 2));
                        token = lexer.GetNextToken();
                    }
                    else {
    					StringBuilder typeBuilder = new StringBuilder(token.Value);
    					token = lexer.GetNextToken();
    					while (token.SymbolCode == CODE_DOT) {
    						typeBuilder.Append(".");
    						token = lexer.GetNextToken();
    						typeBuilder.Append(token.Value);
    						token = lexer.GetNextToken();
    					}
    					
    					typeMappings.Add(nonterminal, typeBuilder.ToString());
                    }
					
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
	
                        productions.Add(new ProductionWithAction(new Production(lhsSymbolCode, rhsSymbols), code));
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
            GrammarCode grammarCode = new GrammarCode(headerCode, actions, nonterminalTypes, userObjectType);
            Grammar grammar = new Grammar(grammarDefinition, lexerData, grammarCode);

            return grammar;
#else
            LexerData lexerData;
            ParserData parserData;
            Grammar.ReadRuntimeDataFromStream(
                new MemoryStream(YetAnotherParserGenerator.Properties.Resources.SpecificationGrammar),
                out lexerData, out parserData);
            
            GrammarLexer lexer = new GrammarLexer();
            Parser parser = new Parser(parserData);
            
            GrammarParserLocals locals = new GrammarParserLocals(specificationPath, out warningMessages);
            lexer.SourceString = File.ReadAllText(specificationPath);
            return (Grammar)parser.Parse(lexer, locals);
#endif
		}
    }
}