using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace YetAnotherParserGenerator
{
    /// <summary>
    /// Contains information about a grammar which can be found directly in the grammar specification.
    /// </summary>
    public class GrammarDefinition
    {
        private string[] symbolNames;
        private Production[] productions;
        private int[] nonterminalProductionOffset;
        private int numTerminals;
        
        /// <summary>
        /// Creates an instance of GrammarDefinition and fills it with all the data it needs.
        /// </summary>
        /// <param name="symbolNames">An array of symbol names for each symbol used in the grammar, index by symbol codes.</param>
        /// <param name="productions">An array of productions defined in the grammar sorted by left-hand side symbols' codes.</param>
        /// <param name="nonterminalProductionOffset">An array of indices into the <i>productions</i> array.
        /// <i>nonterminalProductionsOffset</i>[<i>i</i>] is the index of the first production with left-hand side symbol code <i>i</i>.</param>
        /// <param name="numTerminals">The number of terminals defined in the grammar.</param>
        public GrammarDefinition(string[] symbolNames, Production[] productions, int[] nonterminalProductionOffset, int numTerminals)
        {
            this.symbolNames = symbolNames;
            this.productions = productions;
            this.nonterminalProductionOffset = nonterminalProductionOffset;
            this.numTerminals = numTerminals;
        }

        /// <summary>
        /// Gets an array of symbol names for each symbol used in the grammar, indexed by symbol codes.
        /// </summary>
        public string[] SymbolNames { get { return symbolNames; } }

        /// <summary>
        /// Gets an array of productions defined in the grammar sorted by left-hand side symbols' codes.
        /// </summary>
        public Production[] Productions { get { return productions; } }
        /// <summary>
        /// Gets the number of productions defined in the grammar.
        /// </summary>
        public int NumProductions { get { return productions.Length; } }

        /// <summary>
        /// Gets an array of indices into the <i>productions</i> array.
        /// <i>nonterminalProductionsOffset</i>[<i>i</i>] is the index of the first production with left-hand side symbol code <i>i</i>.
        /// </summary>
        public int[] NonterminalProductionOffset
        { get { return nonterminalProductionOffset; } }

        /// <summary>
        /// Gets the number of terminals defined in the grammar.
        /// </summary>
        public int NumTerminals { get { return numTerminals; } }

        /// <summary>
        /// Gets the number of nonterminals defined in the grammar.
        /// </summary>
        public int NumNonterminals { get { return symbolNames.Length - NumTerminals; } }

        /// <summary>
        /// Gets the total number of symbols defined in the grammar.
        /// </summary>
        public int NumSymbols { get { return symbolNames.Length; } }
    }

    /// <summary>
    /// Contains information which Lexer needs to know at runtime.
    /// </summary>
    [Serializable]
    public class LexerData
    {
        private Regex regex;
        private List<int> groupSymbolCodes;
        /// <summary>
        /// Creates an instance of LexerData and initializes it with all the needed data.
        /// </summary>
        /// <param name="regex">The Regex which is supposed to capture tokens from the input string.</param>
        /// <param name="groupSymbolCodes">An array of symbol codes defining what token should be returned
        /// when a given capture group matches a string. <i>groupSymbolCodes</i>[<i>i</i>] is the symbol code of the terminal
        /// whose token is to be returned when a capture group named '__'<i>i</i> matches a string.</param>
        public LexerData(Regex regex, List<int> groupSymbolCodes)
        {
            this.regex = regex;
            this.groupSymbolCodes = groupSymbolCodes;
        }

        /// <summary>
        /// Deserializes a LexerData object from the given binary Stream.
        /// </summary>
        /// <param name="inStream">The binary Stream from which the serializer will read the LexerData object.</param>
        /// <returns>A LexerData object deserialized from <i>inStream</i>.</returns>
        public static LexerData FromStream(Stream inStream)
        {
            BinaryFormatter formatter = new BinaryFormatter();

            return (LexerData)formatter.Deserialize(inStream);
        }

        /// <summary>
        /// Serializes this LexerData instance to a Stream using the BinarySerializer.
        /// </summary>
        /// <param name="outStream">The binary Stream to which this instance will be serialized.</param>
        public void WriteToStream(Stream outStream)
        {
            BinaryFormatter formatter = new BinaryFormatter();

            formatter.Serialize(outStream, this);
        }

        /// <summary>
        /// Gets the Regex which is supposed to capture tokens from the input string.
        /// </summary>
        public Regex Regex
        { get { return regex; } }

        /// <summary>
        /// Gets an array of symbol codes defining what token should be returned when a given capture group matches a string.
        /// <i>groupSymbolCodes</i>[<i>i</i>] is the symbol code of the terminal
        /// whose token is to be returned when a capture group named '__'<i>i</i> matches a string.
        /// </summary>
        public List<int> GroupSymbolCodes
        { get { return groupSymbolCodes; } }
    }

    /// <summary>
    /// Contains information which Parser needs to know at runtime.
    /// </summary>
    [Serializable]
    public class ParserData
    {
        private ParserAction[,] parseTable;
        private int[,] gotoTable;
        private string[] symbolNames;
        private ProductionOutline[] productions;
        private Assembly actionAssembly;
        
        /// <summary>
        /// Creates a new instance of ParserData and initializes the properties which can be read
        /// directly from the grammar specification.
        /// </summary>
        /// <param name="symbolNames">An array of symbol names for each symbol used in the grammar, index by symbol codes.</param>
        /// <param name="productions">An array of productions defined in the grammar sorted by left-hand side symbols' codes.</param>
        public ParserData(string[] symbolNames, ProductionOutline[] productions)
        {
            this.symbolNames = symbolNames;
            this.productions = productions;
        }

        /// <summary>
        /// Deserializes a ParserData object from the given binary Stream.
        /// </summary>
        /// <param name="inStream">The binary Stream from which the serializer will read the ParserData object.</param>
        /// <returns>A ParserData object deserialized from <i>inStream</i>.</returns>
        public static ParserData FromStream(Stream inStream)
        {
            BinaryFormatter formatter = new BinaryFormatter();

            return (ParserData)formatter.Deserialize(inStream);
        }

        /// <summary>
        /// Serializes this ParserData instance to a Stream using the BinarySerializer.
        /// </summary>
        /// <param name="outStream">The binary Stream to which this instance will be serialized.</param>
        /// <exception cref="InvalidOperationException">when the ParseTable or GotoTable properties are not set.</exception>
        public void WriteToStream(Stream outStream)
        {
            if (ParseTable == null)
                throw new InvalidOperationException("The ParseTable of this ParserData instance is null. Ensure you have processed the grammar and computed the tables.");
            if (GotoTable == null)
                throw new InvalidOperationException("The GotoTable of this ParserData instance is null. Ensure you have processed the grammar and computed the tables.");

            BinaryFormatter formatter = new BinaryFormatter();

            formatter.Serialize(outStream, this);
        }

        /// <summary>
        /// Gets or sets the parse table, which is a 2D array indexed by state numbers and a terminal lookahead
        /// symbol codes that specifies the parser's next action in a given configuration.
        /// </summary>
        public ParserAction[,] ParseTable
        {
            get { return parseTable; }
            set { parseTable = value; }
        }

        /// <summary>
        /// Gets or sets the goto table, which is a 2D array indexed by state numbers and nonterminal symbol
        /// numbers that specifies which state should be pushed on the state stack after reducing to a nonterminal
        /// in a given state.
        /// </summary>
        public int[,] GotoTable
        {
            get { return gotoTable; }
            set { gotoTable = value; }
        }

        /// <summary>
        /// Gets an array of symbol names for each symbol used in the grammar, indexed by symbol codes.
        /// </summary>
        public string[] SymbolNames
        { get { return symbolNames; } }

        /// <summary>
        /// Gets an array containing stripped down copies of productions of the grammar, containing only
        /// information necessary for the parser to work.
        /// </summary>
        public ProductionOutline[] Productions
        { get { return productions; } }
        
        /// <summary>
        /// Gets the compiled assembly containing the code which is run when reducing symbols in the parser.
        /// </summary>
		public Assembly ActionAssembly
		{
			get { return actionAssembly; }
			set { actionAssembly = value; }
		}
    }
    
    /// <summary>
    /// A class containing all the user code elemetns extracted from the grammar definition file.
    /// </summary>
	public class GrammarCode
	{
		private string headerCode;
		private string[] productionActions;
		private string[] nonterminalTypes;
		
		public GrammarCode(string headerCode, string[] productionActions, string[] nonterminalTypes)
		{
			this.headerCode = headerCode;
			this.productionActions = productionActions;
			this.nonterminalTypes = nonterminalTypes;
		}
		
		/// <summary>
		/// The code to be inserted at the beginning of the generated source containing
		/// helper classes and using statements.
		/// </summary>
		public string HeaderCode { get { return headerCode; } }
		
		/// <summary>
		/// An array of function bodies. These functions describe what action is to be
		/// taken when a given the symbols are reduced on the basis of a specific production.
		/// </summary>
		public string[] ProductionActions { get { return productionActions; } }
		
		/// <summary>
		/// The names of types of the values carried by the individual nonterminals.
		/// </summary>
		public string[] NonterminalTypes { get { return nonterminalTypes; } }
	}

    /// <summary>
    /// Represents a grammar with both it's defining properties and computed data needed by a running parser.
    /// </summary>
    public class Grammar
    {
        private GrammarDefinition grammarDefinition;
        private LexerData lexerData;
        private GrammarCode grammarCode;
        private ParserData parserData;
        
        /// <summary>
        /// Creates a new Grammar instance and sets it's GrammarDefiniton, LexerData and 
        /// GrammarCode properties to provided values. The ParserData structure will be
        /// initialized and it's ProductionOutlines and SymbolNames properties will be set.
        /// </summary>
        /// <param name="grammarDefinition">The definition of the grammar.</param>
        /// <param name="lexerData">A structure defining the lexer's runtime behaviour.</param>
        /// <param name="grammarCode">The collection of user code describing the actions of the parser.</param>
        public Grammar(GrammarDefinition grammarDefinition, LexerData lexerData, GrammarCode grammarCode)
        {
            this.grammarDefinition = grammarDefinition;
            this.lexerData = lexerData;
            this.grammarCode = grammarCode;

            ProductionOutline[] productionOutlines = new ProductionOutline[grammarDefinition.Productions.Length];
            for (int i = 0; i < productionOutlines.Length; i++)
                productionOutlines[i] = new ProductionOutline(grammarDefinition.Productions[i].LHSSymbol, grammarDefinition.Productions[i].RHSSymbols.Count);

            this.parserData = new ParserData(grammarDefinition.SymbolNames, productionOutlines);
        }

        /// <summary>
        /// Writes data required for lexing/parsing to the specified binary Stream.
        /// </summary>
        /// <param name="outStream">The Stream to which the grammar data will be written.</param>
        /// <exception cref="InvalidOperationException">when the ParseTable or GotoTable properties of the
        /// contained ParserData instance are not set.</exception>
        public void WriteRuntimeDataToStream(Stream outStream)
        {
            if (ParserData.ParseTable == null)
                throw new InvalidOperationException("The ParseTable of the contained ParserData instance is null. Ensure you have processed this Grammar and computed the tables.");
            if (ParserData.GotoTable == null)
                throw new InvalidOperationException("The GotoTable of the contained ParserData instance is null. Ensure you have processed this Grammar and computed the tables.");

            BinaryFormatter formatter = new BinaryFormatter();

            formatter.Serialize(outStream, lexerData);
            formatter.Serialize(outStream, parserData);
        }

        /// <summary>
        /// Writes data required for lexing/parsing to the specified binary file.
        /// </summary>
        /// <param name="outFile">The name of the file to which the grammar data will be written.</param>
        public void WriteRuntimeDataToFile(string outFile)
        {
            Stream outStream = new FileStream(outFile, FileMode.Create, FileAccess.Write, FileShare.None);

            this.WriteRuntimeDataToStream(outStream);
            outStream.Flush();

            outStream.Close();
        }

        /// <summary>
        /// Reads runtime data for lexer/parser construction from a binary Stream.
        /// </summary>
        /// <param name="inStream">The Stream from which the data shall be read.</param>
        /// <param name="lexerData">The LexerData serialized from the Stream.</param>
        /// <param name="parserData">The ParserData serialized from the Stream.</param>
        public static void ReadRuntimeDataFromStream(Stream inStream, out LexerData lexerData, out ParserData parserData)
        {
            lexerData = LexerData.FromStream(inStream);
            parserData = ParserData.FromStream(inStream);
        }

        /// <summary>
        /// Reads runtime data for lexer/parser construction from a binary file.
        /// </summary>
        /// <param name="inFile">The name of the file from which the data shall be read.</param>
        /// <param name="lexerData">The LexerData serialized from the Stream.</param>
        /// <param name="parserData">The ParserData serialized from the Stream.</param>
        public static void ReadRuntimeDataFromFile(string inFile, out LexerData lexerData, out ParserData parserData)
        {
            Stream inStream = new FileStream(inFile, FileMode.Open, FileAccess.Read, FileShare.Read);

            lexerData = LexerData.FromStream(inStream);
            parserData = ParserData.FromStream(inStream);

            inStream.Close();
        }

        /// <summary>
        /// Gets the GrammarDefinition containing all the information which can be read directly from the grammar specification.
        /// </summary>
        public GrammarDefinition GrammarDefinition { get { return grammarDefinition; } }
        
        /// <summary>
        /// Gets the GrammarCode containing all the user-supplied code describing the parser's actions.
        /// </summary>
		public GrammarCode GrammarCode { get { return grammarCode; } }

        #region GrammarDefinition members
        /// <summary>
        /// Gets an array of symbol names for each symbol used in the grammar, indexed by symbol codes.
        /// </summary>
        public string[] SymbolNames { get { return grammarDefinition.SymbolNames; } }
        /// <summary>
        /// Gets an array of productions defined in the grammar sorted by left-hand side symbols' codes.
        /// </summary>
        public Production[] Productions { get { return grammarDefinition.Productions; } }
        /// <summary>
        /// Gets the number of productions defined in the grammar.
        /// </summary>
        public int NumProductions { get { return grammarDefinition.NumProductions; } }
        /// <summary>
        /// Gets an array of indices into the <i>productions</i> array.
        /// <i>nonterminalProductionsOffset</i>[<i>i</i>] is the index of the first production with left-hand side symbol code <i>i</i>.
        /// </summary>
        public int[] NonterminalProductionOffset { get { return grammarDefinition.NonterminalProductionOffset; } }
        /// <summary>
        /// Gets the number of terminals defined in the grammar.
        /// </summary>
        public int NumTerminals { get { return grammarDefinition.NumTerminals; } }
        /// <summary>
        /// Gets the number of nonterminals defined in the grammar.
        /// </summary>
        public int NumNonterminals { get { return grammarDefinition.NumNonterminals; } }
        /// <summary>
        /// Gets the total number of symbols defined in the grammar.
        /// </summary>
        public int NumSymbols { get { return grammarDefinition.NumSymbols; } }
#endregion

        /// <summary>
        /// Gets the LexerData which contains all the necessary runtime information for the lexer.
        /// </summary>
        public LexerData LexerData
        { get { return lexerData; } }

        /// <summary>
        /// Gets the ParserData which contains all the necessary runtime information for the parser.
        /// </summary>
        public ParserData ParserData
        { get { return parserData; } }
    }
}