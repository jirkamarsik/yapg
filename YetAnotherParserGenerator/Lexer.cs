using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace YetAnotherParserGenerator
{
    /// <summary>
    /// Represents an object which can act as a lexer for the Parser class.
    /// </summary>
    public interface ILexer
    {
        /// <summary>
        /// Fetches the next token found by the lexer.
        /// </summary>
        /// <returns></returns>
        Token GetNextToken();

        /// <summary>
        /// Gets whether the lexer's underlying token source has any more tokens to divulge.
        /// (including the final $end symbol)
        /// </summary>
        bool HasTokens
        {
            get;
        }
    }

    /// <summary>
    /// A Lexer that uses the .NET RegEx engine to parse a string into tokens.
    /// </summary>
    public class Lexer : ILexer
    {
        IList<int> groupSymbolCodes;
        Regex regex;
        int position = 0, lineNumber = 1, columnNumber = 1;
        string input = "";
        bool hasTokens = false;

        /// <summary>
        /// Constructs a Lexer using the necessary data stored in the LexerData instance.
        /// </summary>
        /// <param name="lexerData">The LexerData containing information necessary to construct the Lexer.</param>
        public Lexer(LexerData lexerData)
        {
            this.groupSymbolCodes = lexerData.GroupSymbolCodes;
            this.regex = lexerData.Regex;
        }

        /// <summary>
        /// Constructs a Lexer using the individual pieces of data necessary.
        /// </summary>
        /// <param name="regex">The Regex which captures tokens from the source string.</param>
        /// <param name="groupSymbolCodes">An array of symbol codes defining what token should be returned
        /// when a given capture group matches a string. <i>groupSymbolCodes</i>[<i>i</i>] is the symbol code of the terminal
        /// whose token is to be returned when a capture group named '__'<i>i</i> matches a string.</param>
        public Lexer(Regex regex, IList<int> groupSymbolCodes)
        {
            this.regex = regex;
            this.groupSymbolCodes = groupSymbolCodes;
        }

        /// <summary>
        /// Fetches the next token from the source string.
        /// </summary>
        /// <returns></returns>
        public Token GetNextToken()
        {
            if (position < input.Length)
            {
                Match match = regex.Match(input, position);
                Token token = new Token();
                token.LineNumber = lineNumber;
                token.ColumnNumber = columnNumber;

                //pokud regex nenamatchuje řetězec začínající na aktuální pozici, museli jsme narazit
                //na něco, co není popsáno v definicích tokenů a oznámíme to výjimkou
                if (!match.Success || (match.Index > position))
                {
                    string unexpectedCharacter = null;
                    switch (input[position])
                    {
                        case '\n':
                            unexpectedCharacter = "\\n";
                            break;
                        case '\r':
                            unexpectedCharacter = "\\r";
                            break;
                        case '\t':
                            unexpectedCharacter = "\\t";
                            break;
                        default:
                            unexpectedCharacter = input[position].ToString();
                            break;
                    }
                    throw new ParsingException(string.Format("Unexpected character '{0}' encountered by the lexer.",
                                                             unexpectedCharacter), lineNumber, columnNumber);
                }

                //regex obsahuje capture groupy s názvy __0, __1..., groupy jsou seřazeny tak, že ty s nižším
                //číslem mají přednost (dokumentovaná vlastnost regexího operátoru | )
                //když u nějaké groupy najdeme match, podíváme se do groupSymbolCodes, o jaký terminál se jedná
                for (int i = 0; i < groupSymbolCodes.Count; i++)
                {
                    if (match.Groups["__" + i.ToString()].Success)
                    {
                        token.SymbolCode = groupSymbolCodes[i];
                        token.Value = match.Value;
                        break;
                    }
                }

                position += match.Length;

                //přepočítáme "lidskou" adresu v textu (namatchované řetězce můžou být víceřádkové)
                int lastNewLine = match.Value.LastIndexOf('\n');
                if (lastNewLine == -1)
                {
                    columnNumber += match.Length;
                }
                else
                {
                    columnNumber = match.Length - lastNewLine;
                    foreach (char c in match.Value)
                        if (c == '\n')
                            lineNumber++;
                }

                if (token.SymbolCode == -1)
                    return GetNextToken();
                else
                    return token;
            }
            else
            {
                Token finalToken = new Token();
                finalToken.LineNumber = lineNumber;
                finalToken.ColumnNumber = columnNumber;
                //token == $end
                finalToken.SymbolCode = 0;
                finalToken.Value = "";

                hasTokens = false;

                return finalToken;
            }
        }

        /// <summary>
        /// Gets whether the remainder of the source string contains any more tokens.
        /// (including the final $end symbol)
        /// </summary>
        public bool HasTokens
        { get { return hasTokens; } }

        /// <summary>
        /// Gets or sets the string from which tokens are read.
        /// </summary>
        public string SourceString
        {
            get
            {
                return input.Substring(position);
            }
            set
            {
                input = value;
                hasTokens = true;
                position = 0;
                lineNumber = 1;
                columnNumber = 1;
            }
        }
    }
}
