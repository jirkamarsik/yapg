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
        /// <returns>The next token discovered in the input string.</returns>
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
    
    /// <summary>
    /// Fetches the next token from the source string.
    /// </summary>
    /// <returns>The next token discovered in the input string.</returns>
	public class GrammarLexer : ILexer
	{
        int position = 0, lineNumber = 1, columnNumber = 1;
        string input = "";
        bool hasTokens = false;
        
		private static readonly int code_skip = -1, code_end = 0, code_header = 1,
			code_headerCode = 2, code_lexer = 3, code_null = 4, code_identifier = 5,
			code_regexopts = 6, code_equals = 7, code_regex = 8, code_parser = 9,
			code_start = 10, code_type = 11, code_dot = 12, code_lAngle = 13,
			code_rAngle = 14, code_derives = 15, code_code = 16, code_or = 17;
		
		private static readonly Regex identifier = new Regex("\\w+"),
									  whitespace = new Regex("\\s+"),
									  regexopts = new Regex("\\(\\?[imnsx\\-]+\\)"),
									  endOfHeader = new Regex("^\\s*%lexer\\b", RegexOptions.Multiline),
								      singleQuoteContents = new Regex("([^\\']|\\.)*"),
								      doubleQuoteContents = new Regex("([^\\\"]|\\.)*");
		
		private int previousSymbolCode = code_skip;
        
        /// <summary>
        /// Fetches the next token from the source string.
        /// </summary>
        /// <returns>The next token discovered in the input string.</returns>
        public Token GetNextToken()
        {
            if (position < input.Length)
            {
            	int matchLength = 0;
            	int symbolCode = -2;
	        	if (previousSymbolCode == code_header) {
	        		symbolCode = code_headerCode;
	        		var endOfHeaderMatch = endOfHeader.Match(input, position);
					if (!endOfHeaderMatch.Success)
						matchLength = input.Length - position;
					else
						matchLength = endOfHeaderMatch.Index - position;
				} else if (previousSymbolCode == code_equals) {
					symbolCode = code_regex;
					int endOfLine = input.IndexOf('\n', position);
					if (endOfLine == -1)
						matchLength = input.Length - position;
					else
						matchLength = endOfLine - position;
				} else switch (input[position]) {
				case '<':
					symbolCode = code_lAngle;
					matchLength = 1;
					break;
				case '>':
					symbolCode = code_rAngle;
					matchLength = 1;
					break;
				case '.':
					symbolCode = code_dot;
					matchLength = 1;
					break;
				case '|':
					symbolCode = code_or;
					matchLength = 1;
					break;
				case '=':
					symbolCode = code_equals;
					matchLength = 1;
					break;
				case ':':
					if ((input[position + 1] == ':') && (input[position + 2] == '=')) {
						symbolCode = code_derives;
						matchLength = 3;
					} else {
						// TODO: Scream about malformed operator.
					}
					break;
				case '%':
					var keyword = identifier.Match(input, position + 1);
					switch (keyword.Value) {
					case "header":
						symbolCode = code_header;
						break;
					case "lexer":
						symbolCode = code_lexer;
						break;
					case "null":
						symbolCode = code_null;
						break;
					case "parser":
						symbolCode = code_parser;
						break;
					case "start":
						symbolCode = code_start;
						break;
					case "type":
						symbolCode = code_type;
						break;
					default:
						// TODO: Scream about unknown keyword.
						break;
					}
					matchLength = 1 + keyword.Length;
					break;
				case '{':
					int blockDepth = 1;
					int blockPosition = position + 1;
					while ((blockDepth > 0) && (blockPosition < input.Length)) {
						switch (input[blockPosition]) {
						case '{':
							blockDepth++;
							blockPosition++;
							break;
						case '}':
							blockDepth--;
							blockPosition++;
							break;
						case '/':
							if (input[blockPosition + 1] == '/') {
								int endOfLine = input.IndexOf('\n', blockPosition + 2);
								if (endOfLine == -1) {
									blockPosition = input.Length;
								} else {
									blockPosition = endOfLine + 1;
								}
							} else if (input[blockPosition + 1] == '*') {
								int endOfComment = input.IndexOf("*/", blockPosition + 2);
								if (endOfComment == -1) {
									blockPosition = input.Length;
								} else {
									blockPosition = endOfComment + 2;
								}
							} else {
								blockPosition++;
							}
							break;
						case '\'':
						case '"':
							char quote = input[blockPosition];
							Regex quoteContents = quote == '"' ? doubleQuoteContents : singleQuoteContents;
							int literalLength = quoteContents.Match(input, blockPosition + 1).Length;
							blockPosition += 1 + literalLength + 1;
							break;
						default:
							blockPosition++;
							break;
						}
					}
					symbolCode = code_code;
					matchLength = blockPosition - position;
					break;
				case '(':
					var match = regexopts.Match(input, position);
					if (match.Index == position) {
						symbolCode = code_regexopts;
						matchLength = match.Length;
					} else {
						// TODO: Scream about malformed regexopts.
					}
					break;
				case '#':
					int endOfLine = input.IndexOf('\n', position + 1);
					symbolCode = code_skip;
					if (endOfLine == -1)
						matchLength = input.Length - position;
					else
						matchLength = (endOfLine + 1) - position;
					break;
				default:
					var whitespace_match = whitespace.Match(input, position);
					if (whitespace_match.Index == position) {
						symbolCode = code_skip;
						matchLength = whitespace_match.Length;
					} else {
						var identifier_match = identifier.Match(input, position);
						if (identifier_match.Index == position) {
							symbolCode = code_identifier;
							matchLength = identifier_match.Length;
						} else {
							//TODO: Scream about unexpected character.
						}
					}
					break;
				}
				
                Token token = new Token();
                token.LineNumber = lineNumber;
                token.ColumnNumber = columnNumber;
                token.SymbolCode = symbolCode;
				token.Value = input.Substring(position, matchLength);
 

                //přepočítáme "lidskou" adresu v textu (namatchované řetězce můžou být víceřádkové)
                int lastNewLine = token.Value.LastIndexOf('\n');
                if (lastNewLine == -1)
                {
                    columnNumber += matchLength;
                }
                else
                {
                    columnNumber = token.Value.Length - lastNewLine;
                    foreach (char c in token.Value)
                        if (c == '\n')
                            lineNumber++;
                }
                
                position += matchLength;
				previousSymbolCode = symbolCode;
				
                if (token.SymbolCode == -1) {
                    return GetNextToken();
				} else {
                    return token;
				}
            }
            else
            {
                Token finalToken = new Token();
                finalToken.LineNumber = lineNumber;
                finalToken.ColumnNumber = columnNumber;
                //token == $end
                finalToken.SymbolCode = code_end;
                finalToken.Value = "";

                hasTokens = false;

				previousSymbolCode = 0;
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
                previousSymbolCode = code_skip;
            }
        }
	}
}
